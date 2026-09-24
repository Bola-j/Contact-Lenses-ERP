namespace Lensee.Modules.Finance.Data;

public sealed class FinanceCategory
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = null!;
    public string Code { get; set; } = null!;
    public string EnglishName { get; set; } = null!;
    public string ArabicName { get; set; } = null!;
    public Guid? ParentId { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
