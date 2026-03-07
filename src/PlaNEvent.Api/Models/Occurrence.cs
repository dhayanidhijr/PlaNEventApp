namespace PlaNEvent.Api.Models;

public sealed class Occurrence
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? EventGroupId { get; set; }
    public EventGroup? EventGroup { get; set; }
    public int? StaffId { get; set; }
    public StaffMember? Staff { get; set; }
    public bool IsPublished { get; set; }
    public List<OccurrenceSlot> Slots { get; set; } = new();
}
