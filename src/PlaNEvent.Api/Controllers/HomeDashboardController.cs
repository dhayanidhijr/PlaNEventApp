using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlaNEvent.Api.Data;
using PlaNEvent.Shared.Contracts;

namespace PlaNEvent.Api.Controllers;

[ApiController]
[Route("api/home/dashboard")]
[Authorize]
public sealed class HomeDashboardController(AppDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<HomeDashboardDto>> Get([FromQuery] string? timeZoneId = null, CancellationToken cancellationToken = default)
    {
        var ownerId = CurrentUserId();
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return Unauthorized();
        }

        var timeZone = ResolveTimeZone(timeZoneId);
        var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);
        var todayLocal = nowLocal.Date;

        var occurrences = await dbContext.Occurrences
            .AsNoTracking()
            .Include(x => x.Offering)
            .Include(x => x.Slots)
            .Where(x => x.OwnerId == ownerId && x.IsPublished)
            .ToListAsync(cancellationToken);

        var bookings = await dbContext.Bookings
            .AsNoTracking()
            .Include(x => x.Occurrence)
            .Include(x => x.OccurrenceSlot)
            .Where(x => x.Occurrence != null && x.Occurrence.OwnerId == ownerId && x.OccurrenceSlot != null)
            .ToListAsync(cancellationToken);

        var settings = await dbContext.SageGoalSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.OwnerId == ownerId, cancellationToken);

        var entries = occurrences
            .SelectMany(occurrence => occurrence.Slots.Select(slot => new SlotEntry(
                occurrence.Id,
                occurrence.Offering?.Name ?? occurrence.Title,
                occurrence.Color,
                slot.Id,
                slot.StartUtc,
                slot.EndUtc)))
            .OrderBy(x => x.StartUtc)
            .ToList();

        var bookingCountsBySlot = bookings
            .GroupBy(x => x.OccurrenceSlotId)
            .ToDictionary(x => x.Key, x => x.Count());

        var todayEntries = entries
            .Where(x => ToLocalDate(x.StartUtc, timeZone) == todayLocal)
            .ToList();

        var todayEvents = todayEntries
            .Select(x => new HomeScheduleItemDto
            {
                OccurrenceId = x.OccurrenceId,
                Title = x.Title,
                StartUtc = x.StartUtc,
                EndUtc = x.EndUtc,
                Color = x.Color
            })
            .ToList();

        var todayBookingsByEvent = todayEntries
            .Select(x => new HomeBookingSummaryItemDto
            {
                OccurrenceId = x.OccurrenceId,
                SlotId = x.SlotId,
                Title = x.Title,
                StartUtc = x.StartUtc,
                EndUtc = x.EndUtc,
                BookingCount = bookingCountsBySlot.GetValueOrDefault(x.SlotId, 0),
                Color = x.Color
            })
            .OrderByDescending(x => x.BookingCount)
            .ThenBy(x => x.StartUtc)
            .ToList();

        var weekStart = StartOfWeek(todayLocal);
        var monthStart = new DateTime(todayLocal.Year, todayLocal.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var yearStart = new DateTime(todayLocal.Year, 1, 1);

        var monthlyTarget = settings?.ExpectedMonthlyBookingCount ?? 0;

        var result = new HomeDashboardDto
        {
            TodayEvents = todayEvents,
            TodayBookingsByEvent = todayBookingsByEvent,
            OfferedBookedDay = new HomeComparisonSummaryDto
            {
                PrimaryValue = todayEntries.Count,
                SecondaryValue = todayBookingsByEvent.Sum(x => x.BookingCount)
            },
            OfferedBookedWeek = Enumerable.Range(0, 7)
                .Select(offset =>
                {
                    var day = weekStart.AddDays(offset);
                    var dayEntries = entries.Where(x => ToLocalDate(x.StartUtc, timeZone) == day).ToList();
                    return new HomeComparisonPointDto
                    {
                        Label = day.ToString("ddd"),
                        PrimaryValue = dayEntries.Count,
                        SecondaryValue = dayEntries.Sum(x => bookingCountsBySlot.GetValueOrDefault(x.SlotId, 0))
                    };
                })
                .ToList(),
            OfferedBookedMonth = BuildMonthPoints(monthStart, monthEnd, entries, bookingCountsBySlot, timeZone),
            TargetActualDay = new HomeComparisonSummaryDto
            {
                PrimaryValue = DailyTarget(monthlyTarget, todayLocal),
                SecondaryValue = todayBookingsByEvent.Sum(x => x.BookingCount)
            },
            TargetActualWeek = Enumerable.Range(0, 7)
                .Select(offset =>
                {
                    var day = weekStart.AddDays(offset);
                    return new HomeComparisonPointDto
                    {
                        Label = day.ToString("ddd"),
                        PrimaryValue = DailyTarget(monthlyTarget, day),
                        SecondaryValue = bookings.Count(x => ToLocalDate(x.OccurrenceSlot!.StartUtc, timeZone) == day)
                    };
                })
                .ToList(),
            TargetActualMonth = BuildMonthTargetPoints(monthStart, monthEnd, bookings, monthlyTarget, timeZone),
            TargetActualYear = Enumerable.Range(1, 12)
                .Select(month =>
                {
                    var start = new DateTime(todayLocal.Year, month, 1);
                    var end = start.AddMonths(1).AddDays(-1);
                    return new HomeComparisonPointDto
                    {
                        Label = start.ToString("MMM"),
                        PrimaryValue = monthlyTarget,
                        SecondaryValue = bookings.Count(x =>
                        {
                            var localDate = ToLocalDate(x.OccurrenceSlot!.StartUtc, timeZone);
                            return localDate >= start && localDate <= end;
                        })
                    };
                })
                .ToList()
        };

        return Ok(result);
    }

    private static IReadOnlyCollection<HomeComparisonPointDto> BuildMonthPoints(
        DateTime monthStart,
        DateTime monthEnd,
        IReadOnlyCollection<SlotEntry> entries,
        IReadOnlyDictionary<int, int> bookingCountsBySlot,
        TimeZoneInfo timeZone)
    {
        var points = new List<HomeComparisonPointDto>();
        var cursor = monthStart;
        var weekNumber = 1;
        while (cursor <= monthEnd)
        {
            var segmentEnd = cursor.AddDays(6);
            if (segmentEnd > monthEnd)
            {
                segmentEnd = monthEnd;
            }

            var segmentEntries = entries.Where(x =>
            {
                var localDate = ToLocalDate(x.StartUtc, timeZone);
                return localDate >= cursor && localDate <= segmentEnd;
            }).ToList();

            points.Add(new HomeComparisonPointDto
            {
                Label = $"W{weekNumber++}",
                PrimaryValue = segmentEntries.Count,
                SecondaryValue = segmentEntries.Sum(x => bookingCountsBySlot.GetValueOrDefault(x.SlotId, 0))
            });

            cursor = segmentEnd.AddDays(1);
        }

        return points;
    }

    private static IReadOnlyCollection<HomeComparisonPointDto> BuildMonthTargetPoints(
        DateTime monthStart,
        DateTime monthEnd,
        IReadOnlyCollection<Api.Models.Booking> bookings,
        int monthlyTarget,
        TimeZoneInfo timeZone)
    {
        var points = new List<HomeComparisonPointDto>();
        var cursor = monthStart;
        var weekNumber = 1;
        while (cursor <= monthEnd)
        {
            var segmentEnd = cursor.AddDays(6);
            if (segmentEnd > monthEnd)
            {
                segmentEnd = monthEnd;
            }

            var days = (segmentEnd - cursor).Days + 1;
            points.Add(new HomeComparisonPointDto
            {
                Label = $"W{weekNumber++}",
                PrimaryValue = DailyTarget(monthlyTarget, cursor) * days,
                SecondaryValue = bookings.Count(x =>
                {
                    var localDate = ToLocalDate(x.OccurrenceSlot!.StartUtc, timeZone);
                    return localDate >= cursor && localDate <= segmentEnd;
                })
            });

            cursor = segmentEnd.AddDays(1);
        }

        return points;
    }

    private static int DailyTarget(int monthlyTarget, DateTime day)
    {
        if (monthlyTarget <= 0)
        {
            return 0;
        }

        var daysInMonth = DateTime.DaysInMonth(day.Year, day.Month);
        return (int)Math.Ceiling(monthlyTarget / (decimal)daysInMonth);
    }

    private static DateTime StartOfWeek(DateTime day)
        => day.AddDays(-(int)day.DayOfWeek);

    private static DateTime ToLocalDate(DateTime utc, TimeZoneInfo timeZone)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), timeZone).Date;

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch
        {
            return TimeZoneInfo.Utc;
        }
    }

    private string CurrentUserId()
        => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;

    private sealed record SlotEntry(int OccurrenceId, string Title, string Color, int SlotId, DateTime StartUtc, DateTime EndUtc);
}
