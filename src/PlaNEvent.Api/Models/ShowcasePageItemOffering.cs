namespace PlaNEvent.Api.Models;

public sealed class ShowcasePageItemOffering
{
    public int Id { get; set; }
    public int ShowcasePageItemId { get; set; }
    public ShowcasePageItem? ShowcasePageItem { get; set; }
    public int OfferingId { get; set; }
    public Offering? Offering { get; set; }
    public int SortOrder { get; set; }
}
