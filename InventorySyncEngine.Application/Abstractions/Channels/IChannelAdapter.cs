namespace InventorySyncEngine.Application.Abstractions.Channels;

public interface IChannelAdapter
{
    string Channel { get; } // e.g. "shopify", "trendyol"

    Task<IReadOnlyList<ChannelProductDto>> FetchProductsAsync(ChannelConnectionContext connection, CancellationToken ct);

    /// <summary>
    /// Push absolute available quantity for a listing/variant to the channel.
    /// Must be retry-safe (idempotent in effect): set absolute stock, not increment/decrement.
    /// </summary>
    Task PushStockAsync(ChannelConnectionContext connection, StockPushRequest request, CancellationToken ct);

    /// <summary>Returns true if the webhook signature/HMAC is valid for the raw request body.</summary>
    bool VerifyWebhookSignature(string rawBody, IDictionary<string, string?> headers, ChannelConnectionContext connection);

    /// <summary>Parse provider-specific payload into a normalized order/stock event (or null if irrelevant).</summary>
    NormalizedOrderEvent? ParseOrderEvent(string rawBody, IDictionary<string, string?> headers);
}

public sealed record ChannelConnectionContext(
    Guid MerchantId,
    string Channel,
    string? ShopOrSellerId,
    string? AccessTokenOrApiKey,
    string? Secret,
    IReadOnlyDictionary<string, string>? Extra = null
);

public sealed record ChannelProductDto(
    string ExternalProductId,
    string? ExternalVariantId,
    string Sku,
    string Title,
    int AvailableQuantity
);

public sealed record StockPushRequest(
    string ExternalProductId,
    string? ExternalVariantId,
    string Sku,
    int AbsoluteAvailableQuantity
);

public sealed record NormalizedOrderEvent(
    string Channel,
    string ExternalEventId,
    string EventType,              // "order_created" | "order_canceled" | "refund_created"
    string? ExternalOrderId,
    IReadOnlyList<NormalizedLineItem> LineItems
);

public sealed record NormalizedLineItem(string Sku, int Quantity, string Reason);
