using System.Globalization;
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
[Route("api/lookups")]
[Authorize]
public sealed class LookupsController(AppDbContext dbContext, IActivityService activityService) : ControllerBase
{
    [HttpGet("countries")]
    [AllowAnonymous]
    public ActionResult<IReadOnlyCollection<CountryDto>> Countries()
    {
        var countries = CultureInfo
            .GetCultures(CultureTypes.SpecificCultures)
            .Select(culture =>
            {
                try
                {
                    var region = new RegionInfo(culture.Name);
                    return new CountryDto { Code = region.TwoLetterISORegionName, Name = region.EnglishName };
                }
                catch
                {
                    return null;
                }
            })
            .Where(country => country is not null)
            .DistinctBy(country => country!.Code)
            .Select(country => country!)
            .Where(country => country.Code.Length == 2 && country.Code.All(char.IsLetter))
            .OrderBy(country => country.Name)
            .ToList();

        return Ok(countries);
    }

    [HttpGet("groups")]
    public async Task<ActionResult<IReadOnlyCollection<EventGroupDto>>> Groups()
    {
        var ownerId = CurrentUserId();
        var groups = await dbContext.EventGroups
            .AsNoTracking()
            .Where(x => x.OwnerId == ownerId)
            .OrderBy(x => x.Name)
            .Select(x => new EventGroupDto { Id = x.Id, Name = x.Name, Description = x.Description })
            .ToListAsync();

        return Ok(groups);
    }

    [HttpPost("groups")]
    public async Task<ActionResult<EventGroupDto>> CreateGroup(EventGroupDto request, CancellationToken cancellationToken)
    {
        var group = new EventGroup
        {
            OwnerId = CurrentUserId(),
            Name = request.Name,
            Description = request.Description
        };

        dbContext.EventGroups.Add(group);
        await dbContext.SaveChangesAsync(cancellationToken);
        await activityService.LogAsync(CurrentUserId(), "group.create", group.Name, cancellationToken);

        return Ok(new EventGroupDto { Id = group.Id, Name = group.Name, Description = group.Description });
    }

    [HttpGet("staff")]
    public async Task<ActionResult<IReadOnlyCollection<StaffDto>>> Staff()
    {
        var ownerId = CurrentUserId();
        var staff = await dbContext.StaffMembers
            .AsNoTracking()
            .Include(x => x.OfferingMappings)
            .ThenInclude(x => x.Offering)
            .Where(x => x.OwnerId == ownerId)
            .OrderBy(x => x.Name)
            .ToListAsync();

        return Ok(staff.Select(MapStaff));
    }

