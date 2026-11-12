using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InventorySyncEngine.Application.Abstractions.Channels;

namespace InventorySyncEngine.Infrastructure.Channels;

public sealed class TrendyolChannelAdapter : IChannelAdapter
{
    private readonly HttpClient _http;
    public string Channel => "trendyol";

    public TrendyolChannelAdapter(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<ChannelProductDto>> FetchProductsAsync(ChannelConnectionContext connection, CancellationToken ct)
    {
        var supplierId = connection.ShopOrSellerId ?? throw new InvalidOperationException("Trendyol supplierId is required.");
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.trendyol.com/sapigw/suppliers/{supplierId}/products?size=200&page=0");
        AddBasicAuth(req, connection);

        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var list = new List<ChannelProductDto>();
        if (!doc.RootElement.TryGetProperty("content", out var content)) return list;

        foreach (var p in content.EnumerateArray())
        {
            var barcode = p.TryGetProperty("barcode", out var b) ? b.GetString() ?? "" : "";
            var sku = p.TryGetProperty("stockCode", out var sc) ? sc.GetString() ?? barcode : barcode;
            list.Add(new ChannelProductDto(
                ExternalProductId: p.TryGetProperty("productCode", out var pc) ? pc.GetRawText() : barcode,
                ExternalVariantId: barcode,
                Sku: sku,
                Title: p.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                AvailableQuantity: p.TryGetProperty("quantity", out var q) ? q.GetInt32() : 0
            ));
        }
        return list;
    }

    public async Task PushStockAsync(ChannelConnectionContext connection, StockPushRequest request, CancellationToken ct)
    {
        var supplierId = connection.ShopOrSellerId ?? throw new InvalidOperationException("Trendyol supplierId is required.");

        var payload = JsonSerializer.Serialize(new
        {
            items = new[]
            {
                new
                {
                    barcode = request.ExternalVariantId ?? request.ExternalProductId,
                    quantity = request.AbsoluteAvailableQuantity
                }
            }
        });

        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.trendyol.com/sapigw/suppliers/{supplierId}/products/price-and-inventory");
        AddBasicAuth(req, connection);
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    public bool VerifyWebhookSignature(string rawBody, IDictionary<string, string?> headers, ChannelConnectionContext connection)
    {
        // Trendyol webhook signature schemes vary; if a configured header+secret is provided, validate it.
        // Otherwise return false to force polling/explicit ingestion paths until verified per merchant config.
        if (connection.Extra != null &&
            connection.Extra.TryGetValue("WebhookSignatureHeader", out var headerName) &&
            !string.IsNullOrWhiteSpace(headerName) &&
            headers.TryGetValue(headerName, out var provided) &&
            !string.IsNullOrWhiteSpace(connection.Secret))
        {
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(provided ?? string.Empty),
                Encoding.UTF8.GetBytes(connection.Secret));
        }

        return false;
    }

    public NormalizedOrderEvent? ParseOrderEvent(string rawBody, IDictionary<string, string?> headers)
    {
        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;

        var orderId = root.TryGetProperty("orderNumber", out var o) ? o.GetRawText()
                    : root.TryGetProperty("id", out var id) ? id.GetRawText() : null;
        var status = root.TryGetProperty("status", out var st) ? st.GetString() : null;

        var normalizedType = status switch
        {
            "Created" or "Picking" or "Invoiced" => "order_created",
            "Cancelled" => "order_canceled",
            "Returned" or "UnDeliveredAndReturned" => "refund_created",
            _ => null
        };
        if (normalizedType is null || string.IsNullOrWhiteSpace(orderId)) return null;

        var items = new List<NormalizedLineItem>();
        if (root.TryGetProperty("lines", out var lines))
        {
            foreach (var line in lines.EnumerateArray())
            {
                var sku = line.TryGetProperty("merchantSku", out var ms) ? ms.GetString() ?? "" :
                          line.TryGetProperty("barcode", out var bc) ? bc.GetString() ?? "" : "";
                var qty = line.TryGetProperty("quantity", out var q) ? q.GetInt32() : 1;
                if (!string.IsNullOrWhiteSpace(sku) && qty > 0)
                    items.Add(new NormalizedLineItem(sku, qty, normalizedType));
            }
        }

        headers.TryGetValue("X-Event-Id", out var extEventId);
        var eventId = !string.IsNullOrWhiteSpace(extEventId)
            ? extEventId!
            : $"trendyol:{normalizedType}:{orderId}:{HashLineItems(items)}";

        return new NormalizedOrderEvent("trendyol", eventId, normalizedType, orderId, items);
    }

    private static void AddBasicAuth(HttpRequestMessage req, ChannelConnectionContext connection)
    {
        var apiKey = connection.AccessTokenOrApiKey ?? "";
        var apiSecret = connection.Secret ?? "";
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:{apiSecret}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        if (!string.IsNullOrWhiteSpace(connection.ShopOrSellerId))
            req.Headers.UserAgent.ParseAdd($"{connection.ShopOrSellerId} - SelfIntegration");
    }

    private static string HashLineItems(IEnumerable<NormalizedLineItem> items)
    {
        var canonical = string.Join("|", items.OrderBy(i => i.Sku).Select(i => $"{i.Sku}:{i.Quantity}:{i.Reason}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
