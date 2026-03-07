using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PlaNEvent.Api.Models;

namespace PlaNEvent.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<EventGroup> EventGroups => Set<EventGroup>();
    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();
    public DbSet<Occurrence> Occurrences => Set<Occurrence>();
    public DbSet<OccurrenceSlot> OccurrenceSlots => Set<OccurrenceSlot>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>()
            .HasIndex(x => x.PublicSlug)
            .IsUnique();

        builder.Entity<Occurrence>()
            .HasMany(x => x.Slots)
            .WithOne(x => x.Occurrence)
            .HasForeignKey(x => x.OccurrenceId)
            .OnDelete(DeleteBehavior.Cascade);

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
    }
}
