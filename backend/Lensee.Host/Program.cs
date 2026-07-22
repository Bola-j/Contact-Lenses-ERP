using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Lensee.Host.Endpoints;
using Lensee.Host.Infrastructure;
using Lensee.Modules.Catalog.Data;
using Lensee.Modules.Catalog.Services;
using Lensee.Modules.CRM.Data;
using Lensee.Modules.Identity.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Inventory.Services;
using Lensee.Modules.Notifications.Data;
using Lensee.Modules.Operations.Data;
using Lensee.Modules.Payments.Data;
using Lensee.Modules.Reporting.Data;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Data;
using Lensee.SharedKernel.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Npgsql;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

QuestPDF.Settings.License = LicenseType.Community;

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");

builder.Services.AddProblemDetails();
builder.Services.AddHttpContextAccessor();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Lensee API", Version = "v1" });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste a JWT access token."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddCors(options =>
{
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? ["http://localhost:3000", "http://localhost:3001", "http://localhost:5173", "http://localhost:8080", "http://127.0.0.1:3000", "http://127.0.0.1:3001", "http://127.0.0.1:5173", "http://127.0.0.1:8080"];
    var allowedOriginSuffixes = builder.Configuration.GetSection("Cors:AllowedOriginSuffixes").Get<string[]>()
        ?? [];

    options.AddPolicy("Spa", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .SetIsOriginAllowed(origin =>
            {
                if (allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                {
                    return false;
                }

                return allowedOriginSuffixes.Any(suffix =>
                    uri.Host.EndsWith(suffix.TrimStart('.'), StringComparison.OrdinalIgnoreCase));
            })
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var rateLimitOptions = builder.Configuration.GetSection("RateLimiting");
var permitLimit = rateLimitOptions.GetValue("PermitLimit", 120);
var windowSeconds = rateLimitOptions.GetValue("WindowSeconds", 60);
var queueLimit = rateLimitOptions.GetValue("QueueLimit", 0);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Title = "Too many requests.",
            Detail = "Request limit exceeded. Please wait and try again.",
            Status = StatusCodes.Status429TooManyRequests,
            Instance = context.HttpContext.Request.Path
        };

        await context.HttpContext.Response.WriteAsync(JsonSerializer.Serialize(problem), cancellationToken);
    };

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var userId = context.User.FindFirst("userId")?.Value
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        var partitionKey = !string.IsNullOrWhiteSpace(userId)
            ? $"user:{userId}"
            : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromSeconds(windowSeconds),
                QueueLimit = queueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            });
    });
});

builder.Services.AddScoped(_ => new NpgsqlConnection(connectionString));

builder.Services.AddDbContext<IdentityDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<NpgsqlConnection>()));

builder.Services.AddDbContext<CatalogDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<NpgsqlConnection>()));

builder.Services.AddDbContext<InventoryDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<NpgsqlConnection>()));

builder.Services.AddDbContext<CrmDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<NpgsqlConnection>()));

builder.Services.AddDbContext<OperationsDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<NpgsqlConnection>()));

builder.Services.AddDbContext<PaymentsDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<NpgsqlConnection>()));

builder.Services.AddDbContext<NotificationsDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<NpgsqlConnection>()));

builder.Services.AddDbContext<ReportingDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<NpgsqlConnection>()));

builder.Services.AddDbContext<SharedDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<NpgsqlConnection>()));

builder.Services.AddScoped<IClock, SystemClock>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IAuditLogWriter, AuditLogWriter>();
builder.Services.AddScoped<IAppEventPublisher, InProcessAppEventPublisher>();
builder.Services.AddScoped<IAppEventHandler<PaymentWorkflowChangedEvent>, PaymentWorkflowNotificationHandler>();
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<CategoryTreeService>();
builder.Services.AddScoped<SkuCodeGenerator>();
builder.Services.AddScoped<ICatalogEventPublisher, NoOpCatalogEventPublisher>();
builder.Services.AddScoped<StockLedgerService>();
builder.Services.AddScoped<MerchantBalanceService>();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("postgresql", tags: ["live", "ready"])
    .AddCheck<PendingMigrationsHealthCheck>("pending-migrations", tags: ["ready"]);

var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is not configured.");

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "Lensee";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "Lensee.App";

