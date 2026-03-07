using Microsoft.AspNetCore.Identity;

namespace PlaNEvent.Api.Models;

public sealed class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
    public string PublicSlug { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public bool IsDisabled { get; set; }
}
