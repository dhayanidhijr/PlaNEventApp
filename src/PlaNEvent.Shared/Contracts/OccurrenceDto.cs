namespace PlaNEvent.Shared.Contracts;

public sealed class OccurrenceDto
{
    public int Id { get; set; }
    public int? OfferingId { get; set; }
    public int? RuleGroupId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? EventGroupId { get; set; }
    public int? StaffId { get; set; }
    public bool IsPublished { get; set; }
    public string OwnerPublicSlug { get; set; } = string.Empty;
    public string? StaffName { get; set; }
    public string? EventGroupName { get; set; }
    public string? OfferingName { get; set; }
    public string? RuleGroupName { get; set; }
    public string Color { get; set; } = "#2f80ff";
    public int TotalBookingCount { get; set; }
    public IReadOnlyCollection<OccurrenceSlotDto> Slots { get; set; } = Array.Empty<OccurrenceSlotDto>();
}