    [HttpPost("staff")]
    public async Task<ActionResult<StaffDto>> SaveStaff(StaffDto request, CancellationToken cancellationToken)
    {
        var ownerId = CurrentUserId();
        var isCreate = request.Id <= 0;
        StaffMember? staff;

        if (isCreate)
        {
            staff = new StaffMember
            {
                OwnerId = ownerId
            };

            dbContext.StaffMembers.Add(staff);
        }
        else
        {
            staff = await dbContext.StaffMembers
                .Include(x => x.OfferingMappings)
                .FirstOrDefaultAsync(x => x.OwnerId == ownerId && x.Id == request.Id, cancellationToken);

            if (staff is null)
            {
                return NotFound();
            }
        }

        staff.Name = request.Name.Trim();
        staff.Email = request.Email.Trim();
        staff.TrainingQualityRating = Math.Clamp(decimal.Round(request.TrainingQualityRating, 0, MidpointRounding.AwayFromZero), 0m, 5m);

        if (staff.OfferingMappings.Count > 0)
        {
            dbContext.StaffOfferingMappings.RemoveRange(staff.OfferingMappings);
            staff.OfferingMappings.Clear();
        }

        foreach (var mapping in request.OfferingMappings
                     .Where(x => x.OfferingId > 0)
                     .GroupBy(x => x.OfferingId)
                     .Select(x => x.First()))
        {
            staff.OfferingMappings.Add(new StaffOfferingMapping
            {
                OfferingId = mapping.OfferingId,
                ProficiencyLevel = string.IsNullOrWhiteSpace(mapping.ProficiencyLevel) ? "Intermediate" : mapping.ProficiencyLevel.Trim()
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await activityService.LogAsync(ownerId, isCreate ? "staff.create" : "staff.update", staff.Name, cancellationToken);

        var saved = await dbContext.StaffMembers
            .AsNoTracking()
            .Include(x => x.OfferingMappings)
            .ThenInclude(x => x.Offering)
            .FirstAsync(x => x.Id == staff.Id, cancellationToken);

        return Ok(MapStaff(saved));
    }

    [HttpGet("staff/{id:int}/calendar")]
    public async Task<ActionResult<StaffCalendarDto>> StaffCalendar(int id, [FromQuery] DateTime? startUtc, [FromQuery] DateTime? endUtc, [FromQuery] int? offeringId)
    {
        if (!startUtc.HasValue || !endUtc.HasValue)
        {
            return BadRequest("Start and end dates are required.");
        }

        var ownerId = CurrentUserId();
        var staff = await dbContext.StaffMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.OwnerId == ownerId && x.Id == id);

        if (staff is null)
        {
            return NotFound();
        }

        var rangeStart = DateTime.SpecifyKind(startUtc.Value, DateTimeKind.Utc);
        var rangeEnd = DateTime.SpecifyKind(endUtc.Value, DateTimeKind.Utc);

        var occurrences = await dbContext.Occurrences
            .AsNoTracking()
            .Include(x => x.Offering)
            .Include(x => x.Slots)
            .Where(x => x.OwnerId == ownerId && x.StaffId == id)
            .Where(x => !offeringId.HasValue || x.OfferingId == offeringId.Value)
            .Where(x => x.Slots.Any(slot => slot.StartUtc <= rangeEnd && slot.EndUtc >= rangeStart))
            .OrderBy(x => x.Title)
            .ToListAsync();

        var bookingCounts = await dbContext.Bookings
            .AsNoTracking()
            .Where(x => x.OccurrenceSlot != null && x.OccurrenceSlot.Occurrence != null)
            .Where(x => x.OccurrenceSlot!.Occurrence!.OwnerId == ownerId && x.OccurrenceSlot.Occurrence.StaffId == id)
            .Where(x => !offeringId.HasValue || x.OccurrenceSlot!.Occurrence!.OfferingId == offeringId.Value)
            .Where(x => x.OccurrenceSlot!.StartUtc <= rangeEnd && x.OccurrenceSlot.EndUtc >= rangeStart)
            .GroupBy(x => x.OccurrenceSlotId)
            .Select(x => new { OccurrenceSlotId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.OccurrenceSlotId, x => x.Count);

        var days = Enumerable.Range(0, (rangeEnd.Date - rangeStart.Date).Days + 1)
            .Select(offset => rangeStart.Date.AddDays(offset))
            .Select(day => new StaffCalendarDayDto
            {
                DateUtc = day,
                Entries = occurrences
                    .SelectMany(occurrence => occurrence.Slots
                        .Where(slot => slot.StartUtc.Date == day.Date)
                        .OrderBy(slot => slot.StartUtc)
                        .Select(slot => new StaffCalendarEntryDto
                        {
                            OccurrenceId = occurrence.Id,
                            OfferingId = occurrence.OfferingId,
                            Title = occurrence.Title,
                            OfferingName = occurrence.Offering?.Name ?? occurrence.Title,
                            Description = occurrence.Description,
                            Color = occurrence.Color,
                            SlotStartUtc = slot.StartUtc,
                            SlotEndUtc = slot.EndUtc,
                            TotalBookingCount = bookingCounts.GetValueOrDefault(slot.Id)
                        }))
                    .OrderBy(entry => entry.SlotStartUtc)
                    .ToList()
            })
            .ToList();

        return Ok(new StaffCalendarDto
        {
            StaffId = staff.Id,
            StaffName = staff.Name,
            StartUtc = rangeStart,
            EndUtc = rangeEnd,
            Days = days
        });
    }

    private string CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;

    private static StaffDto MapStaff(StaffMember staff)
        => new()
        {
            Id = staff.Id,
            Name = staff.Name,
            Email = staff.Email,
            TrainingQualityRating = staff.TrainingQualityRating,
            OfferingMappings = staff.OfferingMappings
                .OrderBy(x => x.Offering?.Name ?? string.Empty)
                .Select(x => new StaffOfferingMappingDto
                {
                    Id = x.Id,
                    OfferingId = x.OfferingId,
                    OfferingName = x.Offering?.Name ?? string.Empty,
                    ProficiencyLevel = x.ProficiencyLevel
                })
                .ToList()
        };
}
