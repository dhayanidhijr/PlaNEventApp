namespace PlaNEvent.Api.Models;

public sealed class SageGoalSettings
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public string FacilityBusinessSummary { get; set; } = string.Empty;
    public int ExpectedMonthlyBookingCount { get; set; }
    public decimal ExpectedMonthlySalesAmount { get; set; }
    public List<SageGoalFeature> Features { get; set; } = new();
}
