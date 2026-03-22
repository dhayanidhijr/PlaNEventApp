namespace PlaNEvent.Shared.Contracts;

public sealed class ShowcasePageSummaryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsHomePage { get; set; }
    public int ItemCount { get; set; }
}

public sealed class ShowcasePageEditorDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsHomePage { get; set; }
    public List<ShowcasePageItemDto> Items { get; set; } = new();
}

public sealed class SaveShowcasePageRequest
{
    public int? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsHomePage { get; set; }
    public List<ShowcasePageItemDto> Items { get; set; } = new();
}

public sealed class ShowcasePageItemDto
{
    public int Id { get; set; }
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
}

public sealed class PublicShowcaseDto
{
    public string OwnerSlug { get; set; } = string.Empty;
    public string CurrentPageSlug { get; set; } = string.Empty;
    public string CurrentPageName { get; set; } = string.Empty;
    public IReadOnlyCollection<PublicShowcaseTabDto> Tabs { get; set; } = Array.Empty<PublicShowcaseTabDto>();
    public IReadOnlyCollection<PublicShowcaseRowDto> Rows { get; set; } = Array.Empty<PublicShowcaseRowDto>();
    public IReadOnlyCollection<string> Breadcrumbs { get; set; } = Array.Empty<string>();
    public IReadOnlyCollection<OccurrenceVariantDto> OccurrenceChoices { get; set; } = Array.Empty<OccurrenceVariantDto>();
    public PublicBookingCalendarDto? BookingCalendar { get; set; }
    public string SearchTerm { get; set; } = string.Empty;
}

public sealed class PublicShowcaseTabDto
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public sealed class PublicShowcaseRowDto
{
    public string Name { get; set; } = string.Empty;
    public string CarouselType { get; set; } = "carousel";
    public IReadOnlyCollection<PublicShowcaseCardDto> Cards { get; set; } = Array.Empty<PublicShowcaseCardDto>();
}

public sealed class PublicShowcaseCardDto
{
    public string CardType { get; set; } = string.Empty;
    public int SourceId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal? Price { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string AccentColor { get; set; } = "#2f80ff";
    public string NavigationSlug { get; set; } = string.Empty;
    public bool Blur { get; set; }
    public bool HideTitle { get; set; }
    public bool ShowDescription { get; set; } = true;
}

public sealed class OccurrenceVariantDto
{
    public int OfferingId { get; set; }
    public int? RuleGroupId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#ec3e47";
    public DateTime? StartDateUtc { get; set; }
    public DateTime? EndDateUtc { get; set; }
}

public sealed class PublicBookingCalendarDto
{
    public string Title { get; set; } = string.Empty;
    public string SelectedOccurrenceName { get; set; } = string.Empty;
    public string Color { get; set; } = "#ec3e47";
    public IReadOnlyCollection<OccurrenceDto> Occurrences { get; set; } = Array.Empty<OccurrenceDto>();
}
