namespace PlaNEvent.Api.Models;

public sealed class ShowcasePageItem
{
    public int Id { get; set; }
    public int ShowcasePageId { get; set; }
    public ShowcasePage? ShowcasePage { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SourceType { get; set; } = "category";
    public int SourceId { get; set; }
    public string CarouselType { get; set; } = "carousel";
    public string Description { get; set; } = string.Empty;
    public bool Blur { get; set; }
    public bool HideTitle { get; set; }
    public bool ShowDescription { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public List<ShowcasePageItemOffering> OfferingReferences { get; set; } = new();
}
