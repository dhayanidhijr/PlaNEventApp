namespace PlaNEvent.Api.Infrastructure;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Standard = "Standard";
    public const string Customer = "Customer";

    public static readonly string[] All = [Admin, Standard, Customer];
}
