# Shopify integration: operator configuration

This repository does not create Shopify apps, webhook subscriptions, HMAC values, passwords, or secrets. Complete the following steps only after the ERP deployment is ready.

## 1. Prepare ERP configuration

Supply these values through the deployment platform's secret/configuration facility. Do not commit them to `.env`, source control, logs, Postman collections, or support tickets.

| Placeholder | Operator supplies | Notes |
|---|---|---|
| `SHOPIFY_ENABLED` | `true` when ready | Leave `false` until mappings and networking are verified. |
| `SHOPIFY_WEBHOOK_SECRET` | Shopify-provided secret | Production secret injection only. |
| `SHOPIFY_STORE_DOMAIN` | `<your-store>.myshopify.com` | One store is accepted in v1. |
| `SHOPIFY_ONLINE_LOCATION_ID` | Existing Lensee Online-location GUID | Must be an active location whose type is `Online`. |
| `SHOPIFY_COD_GATEWAY_NAME_1` | Exact Shopify gateway label | Example placeholder: `Cash on Delivery`. |
| `HOSTING_TRUSTED_PROXY_NETWORK` | Caddy container-network CIDR | Obtain this from the deployment platform/Docker network; never use a broad public CIDR. |

The receiver also uses `SHOPIFY_WEBHOOK_MAX_BODY_BYTES=262144` and `SHOPIFY_PAYLOAD_RETENTION_DAYS=30` unless the operator deliberately changes them.

## 2. Deploy and verify ERP first

1. Deploy the migrations and host with `SHOPIFY_ENABLED=false`.
2. Confirm `/ready` succeeds and the Integration Queue is visible to Admin, ERP Admin, and Warehouse Clerk.
3. Add Shopify variant-to-Lensee SKU mappings in **Online intake**.
4. Inject the values above through the platform secret/configuration service, then redeploy.
5. Confirm the Online intake receiver says **Ready**. It never exposes the secret.

## 3. Configure Shopify manually

The operator creates/configures the Shopify webhook outside this repository. Use the public HTTPS ERP endpoint:

```text
https://<ERP_PUBLIC_DOMAIN>/api/v1/integrations/shopify/webhooks
```

Subscribe only to:

```text
orders/create
orders/cancelled
refunds/create
```

After Shopify sends a test delivery, verify an event appears in **Online intake**. Shopify must receive a 2xx response; the ERP worker processes the event afterwards. Do not send secrets, raw payloads, or HMAC headers through chat or email.

## 4. Normal operation

- Warehouse Clerks repair variant mappings and resolve/retry exceptions.
- Admin and ERP Admin oversee configuration health and all integration events.
- Shopify can create only an unallocated Draft. Staff allocate batches, fulfill, and complete the sale through normal ERP workflows.
- Raw encrypted payloads are purged after 30 days. Event metadata and resolution history remain available.
