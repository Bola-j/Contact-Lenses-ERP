using Microsoft.EntityFrameworkCore;

namespace Lensee.Modules.Payments.Data;

/// <summary>Single authoritative view for allocations that currently consume a collection and settle an obligation.</summary>
public static class MerchantAllocationQueries
{
    public static IQueryable<MerchantEntryAllocation> EffectiveAllocations(this PaymentsDbContext payments) =>
        payments.MerchantEntryAllocations.Where(allocation =>
            !payments.MerchantAllocationReconciliations.Any(reconciliation => reconciliation.SourceAllocationId == allocation.Id));

    public static IQueryable<MerchantEntryAllocation> EffectiveCreditAllocations(this PaymentsDbContext payments) =>
        payments.EffectiveAllocations().Where(allocation => allocation.Entry.CreditAmount > 0m);
}
