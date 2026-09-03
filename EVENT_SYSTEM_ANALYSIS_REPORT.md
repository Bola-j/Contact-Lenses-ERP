# Lensee Production - Event System Meta Analysis Report

## Executive Summary

The Lensee Production system implements a **multi-layered event-driven architecture** with three distinct event systems:

1. **Application Events (Outbox Pattern)** - Domain events published via transactional outbox for cross-module communication
2. **Catalog Domain Events** - Entity lifecycle events for catalog entities (Category, Brand, Product, SKU)
3. **Shopify Webhook Events** - External integration events with durable inbox and retry mechanism
4. **Audit Events** - Immutable audit trail for all entity changes
5. **Frontend Synchronization Events** - Cross-tab communication via BroadcastChannel and Storage API

---

## 1. Application Events (Outbox Pattern)

### Infrastructure
- **Location**: `Lensee.Host.Infrastructure.AppEventInfrastructure`
- **Storage**: `shared.outbox_messages` table (PostgreSQL)
- **Publisher**: `OutboxAppEventPublisher : IAppEventPublisher`
- **Processor**: `OutboxWorker : BackgroundService` (batch size 25, max 10 attempts, exponential backoff)
- **Delivery Receipts**: `shared.outbox_delivery_receipts` for exactly-once handler execution

### Event Interface
```csharp
public interface IAppEvent { DateTime OccurredAt { get; } }
public interface IAppEventPublisher { Task PublishAsync<TEvent>(TEvent, CancellationToken) where TEvent : IAppEvent; }
public interface IAppEventHandler<in TEvent> { Task HandleAsync(TEvent, CancellationToken) where TEvent : IAppEvent; }
```

### Registered Events & Handlers

| Event Type | Publisher Location | Handler | Handler Action |
|------------|-------------------|---------|----------------|
| `PaymentWorkflowChangedEvent` | PaymentsEndpoints.cs (6 locations), OperationsEndpoints.cs (1) | `PaymentWorkflowNotificationHandler` | Creates `NotificationLog` for assigned accountant or Admin role |
| `OperationCorrectionChangedEvent` | OperationCorrectionService.cs (4 locations) | `OperationCorrectionNotificationHandler` | Creates `NotificationLog` for Admin role |
| `CatalogEventEnvelope` | `TransactionalCatalogEventPublisher` | *Auto-dispatched by OutboxWorker* | Wraps catalog domain events for outbox |

### Event Definitions

#### PaymentWorkflowChangedEvent
```csharp
record PaymentWorkflowChangedEvent(
    Guid PaymentLogId,
    Guid? MerchantId,
    Guid? OperationId,
    string EventType,           // "PaymentAssigned", "PaymentApproved", "PaymentRejected", etc.
    string Message,
    Guid? TargetUserId,
    string? TargetRole,
    DateTime OccurredAt) : IAppEvent;
```
**Published at**: Payment assignment, approval, rejection, completion, reversal

#### OperationCorrectionChangedEvent
```csharp
record OperationCorrectionChangedEvent(
    Guid CorrectionProposalId,
    Guid OperationId,
    string Action,              // "Requested", "SettlementSubmitted", "Rejected", "Approved"
    Guid ActorId,
    DateTime OccurredAt) : IAppEvent;
```
**Published at**: Correction proposal creation, settlement submission, rejection, approval

#### CatalogEventEnvelope (Wraps Catalog Domain Events)
```csharp
record CatalogEventEnvelope(
    Guid EntityId,
    string EntityType,          // "Category", "Brand", "Product", "Sku"
    string Action,              // Event class name: "CategoryCreated", "ProductUpdated", etc.
    DateTime OccurredAt) : IAppEvent;
```

---

## 2. Catalog Domain Events

### Location
`Lensee.Modules.Catalog.Domain.Events.CatalogEvents`

### Event Hierarchy
```csharp
abstract record CatalogEvent(Guid EntityId, string EntityType, DateTime OccurredAt)
├── CategoryCreated(Guid, DateTime)
├── CategoryUpdated(Guid, DateTime)
├── BrandCreated(Guid, DateTime)
├── BrandUpdated(Guid, DateTime)
├── ProductCreated(Guid, DateTime)
├── ProductUpdated(Guid, DateTime)
├── ProductDeactivated(Guid, DateTime)
├── ProductReactivated(Guid, DateTime)
├── SkuCreated(Guid, DateTime)
├── SkuUpdated(Guid, DateTime)
├── SkuDeactivated(Guid, DateTime)
└── SkuReactivated(Guid, DateTime)
```

