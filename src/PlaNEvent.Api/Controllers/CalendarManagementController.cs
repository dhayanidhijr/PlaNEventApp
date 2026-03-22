using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlaNEvent.Api.Data;
using PlaNEvent.Api.Infrastructure;
using PlaNEvent.Api.Models;
using PlaNEvent.Api.Services;
using PlaNEvent.Shared.Contracts;

namespace PlaNEvent.Api.Controllers;

[ApiController]
[Route("api/calendar-management")]
[Authorize]
public sealed class CalendarManagementController(
    AppDbContext dbContext,
    IOfferingScheduleService offeringScheduleService) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<ActionResult<CalendarDashboardDto>> Dashboard([FromQuery] DateTime? startUtc, [FromQuery] DateTime? endUtc, CancellationToken cancellationToken)
    {
        var ownerId = CurrentUserId();
        var categories = await dbContext.Categories
            .AsNoTracking()
            .Where(x => x.OwnerId == ownerId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var offerings = await dbContext.Offerings
            .AsNoTracking()
            .Where(x => x.OwnerId == ownerId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var query = dbContext.Occurrences
            .AsNoTracking()
            .Include(x => x.Slots)
            .Include(x => x.Offering)
            .Include(x => x.RuleGroup)
            .Where(x => x.OwnerId == ownerId);

        if (startUtc.HasValue && endUtc.HasValue)
        {
            query = query.Where(x => x.Slots.Any(s => s.StartUtc <= endUtc && s.EndUtc >= startUtc));
        }

        var occurrences = await query.OrderBy(x => x.Title).ToListAsync(cancellationToken);
        var pages = await dbContext.ShowcasePages
            .AsNoTracking()
            .Where(x => x.OwnerId == ownerId)
            .Select(x => new ShowcasePageSummaryDto
            {
                Id = x.Id,
                Name = x.Name,
                Slug = x.Slug,
                IsActive = x.IsActive,
                IsHomePage = x.IsHomePage,
                ItemCount = x.Items.Count
            })
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return Ok(new CalendarDashboardDto
        {
            Nodes = categories.Select(MapCategoryNode)
                .Concat(offerings.Select(MapOfferingNode))
                .ToList(),
            Occurrences = occurrences.Select(MapOccurrence).ToList(),
            ShowcasePages = pages
        });
    }

    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyCollection<CategoryDto>>> Categories(CancellationToken cancellationToken)
    {
        var ownerId = CurrentUserId();
        var categories = await dbContext.Categories
            .AsNoTracking()
            .Where(x => x.OwnerId == ownerId)
            .OrderBy(x => x.Name)
            .Select(x => new CategoryDto
            {
                Id = x.Id,
                ParentCategoryId = x.ParentCategoryId,
                Name = x.Name,
                Color = x.Color,
                IsActive = x.IsActive
            })
            .ToListAsync(cancellationToken);

        return Ok(categories);
    }

    [HttpPost("categories")]
    public async Task<ActionResult<CategoryDto>> SaveCategory(SaveCategoryRequest request, CancellationToken cancellationToken)
    {
        var ownerId = CurrentUserId();
        Category category;
        if (request.Id.HasValue)
        {
            category = await dbContext.Categories.FirstOrDefaultAsync(x => x.Id == request.Id && x.OwnerId == ownerId, cancellationToken)
                ?? throw new InvalidOperationException("Category not found.");
        }
        else
        {
            category = new Category { OwnerId = ownerId };
            dbContext.Categories.Add(category);
        }

        category.ParentCategoryId = request.ParentCategoryId;
        category.Name = request.Name.Trim();
        category.Color = request.Color;
        category.IsActive = request.IsActive;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new CategoryDto
        {
            Id = category.Id,
            ParentCategoryId = category.ParentCategoryId,
            Name = category.Name,
            Color = category.Color,
            IsActive = category.IsActive
        });
    }

    [HttpDelete("categories/{id:int}")]
    public async Task<IActionResult> DeleteCategory(int id, CancellationToken cancellationToken)
    {
        var ownerId = CurrentUserId();
        var category = await dbContext.Categories.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        var children = await dbContext.Categories.AnyAsync(x => x.ParentCategoryId == id && x.OwnerId == ownerId, cancellationToken);
        var offerings = await dbContext.Offerings.AnyAsync(x => x.CategoryId == id && x.OwnerId == ownerId, cancellationToken);
        if (children || offerings)
        {
            return BadRequest("Remove child categories and offerings first.");
        }

        dbContext.Categories.Remove(category);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("offerings")]
    public async Task<ActionResult<IReadOnlyCollection<OfferingSummaryDto>>> Offerings(CancellationToken cancellationToken)
    {
        var ownerId = CurrentUserId();
        var offerings = await dbContext.Offerings
            .AsNoTracking()
            .Include(x => x.Category)
            .Include(x => x.RuleGroups)
            .ThenInclude(x => x.Timeslots)
            .Where(x => x.OwnerId == ownerId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return Ok(offerings.Select(x => new OfferingSummaryDto
        {
            Id = x.Id,
            Name = x.Name,
            Description = x.Description,
            CategoryId = x.CategoryId,
            CategoryName = x.Category?.Name ?? string.Empty,
            Color = x.Color,
            IsPublished = x.IsPublished,
            IsActive = x.IsActive,
            RuleGroupCount = x.RuleGroups.Count,
            TimeslotCount = x.RuleGroups.Sum(g => g.Timeslots.Count)
        }).ToList());
    }

    [HttpGet("offerings/{id:int}")]
    public async Task<ActionResult<OfferingEditorDto>> Offering(int id, CancellationToken cancellationToken)
    {
        var ownerId = CurrentUserId();
        var offering = await dbContext.Offerings
            .AsNoTracking()
            .Include(x => x.RuleGroups.OrderBy(g => g.Id))
            .ThenInclude(x => x.Timeslots.OrderBy(t => t.Id))
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken);

        if (offering is null)
        {
            return NotFound();
        }

        return Ok(MapOfferingEditor(offering));
    }

    [HttpPost("offerings")]
    public async Task<ActionResult<OfferingEditorDto>> SaveOffering(SaveOfferingRequest request, CancellationToken cancellationToken)
    {
        var ownerId = CurrentUserId();
        Offering offering;
        if (request.Id.HasValue)
        {
            offering = await dbContext.Offerings
                .Include(x => x.RuleGroups)
                .ThenInclude(x => x.Timeslots)
                .FirstOrDefaultAsync(x => x.Id == request.Id && x.OwnerId == ownerId, cancellationToken)
                ?? throw new InvalidOperationException("Offering not found.");
        }
        else
        {
            offering = new Offering { OwnerId = ownerId };
            dbContext.Offerings.Add(offering);
        }

        offering.CategoryId = request.CategoryId;
        offering.Name = request.Name.Trim();
        offering.Description = request.Description.Trim();
        offering.Color = request.Color;
        offering.CoverImageUrl = request.CoverImageUrl.Trim();
        offering.IsPublished = request.IsPublished;
        offering.IsActive = request.IsActive;
        offering.AllowNewBookings = request.AllowNewBookings;
        offering.LimitBookingPeriodDays = request.LimitBookingPeriodDays;
        offering.RequireSignUpEveryTime = request.RequireSignUpEveryTime;

        SyncRuleGroups(offering, request.RuleGroups);
        await dbContext.SaveChangesAsync(cancellationToken);
        await offeringScheduleService.RebuildOccurrencesAsync(offering, cancellationToken);

        var refreshed = await dbContext.Offerings
            .AsNoTracking()
            .Include(x => x.RuleGroups)
            .ThenInclude(x => x.Timeslots)
            .FirstAsync(x => x.Id == offering.Id, cancellationToken);

        return Ok(MapOfferingEditor(refreshed));
    }

    [HttpDelete("offerings/{id:int}")]
    public async Task<IActionResult> DeleteOffering(int id, CancellationToken cancellationToken)
    {
        var ownerId = CurrentUserId();
        var offering = await dbContext.Offerings
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken);
        if (offering is null)
        {
            return NotFound();
        }

        dbContext.Offerings.Remove(offering);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("showcase-pages")]
    public async Task<ActionResult<IReadOnlyCollection<ShowcasePageSummaryDto>>> ShowcasePages(CancellationToken cancellationToken)
    {
        var ownerId = CurrentUserId();
        var pages = await dbContext.ShowcasePages
            .AsNoTracking()
            .Where(x => x.OwnerId == ownerId)
            .OrderBy(x => x.Name)
            .Select(x => new ShowcasePageSummaryDto
            {
                Id = x.Id,
                Name = x.Name,
                Slug = x.Slug,
                IsActive = x.IsActive,
                IsHomePage = x.IsHomePage,
                ItemCount = x.Items.Count
            })
            .ToListAsync(cancellationToken);

        return Ok(pages);
    }

    [HttpGet("showcase-pages/{id:int}")]
    public async Task<ActionResult<ShowcasePageEditorDto>> ShowcasePage(int id, CancellationToken cancellationToken)
    {
        var ownerId = CurrentUserId();
        var page = await dbContext.ShowcasePages
            .AsNoTracking()
            .Include(x => x.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Id))
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken);
        if (page is null)
        {
            return NotFound();
        }

        return Ok(MapPage(page));
    }

    [HttpPost("showcase-pages")]
    public async Task<ActionResult<ShowcasePageEditorDto>> SaveShowcasePage(SaveShowcasePageRequest request, CancellationToken cancellationToken)
    {
        var validSourceTypes = new[] { "category", "offering" };
        var validCarouselTypes = new[] { "carousel", "rail" };
        var invalidItem = request.Items.FirstOrDefault(x =>
            string.IsNullOrWhiteSpace(x.SourceType) ||
            string.IsNullOrWhiteSpace(x.CarouselType) ||
            !validSourceTypes.Contains(x.SourceType, StringComparer.OrdinalIgnoreCase) ||
            !validCarouselTypes.Contains(x.CarouselType, StringComparer.OrdinalIgnoreCase));

        if (invalidItem is not null)
        {
            return BadRequest("Each booking data row must include both Source Type and Carousel Type.");
        }

        var ownerId = CurrentUserId();
        ShowcasePage page;
        if (request.Id.HasValue)
        {
            page = await dbContext.ShowcasePages
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.Id == request.Id && x.OwnerId == ownerId, cancellationToken)
                ?? throw new InvalidOperationException("Page not found.");
        }
        else
        {
            page = new ShowcasePage { OwnerId = ownerId };
            dbContext.ShowcasePages.Add(page);
        }

        page.Name = request.Name.Trim();
        page.Slug = Slugify(request.Slug, request.Name);
        page.IsActive = request.IsActive;
        page.IsHomePage = request.IsHomePage;

        if (page.IsHomePage)
        {
            var otherPages = await dbContext.ShowcasePages
                .Where(x => x.OwnerId == ownerId && x.Id != page.Id && x.IsHomePage)
                .ToListAsync(cancellationToken);
            foreach (var other in otherPages)
            {
                other.IsHomePage = false;
            }
        }

        SyncPageItems(page, request.Items);
        await dbContext.SaveChangesAsync(cancellationToken);

        var refreshed = await dbContext.ShowcasePages
            .AsNoTracking()
            .Include(x => x.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Id))
            .FirstAsync(x => x.Id == page.Id, cancellationToken);

        return Ok(MapPage(refreshed));
    }

    [HttpDelete("showcase-pages/{id:int}")]
    public async Task<IActionResult> DeleteShowcasePage(int id, CancellationToken cancellationToken)
    {
        var ownerId = CurrentUserId();
        var page = await dbContext.ShowcasePages
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId, cancellationToken);
        if (page is null)
        {
            return NotFound();
        }

        dbContext.ShowcasePages.Remove(page);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private string CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;

    private static CalendarNodeDto MapCategoryNode(Category category) => new()
    {
        Id = category.Id,
        ParentCategoryId = category.ParentCategoryId,
        NodeType = "category",
        Name = category.Name,
        Color = category.Color,
        IsActive = category.IsActive
    };

    private static CalendarNodeDto MapOfferingNode(Offering offering) => new()
    {
        Id = offering.Id,
        ParentCategoryId = offering.CategoryId,
        NodeType = "offering",
        Name = offering.Name,
        Color = offering.Color,
        IsPublished = offering.IsPublished,
        IsActive = offering.IsActive
    };

    private static OfferingEditorDto MapOfferingEditor(Offering offering) => new()
    {
        Id = offering.Id,
        CategoryId = offering.CategoryId,
        Name = offering.Name,
        Description = offering.Description,
        Color = offering.Color,
        CoverImageUrl = offering.CoverImageUrl,
        IsPublished = offering.IsPublished,
        IsActive = offering.IsActive,
        AllowNewBookings = offering.AllowNewBookings,
        LimitBookingPeriodDays = offering.LimitBookingPeriodDays,
        RequireSignUpEveryTime = offering.RequireSignUpEveryTime,
        RuleGroups = offering.RuleGroups
            .OrderBy(x => x.Id)
            .Select(x => new RuleGroupEditorDto
            {
                Id = x.Id,
                Name = x.Name,
                Color = x.Color,
                StartDateUtc = x.StartDateUtc,
                EndDateUtc = x.EndDateUtc,
                FrequencyType = x.FrequencyType,
                Weekdays = ParseWeekdays(x.WeekdaysCsv),
                Interval = x.Interval,
                NthWeekOfMonth = x.NthWeekOfMonth,
                DayOfMonth = x.DayOfMonth,
                Timeslots = x.Timeslots.OrderBy(t => t.Id).Select(t => new TimeslotEditorDto
                {
                    Id = t.Id,
                    IsAllDay = t.IsAllDay,
                    StartTime = t.StartTime,
                    EndTime = t.EndTime,
                    RepeatGeneratedSlots = t.RepeatGeneratedSlots,
                    RepeatUntilLastStartTime = t.RepeatUntilLastStartTime,
                    RepeatEveryMinutes = t.RepeatEveryMinutes
                }).ToList()
            }).ToList()
    };

    private static ShowcasePageEditorDto MapPage(ShowcasePage page) => new()
    {
        Id = page.Id,
        Name = page.Name,
        Slug = page.Slug,
        IsActive = page.IsActive,
        IsHomePage = page.IsHomePage,
        Items = page.Items.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).Select(x => new ShowcasePageItemDto
        {
            Id = x.Id,
            Name = x.Name,
            SourceType = x.SourceType,
            SourceId = x.SourceId,
            CarouselType = x.CarouselType,
            Description = x.Description,
            Blur = x.Blur,
            HideTitle = x.HideTitle,
            ShowDescription = x.ShowDescription,
            IsActive = x.IsActive,
            SortOrder = x.SortOrder
        }).ToList()
    };

    private static OccurrenceDto MapOccurrence(Occurrence occurrence) => new()
    {
        Id = occurrence.Id,
        OfferingId = occurrence.OfferingId,
        RuleGroupId = occurrence.RuleGroupId,
        Title = occurrence.Title,
        Description = occurrence.Description,
        IsPublished = occurrence.IsPublished,
        OfferingName = occurrence.Offering?.Name,
        RuleGroupName = occurrence.RuleGroup?.Name,
        Color = occurrence.Color,
        Slots = occurrence.Slots.Select(x => new OccurrenceSlotDto
        {
            Id = x.Id,
            StartUtc = x.StartUtc,
            EndUtc = x.EndUtc
        }).OrderBy(x => x.StartUtc).ToList()
    };

    private static void SyncRuleGroups(Offering offering, IReadOnlyCollection<RuleGroupEditorDto> ruleGroups)
    {
        var incomingIds = ruleGroups.Where(x => x.Id > 0).Select(x => x.Id).ToHashSet();
        foreach (var existing in offering.RuleGroups.Where(x => !incomingIds.Contains(x.Id)).ToList())
        {
            offering.RuleGroups.Remove(existing);
        }

        foreach (var ruleGroupDto in ruleGroups)
        {
            var ruleGroup = offering.RuleGroups.FirstOrDefault(x => x.Id == ruleGroupDto.Id);
            if (ruleGroup is null)
            {
                ruleGroup = new OfferingRuleGroup();
                offering.RuleGroups.Add(ruleGroup);
            }

            ruleGroup.Name = ruleGroupDto.Name.Trim();
            ruleGroup.Color = ruleGroupDto.Color;
            ruleGroup.StartDateUtc = AsUtcDate(ruleGroupDto.StartDateUtc);
            ruleGroup.EndDateUtc = ruleGroupDto.EndDateUtc.HasValue ? AsUtcDate(ruleGroupDto.EndDateUtc.Value) : null;
            ruleGroup.FrequencyType = ruleGroupDto.FrequencyType;
            ruleGroup.WeekdaysCsv = string.Join(",", ruleGroupDto.Weekdays.OrderBy(x => x));
            ruleGroup.Interval = Math.Max(ruleGroupDto.Interval, 1);
            ruleGroup.NthWeekOfMonth = ruleGroupDto.NthWeekOfMonth;
            ruleGroup.DayOfMonth = ruleGroupDto.DayOfMonth;

            var incomingTimeslotIds = ruleGroupDto.Timeslots.Where(x => x.Id > 0).Select(x => x.Id).ToHashSet();
            foreach (var existingTimeslot in ruleGroup.Timeslots.Where(x => !incomingTimeslotIds.Contains(x.Id)).ToList())
            {
                ruleGroup.Timeslots.Remove(existingTimeslot);
            }

            foreach (var timeslotDto in ruleGroupDto.Timeslots)
            {
                var timeslot = ruleGroup.Timeslots.FirstOrDefault(x => x.Id == timeslotDto.Id);
                if (timeslot is null)
                {
                    timeslot = new OfferingTimeslot();
                    ruleGroup.Timeslots.Add(timeslot);
                }

                timeslot.IsAllDay = timeslotDto.IsAllDay;
                timeslot.StartTime = timeslotDto.StartTime;
                timeslot.EndTime = timeslotDto.EndTime;
                timeslot.RepeatGeneratedSlots = timeslotDto.RepeatGeneratedSlots;
                timeslot.RepeatUntilLastStartTime = timeslotDto.RepeatUntilLastStartTime;
                timeslot.RepeatEveryMinutes = timeslotDto.RepeatEveryMinutes;
            }
        }
    }

    private static void SyncPageItems(ShowcasePage page, IReadOnlyCollection<ShowcasePageItemDto> items)
    {
        var incomingIds = items.Where(x => x.Id > 0).Select(x => x.Id).ToHashSet();
        foreach (var existing in page.Items.Where(x => !incomingIds.Contains(x.Id)).ToList())
        {
            page.Items.Remove(existing);
        }

        foreach (var itemDto in items)
        {
            var item = page.Items.FirstOrDefault(x => x.Id == itemDto.Id);
            if (item is null)
            {
                item = new ShowcasePageItem();
                page.Items.Add(item);
            }

            item.Name = itemDto.Name.Trim();
            item.SourceType = itemDto.SourceType;
            item.SourceId = itemDto.SourceId;
            item.CarouselType = itemDto.CarouselType;
            item.Description = itemDto.Description;
            item.Blur = itemDto.Blur;
            item.HideTitle = itemDto.HideTitle;
            item.ShowDescription = itemDto.ShowDescription;
            item.IsActive = itemDto.IsActive;
            item.SortOrder = itemDto.SortOrder;
        }
    }

    private static string Slugify(string slug, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(slug) ? fallback : slug;
        var clean = string.Concat(value.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-'));
        clean = string.Join('-', clean.Split('-', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(clean) ? Guid.NewGuid().ToString("N")[..8] : clean;
    }

    private static List<int> ParseWeekdays(string csv)
        => csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(static value => int.TryParse(value, out var parsed) ? parsed : -1)
            .Where(static value => value >= 0 && value <= 6)
            .ToList();

    private static DateTime AsUtcDate(DateTime value)
        => DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
}
