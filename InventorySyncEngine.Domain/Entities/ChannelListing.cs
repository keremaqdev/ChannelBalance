namespace InventorySyncEngine.Domain.Entities;

public sealed class ChannelListing
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }
    public Guid InventoryItemId { get; set; }
    public string Channel { get; set; } = default!;          // "shopify", "trendyol"
    public string ExternalProductId { get; set; } = default!;
    public string? ExternalVariantId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; }
}
