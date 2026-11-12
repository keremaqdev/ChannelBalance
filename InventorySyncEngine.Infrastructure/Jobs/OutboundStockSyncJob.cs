using System.Data;
using Dapper;
using InventorySyncEngine.Application.Abstractions.Channels;
using InventorySyncEngine.Application.Abstractions.Persistence;
using InventorySyncEngine.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace InventorySyncEngine.Infrastructure.Jobs;

/// <summary>
/// Fan-out job: reads committed stock and pushes absolute quantity to all connected channels except the source.
/// Must be retry-safe (absolute sets) and update ledger sync status.
/// </summary>
public sealed class OutboundStockSyncJob : IOutboundStockSyncJob
{
    private readonly IInventoryDbContext _db;
    private readonly IEnumerable<IChannelAdapter> _adapters;

    public OutboundStockSyncJob(IInventoryDbContext db, IEnumerable<IChannelAdapter> adapters)
    {
        _db = db;
        _adapters = adapters;
    }

    public async Task BroadcastStockUpdateAsync(Guid merchantId, Guid inventoryItemId, string sourceChannel, long ledgerLogId, CancellationToken ct)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await _db.Database.OpenConnectionAsync(ct);

        const string itemSql = @"SELECT sku, available_stock FROM inventory_items WHERE id = @inventoryItemId AND merchant_id = @merchantId;";
        var item = await conn.QuerySingleAsync<(string sku, int available_stock)>(new CommandDefinition(itemSql, new { inventoryItemId, merchantId }, cancellationToken: ct));

        const string listingsSql = @"
SELECT channel, external_product_id, external_variant_id
FROM channel_listings
WHERE merchant_id = @merchantId AND inventory_item_id = @inventoryItemId AND is_active = true AND channel <> @sourceChannel;";

        var listings = (await conn.QueryAsync<(string channel, string external_product_id, string? external_variant_id)>(
            new CommandDefinition(listingsSql, new { merchantId, inventoryItemId, sourceChannel }, cancellationToken: ct))).ToList();

        var failures = 0;
        foreach (var listing in listings)
        {
            var adapter = _adapters.FirstOrDefault(a => string.Equals(a.Channel, listing.channel, StringComparison.OrdinalIgnoreCase));
            if (adapter is null) { failures++; continue; }

            try
            {
                // Resolve per-merchant/channel credentials from secure store in production.
                var connection = new ChannelConnectionContext(merchantId, listing.channel, null, null, null);
                await adapter.PushStockAsync(connection, new StockPushRequest(
                    listing.external_product_id,
                    listing.external_variant_id,
                    item.sku,
                    item.available_stock), ct);
            }
            catch
            {
                failures++;
                throw; // Let Hangfire retry with backoff; aggregate status can be refined with per-channel attempt tracking.
            }
        }

        var status = failures == 0 ? "Synced" : "Partial";
        const string updateSql = @"UPDATE inventory_ledger_logs SET sync_status = @status WHERE id = @ledgerLogId;";
        await conn.ExecuteAsync(new CommandDefinition(updateSql, new { status, ledgerLogId }, cancellationToken: ct));
    }
}
