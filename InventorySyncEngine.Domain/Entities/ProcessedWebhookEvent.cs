namespace InventorySyncEngine.Domain.Entities;

public sealed class ProcessedWebhookEvent
{
    public long Id { get; set; }
    public string Channel { get; set; } = default!;          // "shopify", "trendyol"
    public string ExternalEventId { get; set; } = default!;  // provider event/webhook id (or deterministic hash)
    public string EventType { get; set; } = default!;        // "orders/create", "order.cancelled", etc.
    public string? PayloadHash { get; set; }                 // optional integrity/debug aid
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string Status { get; set; } = "Processed";        // Processed / Failed / Ignored
}
