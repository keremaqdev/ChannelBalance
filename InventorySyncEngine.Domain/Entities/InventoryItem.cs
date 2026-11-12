namespace InventorySyncEngine.Domain.Entities;

public sealed class InventoryItem
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }

    /// <summary>Merchant's master SKU (unique per merchant).</summary>
    public string Sku { get; set; } = default!;
    public string ProductName { get; set; } = default!;

    /// <summary>Sellable units available to allocate across channels.</summary>
    public int AvailableStock { get; set; }

    /// <summary>Units reserved but not yet finalized (e.g. payment pending), if used by flows.</summary>
    public int ReservedStock { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
