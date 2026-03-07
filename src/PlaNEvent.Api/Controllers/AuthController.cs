using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PlaNEvent.Api.Infrastructure;
using PlaNEvent.Api.Models;
using PlaNEvent.Api.Services;
using PlaNEvent.Shared.Contracts;

namespace PlaNEvent.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IJwtTokenService jwtTokenService,
    IActivityService activityService) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        if (await userManager.FindByEmailAsync(request.Email) is not null)
        {
            return BadRequest("Email already registered.");
        }

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName,
            PublicSlug = BuildSlug(request.DisplayName)
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return BadRequest(result.Errors.Select(x => x.Description));
        }

        var role = request.RegisterAsCustomer ? Roles.Customer : Roles.Standard;
        await userManager.AddToRoleAsync(user, role);

        var roles = await userManager.GetRolesAsync(user);
        var (token, expiresAtUtc) = jwtTokenService.CreateToken(user, roles.ToArray());
        await activityService.LogAsync(user.Id, "user.register", user.Email ?? string.Empty, cancellationToken);

        return Ok(new AuthResponse(token, expiresAtUtc, ToProfile(user, roles.ToArray())));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || user.IsDisabled)
        {
            return Unauthorized();
        }

        var signIn = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!signIn.Succeeded)
        {
            return Unauthorized();
        }

        var roles = await userManager.GetRolesAsync(user);
        var (token, expiresAtUtc) = jwtTokenService.CreateToken(user, roles.ToArray());
        await activityService.LogAsync(user.Id, "user.login", user.Email ?? string.Empty, cancellationToken);

        return Ok(new AuthResponse(token, expiresAtUtc, ToProfile(user, roles.ToArray())));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserProfileDto>> Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (userId is null)
        {
            return Unauthorized();
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return NotFound();
        }

        var roles = await userManager.GetRolesAsync(user);
        return Ok(ToProfile(user, roles.ToArray()));
    }

    private static UserProfileDto ToProfile(ApplicationUser user, IReadOnlyCollection<string> roles)
        => new()
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            DisplayName = user.DisplayName,
            PublicSlug = user.PublicSlug,
            IsDisabled = user.IsDisabled,
            Roles = roles
        };

    private static string BuildSlug(string displayName)
    {
        var clean = string.Concat(displayName
            .Trim()
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-'));

        clean = string.Join('-', clean.Split('-', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(clean) ? Guid.NewGuid().ToString("N")[..10] : clean;
    }
}
