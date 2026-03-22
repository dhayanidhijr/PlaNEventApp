using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlaNEvent.Api.Data;
using PlaNEvent.Api.Models;
using PlaNEvent.Shared.Contracts;

namespace PlaNEvent.Api.Controllers;

[ApiController]
[Route("api/public/showcase")]
[AllowAnonymous]
public sealed class PublicShowcaseController(AppDbContext dbContext) : ControllerBase
{
    [HttpGet("{ownerSlug}")]
    public async Task<ActionResult<PublicShowcaseDto>> Get(
        string ownerSlug,
        [FromQuery] string? pageSlug,
        [FromQuery] string? sourceType,
        [FromQuery] int? sourceId,
        [FromQuery] int? offeringId,
        [FromQuery] int? ruleGroupId,
        [FromQuery] string? searchTerm,
        CancellationToken cancellationToken)
    {
        var owner = await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(x => x.PublicSlug == ownerSlug && !x.IsDisabled, cancellationToken);
        if (owner is null)
        {
            return NotFound();
        }

        var pages = await dbContext.ShowcasePages
            .AsNoTracking()
            .Include(x => x.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Id))
            .Where(x => x.OwnerId == owner.Id && x.IsActive)
            .OrderByDescending(x => x.IsHomePage)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var categories = await dbContext.Categories
            .AsNoTracking()
            .Where(x => x.OwnerId == owner.Id && x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var offerings = await dbContext.Offerings
            .AsNoTracking()
            .Where(x => x.OwnerId == owner.Id && x.IsActive && x.IsPublished)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var occurrences = await dbContext.Occurrences
            .AsNoTracking()
            .Include(x => x.Slots)
            .Include(x => x.RuleGroup)
            .Where(x => x.OwnerId == owner.Id && x.IsPublished)
            .OrderBy(x => x.Title)
            .ToListAsync(cancellationToken);

        var page = ResolvePage(pages, pageSlug);
        if (page is null)
        {
            return NotFound();
        }

        var term = searchTerm?.Trim() ?? string.Empty;
        var response = new PublicShowcaseDto
        {
            OwnerSlug = ownerSlug,
            CurrentPageSlug = page.Slug,
            CurrentPageName = page.Name,
            SearchTerm = term,
            Tabs = pages.Select(x => new PublicShowcaseTabDto
            {
                Name = x.Name,
                Slug = x.Slug,
                IsActive = x.Id == page.Id
            }).ToList()
        };

        if (offeringId.HasValue)
        {
            var offering = offerings.FirstOrDefault(x => x.Id == offeringId.Value);
            if (offering is null)
            {
                return NotFound();
            }

            response.Breadcrumbs = BuildOfferingBreadcrumbs(categories, offering).Append(offering.Name).ToList();
            response.OccurrenceChoices = occurrences
                .Where(x => x.OfferingId == offering.Id)
                .GroupBy(x => new { x.RuleGroupId, Name = x.RuleGroup?.Name ?? offering.Name, x.Color })
                .Select(g => new OccurrenceVariantDto
                {
                    OfferingId = offering.Id,
                    RuleGroupId = g.Key.RuleGroupId,
                    Name = g.Key.Name,
                    Color = g.Key.Color,
                    StartDateUtc = g.SelectMany(x => x.Slots).OrderBy(x => x.StartUtc).FirstOrDefault()?.StartUtc,
                    EndDateUtc = g.SelectMany(x => x.Slots).OrderByDescending(x => x.EndUtc).FirstOrDefault()?.EndUtc
                })
                .OrderBy(x => x.Name)
                .ToList();

            response.BookingCalendar = new PublicBookingCalendarDto
            {
                Title = offering.Name,
                SelectedOccurrenceName = response.OccurrenceChoices.FirstOrDefault(x => x.RuleGroupId == ruleGroupId)?.Name ?? response.OccurrenceChoices.FirstOrDefault()?.Name ?? string.Empty,
                Color = offering.Color,
                Occurrences = occurrences
                    .Where(x => x.OfferingId == offering.Id && (!ruleGroupId.HasValue || x.RuleGroupId == ruleGroupId))
                    .Select(MapOccurrence)
                    .ToList()
            };

            return Ok(response);
        }

        if (sourceType == "offering" && sourceId.HasValue)
        {
            return await Get(ownerSlug, page.Slug, null, null, sourceId, null, searchTerm, cancellationToken);
        }

        if (sourceType == "category" && sourceId.HasValue)
        {
            var category = categories.FirstOrDefault(x => x.Id == sourceId.Value);
            if (category is null)
            {
                return NotFound();
            }

            response.Breadcrumbs = BuildCategoryBreadcrumbs(categories, category).ToList();
            response.Rows = new[]
            {
                new PublicShowcaseRowDto
                {
                    Name = category.Name,
                    CarouselType = "carousel",
                    Cards = BuildCardsForCategory(category.Id, categories, offerings, term)
                }
            };

            return Ok(response);
        }

        response.Rows = page.Items
            .Where(x => x.IsActive)
            .Select(x => new PublicShowcaseRowDto
            {
                Name = x.Name,
                CarouselType = x.CarouselType,
                Cards = x.SourceType == "category"
                    ? BuildCardsForCategory(x.SourceId, categories, offerings, term, x)
                    : BuildCardsForOffering(x.SourceId, offerings, x, term)
            })
            .Where(x => x.Cards.Count > 0)
            .ToList();

        return Ok(response);
    }

