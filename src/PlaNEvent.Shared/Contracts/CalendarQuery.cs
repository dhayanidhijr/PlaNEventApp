namespace PlaNEvent.Shared.Contracts;

public sealed class CalendarQuery
{
    public DateTime? StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }
    public string View { get; set; } = "month";
}
