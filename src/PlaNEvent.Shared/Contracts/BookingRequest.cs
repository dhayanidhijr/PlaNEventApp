namespace PlaNEvent.Shared.Contracts;

public sealed class BookingRequest
{
    public int OccurrenceId { get; set; }
    public int OccurrenceSlotId { get; set; }
    public string CustomerNotes { get; set; } = string.Empty;
}
