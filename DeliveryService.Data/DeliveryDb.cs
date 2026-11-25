using DeliveryService.Core;
using Microsoft.EntityFrameworkCore;

namespace DeliveryService.Data;

public sealed class DeliveryDb(DbContextOptions<DeliveryDb> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Recipient> Recipients => Set<Recipient>();
    public DbSet<Shipment> Shipments => Set<Shipment>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<AppUser>().HasIndex(user => user.Login).IsUnique();
        model.Entity<Recipient>().HasIndex(recipient => recipient.Name);
        model.HasDbFunction(typeof(DeliveryDatabase).GetMethod(nameof(DeliveryDatabase.DeliveryContains))!)
            .HasName("delivery_contains").IsBuiltIn();

        var shipment = model.Entity<Shipment>();
        shipment.HasIndex(row => new { row.Status, row.CreatedAt });
        shipment.HasIndex(row => new { row.CourierId, row.CreatedAt });
        shipment.HasIndex(row => row.TrackingNumber).IsUnique();
        shipment.Property(row => row.Version).IsConcurrencyToken();

        var utc = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
            value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        shipment.Property(row => row.CreatedAt).HasConversion(utc);
        shipment.Property(row => row.AcceptedAt).HasConversion(utc);
        shipment.Property(row => row.DeliveredAt).HasConversion(utc);
        model.Entity<Recipient>().Property(row => row.RegisteredAt).HasConversion(utc);

        shipment.HasOne(row => row.Recipient).WithMany().HasForeignKey(row => row.RecipientId)
            .OnDelete(DeleteBehavior.Restrict);
        shipment.HasOne(row => row.Courier).WithMany().HasForeignKey(row => row.CourierId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
