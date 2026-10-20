namespace Lensee.Host.Infrastructure;

/// <summary>Read-only Admin API queries; all nested connections are paged, never silently truncated.</summary>
public static class ShopifyRecoveryQueries
{
    private const string Money = "fragment Money on MoneyBag { shop_money: shopMoney { amount currency_code: currencyCode } presentment_money: presentmentMoney { amount currency_code: currencyCode } }";
    private const string Line = "id sku title variantTitle quantity currentQuantity variant { id } originalUnitPriceSet { ...Money } originalTotalSet { ...Money } discountedTotalSet(withCodeDiscounts: true) { ...Money } discountAllocations { allocatedAmountSet { ...Money } } taxLines { title rate priceSet { ...Money } } customAttributes { key value }";
    private const string ShippingLine = "id title code originalPriceSet { ...Money } discountedPriceSet { ...Money } discountAllocations { allocatedAmountSet { ...Money } } taxLines { title rate priceSet { ...Money } }";
    private const string Address = "firstName lastName company address1 address2 city province provinceCode zip country countryCode: countryCodeV2 phone";
    private const string Amounts = "subtotalPriceSet { ...Money } totalDiscountsSet { ...Money } totalTaxSet { ...Money } totalPriceSet { ...Money } totalShippingPriceSet { ...Money } currentSubtotalPriceSet { ...Money } currentTotalDiscountsSet { ...Money } currentTotalTaxSet { ...Money } currentTotalPriceSet { ...Money } currentShippingPriceSet { ...Money } totalCashRoundingAdjustment { paymentSet { ...Money } refundSet { ...Money } }";
    public static readonly string Orders = "query RecoverOrders($query: String!, $after: String) { orders(first: 5, after: $after, query: $query, sortKey: UPDATED_AT) { nodes { id legacyResourceId name email phone createdAt updatedAt cancelledAt edited displayFinancialStatus currencyCode presentmentCurrencyCode test taxesIncluded paymentGatewayNames " + Amounts + " billingAddress { " + Address + " } shippingAddress { " + Address + " } lineItems(first: 10) { nodes { " + Line + " } pageInfo { hasNextPage endCursor } } shippingLines(first: 5) { nodes { " + ShippingLine + " } pageInfo { hasNextPage endCursor } } refunds { legacyResourceId createdAt totalRefundedSet { ...Money } } } pageInfo { hasNextPage endCursor } } } " + Money;
    public static readonly string Lines = "query RecoverLines($id: ID!, $after: String) { order(id: $id) { lineItems(first: 25, after: $after) { nodes { " + Line + " } pageInfo { hasNextPage endCursor } } } } " + Money;
    public static readonly string Shipping = "query RecoverShipping($id: ID!, $after: String) { order(id: $id) { shippingLines(first: 25, after: $after) { nodes { " + ShippingLine + " } pageInfo { hasNextPage endCursor } } } } " + Money;
}
