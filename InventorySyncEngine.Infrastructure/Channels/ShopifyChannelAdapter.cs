using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InventorySyncEngine.Application.Abstractions.Channels;

namespace InventorySyncEngine.Infrastructure.Channels;

public sealed class ShopifyChannelAdapter : IChannelAdapter
{
    private readonly HttpClient _http;
    public string Channel => "shopify";

    public ShopifyChannelAdapter(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<ChannelProductDto>> FetchProductsAsync(ChannelConnectionContext connection, CancellationToken ct)
    {
        // Starter: REST products endpoint (paginate via Link header in production)
        // GET https://{shop}/admin/api/2024-10/products.json?limit=250
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://{connection.ShopOrSellerId}/admin/api/2024-10/products.json?limit=250");
        req.Headers.Add("X-Shopify-Access-Token", connection.AccessTokenOrApiKey);

        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var list = new List<ChannelProductDto>();
        if (!doc.RootElement.TryGetProperty("products", out var products)) return list;

        foreach (var p in products.EnumerateArray())
        {
            var productId = p.GetProperty("id").GetRawText();
            var title = p.GetProperty("title").GetString() ?? string.Empty;
            foreach (var v in p.GetProperty("variants").EnumerateArray())
            {
                list.Add(new ChannelProductDto(
                    ExternalProductId: productId,
                    ExternalVariantId: v.GetProperty("id").GetRawText(),
                    Sku: v.TryGetProperty("sku", out var skuEl) ? (skuEl.GetString() ?? string.Empty) : string.Empty,
                    Title: title,
                    AvailableQuantity: v.TryGetProperty("inventory_quantity", out var q) ? q.GetInt32() : 0
                ));
            }
        }
        return list;
    }

    public async Task PushStockAsync(ChannelConnectionContext connection, StockPushRequest request, CancellationToken ct)
    {
        // Production-accurate Shopify flow requires inventory_item_id + location_id and InventoryLevel Set API.
        // Store inventory_item_id/location_id in ChannelListing metadata and call:
        // POST /admin/api/2024-10/inventory_levels/set.json { location_id, inventory_item_id, available }
        // This starter shows the adapter contract and resilient HTTP call shape.
        if (string.IsNullOrWhiteSpace(connection.ShopOrSellerId))
            throw new InvalidOperationException("Shopify shop domain is required.");

        var payload = JsonSerializer.Serialize(new
        {
            inventory_level = new
            {
                // location_id = ..., inventory_item_id = ...,
                available = request.AbsoluteAvailableQuantity
            }
        });

        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"https://{connection.ShopOrSellerId}/admin/api/2024-10/inventory_levels/set.json");
        req.Headers.Add("X-Shopify-Access-Token", connection.AccessTokenOrApiKey);
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct);
        // Let Polly/retry policies handle transient/429 at HttpClient registration; throw on non-success for job retry.
        resp.EnsureSuccessStatusCode();
    }

    public bool VerifyWebhookSignature(string rawBody, IDictionary<string, string?> headers, ChannelConnectionContext connection)
    {
        // Shopify: X-Shopify-Hmac-Sha256 = base64(HMAC_SHA256(secret, rawBody))
        if (string.IsNullOrWhiteSpace(connection.Secret)) return false;
        if (!headers.TryGetValue("X-Shopify-Hmac-Sha256", out var hmacHeader) || string.IsNullOrWhiteSpace(hmacHeader))
            return false;

        var key = Encoding.UTF8.GetBytes(connection.Secret);
        var bodyBytes = Encoding.UTF8.GetBytes(rawBody);
        using var hmac = new HMACSHA256(key);
        var hash = hmac.ComputeHash(bodyBytes);
        var computed = Convert.ToBase64String(hash);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed),
            Encoding.UTF8.GetBytes(hmacHeader!));
    }

    public NormalizedOrderEvent? ParseOrderEvent(string rawBody, IDictionary<string, string?> headers)
    {
        headers.TryGetValue("X-Shopify-Topic", out var topic);
        headers.TryGetValue("X-Shopify-Webhook-Id", out var eventId);

        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        var orderId = root.TryGetProperty("id", out var idEl) ? idEl.GetRawText() : null;

        var normalizedType = topic switch
        {
            "orders/create" => "order_created",
            "orders/cancelled" => "order_canceled",
            "refunds/create" => "refund_created",
            _ => null
        };
        if (normalizedType is null) return null;

        var items = new List<NormalizedLineItem>();
        if (normalizedType == "refund_created" && root.TryGetProperty("refund_line_items", out var rlis))
        {
            foreach (var rli in rlis.EnumerateArray())
            {
                if (!rli.TryGetProperty("line_item", out var li)) continue;
                var sku = li.TryGetProperty("sku", out var s) ? (s.GetString() ?? "") : "";
                var qty = rli.TryGetProperty("quantity", out var q) ? q.GetInt32() : 0;
                if (!string.IsNullOrWhiteSpace(sku) && qty > 0)
                    items.Add(new NormalizedLineItem(sku, qty, "refund_created"));
            }
        }
        else if (root.TryGetProperty("line_items", out var lis))
        {
            foreach (var li in lis.EnumerateArray())
            {
                var sku = li.TryGetProperty("sku", out var s) ? (s.GetString() ?? "") : "";
                var qty = li.TryGetProperty("quantity", out var q) ? q.GetInt32() : 0;
                if (!string.IsNullOrWhiteSpace(sku) && qty > 0)
                    items.Add(new NormalizedLineItem(sku, qty, normalizedType));
            }
        }

        var resolvedEventId = !string.IsNullOrWhiteSpace(eventId)
            ? eventId!
            : $"{normalizedType}:{orderId}:{ComputeHash(rawBody)}";

        return new NormalizedOrderEvent("shopify", resolvedEventId, normalizedType, orderId, items);
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
