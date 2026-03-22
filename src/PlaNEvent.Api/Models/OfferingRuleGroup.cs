namespace PlaNEvent.Api.Models;

public sealed class OfferingRuleGroup
{
    public int Id { get; set; }
    public int OfferingId { get; set; }
    public Offering? Offering { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#ec3e47";
    public DateTime StartDateUtc { get; set; }
    public DateTime? EndDateUtc { get; set; }
    public string FrequencyType { get; set; } = "daysOfWeek";
    public string WeekdaysCsv { get; set; } = string.Empty;
    public int Interval { get; set; } = 1;
    public int? NthWeekOfMonth { get; set; }
    public int? DayOfMonth { get; set; }
    public List<OfferingTimeslot> Timeslots { get; set; } = new();
}
