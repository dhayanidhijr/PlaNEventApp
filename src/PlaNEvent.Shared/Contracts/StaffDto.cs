namespace PlaNEvent.Shared.Contracts;

public sealed class StaffDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public decimal TrainingQualityRating { get; set; }
    public List<StaffOfferingMappingDto> OfferingMappings { get; set; } = new();
}

public sealed class StaffOfferingMappingDto
{
    public int Id { get; set; }
    public int OfferingId { get; set; }
    public string OfferingName { get; set; } = string.Empty;
    public string ProficiencyLevel { get; set; } = "Intermediate";
}

public sealed class StaffCalendarDto
{
    public int StaffId { get; set; }
    public string StaffName { get; set; } = string.Empty;
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public IReadOnlyCollection<StaffCalendarDayDto> Days { get; set; } = Array.Empty<StaffCalendarDayDto>();
}

public sealed class StaffCalendarDayDto
{
    public DateTime DateUtc { get; set; }
    public IReadOnlyCollection<StaffCalendarEntryDto> Entries { get; set; } = Array.Empty<StaffCalendarEntryDto>();
}

public sealed class StaffCalendarEntryDto
{
    public int OccurrenceId { get; set; }
    public int? OfferingId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string OfferingName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Color { get; set; } = "#2f80ff";
    public DateTime SlotStartUtc { get; set; }
    public DateTime SlotEndUtc { get; set; }
    public int TotalBookingCount { get; set; }
}
