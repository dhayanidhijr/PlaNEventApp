using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlaNEvent.Api.Data;
using PlaNEvent.Api.Models;
using PlaNEvent.Api.Services;
using PlaNEvent.Shared.Contracts;

namespace PlaNEvent.Api.Controllers;

[ApiController]
[Route("api/bookings")]
[Authorize]
public sealed class BookingsController(
    AppDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    IActivityService activityService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<BookingPageDto>> List([FromQuery] DateTime? startUtc, [FromQuery] DateTime? endUtc, [FromQuery] int? occurrenceId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
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

        var userId = CurrentUserId();

        var query = dbContext.Bookings
            .AsNoTracking()
            .Include(x => x.Occurrence)
            .ThenInclude(x => x!.Offering)
            .Include(x => x.OccurrenceSlot)
            .Where(x => x.CustomerId == userId || x.Occurrence!.OwnerId == userId)
            .Where(x => x.OccurrenceSlot != null && x.OccurrenceSlot.StartUtc <= endUtc && x.OccurrenceSlot.EndUtc >= startUtc);

        if (occurrenceId.HasValue)
        {
            query = query.Where(x => x.OccurrenceId == occurrenceId.Value);
        }

        var users = await userManager.Users.ToDictionaryAsync(x => x.Id, x => x.Email ?? string.Empty);
        var groupedBookings = (await query
                .OrderByDescending(x => x.CreatedAtUtc)
                .ToListAsync())
            .GroupBy(x => new
            {
                x.OccurrenceId,
                x.OccurrenceSlotId,
                x.CustomerId
            })
            .Select(group =>
            {
                var latestBooking = group
                    .OrderByDescending(x => x.CreatedAtUtc)
                    .ThenByDescending(x => x.Id)
                    .First();

                return new BookingDto
                {
                    Id = latestBooking.Id,
                    OccurrenceId = latestBooking.OccurrenceId,
                    OccurrenceSlotId = latestBooking.OccurrenceSlotId,
                    CustomerId = latestBooking.CustomerId,
                    CustomerEmail = users.GetValueOrDefault(latestBooking.CustomerId, string.Empty),
                    CustomerNotes = latestBooking.CustomerNotes,
                    CreatedAtUtc = latestBooking.CreatedAtUtc,
                    OccurrenceName = latestBooking.Occurrence?.Offering?.Name ?? latestBooking.Occurrence?.Title ?? latestBooking.OccurrenceId.ToString(),
                    SlotStartUtc = latestBooking.OccurrenceSlot?.StartUtc,
                    TotalBookingCount = group.Count()
                };
            })
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .ToList();

        var totalCount = groupedBookings.Count;
        var visibleBookings = groupedBookings
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Ok(new BookingPageDto
        {
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            Items = visibleBookings
        });
    }

    [HttpPost]
    public async Task<ActionResult<BookingDto>> Create(BookingRequest request, CancellationToken cancellationToken)
    {
        var occurrence = await dbContext.Occurrences
            .Include(x => x.Slots)
            .FirstOrDefaultAsync(x => x.Id == request.OccurrenceId && x.IsPublished, cancellationToken);

        if (occurrence is null)
        {
            return NotFound("Occurrence not found or not published.");
        }

        if (!occurrence.Slots.Any(x => x.Id == request.OccurrenceSlotId))
        {
            return BadRequest("Invalid slot.");
        }

        var booking = new Booking
        {
            OccurrenceId = request.OccurrenceId,
            OccurrenceSlotId = request.OccurrenceSlotId,
            CustomerId = CurrentUserId(),
            CustomerNotes = request.CustomerNotes
        };

        dbContext.Bookings.Add(booking);
        await dbContext.SaveChangesAsync(cancellationToken);
        await activityService.LogAsync(CurrentUserId(), "booking.create", $"occurrence:{request.OccurrenceId}", cancellationToken);

        var customer = await userManager.FindByIdAsync(CurrentUserId());

        return Ok(new BookingDto
        {
            Id = booking.Id,
            OccurrenceId = booking.OccurrenceId,
            OccurrenceSlotId = booking.OccurrenceSlotId,
            CustomerId = booking.CustomerId,
            CustomerEmail = customer?.Email ?? string.Empty,
            CustomerNotes = booking.CustomerNotes,
            CreatedAtUtc = booking.CreatedAtUtc,
            OccurrenceName = occurrence.Offering?.Name ?? occurrence.Title,
            SlotStartUtc = occurrence.Slots.FirstOrDefault(x => x.Id == booking.OccurrenceSlotId)?.StartUtc,
            TotalBookingCount = 1
        });
    }

    private string CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
}
