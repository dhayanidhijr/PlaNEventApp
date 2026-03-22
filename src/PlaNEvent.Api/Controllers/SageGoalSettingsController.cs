using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlaNEvent.Api.Data;
using PlaNEvent.Api.Models;
using PlaNEvent.Shared.Contracts;

namespace PlaNEvent.Api.Controllers;

[ApiController]
[Route("api/sage-goal-settings")]
[Authorize]
public sealed class SageGoalSettingsController(AppDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SageGoalSettingsDto>> Get(CancellationToken cancellationToken)
    {
        var ownerId = CurrentUserId();
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return Unauthorized();
        }

        var settings = await dbContext.SageGoalSettings
            .AsNoTracking()
            .Include(x => x.Features.OrderBy(f => f.SortOrder).ThenBy(f => f.Id))
            .FirstOrDefaultAsync(x => x.OwnerId == ownerId, cancellationToken);

        return Ok(settings is null ? new SageGoalSettingsDto() : Map(settings));
    }

    [HttpPut]
    public async Task<ActionResult<SageGoalSettingsDto>> Save(SageGoalSettingsDto request, CancellationToken cancellationToken)
    {
        var ownerId = CurrentUserId();
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return Unauthorized();
        }

        var settings = await dbContext.SageGoalSettings
            .Include(x => x.Features)
            .FirstOrDefaultAsync(x => x.OwnerId == ownerId, cancellationToken);

        if (settings is null)
        {
            settings = new SageGoalSettings
            {
                OwnerId = ownerId
            };
            dbContext.SageGoalSettings.Add(settings);
        }

        settings.FacilityBusinessSummary = request.FacilityBusinessSummary?.Trim() ?? string.Empty;
        settings.ExpectedMonthlyBookingCount = Math.Max(0, request.ExpectedMonthlyBookingCount);
        settings.ExpectedMonthlySalesAmount = Math.Max(0, request.ExpectedMonthlySalesAmount);

        var incomingById = request.Features
            .Where(x => x.Id > 0)
            .ToDictionary(x => x.Id, x => x);

        var toRemove = settings.Features
            .Where(x => !incomingById.ContainsKey(x.Id))
            .ToList();

        foreach (var feature in toRemove)
        {
            dbContext.SageGoalFeatures.Remove(feature);
        }

        for (var index = 0; index < request.Features.Count; index++)
        {
            var item = request.Features[index];
            if (string.IsNullOrWhiteSpace(item.Name))
            {
                continue;
            }

            var feature = settings.Features.FirstOrDefault(x => x.Id == item.Id);
            if (feature is null)
            {
                feature = new SageGoalFeature();
                settings.Features.Add(feature);
            }

            feature.Name = item.Name.Trim();
            feature.ExpectedMonthlyBookingCount = Math.Max(0, item.ExpectedMonthlyBookingCount);
            feature.ExpectedMonthlySalesAmount = Math.Max(0, item.ExpectedMonthlySalesAmount);
            feature.TargetSharePercent = Math.Clamp(item.TargetSharePercent, 0, 100);
            feature.SortOrder = index;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var refreshed = await dbContext.SageGoalSettings
            .AsNoTracking()
            .Include(x => x.Features.OrderBy(f => f.SortOrder).ThenBy(f => f.Id))
            .FirstAsync(x => x.OwnerId == ownerId, cancellationToken);

        return Ok(Map(refreshed));
    }

    private string CurrentUserId()
        => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;

    internal static SageGoalSettingsDto Map(SageGoalSettings settings) => new()
    {
        Id = settings.Id,
        FacilityBusinessSummary = settings.FacilityBusinessSummary,
        ExpectedMonthlyBookingCount = settings.ExpectedMonthlyBookingCount,
        ExpectedMonthlySalesAmount = settings.ExpectedMonthlySalesAmount,
        Features = settings.Features
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .Select(x => new SageGoalFeatureDto
            {
                Id = x.Id,
                Name = x.Name,
                ExpectedMonthlyBookingCount = x.ExpectedMonthlyBookingCount,
                ExpectedMonthlySalesAmount = x.ExpectedMonthlySalesAmount,
                TargetSharePercent = x.TargetSharePercent,
                SortOrder = x.SortOrder
            })
            .ToList()
    };
}
