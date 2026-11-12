# InventorySyncEngine (MVP Scaffold)

Deterministic, high-reliability multi-channel e-commerce inventory sync engine.

**Stack:** .NET 8 Web API (Clean Architecture) · PostgreSQL · Redis · Hangfire · Polly

## Prerequisites
- .NET 8 SDK
- Docker Desktop (for PostgreSQL and Redis)

## Quick start
```powershell
# 1) Start infrastructure
docker compose up -d

# 2) Restore & build (requires .NET 8 SDK)
dotnet restore
dotnet build

# 3) Apply schema
# Run schema.sql against the local PostgreSQL database (inventory_sync), e.g.:
# psql -h localhost -U ise_user -d inventory_sync -f schema.sql

# 4) Run API
dotnet run --project .\InventorySyncEngine.Api\InventorySyncEngine.Api.csproj
```

## Structure
- `InventorySyncEngine.Domain` — entities & domain primitives (no infra deps)
- `InventorySyncEngine.Application` — contracts, DTOs, transactional service
- `InventorySyncEngine.Infrastructure` — EF/Dapper persistence, channel adapters, jobs
- `InventorySyncEngine.Api` — ASP.NET Core Web API host

## Notes
- Stock mutations use PostgreSQL `SELECT ... FOR UPDATE` row-level locking inside ACID transactions.
- Inbound webhooks are deduplicated via `processed_webhook_events (channel, external_event_id)`.
- Outbound sync uses absolute-quantity pushes and durable background jobs (Hangfire).
- Shopify/Trendyol adapters are starter implementations; replace placeholder endpoints/field mappings with your merchant-specific integration details (e.g. Shopify InventoryLevel location/inventory_item IDs).
