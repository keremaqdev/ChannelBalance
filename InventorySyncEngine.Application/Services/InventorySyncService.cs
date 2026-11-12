using System.Data;
using Dapper;
using Hangfire;
using InventorySyncEngine.Application.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InventorySyncEngine.Application.Services;

public sealed record StockMutationCommand(
    Guid MerchantId,
    string Sku,
    int Delta,                     // negative = deduct, positive = restock/cancel/refund
    string SourceChannel,
    string? ExternalOrderId,
    string? ExternalEventId,       // required for webhook-driven idempotency
    string EventType,
    string Reason
);

public sealed record StockMutationResult(bool Applied, string Status, int? NewAvailableStock, long? LedgerLogId);

public interface IInventorySyncService
{
    Task<StockMutationResult> ApplyStockMutationAsync(StockMutationCommand command, CancellationToken ct);
}

public interface IOutboundStockSyncJob
{
    Task BroadcastStockUpdateAsync(Guid merchantId, Guid inventoryItemId, string sourceChannel, long ledgerLogId, CancellationToken ct);
}

public sealed class InventorySyncService : IInventorySyncService
{
    private readonly IInventoryDbContext _db;
    private readonly IBackgroundJobClient _jobs;
    private readonly TimeProvider _time;

    public InventorySyncService(IInventoryDbContext db, IBackgroundJobClient jobs, TimeProvider time)
    {
        _db = db;
        _jobs = jobs;
        _time = time;
    }

    public async Task<StockMutationResult> ApplyStockMutationAsync(StockMutationCommand command, CancellationToken ct)
    {
        if (command.Delta == 0)
            return new StockMutationResult(false, "NoOp", null, null);

        var now = _time.GetUtcNow();

        // EF Core + Dapper share the same connection/transaction for a single unit of work.
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var conn = _db.Database.GetDbConnection();
        var dbTx = tx.GetDbTransaction();

        // 1) Idempotency gate (for inbound webhook/event driven mutations)
        if (!string.IsNullOrWhiteSpace(command.ExternalEventId))
        {
            const string insertEventSql = @"
INSERT INTO processed_webhook_events (channel, external_event_id, event_type, status, received_at, processed_at)
VALUES (@Channel, @ExternalEventId, @EventType, 'Processed', @Now, @Now)
ON CONFLICT (channel, external_event_id) DO NOTHING;";

            var inserted = await conn.ExecuteAsync(new CommandDefinition(
                insertEventSql,
                new
                {
                    Channel = command.SourceChannel,
                    ExternalEventId = command.ExternalEventId,
                    EventType = command.EventType,
                    Now = now
                },
                dbTx,
                cancellationToken: ct));

            if (inserted == 0)
            {
                await tx.RollbackAsync(ct);
                return new StockMutationResult(false, "DuplicateEvent", null, null);
            }
        }

        // 2) Lock the inventory row (serializes concurrent mutations for the same SKU)
        const string lockItemSql = @"
SELECT id, merchant_id, sku, available_stock, reserved_stock
FROM inventory_items
WHERE merchant_id = @MerchantId AND sku = @Sku
FOR UPDATE;";

        var item = await conn.QuerySingleOrDefaultAsync<InventoryRow>(new CommandDefinition(
            lockItemSql,
            new { command.MerchantId, command.Sku },
            dbTx,
            cancellationToken: ct));

        if (item is null)
        {
            await tx.RollbackAsync(ct);
            throw new InvalidOperationException($"Inventory item not found for MerchantId={command.MerchantId}, Sku={command.Sku}");
        }

        var previousQty = item.available_stock;
        var newQty = previousQty + command.Delta;

        // 3) Deterministic stock invariant (never allow negative availability)
        if (newQty < 0)
        {
            await tx.RollbackAsync(ct);
            return new StockMutationResult(false, "InsufficientStock", previousQty, null);
        }

        // 4) Apply mutation
        const string updateItemSql = @"
UPDATE inventory_items
SET available_stock = @NewQty,
    updated_at = @Now
WHERE id = @Id;";

        await conn.ExecuteAsync(new CommandDefinition(
            updateItemSql,
            new { NewQty = newQty, Now = now, item.id },
            dbTx,
            cancellationToken: ct));

        // 5) Immutable audit log entry
        const string insertLogSql = @"
INSERT INTO inventory_ledger_logs
    (inventory_item_id, merchant_id, sku, previous_qty, delta, new_qty, source_channel, external_order_id, reason, sync_status, created_at)
VALUES
    (@InventoryItemId, @MerchantId, @Sku, @PreviousQty, @Delta, @NewQty, @SourceChannel, @ExternalOrderId, @Reason, 'Pending', @Now)
RETURNING id;";

        var ledgerLogId = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            insertLogSql,
            new
            {
                InventoryItemId = item.id,
                command.MerchantId,
                command.Sku,
                PreviousQty = previousQty,
                command.Delta,
                NewQty = newQty,
                command.SourceChannel,
                command.ExternalOrderId,
                command.Reason,
                Now = now
            },
            dbTx,
            cancellationToken: ct));

        await tx.CommitAsync(ct);

        // 6) Enqueue outbound propagation to other channels (after commit)
        _jobs.Enqueue<IOutboundStockSyncJob>(job =>
            job.BroadcastStockUpdateAsync(command.MerchantId, item.id, command.SourceChannel, ledgerLogId, CancellationToken.None));

        return new StockMutationResult(true, "Applied", newQty, ledgerLogId);
    }

    private sealed class InventoryRow
    {
        public Guid id { get; init; }
        public Guid merchant_id { get; init; }
        public string sku { get; init; } = default!;
        public int available_stock { get; init; }
        public int reserved_stock { get; init; }
    }
}
