namespace PlaNEvent.Api.Models;

public sealed class Booking
{
    public int Id { get; set; }
    public int OccurrenceId { get; set; }
    public Occurrence? Occurrence { get; set; }
    public int OccurrenceSlotId { get; set; }
    public OccurrenceSlot? OccurrenceSlot { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerNotes { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
