namespace PlaNEvent.Shared.Contracts;

public sealed class CreateOccurrenceRequest
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? EventGroupId { get; set; }
    public int? StaffId { get; set; }
    public DateTime? RepeatUntilUtc { get; set; }
    public bool PublishOnCreate { get; set; }
    public List<SlotInputDto> Slots { get; set; } = new();
}
