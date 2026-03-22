namespace PlaNEvent.Shared.Contracts;

public sealed class OccurrenceSlotDto
{
    public int Id { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public int TotalBookingCount { get; set; }
}
