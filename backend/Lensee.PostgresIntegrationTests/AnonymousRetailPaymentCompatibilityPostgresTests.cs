using Lensee.Host.Infrastructure;
using Lensee.Modules.CRM.Data;
using Lensee.Modules.Operations.Data;
using Lensee.Modules.Payments.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Lensee.PostgresIntegrationTests;

public sealed class AnonymousRetailPaymentCompatibilityPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("lensee")
        .WithUsername("lensee_user")
        .WithPassword("SomeStrongPassword123!")
        .Build();

    private PostgresApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = new PostgresApplicationFactory(_postgres.GetConnectionString());
        await _factory.MigrateAllAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [PostgreSqlIntegrationFact]
    public async Task RepeatedCompatibilityBackfill_PreservesAnonymousIdentityAndExplicitMerchantTrack()
    {
        var seed = await _factory.SeedOperationReferencesAsync();
        var actorId = Guid.NewGuid();
        await _factory.SeedUserAsync(actorId);

        var merchantId = Guid.NewGuid();
        var anonymousOperationId = Guid.NewGuid();
        var anonymousTransactionOperationId = Guid.NewGuid();
        var merchantOperationId = Guid.NewGuid();
        var now = new DateTime(2042, 4, 5, 12, 0, 0, DateTimeKind.Unspecified);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var crm = services.GetRequiredService<CrmDbContext>();
            var operations = services.GetRequiredService<OperationsDbContext>();
            crm.Merchants.Add(new Merchant
            {
                Id = merchantId,
                BusinessName = "Walk-in Buyer",
                ContactPersonName = "Walk-in Buyer",
                PhoneNumbers = [],
                BusinessType = "Other",
                Status = "Active",
                CreatedAt = now,
                UpdatedAt = now
            });

            operations.OperationLogs.AddRange(
                CreateSale(anonymousOperationId, seed.OnlineLocationId, actorId, now, clientId: null),
                CreateSale(anonymousTransactionOperationId, seed.OnlineLocationId, actorId, now, clientId: null, paymentMethod: "CashTransaction"),
                CreateSale(merchantOperationId, seed.OnlineLocationId, actorId, now, merchantId));
            operations.OperationLines.AddRange(
                CreateSaleLine(anonymousOperationId, seed.SkuId),
                CreateSaleLine(anonymousTransactionOperationId, seed.SkuId),
                CreateSaleLine(merchantOperationId, seed.SkuId));

            await crm.SaveChangesAsync();
            await operations.SaveChangesAsync();
        }

        using var logCapture = new CompatibilityLogCapture();
        _factory.Services.GetRequiredService<ILoggerFactory>().AddProvider(logCapture);
        await DatabaseCompatibility.EnsureSchemaAsync(_factory.Services);
        await DatabaseCompatibility.EnsureSchemaAsync(_factory.Services);
        Assert.True(logCapture.Errors.Count == 0, string.Join(Environment.NewLine, logCapture.Errors));

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var crm = services.GetRequiredService<CrmDbContext>();
            var operations = services.GetRequiredService<OperationsDbContext>();
            var payments = services.GetRequiredService<PaymentsDbContext>();

            Assert.Equal(1, await crm.Merchants.CountAsync(value => value.BusinessName == "Walk-in Buyer"));

            var anonymousSale = await operations.OperationLogs.SingleAsync(value => value.Id == anonymousOperationId);
            Assert.Null(anonymousSale.ClientId);
            Assert.Equal("Walk-in Buyer", anonymousSale.ClientName);

            var merchantSale = await operations.OperationLogs.SingleAsync(value => value.Id == merchantOperationId);
            Assert.Equal(merchantId, merchantSale.ClientId);

            var anonymousLog = await payments.MainPaymentLogs.SingleAsync(value => value.OperationId == anonymousOperationId);
            Assert.Null(anonymousLog.MerchantId);
            Assert.Equal("OtherPayments", anonymousLog.Scope);
            Assert.Equal("CashHandToHand", anonymousLog.PaymentMethod);
            Assert.Equal("PendingAccountant", anonymousLog.Status);

            var transactionLog = await payments.MainPaymentLogs.SingleAsync(value => value.OperationId == anonymousTransactionOperationId);
            Assert.Null(transactionLog.MerchantId);
            Assert.Equal("OtherPayments", transactionLog.Scope);
            Assert.Equal("CashTransaction", transactionLog.PaymentMethod);
            Assert.Equal("PendingAdmin", transactionLog.Status);

            var merchantLog = await payments.MainPaymentLogs.SingleAsync(value => value.OperationId == merchantOperationId);
            Assert.Equal(merchantId, merchantLog.MerchantId);
            Assert.Equal("MerchantAccount", merchantLog.Scope);

            // Compatibility initializes payment tracks; it must not fabricate
            // collection records for either anonymous or merchant sales.
            Assert.DoesNotContain(await payments.CashRecords.ToListAsync(), value =>
                value.OperationId == anonymousOperationId || value.OperationId == merchantOperationId);
            Assert.DoesNotContain(await payments.CashRecords.ToListAsync(), value => value.OperationId == anonymousTransactionOperationId);
            Assert.Equal(3, await payments.MainPaymentLogs.CountAsync());
        }
    }

    private static OperationLog CreateSale(Guid id, Guid locationId, Guid actorId, DateTime now, Guid? clientId, string paymentMethod = "CashHandToHand") => new()
    {
        Id = id,
        OperationNumber = $"ANON-COMPAT-{id:N}",
        OperationType = "RetailSale",
        Status = "Completed",
        RecordKind = "Standard",
        SourceLocationId = locationId,
        ClientId = clientId,
        ClientName = "Walk-in Buyer",
        PaymentMethod = paymentMethod,
        CreatedBy = actorId,
        CreatedAt = now,
        ConfirmedAt = now
    };

    private static OperationLine CreateSaleLine(Guid operationId, Guid skuId) => new()
    {
        Id = Guid.NewGuid(),
        OperationId = operationId,
        SkuId = skuId,
        ProductNameSnapshot = "Compatibility test product",
        SkuCodeSnapshot = "ANON-COMPAT-SKU",
        Section = "Standard",
        EntryMode = "Packs",
        Quantity = 1,
        UnitPrice = 100m,
        LineTotal = 100m
    };

    private sealed class CompatibilityLogCapture : ILoggerProvider
    {
        public List<string> Errors { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CaptureLogger(Errors);

        public void Dispose() { }

        private sealed class CaptureLogger(List<string> errors) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= LogLevel.Warning)
                    errors.Add(exception?.ToString() ?? formatter(state, exception));
            }
        }
    }
}
