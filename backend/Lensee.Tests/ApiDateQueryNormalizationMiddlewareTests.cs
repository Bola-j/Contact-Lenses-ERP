using Lensee.Host.Infrastructure;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Lensee.Tests;

public sealed class ApiDateQueryNormalizationMiddlewareTests
{
    [Fact]
    public async Task DateOnlyReportRange_UsesInclusiveCalendarBounds()
    {
        var context = NewContext("/api/v1/reports/sales", "?from=14%2F10%2F2026&to=14%2F10%2F2026");
        var middleware = new ApiDateQueryNormalizationMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.Equal("2026-10-14", context.Request.Query["from"]);
        Assert.Equal("2026-10-14T23:59:59.9999999", context.Request.Query["to"]);
    }

    [Fact]
    public async Task DateOnlyEndpoint_KeepsCalendarDateForBothBounds()
    {
        var context = NewContext("/api/v1/reports/executive", "?from=14%2F10%2F2026&to=15%2F10%2F2026");
        var middleware = new ApiDateQueryNormalizationMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.Equal("2026-10-14", context.Request.Query["from"]);
        Assert.Equal("2026-10-15", context.Request.Query["to"]);
    }

    [Fact]
    public async Task ExplicitDateTime_PreservesItsPreciseBound()
    {
        var context = NewContext("/api/v1/audit", "?from=14%2F10%2F2026%2009%3A08%3A07");
        var middleware = new ApiDateQueryNormalizationMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.Equal("2026-10-14T09:08:07.0000000", context.Request.Query["from"]);
    }

    [Fact]
    public async Task OperationsCreatedRange_UsesInclusiveCalendarBounds()
    {
        var context = NewContext("/api/v1/operations", "?createdFrom=14%2F10%2F2026&createdTo=14%2F10%2F2026");
        var middleware = new ApiDateQueryNormalizationMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.Equal("2026-10-14", context.Request.Query["createdFrom"]);
        Assert.Equal("2026-10-14T23:59:59.9999999", context.Request.Query["createdTo"]);
    }

    private static DefaultHttpContext NewContext(string path, string query)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        return context;
    }
}
