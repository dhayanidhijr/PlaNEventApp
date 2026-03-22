namespace PlaNEvent.Api.Models;

public sealed class ShowcasePage
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsHomePage { get; set; }
    public List<ShowcasePageItem> Items { get; set; } = new();
}
