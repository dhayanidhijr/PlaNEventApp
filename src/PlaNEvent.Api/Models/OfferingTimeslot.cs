namespace PlaNEvent.Api.Models;

public sealed class OfferingTimeslot
{
    public int Id { get; set; }
    public int RuleGroupId { get; set; }
    public OfferingRuleGroup? RuleGroup { get; set; }
    public bool IsAllDay { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public bool RepeatGeneratedSlots { get; set; }
    public TimeSpan? RepeatUntilLastStartTime { get; set; }
    public int? RepeatEveryMinutes { get; set; }
}
