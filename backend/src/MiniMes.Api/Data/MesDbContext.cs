using Microsoft.EntityFrameworkCore;
using MiniMes.Api.Domain;
using MiniMes.Api.Events;

namespace MiniMes.Api.Data;

public class MesDbContext(DbContextOptions<MesDbContext> options) : DbContext(options)
{
    public DbSet<Part> Parts => Set<Part>();
    public DbSet<PartRevision> PartRevisions => Set<PartRevision>();
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<Routing> Routings => Set<Routing>();
    public DbSet<SerializedPart> Serials => Set<SerializedPart>();
    public DbSet<Installation> Installations => Set<Installation>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WorkOrderOperation> WorkOrderOperations => Set<WorkOrderOperation>();
    public DbSet<SignOff> SignOffs => Set<SignOff>();
    public DbSet<Ncr> Ncrs => Set<Ncr>();
    public DbSet<Flight> Flights => Set<Flight>();
    public DbSet<FlightManifestEntry> FlightManifest => Set<FlightManifestEntry>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Store enums as text: readable in psql and in the raw SQL queries, and safe to reorder.
        builder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasSequence<long>("work_order_numbers").StartsAt(1001);
        b.HasSequence<long>("ncr_numbers");

        b.Entity<Part>().HasIndex(p => p.PartNumber).IsUnique();
        b.Entity<PartRevision>().HasIndex(r => new { r.PartId, r.Revision }).IsUnique();
        b.Entity<Station>().HasIndex(s => s.Code).IsUnique();
        b.Entity<Routing>().HasIndex(r => new { r.PartRevisionId, r.Type }).IsUnique();
        b.Entity<RoutingOperation>().ToTable("routing_operations");

        b.Entity<SerializedPart>(e =>
        {
            e.ToTable("serials");
            e.HasIndex(s => s.SerialNumber).IsUnique();
            e.HasIndex(s => s.LotNumber);
        });

        b.Entity<Installation>(e =>
        {
            e.HasOne(i => i.Parent).WithMany().HasForeignKey(i => i.ParentId);
            e.HasOne(i => i.Child).WithMany().HasForeignKey(i => i.ChildId);
            e.HasIndex(i => i.ParentId).HasFilter("removed_at IS NULL");
            // A serial can sit in only one parent at a time; enforced by the database, not just the API.
            e.HasIndex(i => i.ChildId).IsUnique().HasFilter("removed_at IS NULL");
        });

        b.Entity<WorkOrder>(e =>
        {
            e.HasIndex(w => w.Number).IsUnique();
            e.HasIndex(w => new { w.SerialId, w.Type, w.Status });
            e.Property(w => w.Version).IsRowVersion(); // Npgsql maps a uint row version to xmin
            e.HasMany(w => w.Operations).WithOne().HasForeignKey(o => o.WorkOrderId);
            e.HasMany(w => w.Ncrs).WithOne(n => n.WorkOrder).HasForeignKey(n => n.WorkOrderId);
        });

        b.Entity<WorkOrderOperation>()
            .HasOne(o => o.SignOff).WithOne().HasForeignKey<SignOff>(s => s.WorkOrderOperationId);

        b.Entity<Ncr>(e =>
        {
            e.HasIndex(n => n.Number).IsUnique();
            e.HasIndex(n => n.Status);
        });

        b.Entity<Flight>().HasIndex(f => new { f.VehicleId, f.FlightNumber }).IsUnique();
        b.Entity<FlightManifestEntry>(e =>
        {
            e.ToTable("flight_manifest");
            e.HasKey(m => new { m.FlightId, m.SerialId });
            e.HasIndex(m => m.SerialId);
        });
        b.Entity<Flight>().HasMany(f => f.Manifest).WithOne().HasForeignKey(m => m.FlightId);

        b.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox");
            e.Property(m => m.Payload).HasColumnType("jsonb");
            e.HasIndex(m => m.Id).HasFilter("published_at IS NULL").HasDatabaseName("ix_outbox_unpublished");
        });

        // Manufacturing records are never deleted, so no cascades anywhere.
        foreach (var fk in b.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RejectAuditTrailMutations();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        RejectAuditTrailMutations();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }

    // First line of defence; a trigger in the database is the second (see the InitialSchema migration).
    private void RejectAuditTrailMutations()
    {
        var mutated = ChangeTracker.Entries<IAppendOnly>()
            .FirstOrDefault(e => e.State is EntityState.Modified or EntityState.Deleted);
        if (mutated is not null)
            throw new InvalidOperationException($"{mutated.Metadata.ClrType.Name} records are append-only");
    }
}
