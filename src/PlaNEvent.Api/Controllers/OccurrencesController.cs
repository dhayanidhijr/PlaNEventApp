using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlaNEvent.Api.Data;
using PlaNEvent.Api.Models;
using PlaNEvent.Api.Services;
using PlaNEvent.Shared.Contracts;

namespace PlaNEvent.Api.Controllers;

[ApiController]
[Route("api/occurrences")]
[Authorize]
public sealed class OccurrencesController(AppDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<OccurrencePageDto>> List([FromQuery] DateTime? startUtc, [FromQuery] DateTime? endUtc, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (!startUtc.HasValue || !endUtc.HasValue)
        {
            return BadRequest("A start and end date filter is required.");
        }

        page = Math.Max(1, page);
        pageSize = pageSize switch
        {
            10 or 20 or 50 or 100 => pageSize,
            _ => 20
        };

        var query = dbContext.Occurrences
            .AsNoTracking()
            .Include(x => x.Slots)
            .Include(x => x.EventGroup)
            .Include(x => x.Staff)
            .Include(x => x.Offering)
            .Include(x => x.RuleGroup)
            .Where(x => x.OwnerId == CurrentUserId());

        query = query.Where(x => x.Slots.Any(s => s.StartUtc <= endUtc && s.EndUtc >= startUtc));

        var totalCount = await query.CountAsync();
        var occurrences = await query
            .OrderBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new OccurrencePageDto
        {
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            Items = occurrences.Select(Map).ToList()
        });
    }

    [HttpPost]
    public ActionResult<OccurrenceDto> Create(CreateOccurrenceRequest request, CancellationToken cancellationToken)
    {
        _ = request;
        _ = cancellationToken;
        return BadRequest("Occurrences are generated from offerings. Create or edit an offering to update its occurrences.");
    }

    [HttpPut("{id}/publish")]
    public IActionResult Publish(int id, CancellationToken cancellationToken)
    {
        _ = id;
        _ = cancellationToken;
        return BadRequest("Occurrence publish state follows the offering. Update the offering instead.");
    }

    private string CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;

    private static OccurrenceDto Map(Occurrence occurrence)
        => new()
        {
            Id = occurrence.Id,
            OfferingId = occurrence.OfferingId,
            RuleGroupId = occurrence.RuleGroupId,
            Title = occurrence.Title,
            Description = occurrence.Description,
            EventGroupId = occurrence.EventGroupId,
            StaffId = occurrence.StaffId,
            EventGroupName = occurrence.EventGroup?.Name,
            StaffName = occurrence.Staff?.Name,
            OfferingName = occurrence.Offering?.Name,
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