ValidateProductionConfiguration(builder.Environment, connectionString, jwtSecret);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("users.read", policy =>
        policy.RequireClaim("permission", LenseePermissions.UsersRead));

    options.AddPolicy("users.write", policy =>
        policy.RequireClaim("permission", LenseePermissions.UsersWrite));

    options.AddPolicy("users.password.write", policy =>
        policy.RequireRole(LenseeRoles.Admin)
            .RequireClaim("permission", LenseePermissions.UsersPasswordWrite));

    options.AddPolicy("catalog.read", policy =>
        policy.RequireClaim("permission", LenseePermissions.CatalogRead));

    options.AddPolicy("catalog.write", policy =>
        policy.RequireClaim("permission", LenseePermissions.CatalogWrite));

    options.AddPolicy("inventory.read", policy =>
        policy.RequireClaim("permission", LenseePermissions.InventoryRead));

    options.AddPolicy("inventory.write", policy =>
        policy.RequireRole(LenseeRoles.Admin, LenseeRoles.ERPAdmin)
            .RequireClaim("permission", LenseePermissions.InventoryWrite));

    options.AddPolicy("operations.read", policy =>
        policy.RequireClaim("permission", LenseePermissions.OperationsRead));

    options.AddPolicy("operations.write", policy =>
        policy.RequireClaim("permission", LenseePermissions.OperationsWrite));

    options.AddPolicy("payments.read", policy =>
        policy.RequireClaim("permission", LenseePermissions.PaymentsRead));

    options.AddPolicy("payments.write", policy =>
        policy.RequireRole(LenseeRoles.Admin, LenseeRoles.ERPAdmin)
            .RequireClaim("permission", LenseePermissions.PaymentsWrite));

    options.AddPolicy("payments.draft", policy =>
        policy.RequireClaim("permission", LenseePermissions.PaymentsDraft));

    options.AddPolicy("payments.approve", policy =>
        policy.RequireRole(LenseeRoles.Admin, LenseeRoles.ERPAdmin, LenseeRoles.Accountant)
            .RequireClaim("permission", LenseePermissions.PaymentsApprove));

    options.AddPolicy("reports.read", policy =>
        policy.RequireClaim("permission", LenseePermissions.ReportsRead));

    options.AddPolicy("supply.read", policy =>
        policy.RequireRole(LenseeRoles.Admin, LenseeRoles.CLevel)
            .RequireClaim("permission", LenseePermissions.SupplyRead));

    options.AddPolicy("supply.write", policy =>
        policy.RequireRole(LenseeRoles.Admin)
            .RequireClaim("permission", LenseePermissions.SupplyWrite));

    options.AddPolicy("settings.write", policy =>
        policy.RequireRole(LenseeRoles.Admin, LenseeRoles.ERPAdmin)
            .RequireClaim("permission", LenseePermissions.SettingsWrite));
});

var app = builder.Build();

await InitializeDatabaseAsync(app);

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var (status, title) = exception switch
        {
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "The record was changed or removed before your update could be saved."),
            DbUpdateException => (StatusCodes.Status400BadRequest, "The requested database change could not be saved."),
            InvalidOperationException => (StatusCodes.Status400BadRequest, "The request cannot be completed in the current state."),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
        };

        var detail = app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing")
            ? exception?.Message
            : null;
        if (app.Environment.IsEnvironment("Testing") && exception is DbUpdateConcurrencyException concurrencyException)
        {
            var entries = string.Join(", ", concurrencyException.Entries.Select(entry => $"{entry.Entity.GetType().Name}:{entry.State}"));
            detail = string.IsNullOrWhiteSpace(entries) ? detail : $"{detail} Entries: {entries}.";
        }

        var problem = new ProblemDetails
        {
            Title = title,
            Detail = detail,
            Status = status,
            Instance = context.Request.Path
        };

        context.Response.StatusCode = problem.Status.Value;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
    });
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
    context.Response.Headers.TryAdd("Content-Security-Policy", "default-src 'self'; frame-ancestors 'none'; object-src 'none'; base-uri 'self'");
    await next();
});

app.UseCors("Spa");
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = WriteHealthResponseAsync });
app.MapHealthChecks("/api/v1/health", new HealthCheckOptions { ResponseWriter = WriteHealthResponseAsync });
app.MapHealthChecks("/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponseAsync
});
app.MapHealthChecks("/api/v1/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponseAsync
});

app.MapGet("/api/v1", () => Results.Ok(new
{
    name = "Lensee API",
    version = "v1",
    serverTime = DateTimeOffset.UtcNow
}))
.AllowAnonymous()
.WithTags("Platform");

