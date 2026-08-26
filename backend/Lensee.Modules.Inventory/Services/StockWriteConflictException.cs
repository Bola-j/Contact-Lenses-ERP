namespace Lensee.Modules.Inventory.Services;

/// <summary>
/// Raised when a relational stock write lost a race with another mutation.
/// Callers must reload the current balance before retrying.
/// </summary>
public sealed class StockWriteConflictException : InvalidOperationException
{
    public StockWriteConflictException()
        : base("Inventory changed while the reservation was being processed. Reload the current stock and retry.")
    {
    }
}
