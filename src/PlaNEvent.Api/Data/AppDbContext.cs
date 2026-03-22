using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PlaNEvent.Api.Models;

namespace PlaNEvent.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<EventGroup> EventGroups => Set<EventGroup>();
    public DbSet<Offering> Offerings => Set<Offering>();
    public DbSet<OfferingRuleGroup> OfferingRuleGroups => Set<OfferingRuleGroup>();
    public DbSet<OfferingTimeslot> OfferingTimeslots => Set<OfferingTimeslot>();
    public DbSet<ShowcasePage> ShowcasePages => Set<ShowcasePage>();
    public DbSet<ShowcasePageItem> ShowcasePageItems => Set<ShowcasePageItem>();
    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();
    public DbSet<StaffOfferingMapping> StaffOfferingMappings => Set<StaffOfferingMapping>();
    public DbSet<Occurrence> Occurrences => Set<Occurrence>();
    public DbSet<OccurrenceSlot> OccurrenceSlots => Set<OccurrenceSlot>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<SageGoalSettings> SageGoalSettings => Set<SageGoalSettings>();
    public DbSet<SageGoalFeature> SageGoalFeatures => Set<SageGoalFeature>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>()
            .HasIndex(x => x.PublicSlug)
            .IsUnique();

        builder.Entity<Category>()
            .HasMany(x => x.Children)
            .WithOne(x => x.ParentCategory)
            .HasForeignKey(x => x.ParentCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Offering>()
            .HasOne(x => x.Category)
            .WithMany()
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<Offering>()
            .HasMany(x => x.RuleGroups)
            .WithOne(x => x.Offering)
            .HasForeignKey(x => x.OfferingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<OfferingRuleGroup>()
            .HasMany(x => x.Timeslots)
            .WithOne(x => x.RuleGroup)
            .HasForeignKey(x => x.RuleGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ShowcasePage>()
            .HasIndex(x => new { x.OwnerId, x.Slug })
            .IsUnique();

        builder.Entity<ShowcasePage>()
            .HasMany(x => x.Items)
            .WithOne(x => x.ShowcasePage)
            .HasForeignKey(x => x.ShowcasePageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StaffMember>()
            .HasMany(x => x.OfferingMappings)
            .WithOne(x => x.StaffMember)
            .HasForeignKey(x => x.StaffMemberId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StaffOfferingMapping>()
            .HasOne(x => x.Offering)
            .WithMany()
            .HasForeignKey(x => x.OfferingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Occurrence>()
            .HasMany(x => x.Slots)
            .WithOne(x => x.Occurrence)
            .HasForeignKey(x => x.OccurrenceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Occurrence>()
            .HasOne(x => x.Offering)
            .WithMany()
            .HasForeignKey(x => x.OfferingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Occurrence>()
            .HasOne(x => x.RuleGroup)
            .WithMany()
            .HasForeignKey(x => x.RuleGroupId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<Occurrence>()
            .HasOne(x => x.EventGroup)
            .WithMany()
            .HasForeignKey(x => x.EventGroupId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<Occurrence>()
            .HasOne(x => x.Staff)
            .WithMany()
            .HasForeignKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<Booking>()
            .HasOne(x => x.OccurrenceSlot)
            .WithMany()
            .HasForeignKey(x => x.OccurrenceSlotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<SageGoalSettings>()
            .HasIndex(x => x.OwnerId)
            .IsUnique();

        builder.Entity<SageGoalSettings>()
            .HasMany(x => x.Features)
            .WithOne(x => x.SageGoalSettings)
            .HasForeignKey(x => x.SageGoalSettingsId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
