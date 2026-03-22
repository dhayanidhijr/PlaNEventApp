namespace PlaNEvent.Shared.Contracts;

public sealed class SageGoalSettingsDto
{
    public int Id { get; set; }
    public string FacilityBusinessSummary { get; set; } = string.Empty;
    public int ExpectedMonthlyBookingCount { get; set; }
    public decimal ExpectedMonthlySalesAmount { get; set; }
    public List<SageGoalFeatureDto> Features { get; set; } = new();
}

public sealed class SageGoalFeatureDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ExpectedMonthlyBookingCount { get; set; }
    public decimal ExpectedMonthlySalesAmount { get; set; }
    public decimal TargetSharePercent { get; set; }
    public int SortOrder { get; set; }
}

public sealed class SageGoalOverrideSettingsDto
{
    public int Id { get; set; }
    public bool OverrideExpectedMonthlyBookingCount { get; set; }
    public int ExpectedMonthlyBookingCount { get; set; }
    public bool OverrideExpectedMonthlySalesAmount { get; set; }
    public decimal ExpectedMonthlySalesAmount { get; set; }
    public List<SageGoalOverrideFeatureDto> Features { get; set; } = new();
}

public sealed class SageGoalOverrideFeatureDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsOverrideEnabled { get; set; }
    public int ExpectedMonthlyBookingCount { get; set; }
    public decimal ExpectedMonthlySalesAmount { get; set; }
    public decimal TargetSharePercent { get; set; }
    public int SortOrder { get; set; }
}