### Publishing Path
1. **Endpoint** (CatalogEndpoints.cs) → 2. **CatalogMutationTransaction** → 3. **ICatalogEventPublisher** → 4. **TransactionalCatalogEventPublisher** → 5. **shared.outbox_messages** (as CatalogEventEnvelope) → 6. **OutboxWorker** → 7. **Handlers** (currently none registered)

### Publishers
- `TransactionalCatalogEventPublisher` (production) - wraps in outbox envelope
- `NoOpCatalogEventPublisher` (testing) - no-op

---

## 3. Shopify Webhook Events (Durable Inbox)

### Entity
`Lensee.Modules.Operations.Data.ShopifyWebhookEvent` → `operations.shopify_webhook_events`

### Schema
```sql
id                  UUID PK
webhook_id          VARCHAR(100) UNIQUE  -- Shopify's X-Shopify-Webhook-Id
topic               VARCHAR(100)         -- "orders/create", "orders/cancelled", "refunds/create"
shop_domain         VARCHAR(255)
verification_mode   VARCHAR(50)          -- "Hmac", "LegacyPath"
event_id            VARCHAR(100)         -- Shopify's event ID
api_version         VARCHAR(30)
payload_hash        CHAR(128)            -- SHA256 of raw payload
protected_payload   TEXT                 -- Encrypted payload (DataProtection)
status              VARCHAR(50)          -- "Queued", "Processing", "Imported", "Cancelled", "RequiresAttention", "Retrying", "Ignored", "Duplicate"
detail              TEXT
shopify_order_id    VARCHAR(100)
operation_id        UUID FK              -- Link to created operation
received_at         TIMESTAMP
verified_at         TIMESTAMP?
triggered_at        TIMESTAMP?
processed_at        TIMESTAMP?
next_attempt_at     TIMESTAMP?
lease_until         TIMESTAMP?           -- Prevents concurrent processing
attempt_count       INT
resolved_at         TIMESTAMP?
resolved_by         UUID?
resolution_note     VARCHAR(1000)
```

### Processing Flow

```
ReceiveAsync() → Validate HMAC/Legacy → Store in shopify_webhook_events (status=Queued/Ignored)
       ↓
OutboxWorker/ShopifyWebhookWorker → ClaimDueEvents() → Lease (2 min) → ProcessQueuedEvent()
       ↓
Decrypt payload → Route by topic:
  ├── orders/create → CreateOrderAsync() → Creates OperationLog (RetailSale) + ShopifyOrderLink
  ├── orders/cancelled → CancelOrderAsync() → Cancels operation + releases stock
  └── refunds/create → RegisterRefundExceptionAsync() → Creates notification
       ↓
Update status, processed_at, operation_id → Audit log
```

### Retry Policy
| Attempt | Delay |
|---------|-------|
| 1 | 1 minute |
| 2 | 5 minutes |
| 3 | 30 minutes |
| 4 | 2 hours |
| 5+ | 8 hours |

After 5 failed attempts → `RequiresAttention` + Admin notification

### Payload Retention
- Configurable: `PayloadRetentionDays` (default 30)
- Background job `PurgeExpiredPayloadsAsync()` nullifies `protected_payload`

---

## 4. Audit Events

### Entity
`identity.audit_logs` (PostgreSQL table)

### Schema
```sql
id                  UUID PK
entity_type         VARCHAR(100)   -- "User", "Category", "Operation", "PaymentLog", etc.
entity_id           UUID
action              VARCHAR(50)    -- "Create", "Update", "Delete", "Assign", "Approve", etc.
changed_fields      JSONB          -- Serialized changed field values
stock_delta_applied INT?           -- For inventory operations
user_id             UUID FK → identity.users
ip_address          VARCHAR(45)
created_at          TIMESTAMP
```

### Indexes
- `idx_audit_logs_entity` (entity_type, entity_id)
- `idx_audit_logs_user` (user_id)
- `idx_audit_logs_created_at` (created_at DESC)

### Section Mapping (AuditEndpoints.cs:139-153)
| Entity Type | Section |
|-------------|---------|
| User | admin |
| Category, Brand, Product, Sku | catalog |
| Merchant, Representative | crm |
| StockBalance, InventoryReceipt | inventory |
| Operation | operations |
| PaymentLog, PaymentSubLog, CashRecord, FinancialAdjustment | payments |
| SupplyShipment | supply |
| Stocktake | stocktakes |
| Notification | notifications |
| ShopifyWebhookEvent, Shopify | integrations |
| Export, Reports | reports |

