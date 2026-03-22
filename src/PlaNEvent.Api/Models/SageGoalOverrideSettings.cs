namespace PlaNEvent.Api.Models;

public sealed class SageGoalOverrideSettings
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public bool OverrideExpectedMonthlyBookingCount { get; set; }
    public int ExpectedMonthlyBookingCount { get; set; }
    public bool OverrideExpectedMonthlySalesAmount { get; set; }
    public decimal ExpectedMonthlySalesAmount { get; set; }
    public List<SageGoalOverrideFeature> Features { get; set; } = new();
}
