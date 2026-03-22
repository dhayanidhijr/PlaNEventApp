namespace PlaNEvent.Shared.Contracts;

public sealed class BookingPageDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public IReadOnlyCollection<BookingDto> Items { get; set; } = Array.Empty<BookingDto>();
}