### Writing Audit Events
- **Interface**: `IAuditLogWriter` → `AuditLogWriter` (not shown but inferred)
- **Called from**: Endpoints via `catalogMutationTransaction`, `OperationCorrectionService.StageAudit()`, ShopifyIntegrationService
- **Format**: `{ EntityType, EntityId, Action, ChangedFields, UserId, ActorType, ActorName, IpAddress, CreatedAt }`

---

## 5. Frontend Synchronization Events

### Location
`frontend/synchronization.js` and `frontend/src/core/synchronization.js` (identical)

### Mechanism
```javascript
export function createCrossTabSynchronizer({ onAuthChanged, onDataChanged }) {
  // BroadcastChannel for same-origin tabs
  const channel = new BroadcastChannel("lensee-sync");
  
  // Storage event for cross-origin/older browsers
  window.addEventListener("storage", onStorage);
}
```

### Event Types
| Type | Source | Payload | Consumers |
|------|--------|---------|-----------|
| `auth-changed` | BroadcastChannel / localStorage (`lensee.auth`) | `{ source, value }` | App.js `renderRoute()` |
| `data-changed` | BroadcastChannel / localStorage (`lensee.data-version`) | `{ source, value, at }` | Workspace refresh triggers |

### Publishing
```javascript
publish(type, detail = {}) {
  const message = { type, ...detail, at: Date.now() };
  channel?.postMessage(message);
  if (type === "data-changed") localStorage.setItem("lensee.data-version", String(message.at));
}
```

### App.js Integration (lines 1718-1727)
```javascript
window.addEventListener("storage", (event) => {
  if (event.key === syncStorageKey && event.newValue) handleExternalSync(JSON.parse(event.newValue));
  if (event.key === authKey) { /* auth sync */ }
});
syncChannel?.addEventListener("message", (event) => handleExternalSync(event.data));
window.dispatchEvent(new CustomEvent(authEventName));  // Custom auth event
window.dispatchEvent(new CustomEvent(mutationEventName, { detail: { path, method } }));  // Mutation event
```

---

## 6. Event Flow Diagrams

### Application Event Flow (Outbox)
```
┌─────────────────┐     ┌──────────────────┐     ┌─────────────────┐
│  Endpoint/      │     │  OutboxAppEvent  │     │  shared.        │
│  Service        │────▶│  Publisher       │────▶│  outbox_messages │
└─────────────────┘     └──────────────────┘     └────────┬────────┘
                                                          │
                                                          ▼
┌─────────────────┐     ┌──────────────────┐     ┌─────────────────┐
│  Handler        │◀───│  OutboxWorker    │◀───│  (polling)      │
│  (Notification) │     │  (BackgroundSvc) │     │                 │
└─────────────────┘     └──────────────────┘     └─────────────────┘
        │                        │
        ▼                        ▼
┌─────────────────┐     ┌──────────────────┐
│  Notifications  │     │  outbox_delivery │
│  DbContext      │     │  _receipts       │
└─────────────────┘     └──────────────────┘
```

### Shopify Event Flow
```
┌─────────────┐    ┌──────────────────────┐    ┌─────────────────────┐
│  Shopify    │───▶│  ShopifyIntegration  │───▶│  operations.        │
│  Webhook    │    │  Service.ReceiveAsync│    │  shopify_webhook_   │
└─────────────┘    └──────────────────────┘    │  events (Queued)    │
                                               └─────────┬───────────┘
                                                         │
                                               ┌─────────▼───────────┐
                                               │  ShopifyWebhook     │
                                               │  Worker / Outbox    │
                                               │  Worker             │
                                               └─────────┬───────────┘
                                                         │
                                               ┌─────────▼───────────┐
                                               │  ClaimDueEvents()   │
                                               │  (FOR UPDATE SKIP   │
                                               │   LOCKED lease)     │
                                               └─────────┬───────────┘
                                                         │
                                               ┌─────────▼───────────┐
                                               │  ProcessQueuedEvent │
                                               │  (decrypt, route,   │
                                               │   execute, audit)   │
                                               └─────────────────────┘
```

### Catalog Event Flow
```
┌─────────────┐    ┌──────────────────────┐    ┌─────────────────────┐
│  Catalog    │───▶│  ICatalogEvent       │───▶│  Transactional      │
│  Endpoint   │    │  Publisher           │    │  CatalogEvent       │
└─────────────┘    └──────────────────────┘    │  Publisher          │
                                               └─────────┬───────────┘
                                                         │
                                               ┌─────────▼───────────┐
                                               │  shared.outbox_     │
                                               │  messages           │
                                               │  (CatalogEventEnv.) │
                                               └─────────┬───────────┘
                                                         │
                                               ┌─────────▼───────────┐
                                               │  OutboxWorker       │
                                               │  → No handlers      │
                                               │    registered yet   │
                                               └─────────────────────┘
```

