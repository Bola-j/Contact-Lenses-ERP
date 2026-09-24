using Lensee.Modules.CRM.Data;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Infrastructure;

public static class PaymentTracks
{
    public const string MerchantAccount = "MerchantAccount";
    public const string OtherPayments = "OtherPayments";

    public static string? Normalize(string? value) => value?.Trim() switch
    {
        MerchantAccount => MerchantAccount,
        OtherPayments => OtherPayments,
        "DirectOperation" => OtherPayments,
        _ => null
    };
}

public static class CustomerKinds
{
    public const string RegisteredMerchant = "RegisteredMerchant";
    public const string AdHocCustomer = "AdHocCustomer";
    public const string Anonymous = "Anonymous";
}

public sealed record CustomerPaymentTrack(
    string CustomerKind,
    string PaymentTrack,
    Guid? MerchantId,
    Merchant? Merchant);

public static class CustomerPaymentTrackResolver
{
    public static async Task<(CustomerPaymentTrack? Result, Dictionary<string, string[]> Errors)> ResolveAsync(
        CrmDbContext crmDbContext,
        Guid? merchantId,
        string? buyerName,
        string? buyerPhone,
        string? declaredCustomerKind,
        string? declaredPaymentTrack,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var normalizedTrack = PaymentTracks.Normalize(declaredPaymentTrack);
        if (!string.IsNullOrWhiteSpace(declaredPaymentTrack) && normalizedTrack is null)
        {
            errors[nameof(declaredPaymentTrack)] = ["Payment track must be MerchantAccount or OtherPayments."];
        }

        var normalizedKind = string.IsNullOrWhiteSpace(declaredCustomerKind)
            ? null
            : declaredCustomerKind.Trim() switch
            {
                CustomerKinds.RegisteredMerchant => CustomerKinds.RegisteredMerchant,
                CustomerKinds.AdHocCustomer => CustomerKinds.AdHocCustomer,
                CustomerKinds.Anonymous => CustomerKinds.Anonymous,
                _ => null
            };
        if (!string.IsNullOrWhiteSpace(declaredCustomerKind) && normalizedKind is null)
        {
            errors[nameof(declaredCustomerKind)] = ["Customer kind must be RegisteredMerchant, AdHocCustomer, or Anonymous."];
        }

        Merchant? merchant = null;
        if (merchantId.HasValue)
        {
            merchant = await crmDbContext.Merchants.SingleOrDefaultAsync(
                value => value.Id == merchantId.Value && !value.IsDeleted,
                cancellationToken);
            if (merchant is null || !string.Equals(merchant.Status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                errors[nameof(merchantId)] = ["Merchant must exist and be active."];
            }
        }

        var result = merchant is not null && errors.Count == 0
            ? new CustomerPaymentTrack(CustomerKinds.RegisteredMerchant, PaymentTracks.MerchantAccount, merchant.Id, merchant)
            : new CustomerPaymentTrack(
                string.IsNullOrWhiteSpace(buyerName) && string.IsNullOrWhiteSpace(buyerPhone)
                    ? CustomerKinds.Anonymous
                    : CustomerKinds.AdHocCustomer,
                PaymentTracks.OtherPayments,
                null,
                null);

        if (normalizedKind is not null && !string.Equals(normalizedKind, result.CustomerKind, StringComparison.Ordinal))
        {
            errors[nameof(declaredCustomerKind)] = ["Customer kind does not match the persisted customer identity."];
        }
        if (normalizedTrack is not null && !string.Equals(normalizedTrack, result.PaymentTrack, StringComparison.Ordinal))
        {
            errors[nameof(declaredPaymentTrack)] = ["Payment track does not match the persisted customer identity."];
        }

        return errors.Count == 0 ? (result, errors) : (null, errors);
    }
}
