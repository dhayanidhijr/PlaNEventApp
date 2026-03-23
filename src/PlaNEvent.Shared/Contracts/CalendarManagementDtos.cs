namespace PlaNEvent.Shared.Contracts;

public sealed class CalendarNodeDto
{
    public int Id { get; set; }
    public int? ParentCategoryId { get; set; }
    public string NodeType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#2f80ff";
    public bool IsPublished { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class CalendarDashboardDto
{
    public IReadOnlyCollection<CalendarNodeDto> Nodes { get; set; } = Array.Empty<CalendarNodeDto>();
    public IReadOnlyCollection<OccurrenceDto> Occurrences { get; set; } = Array.Empty<OccurrenceDto>();
    public IReadOnlyCollection<ShowcasePageSummaryDto> ShowcasePages { get; set; } = Array.Empty<ShowcasePageSummaryDto>();
}

public sealed class ShowcasePageUrlDto
{
    public int? PageId { get; set; }
    public string PageName { get; set; } = string.Empty;
    public string PageSlug { get; set; } = string.Empty;
    public string OwnerSlug { get; set; } = string.Empty;
    public string PublicUrl { get; set; } = string.Empty;
}

public sealed class CategoryDto
{
    public int Id { get; set; }
    public int? ParentCategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#2f80ff";
    public bool IsActive { get; set; } = true;
}

public sealed class SaveCategoryRequest
{
    public int? Id { get; set; }
    public int? ParentCategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#2f80ff";
    public bool IsActive { get; set; } = true;
}

public sealed class OfferingEditorDto
{
    public int Id { get; set; }
    public int? CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Color { get; set; } = "#ec3e47";
    public string CoverImageUrl { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
    public bool IsActive { get; set; } = true;
    public bool AllowNewBookings { get; set; } = true;
    public int? LimitBookingPeriodDays { get; set; }
    public bool RequireSignUpEveryTime { get; set; }
    public List<RuleGroupEditorDto> RuleGroups { get; set; } = new();
}

public sealed class OfferingSummaryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int? CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string Color { get; set; } = "#ec3e47";
    public bool IsPublished { get; set; }
    public bool IsActive { get; set; } = true;
    public int RuleGroupCount { get; set; }
    public int TimeslotCount { get; set; }
}

public sealed class SaveOfferingRequest
{
    public int? Id { get; set; }
    public int? CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Color { get; set; } = "#ec3e47";
    public string CoverImageUrl { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
    public bool IsActive { get; set; } = true;
    public bool AllowNewBookings { get; set; } = true;
    public int? LimitBookingPeriodDays { get; set; }
    public bool RequireSignUpEveryTime { get; set; }
    public List<RuleGroupEditorDto> RuleGroups { get; set; } = new();
}

public sealed class RuleGroupEditorDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#ec3e47";
    public DateTime StartDateUtc { get; set; } = DateTime.UtcNow.Date;
    public DateTime? EndDateUtc { get; set; }
    public string FrequencyType { get; set; } = "daysOfWeek";
    public List<int> Weekdays { get; set; } = new();
    public int Interval { get; set; } = 1;
    public int? NthWeekOfMonth { get; set; }
    public int? DayOfMonth { get; set; }
    public List<TimeslotEditorDto> Timeslots { get; set; } = new();
}

public sealed class TimeslotEditorDto
{
    public int Id { get; set; }
    public bool IsAllDay { get; set; }
    public TimeSpan StartTime { get; set; } = new(9, 0, 0);
    public TimeSpan EndTime { get; set; } = new(10, 0, 0);
    public bool RepeatGeneratedSlots { get; set; }
    public TimeSpan? RepeatUntilLastStartTime { get; set; }
    public int? RepeatEveryMinutes { get; set; }
}
