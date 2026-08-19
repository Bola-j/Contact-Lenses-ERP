namespace Lensee.Modules.Operations.Data;

public sealed class ReplenishmentRun
{
    public Guid Id { get; set; }
    public string RunKey { get; set; } = null!;
    public DateOnly CairoDate { get; set; }
    public string Trigger { get; set; } = "Scheduled";
    public string Status { get; set; } = "Completed";
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int CreatedOperations { get; set; }
    public int UncoveredQuantity { get; set; }
}
