namespace PlaNEvent.Api.Models;

public sealed class OccurrenceSlot
{
    public int Id { get; set; }
    public int OccurrenceId { get; set; }
    public Occurrence? Occurrence { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
}
