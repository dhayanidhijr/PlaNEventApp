using Microsoft.EntityFrameworkCore;
using PlaNEvent.Api.Data;
using PlaNEvent.Api.Models;

namespace PlaNEvent.Api.Services;

public sealed class OfferingScheduleService(AppDbContext dbContext) : IOfferingScheduleService
{
    public async Task RebuildOccurrencesAsync(Offering offering, CancellationToken cancellationToken)
    {
        await dbContext.Entry(offering).Collection(x => x.RuleGroups).LoadAsync(cancellationToken);
        foreach (var ruleGroup in offering.RuleGroups)
        {
            await dbContext.Entry(ruleGroup).Collection(x => x.Timeslots).LoadAsync(cancellationToken);
        }

        var existing = await dbContext.Occurrences
            .Include(x => x.Slots)
            .Where(x => x.OfferingId == offering.Id)
            .ToListAsync(cancellationToken);

        dbContext.Occurrences.RemoveRange(existing);

        var horizonStart = DateTime.UtcNow.Date.AddMonths(-1);
        var horizonEnd = DateTime.UtcNow.Date.AddMonths(12);
        foreach (var ruleGroup in offering.RuleGroups)
        {
            foreach (var day in EnumerateDays(ruleGroup, horizonStart, horizonEnd))
            {
                var slots = BuildSlots(day, ruleGroup.Timeslots);
                if (slots.Count == 0)
                {
                    continue;
                }

                dbContext.Occurrences.Add(new Occurrence
                {
                    OwnerId = offering.OwnerId,
                    OfferingId = offering.Id,
                    RuleGroupId = ruleGroup.Id,
                    Title = offering.Name,
                    Description = offering.Description,
                    IsPublished = offering.IsPublished,
                    Color = string.IsNullOrWhiteSpace(ruleGroup.Color) ? offering.Color : ruleGroup.Color,
                    Slots = slots
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static List<OccurrenceSlot> BuildSlots(DateTime day, IEnumerable<OfferingTimeslot> timeslots)
    {
        var result = new List<OccurrenceSlot>();
        foreach (var timeslot in timeslots.OrderBy(x => x.StartTime))
        {
            if (timeslot.IsAllDay)
            {
                result.Add(new OccurrenceSlot
                {
                    StartUtc = DateTime.SpecifyKind(day.Date, DateTimeKind.Utc),
                    EndUtc = DateTime.SpecifyKind(day.Date.AddDays(1), DateTimeKind.Utc)
                });
                continue;
            }

            var start = day.Date + timeslot.StartTime;
            var end = day.Date + timeslot.EndTime;
            if (end <= start)
            {
                end = start.AddHours(1);
            }

            if (!timeslot.RepeatGeneratedSlots || !timeslot.RepeatUntilLastStartTime.HasValue || !timeslot.RepeatEveryMinutes.HasValue || timeslot.RepeatEveryMinutes <= 0)
            {
                result.Add(new OccurrenceSlot
                {
                    StartUtc = DateTime.SpecifyKind(start, DateTimeKind.Utc),
                    EndUtc = DateTime.SpecifyKind(end, DateTimeKind.Utc)
                });
                continue;
            }

            var repeatStart = start;
            var repeatUntil = day.Date + timeslot.RepeatUntilLastStartTime.Value;
            var duration = end - start;
            while (repeatStart <= repeatUntil)
            {
                result.Add(new OccurrenceSlot
                {
                    StartUtc = DateTime.SpecifyKind(repeatStart, DateTimeKind.Utc),
                    EndUtc = DateTime.SpecifyKind(repeatStart + duration, DateTimeKind.Utc)
                });

                repeatStart = repeatStart.AddMinutes(timeslot.RepeatEveryMinutes.Value);
            }
        }

        return result;
    }

    private static IEnumerable<DateTime> EnumerateDays(OfferingRuleGroup ruleGroup, DateTime horizonStart, DateTime horizonEnd)
    {
        var start = Max(ruleGroup.StartDateUtc.Date, horizonStart.Date);
        var end = Min(ruleGroup.EndDateUtc?.Date ?? horizonEnd.Date, horizonEnd.Date);
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            if (Matches(day, ruleGroup))
            {
                yield return day;
            }
        }
    }

    private static bool Matches(DateTime day, OfferingRuleGroup ruleGroup)
    {
        var weekdays = ParseWeekdays(ruleGroup.WeekdaysCsv);
        var monthsBetween = ((day.Year - ruleGroup.StartDateUtc.Year) * 12) + day.Month - ruleGroup.StartDateUtc.Month;
        return (ruleGroup.FrequencyType ?? string.Empty).ToLowerInvariant() switch
        {
            "everyday" => MatchesInterval(ruleGroup.StartDateUtc.Date, day, ruleGroup.Interval),
            "dayofweek" or "daysofweek" => weekdays.Contains((int)day.DayOfWeek) && MatchesInterval(ruleGroup.StartDateUtc.Date, day, ruleGroup.Interval * 7, byWeek: true),
            "nthweekofmonth" => weekdays.Contains((int)day.DayOfWeek) && ruleGroup.NthWeekOfMonth == WeekOfMonth(day) && monthsBetween % Math.Max(ruleGroup.Interval, 1) == 0,
            "nthdayofmonth" => day.Day == ruleGroup.DayOfMonth && monthsBetween % Math.Max(ruleGroup.Interval, 1) == 0,
            "everynthday" => MatchesInterval(ruleGroup.StartDateUtc.Date, day, ruleGroup.Interval),
            _ => weekdays.Count == 0 || weekdays.Contains((int)day.DayOfWeek)
        };
    }

    private static bool MatchesInterval(DateTime start, DateTime day, int interval, bool byWeek = false)
    {
        interval = Math.Max(interval, 1);
        if (byWeek)
        {
            var weeks = (int)((day.Date - start.Date).TotalDays / 7d);
            return weeks >= 0 && weeks % interval == 0;
        }

        var days = (day.Date - start.Date).Days;
        return days >= 0 && days % interval == 0;
    }

    private static HashSet<int> ParseWeekdays(string csv)
        => csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(static value => int.TryParse(value, out var parsed) ? parsed : -1)
            .Where(static value => value >= 0 && value <= 6)
            .ToHashSet();

    private static int WeekOfMonth(DateTime day) => ((day.Day - 1) / 7) + 1;

    private static DateTime Max(DateTime left, DateTime right) => left >= right ? left : right;

    private static DateTime Min(DateTime left, DateTime right) => left <= right ? left : right;
}
