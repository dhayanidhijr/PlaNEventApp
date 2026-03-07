using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlaNEvent.Api.Data;
using PlaNEvent.Api.Infrastructure;
using PlaNEvent.Api.Models;
using PlaNEvent.Api.Services;
using PlaNEvent.Shared.Contracts;

namespace PlaNEvent.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = Roles.Admin)]
public sealed class AdminController(
    UserManager<ApplicationUser> userManager,
    AppDbContext dbContext,
    IActivityService activityService) : ControllerBase
{
    [HttpGet("users")]
    public async Task<ActionResult<IReadOnlyCollection<UserProfileDto>>> Users()
    {
        var users = await userManager.Users.OrderBy(x => x.Email).ToListAsync();
        var list = new List<UserProfileDto>();
        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            list.Add(new UserProfileDto
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                DisplayName = user.DisplayName,
                PublicSlug = user.PublicSlug,
                IsDisabled = user.IsDisabled,
                Roles = roles.ToArray()
            });
        }

        return Ok(list);
    }

    [HttpPost("users")]
    public async Task<ActionResult<UserProfileDto>> CreateUser(AdminCreateUserRequest request, CancellationToken cancellationToken)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName,
            PublicSlug = string.Concat(request.DisplayName.ToLowerInvariant().Where(char.IsLetterOrDigit)) + Random.Shared.Next(100, 999)
        };

        var create = await userManager.CreateAsync(user, request.Password);
        if (!create.Succeeded)
        {
            return BadRequest(create.Errors.Select(x => x.Description));
        }

        var roles = new List<string>();
        if (request.IsAdmin)
        {
            roles.Add(Roles.Admin);
        }

        if (request.IsCustomer)
        {
            roles.Add(Roles.Customer);
        }

        if (roles.Count == 0)
        {
            roles.Add(Roles.Standard);
        }

        await userManager.AddToRolesAsync(user, roles);
        await activityService.LogAsync(CurrentUserId(), "admin.create_user", user.Email ?? string.Empty, cancellationToken);

        return Ok(new UserProfileDto
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            DisplayName = user.DisplayName,
            PublicSlug = user.PublicSlug,
            Roles = roles.ToArray(),
            IsDisabled = false
        });
    }

    [HttpPut("users/{id}/status")]
    public async Task<IActionResult> UpdateUserStatus(string id, UpdateUserStatusRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        user.IsDisabled = request.IsDisabled;
        await userManager.UpdateAsync(user);
        await activityService.LogAsync(CurrentUserId(), "admin.user_status", $"{user.Email}:{request.IsDisabled}", cancellationToken);

        return NoContent();
    }

    [HttpGet("activities")]
    public async Task<ActionResult<IReadOnlyCollection<object>>> Activities()
    {
        var activities = await dbContext.ActivityLogs
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(250)
            .Select(x => new
            {
                x.Id,
                x.ActorUserId,
                x.Action,
                x.Metadata,
                x.CreatedAtUtc
            })
            .ToListAsync();

        return Ok(activities);
    }

    private string CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
}
