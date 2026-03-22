namespace PlaNEvent.Shared.Contracts;

public sealed class OccurrencePageDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public IReadOnlyCollection<OccurrenceDto> Items { get; set; } = Array.Empty<OccurrenceDto>();
}
