using PlaNEvent.Api.Models;

namespace PlaNEvent.Api.Services;

public static class SageGoalSettingsResolver
{
    public static SageGoalSettings ResolveEffectiveSettings(
        SageGoalSettings? baseSettings,
        SageGoalOverrideSettings? overrideSettings)
    {
        var effective = new SageGoalSettings
        {
            Id = baseSettings?.Id ?? 0,
            OwnerId = baseSettings?.OwnerId ?? overrideSettings?.OwnerId ?? string.Empty,
            FacilityBusinessSummary = baseSettings?.FacilityBusinessSummary ?? string.Empty,
            ExpectedMonthlyBookingCount = baseSettings?.ExpectedMonthlyBookingCount ?? 0,
            ExpectedMonthlySalesAmount = baseSettings?.ExpectedMonthlySalesAmount ?? 0m,
            Features = (baseSettings?.Features ?? new List<SageGoalFeature>())
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Id)
                .Select(CloneBaseFeature)
                .ToList()
        };

        if (overrideSettings is null)
        {
            return effective;
        }

        if (overrideSettings.OverrideExpectedMonthlyBookingCount)
        {
            effective.ExpectedMonthlyBookingCount = Math.Max(0, overrideSettings.ExpectedMonthlyBookingCount);
        }

        if (overrideSettings.OverrideExpectedMonthlySalesAmount)
        {
            effective.ExpectedMonthlySalesAmount = Math.Max(0, overrideSettings.ExpectedMonthlySalesAmount);
        }

        foreach (var overrideFeature in overrideSettings.Features
                     .Where(x => x.IsOverrideEnabled && !string.IsNullOrWhiteSpace(x.Name))
                     .OrderBy(x => x.SortOrder)
                     .ThenBy(x => x.Id))
        {
            var match = effective.Features.FirstOrDefault(x =>
                string.Equals(x.Name, overrideFeature.Name, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                effective.Features.Add(new SageGoalFeature
                {
                    Name = overrideFeature.Name.Trim(),
                    ExpectedMonthlyBookingCount = Math.Max(0, overrideFeature.ExpectedMonthlyBookingCount),
                    ExpectedMonthlySalesAmount = Math.Max(0, overrideFeature.ExpectedMonthlySalesAmount),
                    TargetSharePercent = Math.Clamp(overrideFeature.TargetSharePercent, 0, 100),
                    SortOrder = overrideFeature.SortOrder
                });
                continue;
            }

            match.ExpectedMonthlyBookingCount = Math.Max(0, overrideFeature.ExpectedMonthlyBookingCount);
            match.ExpectedMonthlySalesAmount = Math.Max(0, overrideFeature.ExpectedMonthlySalesAmount);
            match.TargetSharePercent = Math.Clamp(overrideFeature.TargetSharePercent, 0, 100);
            match.SortOrder = overrideFeature.SortOrder;
        }

        effective.Features = effective.Features
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .ToList();

        return effective;
    }

    private static SageGoalFeature CloneBaseFeature(SageGoalFeature source) => new()
    {
        Id = source.Id,
        SageGoalSettingsId = source.SageGoalSettingsId,
        Name = source.Name,
        ExpectedMonthlyBookingCount = source.ExpectedMonthlyBookingCount,
        ExpectedMonthlySalesAmount = source.ExpectedMonthlySalesAmount,
        TargetSharePercent = source.TargetSharePercent,
        SortOrder = source.SortOrder
    };
}
