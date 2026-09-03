using Lensee.Host.Infrastructure;
using Xunit;

namespace Lensee.Tests;

public sealed class ProductionConfigurationValidatorTests
{
    [Fact]
    public void ValidateCorsAllowedOrigins_AcceptsExactHttpsOrigin()
    {
        var allowedOrigins = ProductionConfigurationValidator.GetValidatedCorsAllowedOrigins([
            "https://portal.lensee-egypt.com"
        ]);

        Assert.Equal(["https://portal.lensee-egypt.com"], allowedOrigins);
    }

    [Fact]
    public void ValidateCorsAllowedOrigins_UsesExactHttpsOriginsWhenDevelopmentDefaultsAreMerged()
    {
        var allowedOrigins = ProductionConfigurationValidator.GetValidatedCorsAllowedOrigins([
            "https://portal.lensee-egypt.com",
            "http://localhost:3001",
            "http://localhost:5000",
            "http://localhost:5173",
            "http://localhost:8080"
        ]);

        Assert.Equal(["https://portal.lensee-egypt.com"], allowedOrigins);
    }

    [Theory]
    [InlineData("http://127.0.0.1:3001")]
    [InlineData("portal.lensee-egypt.com")]
    [InlineData("https://portal.lensee-egypt.com/api")]
    [InlineData("https://portal.lensee-egypt.com?x=1")]
    [InlineData("[https://portal.lensee-egypt.com](https://portal.lensee-egypt.com)")]
    public void ValidateCorsAllowedOrigins_RejectsInvalidProductionOrigins(string origin)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ProductionConfigurationValidator.GetValidatedCorsAllowedOrigins([origin]));

        Assert.Contains(origin, exception.Message);
    }
}
