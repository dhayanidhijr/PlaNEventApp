namespace PlaNEvent.Api.Models;

public sealed class Offering
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; } = 25m;
    public string Color { get; set; } = "#ec3e47";
    public string CoverImageUrl { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
    public bool IsActive { get; set; } = true;
    public bool AllowNewBookings { get; set; } = true;
    public int? LimitBookingPeriodDays { get; set; }
    public bool RequireSignUpEveryTime { get; set; }
    public List<OfferingRuleGroup> RuleGroups { get; set; } = new();
}