---

## 7. Event Manipulation Patterns

### 1. **Transactional Outbox Pattern**
- Events written to outbox within same DB transaction as business logic
- Guarantees at-least-once delivery
- Exactly-once processing via `outbox_delivery_receipts` (per handler)

### 2. **Durable Inbox Pattern (Shopify)**
- Raw payload encrypted and stored before processing
- Lease-based claiming prevents duplicate processing
- Status machine: `Queued → Processing → Imported/Cancelled/RequiresAttention/Retrying`

### 3. **Event Sourcing Lite (Audit Logs)**
- Every mutation creates immutable audit record
- JSONB `changed_fields` captures before/after
- Used for compliance, debugging, navigation references

### 4. **Cross-Tab Sync (Frontend)**
- BroadcastChannel for real-time same-origin sync
- localStorage fallback for cross-origin/persistence
- Version-based invalidation (`lensee.data-version`)

### 5. **Correlation & Causation**
- Outbox messages track `CorrelationId` (HTTP trace) and `CausationId`
- Shopify events track `webhook_id` (Shopify correlation) and `event_id`

---

## 8. Key Files Reference

| Component | File Path |
|-----------|-----------|
| AppEvent Infrastructure | `backend/Lensee.Host/Infrastructure/AppEventInfrastructure.cs` |
| AppEvent Abstractions | `backend/Lensee.SharedKernel/Abstractions/AppEvents.cs` |
| Catalog Events | `backend/Lensee.Modules.Catalog/Domain/Events/CatalogEvents.cs` |
| Catalog Publisher | `backend/Lensee.Modules.Catalog/Services/ICatalogEventPublisher.cs` |
| Shopify Integration | `backend/Lensee.Host/Infrastructure/ShopifyIntegrationService.cs` |
| Shopify Worker | `backend/Lensee.Host/Infrastructure/ShopifyWebhookWorker.cs` |
| Shopify Entity | `backend/Lensee.Modules.Operations/Domain/Entities/ShopifyWebhookEvent.cs` |
| Outbox Entity | `backend/Lensee.SharedKernel/Entities/OutboxMessage.cs` |
| Outbox Migration | `backend/Lensee.SharedKernel/Migrations/20260820090000_AddOutboxMessages.cs` |
| Shopify Migrations | `backend/Lensee.Modules.Operations/Migrations/20260726225251_AddShopifyIntegration.cs` |
| Shopify Durable Inbox | `backend/Lensee.Modules.Operations/Migrations/20260729154000_AddShopifyDurableInbox.cs` |
| Audit Endpoints | `backend/Lensee.Host/Endpoints/AuditEndpoints.cs` |
| Audit Presentation | `backend/Lensee.Host/Infrastructure/AuditEventPresentation.cs` |
| Operation Correction | `backend/Lensee.Host/Services/OperationCorrectionService.cs` |
| Frontend Sync | `frontend/synchronization.js` |
| App.js Events | `frontend/app.js` (lines 1718-1727) |

---

## 9. Observability & Monitoring

### Metrics (via `LenseeTelemetry`)
- `OutboxReplays` - counter by event_type
- `OutboxDeadLetters` - counter by event_type
- `CorrectionRequests` - counter by operation
- `CorrectionFailures` - counter by reason

### Health Checks
- Outbox worker lag (messages pending > threshold)
- Shopify webhook processing lag
- Dead letter queue size

---

## 10. Summary of Event Paths

| Event Source | Transport | Storage | Consumers | Retry |
|--------------|-----------|---------|-----------|-------|
| Payment workflow | In-process → Outbox | `shared.outbox_messages` | Notification handler | Exponential (10 max) |
| Operation correction | In-process → Outbox | `shared.outbox_messages` | Notification handler | Exponential (10 max) |
| Catalog changes | In-process → Outbox | `shared.outbox_messages` | *None registered* | Exponential (10 max) |
| Shopify webhooks | HTTP → Inbox | `operations.shopify_webhook_events` | ShopifyIntegrationService | Fixed schedule (5 max → RequiresAttention) |
| Audit logs | In-process → DB | `identity.audit_logs` | Audit endpoints, Navigation | N/A (immutable) |
| Frontend sync | BroadcastChannel/localStorage | Memory + localStorage | App.js tabs | N/A (best-effort) |

---

*Report generated from codebase analysis on 2026-09-04*