using System.Data.Common;
using InventorySyncEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace InventorySyncEngine.Application.Abstractions.Persistence;

public interface IInventoryDbContext
{
    DbSet<InventoryItem> InventoryItems { get; }
    DbSet<InventoryLedgerLog> InventoryLedgerLogs { get; }
    DbSet<ProcessedWebhookEvent> ProcessedWebhookEvents { get; }
    DbSet<ChannelListing> ChannelListings { get; }

    DatabaseFacade Database { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
