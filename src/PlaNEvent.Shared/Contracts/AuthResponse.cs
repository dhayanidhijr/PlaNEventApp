namespace PlaNEvent.Shared.Contracts;

public sealed record AuthResponse(string Token, DateTime ExpiresAtUtc, UserProfileDto Profile);
