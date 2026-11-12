using Hangfire;
using Hangfire.PostgreSql;
using InventorySyncEngine.Application.Abstractions.Channels;
using InventorySyncEngine.Application.Abstractions.Persistence;
using InventorySyncEngine.Application.Services;
using InventorySyncEngine.Infrastructure.Channels;
using InventorySyncEngine.Infrastructure.Jobs;
using InventorySyncEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var pg = builder.Configuration.GetConnectionString("Postgres")!;
builder.Services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(pg));
builder.Services.AddScoped<IInventoryDbContext>(sp => sp.GetRequiredService<AppDbContext>());

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IInventorySyncService, InventorySyncService>();
builder.Services.AddScoped<IOutboundStockSyncJob, OutboundStockSyncJob>();

builder.Services.AddHttpClient<ShopifyChannelAdapter>();
builder.Services.AddHttpClient<TrendyolChannelAdapter>();
builder.Services.AddScoped<IChannelAdapter, ShopifyChannelAdapter>();
builder.Services.AddScoped<IChannelAdapter, TrendyolChannelAdapter>();

builder.Services.AddHangfire(cfg => cfg.UsePostgreSqlStorage(opts =>
    opts.UseNpgsqlConnection(builder.Configuration.GetConnectionString("Hangfire") ?? pg)));
builder.Services.AddHangfireServer();

// TODO: register Polly retry/circuit-breaker/429 policies per named HttpClient in production.

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHangfireDashboard("/hangfire");
app.MapControllers();

app.Run();
