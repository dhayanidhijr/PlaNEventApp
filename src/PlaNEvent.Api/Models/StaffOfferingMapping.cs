namespace PlaNEvent.Api.Models;

public sealed class StaffOfferingMapping
{
    public int Id { get; set; }
    public int StaffMemberId { get; set; }
    public StaffMember? StaffMember { get; set; }
    public int OfferingId { get; set; }
    public Offering? Offering { get; set; }
    public string ProficiencyLevel { get; set; } = "Intermediate";
}
