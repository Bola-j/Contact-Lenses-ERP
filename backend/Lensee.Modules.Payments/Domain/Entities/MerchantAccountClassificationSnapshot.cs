namespace Lensee.Modules.Payments.Data;

public sealed class MerchantAccountClassificationSnapshot
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public int CalendarYear { get; set; }
    public bool IsPartialYear { get; set; }
    public string Grade { get; set; } = null!;
    public decimal Score { get; set; }
    public string FlagsJson { get; set; } = "[]";
    public string SettingsVersion { get; set; } = "default";
    public DateTime CalculatedAt { get; set; }
}
