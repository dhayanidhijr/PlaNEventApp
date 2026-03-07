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
            .Where(x => x.OwnerId == ownerId)
            .OrderBy(x => x.Name)
            .Select(x => new StaffDto { Id = x.Id, Name = x.Name, Email = x.Email })
            .ToListAsync();

        return Ok(staff);
    }

    [HttpPost("staff")]
    public async Task<ActionResult<StaffDto>> CreateStaff(StaffDto request, CancellationToken cancellationToken)
    {
        var staff = new StaffMember
        {
            OwnerId = CurrentUserId(),
            Name = request.Name,
            Email = request.Email
        };

        dbContext.StaffMembers.Add(staff);
        await dbContext.SaveChangesAsync(cancellationToken);
        await activityService.LogAsync(CurrentUserId(), "staff.create", staff.Name, cancellationToken);

        return Ok(new StaffDto { Id = staff.Id, Name = staff.Name, Email = staff.Email });
    }

    private string CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
}