app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapCatalogEndpoints();
app.MapCrmEndpoints();
app.MapInventoryEndpoints();
app.MapOperationsEndpoints();
app.MapPaymentsEndpoints();
app.MapNotificationsEndpoints();
app.MapReportsEndpoints();
app.MapStocktakeEndpoints();
app.MapSupplyEndpoints();

app.Run();

static async Task InitializeDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;

    var logger = services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("DatabaseStartup");

    var autoMigrate = app.Configuration.GetValue("Database:AutoMigrate", app.Environment.IsDevelopment());
    if (!autoMigrate)
    {
        await ValidatePendingMigrationsAsync(app, services);
        return;
    }

    try
    {
        var sharedDbContext = services.GetRequiredService<SharedDbContext>();

        await sharedDbContext.Database.ExecuteSqlRawAsync("""
            create extension if not exists "uuid-ossp";

            create schema if not exists identity;
            create schema if not exists catalog;
            create schema if not exists inventory;
            create schema if not exists crm;
            create schema if not exists operations;
            create schema if not exists payments;
            create schema if not exists notifications;
            create schema if not exists reporting;
        """);

        var baselineExistingSchema = app.Configuration.GetValue("Database:BaselineExistingSchema", app.Environment.IsDevelopment());
        if (baselineExistingSchema)
        {
            await BaselineExistingSchemaAsync(services);
        }

        await EnsurePreMigrationCompatibilityAsync(services);

        await services.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<CatalogDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<InventoryDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<CrmDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<OperationsDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<PaymentsDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<ReportingDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<SharedDbContext>().Database.MigrateAsync();

        await DatabaseCompatibility.EnsureSchemaAsync(services);
    }
    catch (Exception exception)
    {
        logger.LogError(exception, "Failed to initialize the database.");
        throw;
    }
}

static async Task ValidatePendingMigrationsAsync(WebApplication app, IServiceProvider services)
{
    var contexts = new (string Name, DbContext Context)[]
    {
        ("identity", services.GetRequiredService<IdentityDbContext>()),
        ("catalog", services.GetRequiredService<CatalogDbContext>()),
        ("inventory", services.GetRequiredService<InventoryDbContext>()),
        ("crm", services.GetRequiredService<CrmDbContext>()),
        ("operations", services.GetRequiredService<OperationsDbContext>()),
        ("payments", services.GetRequiredService<PaymentsDbContext>()),
        ("notifications", services.GetRequiredService<NotificationsDbContext>()),
        ("reporting", services.GetRequiredService<ReportingDbContext>()),
        ("shared", services.GetRequiredService<SharedDbContext>())
    };

    var pending = new List<string>();
    foreach (var (name, context) in contexts)
    {
        if (!context.Database.IsRelational())
        {
            continue;
        }

        var migrations = await context.Database.GetPendingMigrationsAsync();
        pending.AddRange(migrations.Select(migration => $"{name}:{migration}"));
    }

    if (pending.Count > 0)
    {
        throw new InvalidOperationException($"Database has pending EF migrations and Database:AutoMigrate is disabled: {string.Join(", ", pending)}.");
    }
}

static async Task EnsurePreMigrationCompatibilityAsync(IServiceProvider services)
{
    var catalogDbContext = services.GetRequiredService<CatalogDbContext>();
    var operationsDbContext = services.GetRequiredService<OperationsDbContext>();

    await catalogDbContext.Database.ExecuteSqlRawAsync("""
        do $$
        begin
            if exists (
                select 1
                from information_schema.columns
                where table_schema = 'catalog'
                  and table_name = 'products'
                  and column_name = 'sealed_expiry_rate'
            ) and not exists (
                select 1
                from information_schema.columns
                where table_schema = 'catalog'
                  and table_name = 'products'
                  and column_name = 'opened_expiry_rate'
            ) then
                alter table catalog.products rename column sealed_expiry_rate to opened_expiry_rate;
            end if;
        end $$;

        alter table if exists catalog.products
            drop constraint if exists chk_products_sealed_expiry_rate;

        alter table if exists catalog.products
            drop constraint if exists chk_products_opened_expiry_rate;

        alter table if exists catalog.products
            add constraint chk_products_opened_expiry_rate
            check (opened_expiry_rate is null or opened_expiry_rate in ('Daily','Monthly','Annual'));
        """);

    await operationsDbContext.Database.ExecuteSqlRawAsync("""
        alter table if exists operations.stocktake_adjustment_lines
            add column if not exists lot_number character varying(100);

        alter table if exists operations.stocktake_adjustment_lines
            add column if not exists expiry_date date;
        """);

    // Fresh databases must let EF migrations create payments.financial_adjustments.
    // Legacy databases are handled by post-migration compatibility.
}

