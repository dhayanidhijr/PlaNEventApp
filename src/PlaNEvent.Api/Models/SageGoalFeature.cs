namespace PlaNEvent.Api.Models;

public sealed class SageGoalFeature
{
    public int Id { get; set; }
    public int SageGoalSettingsId { get; set; }
    public SageGoalSettings? SageGoalSettings { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ExpectedMonthlyBookingCount { get; set; }
    public decimal ExpectedMonthlySalesAmount { get; set; }
    public decimal TargetSharePercent { get; set; }
    public int SortOrder { get; set; }
}
