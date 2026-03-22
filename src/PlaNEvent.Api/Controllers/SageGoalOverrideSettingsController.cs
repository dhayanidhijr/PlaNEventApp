using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlaNEvent.Api.Data;
using PlaNEvent.Api.Models;
using PlaNEvent.Shared.Contracts;

namespace PlaNEvent.Api.Controllers;

[ApiController]
[Route("api/sage-goal-overrides")]
[Authorize]
public sealed class SageGoalOverrideSettingsController(AppDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SageGoalOverrideSettingsDto>> Get(CancellationToken cancellationToken)
    {
        var ownerId = CurrentUserId();
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return Unauthorized();
        }

        var baseSettings = await dbContext.SageGoalSettings
            .AsNoTracking()
            .Include(x => x.Features.OrderBy(f => f.SortOrder).ThenBy(f => f.Id))
            .FirstOrDefaultAsync(x => x.OwnerId == ownerId, cancellationToken);

        var settings = await dbContext.SageGoalOverrideSettings
            .AsNoTracking()
            .Include(x => x.Features.OrderBy(f => f.SortOrder).ThenBy(f => f.Id))
            .FirstOrDefaultAsync(x => x.OwnerId == ownerId, cancellationToken);

        return Ok(MapEditor(baseSettings, settings));
    }

    [HttpPut]
    public async Task<ActionResult<SageGoalOverrideSettingsDto>> Save(SageGoalOverrideSettingsDto request, CancellationToken cancellationToken)
    {
        var ownerId = CurrentUserId();
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return Unauthorized();
        }

        var baseSettings = await dbContext.SageGoalSettings
            .AsNoTracking()
            .Include(x => x.Features.OrderBy(f => f.SortOrder).ThenBy(f => f.Id))
            .FirstOrDefaultAsync(x => x.OwnerId == ownerId, cancellationToken);

        var settings = await dbContext.SageGoalOverrideSettings
            .Include(x => x.Features)
            .FirstOrDefaultAsync(x => x.OwnerId == ownerId, cancellationToken);

        if (settings is null)
        {
            settings = new SageGoalOverrideSettings
            {
                OwnerId = ownerId
            };
            dbContext.SageGoalOverrideSettings.Add(settings);
        }

        settings.OverrideExpectedMonthlyBookingCount = request.OverrideExpectedMonthlyBookingCount;
        settings.ExpectedMonthlyBookingCount = Math.Max(0, request.ExpectedMonthlyBookingCount);
        settings.OverrideExpectedMonthlySalesAmount = request.OverrideExpectedMonthlySalesAmount;
        settings.ExpectedMonthlySalesAmount = Math.Max(0, request.ExpectedMonthlySalesAmount);

        var orderedBaseFeatures = (baseSettings?.Features ?? new List<SageGoalFeature>())
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .ToList();

        var orderedIncomingFeatures = request.Features
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .ToList();

        var existingBySortOrder = settings.Features
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .ToDictionary(x => x.SortOrder, x => x);

        for (var index = 0; index < orderedBaseFeatures.Count; index++)
        {
            var baseFeature = orderedBaseFeatures[index];
            var item = orderedIncomingFeatures.ElementAtOrDefault(index);
            var feature = existingBySortOrder.GetValueOrDefault(index);

            if (feature is null)
            {
                feature = new SageGoalOverrideFeature
                {
                    SageGoalOverrideSettings = settings
                };
                settings.Features.Add(feature);
            }

            feature.Name = baseFeature.Name.Trim();
            feature.IsOverrideEnabled = item?.IsOverrideEnabled ?? false;
            feature.ExpectedMonthlyBookingCount = Math.Max(0, item?.ExpectedMonthlyBookingCount ?? baseFeature.ExpectedMonthlyBookingCount);
            feature.ExpectedMonthlySalesAmount = Math.Max(0, item?.ExpectedMonthlySalesAmount ?? baseFeature.ExpectedMonthlySalesAmount);
            feature.TargetSharePercent = Math.Clamp(item?.TargetSharePercent ?? baseFeature.TargetSharePercent, 0, 100);
            feature.SortOrder = index;
        }

        var validSortOrders = Enumerable.Range(0, orderedBaseFeatures.Count).ToHashSet();
        var toRemove = settings.Features
            .Where(x => !validSortOrders.Contains(x.SortOrder))
            .ToList();

        foreach (var feature in toRemove)
        {
            dbContext.SageGoalOverrideFeatures.Remove(feature);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var refreshed = await dbContext.SageGoalOverrideSettings
            .AsNoTracking()
            .Include(x => x.Features.OrderBy(f => f.SortOrder).ThenBy(f => f.Id))
            .FirstAsync(x => x.OwnerId == ownerId, cancellationToken);

        return Ok(MapEditor(baseSettings, refreshed));
    }

    private string CurrentUserId()
        => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;

    private static SageGoalOverrideSettingsDto MapEditor(SageGoalSettings? baseSettings, SageGoalOverrideSettings? overrideSettings)
    {
        var dto = new SageGoalOverrideSettingsDto
        {
            Id = overrideSettings?.Id ?? 0,
            OverrideExpectedMonthlyBookingCount = overrideSettings?.OverrideExpectedMonthlyBookingCount ?? false,
            ExpectedMonthlyBookingCount = overrideSettings?.ExpectedMonthlyBookingCount ?? baseSettings?.ExpectedMonthlyBookingCount ?? 0,
            OverrideExpectedMonthlySalesAmount = overrideSettings?.OverrideExpectedMonthlySalesAmount ?? false,
            ExpectedMonthlySalesAmount = overrideSettings?.ExpectedMonthlySalesAmount ?? baseSettings?.ExpectedMonthlySalesAmount ?? 0
        };

        var overrideBySortOrder = (overrideSettings?.Features ?? new List<SageGoalOverrideFeature>())
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => x.SortOrder)
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.Id).First());

        foreach (var baseFeature in (baseSettings?.Features ?? new List<SageGoalFeature>())
                     .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                     .OrderBy(x => x.SortOrder)
                     .ThenBy(x => x.Id))
        {
            overrideBySortOrder.TryGetValue(baseFeature.SortOrder, out var existingOverride);
            dto.Features.Add(new SageGoalOverrideFeatureDto
            {
                Id = existingOverride?.Id ?? 0,
                Name = baseFeature.Name,
                IsOverrideEnabled = existingOverride?.IsOverrideEnabled ?? false,
                ExpectedMonthlyBookingCount = existingOverride?.ExpectedMonthlyBookingCount ?? baseFeature.ExpectedMonthlyBookingCount,
                ExpectedMonthlySalesAmount = existingOverride?.ExpectedMonthlySalesAmount ?? baseFeature.ExpectedMonthlySalesAmount,
                TargetSharePercent = existingOverride?.TargetSharePercent ?? baseFeature.TargetSharePercent,
                SortOrder = baseFeature.SortOrder
            });
        }

        return dto;
    }
}
