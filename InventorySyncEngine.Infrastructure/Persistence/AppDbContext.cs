using InventorySyncEngine.Application.Abstractions.Persistence;
using InventorySyncEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InventorySyncEngine.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext, IInventoryDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<InventoryLedgerLog> InventoryLedgerLogs => Set<InventoryLedgerLog>();
    public DbSet<ProcessedWebhookEvent> ProcessedWebhookEvents => Set<ProcessedWebhookEvent>();
    public DbSet<ChannelListing> ChannelListings => Set<ChannelListing>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InventoryItem>(e =>
        {
            e.ToTable("inventory_items");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.MerchantId).HasColumnName("merchant_id");
            e.Property(x => x.Sku).HasColumnName("sku").HasMaxLength(128);
            e.Property(x => x.ProductName).HasColumnName("product_name").HasMaxLength(512);
            e.Property(x => x.AvailableStock).HasColumnName("available_stock");
            e.Property(x => x.ReservedStock).HasColumnName("reserved_stock");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.MerchantId, x.Sku }).IsUnique();
        });

        modelBuilder.Entity<ChannelListing>(e =>
        {
            e.ToTable("channel_listings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.MerchantId).HasColumnName("merchant_id");
            e.Property(x => x.InventoryItemId).HasColumnName("inventory_item_id");
            e.Property(x => x.Channel).HasColumnName("channel").HasMaxLength(32);
            e.Property(x => x.ExternalProductId).HasColumnName("external_product_id").HasMaxLength(128);
            e.Property(x => x.ExternalVariantId).HasColumnName("external_variant_id").HasMaxLength(128);
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<InventoryLedgerLog>(e =>
        {
            e.ToTable("inventory_ledger_logs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.InventoryItemId).HasColumnName("inventory_item_id");
            e.Property(x => x.MerchantId).HasColumnName("merchant_id");
            e.Property(x => x.Sku).HasColumnName("sku").HasMaxLength(128);
            e.Property(x => x.PreviousQty).HasColumnName("previous_qty");
            e.Property(x => x.Delta).HasColumnName("delta");
            e.Property(x => x.NewQty).HasColumnName("new_qty");
            e.Property(x => x.SourceChannel).HasColumnName("source_channel").HasMaxLength(32);
            e.Property(x => x.ExternalOrderId).HasColumnName("external_order_id").HasMaxLength(128);
            e.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(64);
            e.Property(x => x.SyncStatus).HasColumnName("sync_status").HasMaxLength(32);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<ProcessedWebhookEvent>(e =>
        {
            e.ToTable("processed_webhook_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Channel).HasColumnName("channel").HasMaxLength(32);
            e.Property(x => x.ExternalEventId).HasColumnName("external_event_id").HasMaxLength(256);
            e.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(128);
            e.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(128);
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
            e.Property(x => x.ReceivedAt).HasColumnName("received_at");
            e.Property(x => x.ProcessedAt).HasColumnName("processed_at");
            e.HasIndex(x => new { x.Channel, x.ExternalEventId }).IsUnique();
        });
    }
}