    private static ShowcasePage? ResolvePage(IEnumerable<ShowcasePage> pages, string? pageSlug)
        => string.IsNullOrWhiteSpace(pageSlug)
            ? pages.FirstOrDefault(x => x.IsHomePage) ?? pages.FirstOrDefault()
            : pages.FirstOrDefault(x => x.Slug.Equals(pageSlug, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyCollection<PublicShowcaseCardDto> BuildCardsForCategory(
        int categoryId,
        IReadOnlyCollection<Category> categories,
        IReadOnlyCollection<Offering> offerings,
        string searchTerm,
        ShowcasePageItem? config = null)
    {
        var categoryCards = categories
            .Where(x => x.ParentCategoryId == categoryId)
            .Select(x => new PublicShowcaseCardDto
            {
                CardType = "category",
                SourceId = x.Id,
                Title = x.Name,
                Description = "Category",
                AccentColor = x.Color,
                Blur = config?.Blur ?? false,
                HideTitle = config?.HideTitle ?? false,
                ShowDescription = config?.ShowDescription ?? true
            });

        var offeringCards = offerings
            .Where(x => x.CategoryId == categoryId)
            .Select(x => new PublicShowcaseCardDto
            {
                CardType = "offering",
                SourceId = x.Id,
                Title = x.Name,
                Description = string.IsNullOrWhiteSpace(config?.Description) ? x.Description : config.Description,
                ImageUrl = x.CoverImageUrl,
                AccentColor = x.Color,
                Blur = config?.Blur ?? false,
                HideTitle = config?.HideTitle ?? false,
                ShowDescription = config?.ShowDescription ?? true
            });

        return categoryCards.Concat(offeringCards)
            .Where(x => MatchesSearch(x.Title, x.Description, searchTerm))
            .OrderBy(x => x.Title)
            .ToList();
    }

    private static IReadOnlyCollection<PublicShowcaseCardDto> BuildCardsForOffering(
        int offeringId,
        IReadOnlyCollection<Offering> offerings,
        ShowcasePageItem config,
        string searchTerm)
    {
        return offerings
            .Where(x => x.Id == offeringId && MatchesSearch(x.Name, x.Description, searchTerm))
            .Select(x => new PublicShowcaseCardDto
            {
                CardType = "offering",
                SourceId = x.Id,
                Title = x.Name,
                Description = string.IsNullOrWhiteSpace(config.Description) ? x.Description : config.Description,
                ImageUrl = x.CoverImageUrl,
                AccentColor = x.Color,
                Blur = config.Blur,
                HideTitle = config.HideTitle,
                ShowDescription = config.ShowDescription
            })
            .ToList();
    }

    private static bool MatchesSearch(string title, string description, string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return true;
        }

        return title.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
            || description.Contains(searchTerm, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyCollection<string> BuildCategoryBreadcrumbs(IReadOnlyCollection<Category> categories, Category category)
    {
        var breadcrumbs = new List<string>();
        Category? current = category;
        while (current is not null)
        {
            breadcrumbs.Insert(0, current.Name);
            current = current.ParentCategoryId.HasValue ? categories.FirstOrDefault(x => x.Id == current.ParentCategoryId.Value) : null;
        }

        return breadcrumbs;
    }

    private static IReadOnlyCollection<string> BuildOfferingBreadcrumbs(IReadOnlyCollection<Category> categories, Offering offering)
    {
        if (!offering.CategoryId.HasValue)
        {
            return Array.Empty<string>();
        }

        var category = categories.FirstOrDefault(x => x.Id == offering.CategoryId.Value);
        return category is null ? Array.Empty<string>() : BuildCategoryBreadcrumbs(categories, category);
    }

    private static OccurrenceDto MapOccurrence(Occurrence occurrence) => new()
    {
        Id = occurrence.Id,
        OfferingId = occurrence.OfferingId,
        RuleGroupId = occurrence.RuleGroupId,
        Title = occurrence.Title,
        Description = occurrence.Description,
        RuleGroupName = occurrence.RuleGroup?.Name,
        Color = occurrence.Color,
        IsPublished = occurrence.IsPublished,
        Slots = occurrence.Slots.Select(x => new OccurrenceSlotDto
        {
            Id = x.Id,
            StartUtc = x.StartUtc,
            EndUtc = x.EndUtc
        }).OrderBy(x => x.StartUtc).ToList()
    };
}
