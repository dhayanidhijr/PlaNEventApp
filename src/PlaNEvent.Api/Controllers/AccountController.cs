using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PlaNEvent.Api.Models;
using PlaNEvent.Api.Services;
using PlaNEvent.Shared.Contracts;

namespace PlaNEvent.Api.Controllers;

[ApiController]
[Route("api/account")]
[Authorize]
public sealed class AccountController(UserManager<ApplicationUser> userManager, IActivityService activityService) : ControllerBase
{
    [HttpGet("profile")]
    public async Task<ActionResult<UserProfileDto>> Profile()
    {
        var user = await CurrentUserAsync();
        if (user is null)
        {
            return Unauthorized();
        }

        var roles = await userManager.GetRolesAsync(user);
        return Ok(new UserProfileDto
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            DisplayName = user.DisplayName,
            PublicSlug = user.PublicSlug,
            IsDisabled = user.IsDisabled,
            Roles = roles.ToArray()
        });
    }

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile(UserProfileDto request, CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync();
        if (user is null)
        {
            return Unauthorized();
        }

        user.DisplayName = request.DisplayName;
        user.PublicSlug = request.PublicSlug;
        await userManager.UpdateAsync(user);
        await activityService.LogAsync(user.Id, "account.update_profile", request.DisplayName, cancellationToken);

        return NoContent();
    }

    private async Task<ApplicationUser?> CurrentUserAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return userId is null ? null : await userManager.FindByIdAsync(userId);
    }
}
