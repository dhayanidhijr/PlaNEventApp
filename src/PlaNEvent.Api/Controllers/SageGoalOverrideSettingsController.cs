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

        var settings = await dbContext.SageGoalOverrideSettings
            .AsNoTracking()
            .Include(x => x.Features.OrderBy(f => f.SortOrder).ThenBy(f => f.Id))
            .FirstOrDefaultAsync(x => x.OwnerId == ownerId, cancellationToken);

        return Ok(settings is null ? new SageGoalOverrideSettingsDto() : Map(settings));
    }

    [HttpPut]
    public async Task<ActionResult<SageGoalOverrideSettingsDto>> Save(SageGoalOverrideSettingsDto request, CancellationToken cancellationToken)
    {
        var ownerId = CurrentUserId();
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return Unauthorized();
        }

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

        var incomingById = request.Features
            .Where(x => x.Id > 0)
            .ToDictionary(x => x.Id, x => x);

        var toRemove = settings.Features
            .Where(x => !incomingById.ContainsKey(x.Id))
            .ToList();

        foreach (var feature in toRemove)
        {
            dbContext.SageGoalOverrideFeatures.Remove(feature);
        }

        for (var index = 0; index < request.Features.Count; index++)
        {
            var item = request.Features[index];
            if (string.IsNullOrWhiteSpace(item.Name))
            {
                continue;
            }

            var feature = item.Id > 0
                ? settings.Features.FirstOrDefault(x => x.Id == item.Id)
                : null;

            if (feature is null)
            {
                feature = new SageGoalOverrideFeature
                {
                    SageGoalOverrideSettings = settings
                };
                settings.Features.Add(feature);
            }

            feature.Name = item.Name.Trim();
            feature.IsOverrideEnabled = item.IsOverrideEnabled;
            feature.ExpectedMonthlyBookingCount = Math.Max(0, item.ExpectedMonthlyBookingCount);
            feature.ExpectedMonthlySalesAmount = Math.Max(0, item.ExpectedMonthlySalesAmount);
            feature.TargetSharePercent = Math.Clamp(item.TargetSharePercent, 0, 100);
            feature.SortOrder = index;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var refreshed = await dbContext.SageGoalOverrideSettings
            .AsNoTracking()
            .Include(x => x.Features.OrderBy(f => f.SortOrder).ThenBy(f => f.Id))
            .FirstAsync(x => x.OwnerId == ownerId, cancellationToken);

        return Ok(Map(refreshed));
    }

    private string CurrentUserId()
        => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;

    private static SageGoalOverrideSettingsDto Map(SageGoalOverrideSettings settings) => new()
    {
        Id = settings.Id,
        OverrideExpectedMonthlyBookingCount = settings.OverrideExpectedMonthlyBookingCount,
        ExpectedMonthlyBookingCount = settings.ExpectedMonthlyBookingCount,
        OverrideExpectedMonthlySalesAmount = settings.OverrideExpectedMonthlySalesAmount,
        ExpectedMonthlySalesAmount = settings.ExpectedMonthlySalesAmount,
        Features = settings.Features
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .Select(x => new SageGoalOverrideFeatureDto
            {
                Id = x.Id,
                Name = x.Name,
                IsOverrideEnabled = x.IsOverrideEnabled,
                ExpectedMonthlyBookingCount = x.ExpectedMonthlyBookingCount,
                ExpectedMonthlySalesAmount = x.ExpectedMonthlySalesAmount,
                TargetSharePercent = x.TargetSharePercent,
                SortOrder = x.SortOrder
            })
            .ToList()
    };
}
