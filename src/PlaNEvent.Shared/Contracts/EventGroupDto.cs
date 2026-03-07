namespace PlaNEvent.Shared.Contracts;

public sealed class EventGroupDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
