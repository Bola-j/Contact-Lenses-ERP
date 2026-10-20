# Shopify production setup

This is the production-only setup. You do not need a separate Shopify development store or a Shopify DEV environment. The ERP endpoint receives Shopify events directly over the production HTTPS domain and saves them to the durable queue before acknowledging them.

## Before deployment

The production deployment already runs the database migrator. Keep Shopify disabled until migrations, the public HTTPS endpoint, Shopify app configuration and ERP values are ready. Do not reset or backfill the production database.

The webhook URL is:

```text
https://<ERP_PUBLIC_DOMAIN>/api/v1/integrations/shopify/webhooks
```

Use the permanent `*.myshopify.com` domain of the real store, not its branded storefront URL. Set `SHOPIFY_ONLINE_LOCATION_ID` to the GUID of the active ERP location used for Shopify fulfillment. The location may be an `Online` location for the legacy split workflow, or the dedicated physical `SubWarehouse` named Online. When a `SubWarehouse` is configured, Shopify orders must be allocated from that exact location and its warehouse clerks own the Shopify order queue. Leave COD configuration blank.

## ERP values to enter

Start from `.env.production.example`. Add these non-secret values to the production ERP environment:

| Variable | Value |
|---|---|
| `SHOPIFY_STORE_DOMAIN` | The store's permanent `name.myshopify.com` domain. |
| `SHOPIFY_ONLINE_LOCATION_ID` | GUID of the active ERP Online location, or the designated physical Online `SubWarehouse`. |
| `SHOPIFY_ACTIVATION_AT_UTC` | Optional ISO-8601 UTC cutover. Leave blank to record the first enabled startup once. Orders before the saved cutoff are not imported automatically. |
| `SHOPIFY_ENABLED` | Keep `false` until Shopify subscriptions and secrets are in place; then set `true`. |
| `SHOPIFY_COD_GATEWAY_NAME_1` | Keep blank unless COD is deliberately enabled and its exact Shopify gateway label is approved. |

Add secrets directly to the production secret facility; do not put values in Git, tickets or chat:

| Secret | Runtime setting | Use |
|---|---|---|
| `SHOPIFY_WEBHOOK_SECRET` | `Shopify__WebhookSecret` | Required. The app client secret for the ERP-owned Shopify app that signs these deliveries. |
| `SHOPIFY_ADMIN_ACCESS_TOKEN` | `Shopify__AdminAccessToken` | Optional recovery. Needed only when `SHOPIFY_RECONCILIATION_ENABLED=true`. |

For automatic recovery of missed deliveries, set `SHOPIFY_RECONCILIATION_ENABLED=true` and `SHOPIFY_RECONCILIATION_INTERVAL_MINUTES=15`, and provide the Admin API token with `read_orders`. Shopify limits normal order access to the last 60 days; recovering older orders also requires Shopify approval for `read_all_orders`. If you do not set up this optional token, signed webhook intake still works, but periodic API reconciliation is disabled.

The production app refuses to start with Shopify enabled unless signed webhooks, a store domain and an active Online location or designated Online `SubWarehouse` are configured. Production does not accept the legacy path-secret bridge. Enabling reconciliation without its Admin API token also fails fast.

## Shopify app setup (one time)

Use the existing ERP-owned app installed on the production store. Do not create a new app or change the unrelated Shopify connector. The app needs the `read_orders` scope for order and refund topics. Use Shopify's app-specific webhook subscriptions in that app's `shopify.app.toml`; merge this configuration into the existing file, preserving its other settings:

```toml
[access_scopes]
scopes = "read_orders"

[webhooks]
api_version = "2026-07"

[[webhooks.subscriptions]]
topics = ["orders/create", "orders/updated", "orders/paid", "orders/cancelled", "refunds/create"]
uri = "https://<ERP_PUBLIC_DOMAIN>/api/v1/integrations/shopify/webhooks"
```

If the app already declares scopes or subscriptions, merge rather than replacing them. Remove any duplicate shop-specific subscriptions for these same topics before switching to app-specific subscriptions. Release the updated app version and confirm the five topics point to the URL above. Do not configure a tunnel or the DEV receiver for production.

Shopify recommends app-specific subscriptions in `shopify.app.toml`; app-specific subscriptions are deployed with the Shopify app and share its configured API version. See [Manage webhook subscriptions](https://shopify.dev/docs/apps/build/webhooks/subscribe), [webhook signature verification](https://shopify.dev/docs/api/webhooks/2026-07), and [Shopify API access scopes](https://shopify.dev/docs/api/usage/access-scopes).

## Turn intake on

1. Deploy the ERP revision normally with `SHOPIFY_ENABLED=false`; the deployment migrator applies additive migrations before the application starts.
2. Set the production store domain and active Online location ID (use the physical Online `SubWarehouse` when that is where online orders are fulfilled). Add the webhook signing secret to production secret storage. Choose a UTC cutoff or leave it blank for first-start activation.
3. Add and release the webhook configuration in the existing ERP-owned Shopify app. Confirm the five production subscriptions use the HTTPS receiver URL.
4. Set `SHOPIFY_ENABLED=true` and redeploy/restart the ERP application. If using recovery, also set its flag and secret before restart.
5. In the ERP, open Online intake and confirm it reports configured. Monitor Shopify events/queue for durable receipt and then the imported draft. A webhook 2xx means the event was saved; it does not mean import, payment verification or Finance reconciliation has finished.

No production order, checkout, payment method or COD setting is changed by these steps. Shopify-reported paid status, provider verification and Finance reconciliation remain separate. A Shopify-paid order may proceed through normal ERP fulfillment while provider verification is pending; dispatch never creates payment or collection records.

## Routine operation

- ERP SKUs must already be copied into Shopify variants. The integration matches unique active SKU codes, remembers store/variant links, and queues ambiguous or incompatible matches for review.
- Orders import as unallocated drafts. With the physical Online `SubWarehouse` configured, ERP staff allocate each order from that warehouse and choose its batch and expiry; the API enforces the designated source. A legacy logical `Online` location continues to allow active SubWarehouse selection. Shopify line quantities mean pieces for `SinglePiece`/`Both` and sealed packs for `SealedPackOnly`.
- Watch the ERP Shopify queue for waiting, retrying and requires-attention events. Fix the cause and use Retry; do not recreate a delayed order.
- Keep unpaid orders blocked from dispatch. Shopify cancellation before dispatch releases reservations once; later cancellation and all refund notices require the existing governed review workflows.
- Do not place fake paid/refund events or change webhook secrets to test production. Use the ERP queue/readiness screens and actual operational records.

Paymob checkout and real payment-provider certification are separate work and are not enabled by this Shopify intake setup.
