namespace PlaNEvent.Api.Models;

public sealed class SageGoalOverrideFeature
{
    public int Id { get; set; }
    public int SageGoalOverrideSettingsId { get; set; }
    public SageGoalOverrideSettings? SageGoalOverrideSettings { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsOverrideEnabled { get; set; }
    public int ExpectedMonthlyBookingCount { get; set; }
    public decimal ExpectedMonthlySalesAmount { get; set; }
    public decimal TargetSharePercent { get; set; }
    public int SortOrder { get; set; }
}
