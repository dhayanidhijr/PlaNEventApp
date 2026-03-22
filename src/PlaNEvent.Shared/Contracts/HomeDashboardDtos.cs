namespace PlaNEvent.Shared.Contracts;

public sealed class HomeDashboardDto
{
    public IReadOnlyCollection<HomeScheduleItemDto> TodayEvents { get; set; } = Array.Empty<HomeScheduleItemDto>();
    public IReadOnlyCollection<HomeBookingSummaryItemDto> TodayBookingsByEvent { get; set; } = Array.Empty<HomeBookingSummaryItemDto>();
    public HomeComparisonSummaryDto OfferedBookedDay { get; set; } = new();
    public IReadOnlyCollection<HomeComparisonPointDto> OfferedBookedWeek { get; set; } = Array.Empty<HomeComparisonPointDto>();
    public IReadOnlyCollection<HomeComparisonPointDto> OfferedBookedMonth { get; set; } = Array.Empty<HomeComparisonPointDto>();
    public HomeComparisonSummaryDto TargetActualDay { get; set; } = new();
    public IReadOnlyCollection<HomeComparisonPointDto> TargetActualWeek { get; set; } = Array.Empty<HomeComparisonPointDto>();
    public IReadOnlyCollection<HomeComparisonPointDto> TargetActualMonth { get; set; } = Array.Empty<HomeComparisonPointDto>();
    public IReadOnlyCollection<HomeComparisonPointDto> TargetActualYear { get; set; } = Array.Empty<HomeComparisonPointDto>();
}

public sealed class HomeScheduleItemDto
{
    public int OccurrenceId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public string Color { get; set; } = "#2f80ff";
}

public sealed class HomeBookingSummaryItemDto
{
    public int OccurrenceId { get; set; }
    public int SlotId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public int BookingCount { get; set; }
    public string Color { get; set; } = "#2f80ff";
}

public sealed class HomeComparisonSummaryDto
{
    public int PrimaryValue { get; set; }
    public int SecondaryValue { get; set; }
}

public sealed class HomeComparisonPointDto
{
    public string Label { get; set; } = string.Empty;
    public int PrimaryValue { get; set; }
    public int SecondaryValue { get; set; }
}
