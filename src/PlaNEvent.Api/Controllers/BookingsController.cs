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
    public async Task<ActionResult<IReadOnlyCollection<BookingDto>>> List()
    {
        var userId = CurrentUserId();

        var bookings = await dbContext.Bookings
            .AsNoTracking()
            .Where(x => x.CustomerId == userId || x.Occurrence!.OwnerId == userId)
            .Include(x => x.Occurrence)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync();

        var users = await userManager.Users.ToDictionaryAsync(x => x.Id, x => x.Email ?? string.Empty);

        return Ok(bookings.Select(x => new BookingDto
        {
            Id = x.Id,
            OccurrenceId = x.OccurrenceId,
            OccurrenceSlotId = x.OccurrenceSlotId,
            CustomerId = x.CustomerId,
            CustomerEmail = users.GetValueOrDefault(x.CustomerId, string.Empty),
            CustomerNotes = x.CustomerNotes,
            CreatedAtUtc = x.CreatedAtUtc
        }).ToList());
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
            CreatedAtUtc = booking.CreatedAtUtc
        });
    }

    private string CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
}
