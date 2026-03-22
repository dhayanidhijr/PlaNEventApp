using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlaNEvent.Api.Data;
using PlaNEvent.Shared.Contracts;

namespace PlaNEvent.Api.Controllers;

[ApiController]
[Route("api/public/sales")]
[AllowAnonymous]
public sealed class PublicSalesController(AppDbContext dbContext) : ControllerBase
{
    [HttpGet("{ownerSlug}")]
    public async Task<ActionResult<IReadOnlyCollection<OccurrenceDto>>> List(string ownerSlug)
    {
        var owner = await dbContext.Users.FirstOrDefaultAsync(x => x.PublicSlug == ownerSlug && !x.IsDisabled);
        if (owner is null)
        {
            return NotFound();
        }

        var occurrences = await dbContext.Occurrences
            .AsNoTracking()
            .Include(x => x.Slots)
            .Include(x => x.EventGroup)
            .Include(x => x.Staff)
            .Include(x => x.Offering)
            .Include(x => x.RuleGroup)
            .Where(x => x.OwnerId == owner.Id && x.IsPublished)
            .OrderBy(x => x.Title)
            .ToListAsync();

        var list = occurrences.Select(x => new OccurrenceDto
        {
            Id = x.Id,
            OfferingId = x.OfferingId,
            RuleGroupId = x.RuleGroupId,
            Title = x.Title,
            Description = x.Description,
            EventGroupId = x.EventGroupId,
            StaffId = x.StaffId,
            EventGroupName = x.EventGroup?.Name,
            StaffName = x.Staff?.Name,
            OfferingName = x.Offering?.Name,
            RuleGroupName = x.RuleGroup?.Name,
            Color = x.Color,
            IsPublished = true,
            OwnerPublicSlug = ownerSlug,
            Slots = x.Slots.Select(s => new OccurrenceSlotDto
            {
                Id = s.Id,
                StartUtc = s.StartUtc,
                EndUtc = s.EndUtc
            }).OrderBy(s => s.StartUtc).ToList()
        }).ToList();

        return Ok(list);
    }
}
