using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lensee.Host.Infrastructure;

internal sealed record ShopifyCommercialLine(
    string? LineItemId,
    string? VariantId,
    string? Sku,
    int Quantity,
    decimal OriginalLineTotal,
    decimal Discount,
    decimal NetLineTotal,
    decimal Tax,
    string Currency,
    string PresentmentCurrency,
    string SourceJson);

internal sealed record ShopifyOrderMoneySnapshot(
    string Currency,
    string PresentmentCurrency,
    decimal Subtotal,
    decimal Discounts,
    decimal Shipping,
    decimal Tax,
    decimal RoundingAdjustment,
    decimal Total,
    bool TaxesIncluded,
    IReadOnlyList<ShopifyCommercialLine> Lines,
    string SourceJson,
    string Hash);

internal static class ShopifyOrderMoneySnapshotBuilder
{
    public static bool TryBuild(JsonElement order, out ShopifyOrderMoneySnapshot? snapshot, out string error)
    {
        snapshot = null;
        error = string.Empty;
        if (ReadBool(order, "edited") == true)
        {
            error = "Shopify marks this order edited but the payload does not provide enough source facts to safely reconstruct its commercial snapshot.";
            return false;
        }

        var currency = ReadString(order, "currency")?.ToUpperInvariant();
        var presentmentCurrency = (ReadString(order, "presentment_currency") ?? currency)?.ToUpperInvariant();
        if (currency != "EGP")
        {
            error = string.IsNullOrWhiteSpace(currency)
                ? "Shopify order currency is missing; only EGP orders can be allocated operationally."
                : $"Shopify order currency '{currency}' is unsupported for operational allocation; EGP is required.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(presentmentCurrency))
        {
            error = "Shopify presentment currency is missing from the commercial snapshot.";
            return false;
        }
        if (!ValidateMoneyBags(order, currency, presentmentCurrency, out error)) return false;

        if (!TryArray(order, "line_items", out var sourceLines) || sourceLines.Count == 0)
        {
            error = "Shopify order has no line-item money snapshot.";
            return false;
        }

        var lines = new List<ShopifyCommercialLine>();
        foreach (var item in sourceLines)
        {
            var quantity = ReadInt(item, "current_quantity") ?? ReadInt(item, "quantity") ?? 0;
            var id = ReadString(item, "id");
            var variantId = ReadString(item, "variant_id");
            var sku = ReadString(item, "sku");
            if (quantity <= 0 || !TryMoney(item, "price", "price_set", currency, out var unitPrice, out var lineCurrency))
            {
                error = $"Shopify line {id ?? "(unknown)"} is missing a valid unit price, currency, or positive quantity.";
                return false;
            }
            if (!string.Equals(lineCurrency, currency, StringComparison.OrdinalIgnoreCase))
            {
                error = $"Shopify line {id ?? "(unknown)"} currency does not match order currency.";
                return false;
            }

            var gross = RoundMoney(unitPrice * quantity);
            if (TryMoney(item, "original_total", "original_total_set", currency, out var originalTotal, out _))
            {
                if (!MoneyEqual(originalTotal, gross))
                {
                    error = $"Shopify line {id ?? "(unknown)"} original total does not match its unit price and quantity.";
                    return false;
                }
                gross = originalTotal;
            }

            var allocatedDiscount = SumDiscountAllocations(item);
            var explicitDiscount = ReadDecimal(item, "total_discount");
            var hasExplicitDiscount = TryReadDecimal(item, "total_discount", out explicitDiscount);
            if (!hasExplicitDiscount) explicitDiscount = allocatedDiscount.GetValueOrDefault();
            if (allocatedDiscount.HasValue && !MoneyEqual(allocatedDiscount.Value, explicitDiscount))
            {
                error = $"Shopify line {id ?? "(unknown)"} discount allocations do not match its total discount.";
                return false;
            }

            var hasDiscountedTotal = TryMoney(item, "discounted_total", "discounted_total_set", currency, out var discountedTotal, out _);
            var net = hasDiscountedTotal ? discountedTotal : gross - explicitDiscount;
            if (net < 0 || !MoneyEqual(gross - explicitDiscount, net))
            {
                error = $"Shopify line {id ?? "(unknown)"} discounted total is inconsistent with its discounts.";
                return false;
            }
            var lineTax = SumTaxLines(item);
            lines.Add(new ShopifyCommercialLine(id, variantId, sku, quantity, gross, explicitDiscount, RoundMoney(net), lineTax,
                currency, presentmentCurrency, item.GetRawText()));
        }

        var computedSubtotal = RoundMoney(lines.Sum(line => line.NetLineTotal));
        var subtotal = ReadFirstDecimal(order, ["current_subtotal_price", "subtotal_price", "total_line_items_price"]);
        if (!subtotal.HasValue || !MoneyEqual(subtotal.Value, computedSubtotal))
        {
            error = "Shopify order subtotal does not match the discounted line-item totals.";
            return false;
        }
        var discounts = ReadFirstDecimal(order, ["current_total_discounts", "total_discounts"]) ?? RoundMoney(lines.Sum(line => line.Discount));
        if (!MoneyEqual(discounts, lines.Sum(line => line.Discount)))
        {
            error = "Shopify order discounts do not match the line-item discount allocations.";
            return false;
        }

        var shippingFromLines = ShippingLineTotal(order, currency, out var shippingLinesValid);
        if (!shippingLinesValid)
        {
            error = "Shopify shipping lines contain an invalid amount or currency.";
            return false;
        }
        var shipping = ReadMoneyBagAmount(order, "current_shipping_price_set", currency)
            ?? ReadMoneyBagAmount(order, "total_shipping_price_set", currency)
            ?? ReadFirstDecimal(order, ["shipping_price", "total_shipping_price"])
            ?? shippingFromLines;
        if (!shipping.HasValue)
        {
            error = "Shopify order shipping total is missing; the payload must include an aggregate or explicit shipping lines.";
            return false;
        }
        if (shippingFromLines.HasValue && !MoneyEqual(shipping.Value, shippingFromLines.Value))
        {
            error = "Shopify shipping total does not match the shipping-line amounts.";
            return false;
        }

        var tax = ReadMoneyBagAmount(order, "current_total_tax_set", currency)
            ?? ReadMoneyBagAmount(order, "total_tax_set", currency)
            ?? ReadFirstDecimal(order, ["current_total_tax", "total_tax"]);
        if (!tax.HasValue)
        {
            var orderTaxLines = SumTaxLines(order);
            var lineTaxTotal = lines.Sum(line => line.Tax);
            if (!TryArray(order, "tax_lines", out _) && !lines.Any(line => line.Tax != 0))
            {
                error = "Shopify order tax total is missing; the payload must include an aggregate or explicit tax lines.";
                return false;
            }
            tax = orderTaxLines == 0 ? lineTaxTotal : orderTaxLines;
        }
        var allTaxLines = lineItemsAndShippingTax(order);
        if (allTaxLines.HasValue && !MoneyEqual(tax.Value, allTaxLines.Value))
        {
            error = "Shopify order tax total does not match the item and shipping tax lines.";
            return false;
        }
        var taxesIncluded = ReadBool(order, "taxes_included") ?? false;
        var rounding = ReadMoneyBagAmount(order, "total_cash_rounding_payment_adjustment_set", currency).GetValueOrDefault()
            - ReadMoneyBagAmount(order, "total_cash_rounding_refund_adjustment_set", currency).GetValueOrDefault();
        var total = ReadFirstDecimal(order, ["current_total_price", "total_price"]);
        if (!total.HasValue)
        {
            error = "Shopify order total is missing from the commercial snapshot.";
            return false;
        }
        var expectedTotal = RoundMoney(subtotal.Value + shipping.Value + (taxesIncluded ? 0m : tax.Value) + rounding);
        if (!MoneyEqual(total.Value, expectedTotal))
        {
            error = $"Shopify order total {total.Value:0.00} does not reconcile to subtotal, shipping, tax, and rounding ({expectedTotal:0.00}).";
            return false;
        }

        var snapshotSource = new
        {
            currency,
            presentmentCurrency,
            subtotal = subtotal.Value,
            discounts,
            shipping = shipping.Value,
            tax = tax.Value,
            rounding,
            total = total.Value,
            taxesIncluded,
            moneyBags = ExtractMoneyBags(order),
            lines = lines.Select(line => new { line.LineItemId, line.VariantId, line.Sku, line.Quantity, line.OriginalLineTotal, line.Discount, line.NetLineTotal, line.Tax, line.SourceJson })
        };
        var json = JsonSerializer.Serialize(snapshotSource);
        var fingerprint = new
        {
            Version = 1,
            Currency = currency,
            PresentmentCurrency = presentmentCurrency,
            Subtotal = CanonicalMoney(subtotal.Value),
            Discounts = CanonicalMoney(discounts),
            Shipping = CanonicalMoney(shipping.Value),
            Tax = CanonicalMoney(tax.Value),
            Rounding = CanonicalMoney(rounding),
            Total = CanonicalMoney(total.Value),
            TaxesIncluded = taxesIncluded,
            HeaderPresentment = CanonicalHeaderPresentment(order, currency, presentmentCurrency),
            Lines = lines.OrderBy(line => CanonicalIdentity(line.LineItemId), StringComparer.Ordinal)
                .Select(CanonicalLine).ToArray(),
            ShippingLines = CanonicalShippingLines(order, currency, presentmentCurrency),
            Email = CanonicalText(ReadString(order, "email"), lower: true),
            Phone = CanonicalPhone(ReadString(order, "phone")),
            ShippingAddress = CanonicalAddress(order, "shipping_address"),
            BillingAddress = CanonicalAddress(order, "billing_address")
        };
        var canonicalJson = JsonSerializer.Serialize(fingerprint);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson))).ToLowerInvariant();
        snapshot = new ShopifyOrderMoneySnapshot(currency, presentmentCurrency, subtotal.Value, discounts, shipping.Value, tax.Value,
            rounding, total.Value, taxesIncluded, lines, json, hash);
        return true;
    }

    private static object CanonicalLine(ShopifyCommercialLine line)
    {
        using var document = JsonDocument.Parse(line.SourceJson);
        var source = document.RootElement;
        return new
        {
            Id = CanonicalIdentity(line.LineItemId),
            VariantId = CanonicalIdentity(line.VariantId),
            Sku = NormalizeSku(line.Sku),
            line.Quantity,
            Original = CanonicalMoney(line.OriginalLineTotal),
            Discount = CanonicalMoney(line.Discount),
            Net = CanonicalMoney(line.NetLineTotal),
            Tax = CanonicalMoney(line.Tax),
            UnitPresentment = CanonicalPresentmentAmount(source, "price_set", line.Currency, line.PresentmentCurrency,
                line.OriginalLineTotal / line.Quantity),
            OriginalPresentment = CanonicalPresentmentAmount(source, "original_total_set", line.Currency, line.PresentmentCurrency, line.OriginalLineTotal),
            NetPresentment = CanonicalPresentmentAmount(source, "discounted_total_set", line.Currency, line.PresentmentCurrency, line.NetLineTotal),
            DiscountAllocations = CanonicalAllocations(source, "discount_allocations", "amount", "amount_set", line.Currency, line.PresentmentCurrency),
            TaxLines = CanonicalTaxLines(source, line.Currency, line.PresentmentCurrency),
            Title = CanonicalText(ReadString(source, "title")),
            VariantTitle = CanonicalText(ReadString(source, "variant_title")),
            Properties = CanonicalProperties(source)
        };
    }

    private static object[] CanonicalShippingLines(JsonElement order, string currency, string presentmentCurrency)
    {
        if (!TryArray(order, "shipping_lines", out var rows)) return [];
        return rows.Select(line => new
        {
            Id = CanonicalIdentity(ReadString(line, "id")),
            Title = CanonicalText(ReadString(line, "title")),
            Code = CanonicalText(ReadString(line, "code")),
            Amount = CanonicalMoney(ReadMoneyBagAmount(line, "discounted_price_set", currency)
                    ?? ReadFirstDecimal(line, ["discounted_price", "price"])
                    ?? ReadMoneyBagAmount(line, "price_set", currency) ?? 0m),
            PresentmentAmount = CanonicalPresentmentAmount(line, "discounted_price_set", currency, presentmentCurrency,
                    ReadMoneyBagAmount(line, "discounted_price_set", currency) ?? ReadFirstDecimal(line, ["discounted_price", "price"])
                    ?? ReadMoneyBagAmount(line, "price_set", currency))
                    ?? CanonicalPresentmentAmount(line, "price_set", currency, presentmentCurrency,
                        ReadMoneyBagAmount(line, "price_set", currency)),
            DiscountAllocations = CanonicalAllocations(line, "discount_allocations", "amount", "amount_set", currency, presentmentCurrency),
            TaxLines = CanonicalTaxLines(line, currency, presentmentCurrency)
        })
            .OrderBy(value => value.Id, StringComparer.Ordinal)
            .ThenBy(value => value.Title, StringComparer.Ordinal)
            .Cast<object>().ToArray();
    }

    private static object CanonicalHeaderPresentment(JsonElement order, string currency, string presentmentCurrency) => new
    {
        Subtotal = CanonicalHeaderAmount(order, ["current_subtotal_price_set", "subtotal_price_set"], ["current_subtotal_price", "subtotal_price"], currency, presentmentCurrency),
        Discounts = CanonicalHeaderAmount(order, ["current_total_discounts_set", "total_discounts_set"], ["current_total_discounts", "total_discounts"], currency, presentmentCurrency),
        Shipping = CanonicalHeaderAmount(order, ["current_shipping_price_set", "total_shipping_price_set"], ["current_shipping_price", "total_shipping_price"], currency, presentmentCurrency),
        Tax = CanonicalHeaderAmount(order, ["current_total_tax_set", "total_tax_set"], ["current_total_tax", "total_tax"], currency, presentmentCurrency),
        Total = CanonicalHeaderAmount(order, ["current_total_price_set", "total_price_set"], ["current_total_price", "total_price"], currency, presentmentCurrency),
        PaymentRounding = CanonicalHeaderAmount(order, ["total_cash_rounding_payment_adjustment_set"], [], currency, presentmentCurrency)
            ?? (currency == presentmentCurrency ? CanonicalMoney(0m) : null),
        RefundRounding = CanonicalHeaderAmount(order, ["total_cash_rounding_refund_adjustment_set"], [], currency, presentmentCurrency)
            ?? (currency == presentmentCurrency ? CanonicalMoney(0m) : null)
    };

    private static string? CanonicalHeaderAmount(JsonElement order, string[] bags, string[] scalars, string currency, string presentmentCurrency)
    {
        foreach (var key in bags)
        {
            if (!order.TryGetProperty(key, out _)) continue;
            var value = CanonicalPresentmentAmount(order, key, currency, presentmentCurrency);
            if (value is not null) return value;
        }
        if (currency == presentmentCurrency)
            foreach (var key in scalars)
                if (TryReadDecimal(order, key, out var amount)) return CanonicalMoney(amount);
        return null;
    }

    private static string? CanonicalPresentmentAmount(JsonElement source, string bagName, string currency, string presentmentCurrency,
        decimal? fallbackShopAmount = null)
    {
        var shopAmount = 0m;
        var bag = default(JsonElement);
        var hasShopAmount = source.TryGetProperty(bagName, out bag) && bag.ValueKind == JsonValueKind.Object &&
            bag.TryGetProperty("shop_money", out var shopMoney) && TryReadDecimal(shopMoney, "amount", out shopAmount);
        if (hasShopAmount && bag.TryGetProperty("presentment_money", out var presentmentMoney) && TryReadDecimal(presentmentMoney, "amount", out var amount) &&
            string.Equals(ReadString(presentmentMoney, "currency_code"), presentmentCurrency, StringComparison.OrdinalIgnoreCase))
            return CanonicalMoney(amount);
        if (currency != presentmentCurrency) return null;
        return CanonicalMoney(hasShopAmount ? shopAmount : fallbackShopAmount ?? 0m);
    }

    private static object[] CanonicalAllocations(JsonElement parent, string arrayName, string amountField, string bagField,
        string currency, string presentmentCurrency)
    {
        if (!TryArray(parent, arrayName, out var rows)) return [];
        return rows.Select(value => new
        {
            Amount = CanonicalMoney(ReadMoneyBagAmount(value, bagField, null) ?? ReadDecimal(value, amountField)),
            PresentmentAmount = CanonicalPresentmentAmount(value, bagField, currency, presentmentCurrency,
                    ReadMoneyBagAmount(value, bagField, currency) ?? ReadDecimal(value, amountField))
        })
            .OrderBy(value => value.Amount, StringComparer.Ordinal)
            .ThenBy(value => value.PresentmentAmount, StringComparer.Ordinal)
            .Cast<object>().ToArray();
    }

    private static object[] CanonicalProperties(JsonElement line)
    {
        if (!TryArray(line, "properties", out var rows)) return [];
        return rows.Select(value => new
        {
            Name = CanonicalText(ReadString(value, "name") ?? ReadString(value, "key")),
            Value = CanonicalText(ReadString(value, "value"))
        })
            .OrderBy(value => value.Name, StringComparer.Ordinal)
            .ThenBy(value => value.Value, StringComparer.Ordinal)
            .Cast<object>().ToArray();
    }

    private static object[] CanonicalTaxLines(JsonElement parent, string currency, string presentmentCurrency)
    {
        if (!TryArray(parent, "tax_lines", out var rows)) return [];
        return rows.Select(value => new
        {
            Title = CanonicalText(ReadString(value, "title")),
            Amount = CanonicalMoney(ReadMoneyBagAmount(value, "price_set", currency) ?? ReadDecimal(value, "price")),
            PresentmentAmount = CanonicalPresentmentAmount(value, "price_set", currency, presentmentCurrency,
                    ReadMoneyBagAmount(value, "price_set", currency) ?? ReadDecimal(value, "price"))
        })
            .OrderBy(value => value.Title, StringComparer.Ordinal)
            .ThenBy(value => value.Amount, StringComparer.Ordinal)
            .Cast<object>().ToArray();
    }

    private static object? CanonicalAddress(JsonElement order, string property)
    {
        if (!order.TryGetProperty(property, out var address) || address.ValueKind != JsonValueKind.Object) return null;
        return new
        {
            FirstName = CanonicalText(ReadString(address, "first_name")),
            LastName = CanonicalText(ReadString(address, "last_name")),
            Company = CanonicalText(ReadString(address, "company")),
            Address1 = CanonicalText(ReadString(address, "address1")),
            Address2 = CanonicalText(ReadString(address, "address2")),
            City = CanonicalText(ReadString(address, "city")),
            Province = CanonicalText(ReadString(address, "province")),
            ProvinceCode = CanonicalText(ReadString(address, "province_code")),
            Country = CanonicalText(ReadString(address, "country")),
            CountryCode = CanonicalText(ReadString(address, "country_code")),
            Zip = CanonicalText(ReadString(address, "zip")),
            Phone = CanonicalPhone(ReadString(address, "phone"))
        };
    }

    private static string? CanonicalPhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = new string(value.Where(character => char.IsDigit(character) || character == '+').ToArray());
        return normalized.Length == 0 ? null : normalized;
    }

    private static string? CanonicalIdentity(string? value) => CanonicalText(value, lower: true);

    private static string? NormalizeSku(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : string.Join(' ', value.Normalize(NormalizationForm.FormC).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private static string? CanonicalText(string? value, bool lower = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = string.Join(' ', value.Normalize(NormalizationForm.FormC).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return lower ? normalized.ToLowerInvariant() : normalized.ToUpperInvariant();
    }

    private static string CanonicalMoney(decimal value) => RoundMoney(value).ToString("G29", CultureInfo.InvariantCulture);

    private static bool ValidateMoneyBags(JsonElement order, string currency, string presentmentCurrency, out string error)
    {
        error = string.Empty;
        var headers = new[] { "subtotal_price_set", "current_subtotal_price_set", "total_discounts_set", "current_total_discounts_set", "total_shipping_price_set", "current_shipping_price_set", "total_tax_set", "current_total_tax_set", "total_price_set", "current_total_price_set", "total_cash_rounding_payment_adjustment_set", "total_cash_rounding_refund_adjustment_set" };
        foreach (var key in headers)
            if (order.TryGetProperty(key, out var bag) && !MoneyBagCurrenciesMatch(bag, currency, presentmentCurrency))
            { error = $"Shopify money bag '{key}' is malformed or has a currency that does not match the order snapshot."; return false; }
        var scalarBagPairs = new[]
        {
            ("current_subtotal_price", "current_subtotal_price_set"), ("subtotal_price", "subtotal_price_set"),
            ("current_total_discounts", "current_total_discounts_set"), ("total_discounts", "total_discounts_set"),
            ("current_shipping_price", "current_shipping_price_set"), ("total_shipping_price", "total_shipping_price_set"),
            ("current_total_tax", "current_total_tax_set"), ("total_tax", "total_tax_set"),
            ("current_total_price", "current_total_price_set"), ("total_price", "total_price_set")
        };
        foreach (var (scalarName, bagName) in scalarBagPairs)
        {
            if (TryReadDecimal(order, scalarName, out var scalar) && ReadMoneyBagAmount(order, bagName, currency) is { } bagAmount && !MoneyEqual(scalar, bagAmount))
            { error = $"Shopify scalar total '{scalarName}' does not match money bag '{bagName}'."; return false; }
        }
        if (TryArray(order, "line_items", out var lineItems))
            foreach (var line in lineItems)
            {
                foreach (var key in new[] { "price_set", "original_total_set", "discounted_total_set" })
                    if (line.TryGetProperty(key, out var bag) && !MoneyBagCurrenciesMatch(bag, currency, presentmentCurrency))
                    { error = $"Shopify line money bag '{key}' is malformed or has a currency that does not match the order snapshot."; return false; }
                ValidateNestedMoneyBags(line, "discount_allocations", "amount_set", currency, presentmentCurrency, ref error);
                ValidateNestedMoneyBags(line, "tax_lines", "price_set", currency, presentmentCurrency, ref error);
                if (!string.IsNullOrEmpty(error)) return false;
            }
        if (TryArray(order, "shipping_lines", out var shippingLines))
            foreach (var line in shippingLines)
            {
                foreach (var key in new[] { "price_set", "discounted_price_set" })
                    if (line.TryGetProperty(key, out var bag) && !MoneyBagCurrenciesMatch(bag, currency, presentmentCurrency))
                    { error = $"Shopify shipping money bag '{key}' is malformed or has a currency that does not match the order snapshot."; return false; }
                ValidateNestedMoneyBags(line, "discount_allocations", "amount_set", currency, presentmentCurrency, ref error);
                ValidateNestedMoneyBags(line, "tax_lines", "price_set", currency, presentmentCurrency, ref error);
                if (!string.IsNullOrEmpty(error)) return false;
            }
        return true;
    }

    private static void ValidateNestedMoneyBags(JsonElement parent, string arrayName, string bagName, string currency, string presentmentCurrency, ref string error)
    {
        if (!TryArray(parent, arrayName, out var rows)) return;
        foreach (var row in rows)
            if (row.TryGetProperty(bagName, out var bag) && !MoneyBagCurrenciesMatch(bag, currency, presentmentCurrency))
            { error = $"Shopify nested money bag '{bagName}' is malformed or has a currency that does not match the order snapshot."; return; }
    }

    private static bool MoneyBagCurrenciesMatch(JsonElement bag, string currency, string presentmentCurrency)
    {
        if (bag.ValueKind != JsonValueKind.Object || !bag.TryGetProperty("shop_money", out var shopMoney) || shopMoney.ValueKind != JsonValueKind.Object ||
            !TryReadDecimal(shopMoney, "amount", out _) || !string.Equals(ReadString(shopMoney, "currency_code"), currency, StringComparison.OrdinalIgnoreCase)) return false;
        if (!bag.TryGetProperty("presentment_money", out var presentmentMoney)) return true;
        return presentmentMoney.ValueKind == JsonValueKind.Object && TryReadDecimal(presentmentMoney, "amount", out _) &&
            string.Equals(ReadString(presentmentMoney, "currency_code"), presentmentCurrency, StringComparison.OrdinalIgnoreCase);
    }

    private static decimal? lineItemsAndShippingTax(JsonElement order)
    {
        var found = false;
        var total = 0m;
        if (TryArray(order, "line_items", out var items))
            foreach (var item in items)
                if (TryArray(item, "tax_lines", out _)) { found = true; total += SumTaxLines(item); }
        if (TryArray(order, "shipping_lines", out var shipping))
            foreach (var line in shipping)
                if (TryArray(line, "tax_lines", out _)) { found = true; total += SumTaxLines(line); }
        return found ? RoundMoney(total) : null;
    }

    private static object ExtractMoneyBags(JsonElement order)
    {
        var header = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in new[] { "subtotal_price_set", "current_subtotal_price_set", "total_discounts_set", "current_total_discounts_set", "total_shipping_price_set", "current_shipping_price_set", "total_tax_set", "current_total_tax_set", "total_price_set", "current_total_price_set", "total_cash_rounding_payment_adjustment_set", "total_cash_rounding_refund_adjustment_set" })
            if (order.TryGetProperty(key, out var value)) header[key] = value.GetRawText();
        return header;
    }

    private static decimal? ShippingLineTotal(JsonElement root, string currency, out bool valid)
    {
        valid = true;
        if (!TryArray(root, "shipping_lines", out var shippingLines)) return null;
        var total = 0m;
        foreach (var line in shippingLines)
        {
            var amount = ReadMoneyBagAmount(line, "discounted_price_set", currency)
                ?? ReadFirstDecimal(line, ["discounted_price", "price"])
                ?? ReadMoneyBagAmount(line, "price_set", currency);
            if (!amount.HasValue || amount.Value < 0) { valid = false; return null; }
            total += amount.Value;
        }
        return RoundMoney(total);
    }

    private static decimal? SumDiscountAllocations(JsonElement item)
    {
        if (!TryArray(item, "discount_allocations", out var rows)) return null;
        return RoundMoney(rows.Sum(row => ReadMoneyBagAmount(row, "amount_set", null) ?? ReadDecimal(row, "amount")));
    }

    private static decimal SumTaxLines(JsonElement item)
    {
        if (!TryArray(item, "tax_lines", out var rows)) return 0m;
        return RoundMoney(rows.Sum(row => ReadMoneyBagAmount(row, "price_set", null) ?? ReadDecimal(row, "price")));
    }

    private static decimal? ReadMoneyBagAmount(JsonElement root, string property, string? expectedCurrency)
    {
        if (!root.TryGetProperty(property, out var bag) || bag.ValueKind != JsonValueKind.Object) return null;
        if (!bag.TryGetProperty("shop_money", out var shopMoney) || shopMoney.ValueKind != JsonValueKind.Object) return null;
        var currency = ReadString(shopMoney, "currency_code");
        if (expectedCurrency is not null && !string.Equals(currency, expectedCurrency, StringComparison.OrdinalIgnoreCase)) return null;
        return TryReadDecimal(shopMoney, "amount", out var amount) ? amount : null;
    }

    private static bool TryMoney(JsonElement root, string amountProperty, string setProperty, string expectedCurrency, out decimal amount, out string? currency)
    {
        currency = null;
        if (root.TryGetProperty(setProperty, out var bag) && bag.ValueKind == JsonValueKind.Object && bag.TryGetProperty("shop_money", out var shopMoney) && shopMoney.ValueKind == JsonValueKind.Object)
        {
            currency = ReadString(shopMoney, "currency_code");
            return TryReadDecimal(shopMoney, "amount", out amount);
        }
        currency = expectedCurrency;
        return TryReadDecimal(root, amountProperty, out amount);
    }

    private static decimal? ReadFirstDecimal(JsonElement root, string[] properties)
    {
        foreach (var property in properties) if (TryReadDecimal(root, property, out var amount)) return amount;
        return null;
    }

    private static bool TryReadDecimal(JsonElement root, string property, out decimal amount)
    {
        amount = 0;
        if (!root.TryGetProperty(property, out var value)) return false;
        if (value.ValueKind == JsonValueKind.Number) return value.TryGetDecimal(out amount);
        return value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }

    private static decimal ReadDecimal(JsonElement root, string property) => TryReadDecimal(root, property, out var amount) ? amount : 0m;
    private static string? ReadString(JsonElement root, string property) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int? ReadInt(JsonElement root, string property) => int.TryParse(ReadString(root, property) ?? (root.TryGetProperty(property, out var value) ? value.ToString() : null), out var result) ? result : null;
    private static bool? ReadBool(JsonElement root, string property) => root.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
    private static bool TryArray(JsonElement root, string property, out List<JsonElement> rows)
    {
        rows = root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToList() : [];
        return root.TryGetProperty(property, out var candidate) && candidate.ValueKind == JsonValueKind.Array;
    }
    private static bool MoneyEqual(decimal left, decimal right) => RoundMoney(left) == RoundMoney(right);
    private static decimal RoundMoney(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
