namespace PlaNEvent.Api.Models;

public sealed class Category
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public int? ParentCategoryId { get; set; }
    public Category? ParentCategory { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#2f80ff";
    public bool IsActive { get; set; } = true;
    public List<Category> Children { get; set; } = new();
}
