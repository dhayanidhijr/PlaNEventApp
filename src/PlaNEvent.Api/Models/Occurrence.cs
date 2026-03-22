namespace PlaNEvent.Api.Models;

public sealed class Occurrence
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public int? OfferingId { get; set; }
    public Offering? Offering { get; set; }
    public int? RuleGroupId { get; set; }
    public OfferingRuleGroup? RuleGroup { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? EventGroupId { get; set; }
    public EventGroup? EventGroup { get; set; }
    public int? StaffId { get; set; }
    public StaffMember? Staff { get; set; }
    public bool IsPublished { get; set; }
    public string Color { get; set; } = "#2f80ff";
    public List<OccurrenceSlot> Slots { get; set; } = new();
}
