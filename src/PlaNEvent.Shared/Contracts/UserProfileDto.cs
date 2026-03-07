namespace PlaNEvent.Shared.Contracts;

public sealed class UserProfileDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PublicSlug { get; set; } = string.Empty;
    public bool IsDisabled { get; set; }
    public IReadOnlyCollection<string> Roles { get; set; } = Array.Empty<string>();
}