static async Task BaselineExistingSchemaAsync(IServiceProvider services)
{
    await BaselineInitialMigrationsIfObjectExistsAsync(
        services.GetRequiredService<SharedDbContext>(),
        "shared.system_settings");
    await BaselineInitialMigrationsIfObjectExistsAsync(
        services.GetRequiredService<IdentityDbContext>(),
        "identity.roles_permissions");
    await BaselineInitialMigrationsIfObjectExistsAsync(
        services.GetRequiredService<CatalogDbContext>(),
        "catalog.products");
    await BaselineInitialMigrationsIfObjectExistsAsync(
        services.GetRequiredService<InventoryDbContext>(),
        "inventory.locations");
    await BaselineInitialMigrationsIfObjectExistsAsync(
        services.GetRequiredService<CrmDbContext>(),
        "crm.merchants");
    await BaselineInitialMigrationsIfObjectExistsAsync(
        services.GetRequiredService<OperationsDbContext>(),
        "operations.operation_logs");
    await BaselineMigrationIfObjectExistsAsync(
        services.GetRequiredService<OperationsDbContext>(),
        "20260722170716_AddSupplyShipments",
        "operations.supply_shipments");
    await BaselineInitialMigrationsIfObjectExistsAsync(
        services.GetRequiredService<PaymentsDbContext>(),
        "payments.main_payment_logs");
    await BaselineInitialMigrationsIfObjectExistsAsync(
        services.GetRequiredService<NotificationsDbContext>(),
        "notifications.notification_logs");
    await BaselineInitialMigrationsIfObjectExistsAsync(
        services.GetRequiredService<ReportingDbContext>(),
        "reporting.export_logs");
}

static async Task BaselineInitialMigrationsIfObjectExistsAsync(DbContext dbContext, string markerObject)
{
    var initialMigrations = dbContext.Database.GetMigrations()
        .Where(migration => migration.Contains("_Initial", StringComparison.OrdinalIgnoreCase))
        .ToArray();

    if (initialMigrations.Length == 0)
    {
        return;
    }

    await dbContext.Database.ExecuteSqlRawAsync("""
        create table if not exists "__EFMigrationsHistory" (
            "MigrationId" character varying(150) not null primary key,
            "ProductVersion" character varying(32) not null
        );
        """);

    foreach (var migration in initialMigrations)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            insert into "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            select {migration}, '8.0.27'
            where to_regclass({markerObject}) is not null
            on conflict ("MigrationId") do nothing;
            """);
    }
}

static async Task BaselineMigrationIfObjectExistsAsync(DbContext dbContext, string migration, string markerObject)
{
    await dbContext.Database.ExecuteSqlRawAsync("""
        create table if not exists "__EFMigrationsHistory" (
            "MigrationId" character varying(150) not null primary key,
            "ProductVersion" character varying(32) not null
        );
        """);

    await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
        insert into "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
        select {migration}, '8.0.27'
        where to_regclass({markerObject}) is not null
        on conflict ("MigrationId") do nothing;
        """);
}

static Task WriteHealthResponseAsync(HttpContext context, Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report)
{
    context.Response.ContentType = "application/json";

    var payload = new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(entry => new
        {
            name = entry.Key,
            status = entry.Value.Status.ToString(),
            description = entry.Value.Description
        })
    };

    return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
}

static void ValidateProductionConfiguration(IHostEnvironment environment, string connectionString, string jwtSecret)
{
    if (!environment.IsProduction())
    {
        return;
    }

    var weakSecretMarkers = new[]
    {
        "replace-with",
        "change-me",
        "x7kP9mQw2nRjLvBsYdHcZeAuFgTiWoNp"
    };

    if (jwtSecret.Length < 32 ||
        weakSecretMarkers.Any(marker => jwtSecret.Contains(marker, StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException("Production Jwt:Secret must be a strong unique secret of at least 32 characters.");
    }

    var weakConnectionMarkers = new[]
    {
        "SomeStrongPassword123!",
        "change-me",
        "replace-with"
    };

    if (weakConnectionMarkers.Any(marker => connectionString.Contains(marker, StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException("Production database connection string contains a development placeholder password.");
    }
}

public partial class Program;
