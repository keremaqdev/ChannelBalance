namespace InventorySyncEngine.Domain.Entities;

public sealed class InventoryLedgerLog
{
    public long Id { get; set; } // bigserial for append-only high-volume log
    public Guid InventoryItemId { get; set; }
    public Guid MerchantId { get; set; }
    public string Sku { get; set; } = default!;

    public int PreviousQty { get; set; }
    public int Delta { get; set; }       // negative for deduction, positive for restock/cancel/refund
    public int NewQty { get; set; }

    public string SourceChannel { get; set; } = default!; // e.g. "shopify", "trendyol", "manual"
    public string? ExternalOrderId { get; set; }
    public string Reason { get; set; } = default!;        // "order_created", "order_canceled", "refund", "adjustment"

    /// <summary>Tracks outbound propagation state for this mutation (e.g. Pending/Partial/Synced/Failed).</summary>
    public string SyncStatus { get; set; } = "Pending";

    public DateTimeOffset CreatedAt { get; set; }
}
