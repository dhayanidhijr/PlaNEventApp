namespace PlaNEvent.Shared.Contracts;

public sealed class BookingDto
{
    public int Id { get; set; }
    public int OccurrenceId { get; set; }
    public int OccurrenceSlotId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerNotes { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public string OccurrenceName { get; set; } = string.Empty;
    public DateTime? SlotStartUtc { get; set; }
    public int TotalBookingCount { get; set; } = 1;
}
