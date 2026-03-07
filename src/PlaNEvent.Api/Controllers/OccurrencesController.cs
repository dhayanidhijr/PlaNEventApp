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
public sealed class OccurrencesController(AppDbContext dbContext, IActivityService activityService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<OccurrenceDto>>> List([FromQuery] DateTime? startUtc, [FromQuery] DateTime? endUtc)
    {
        var query = dbContext.Occurrences
            .AsNoTracking()
            .Include(x => x.Slots)
            .Include(x => x.EventGroup)
            .Include(x => x.Staff)
            .Where(x => x.OwnerId == CurrentUserId());

        if (startUtc.HasValue && endUtc.HasValue)
        {
            query = query.Where(x => x.Slots.Any(s => s.StartUtc >= startUtc && s.EndUtc <= endUtc));
        }

        var occurrences = await query.OrderBy(x => x.Id).ToListAsync();
        return Ok(occurrences.Select(Map).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<OccurrenceDto>> Create(CreateOccurrenceRequest request, CancellationToken cancellationToken)
    {
        if (request.Slots.Count == 0)
        {
            return BadRequest("At least one slot is required.");
        }

        var occurrence = new Occurrence
        {
            OwnerId = CurrentUserId(),
            Title = request.Title,
            Description = request.Description,
            EventGroupId = request.EventGroupId,
            StaffId = request.StaffId,
            IsPublished = request.PublishOnCreate
        };

        foreach (var slot in ExpandSlots(request))
        {
            occurrence.Slots.Add(new OccurrenceSlot { StartUtc = slot.StartUtc, EndUtc = slot.EndUtc });
        }

        dbContext.Occurrences.Add(occurrence);
        await dbContext.SaveChangesAsync(cancellationToken);
        await activityService.LogAsync(CurrentUserId(), "occurrence.create", occurrence.Title, cancellationToken);

        await dbContext.Entry(occurrence).Reference(x => x.EventGroup).LoadAsync(cancellationToken);
        await dbContext.Entry(occurrence).Reference(x => x.Staff).LoadAsync(cancellationToken);

        return Ok(Map(occurrence));
    }

    [HttpPut("{id}/publish")]
    public async Task<IActionResult> Publish(int id, CancellationToken cancellationToken)
    {
        var occurrence = await dbContext.Occurrences.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == CurrentUserId(), cancellationToken);
        if (occurrence is null)
        {
            return NotFound();
        }

        occurrence.IsPublished = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        await activityService.LogAsync(CurrentUserId(), "occurrence.publish", occurrence.Title, cancellationToken);

        return NoContent();
    }

    private string CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;

    private static IEnumerable<SlotInputDto> ExpandSlots(CreateOccurrenceRequest request)
    {
        if (!request.RepeatUntilUtc.HasValue)
        {
            return request.Slots;
        }

        var list = new List<SlotInputDto>();
        var endDate = request.RepeatUntilUtc.Value.Date;
        foreach (var slot in request.Slots)
        {
            var day = slot.StartUtc.Date;
            while (day <= endDate)
            {
                var duration = slot.EndUtc - slot.StartUtc;
                var start = day + slot.StartUtc.TimeOfDay;
                list.Add(new SlotInputDto
                {
                    StartUtc = DateTime.SpecifyKind(start, DateTimeKind.Utc),
                    EndUtc = DateTime.SpecifyKind(start + duration, DateTimeKind.Utc)
                });

                day = day.AddDays(1);
            }
        }

        return list;
    }

    private static OccurrenceDto Map(Occurrence occurrence)
        => new()
        {
            Id = occurrence.Id,
            Title = occurrence.Title,
            Description = occurrence.Description,
            EventGroupId = occurrence.EventGroupId,
            StaffId = occurrence.StaffId,
            EventGroupName = occurrence.EventGroup?.Name,
            StaffName = occurrence.Staff?.Name,
            IsPublished = occurrence.IsPublished,
            Slots = occurrence.Slots.Select(x => new OccurrenceSlotDto
            {
                Id = x.Id,
                StartUtc = x.StartUtc,
                EndUtc = x.EndUtc
            }).OrderBy(x => x.StartUtc).ToList()
        };
}
