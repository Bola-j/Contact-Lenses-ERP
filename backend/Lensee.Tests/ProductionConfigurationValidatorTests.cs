using Lensee.Host.Infrastructure;
using Xunit;

namespace Lensee.Tests;

public sealed class ProductionConfigurationValidatorTests
{
    [Fact]
    public void ValidateCorsAllowedOrigins_AcceptsExactHttpsOrigin()
    {
        ProductionConfigurationValidator.ValidateCorsAllowedOrigins([
            "https://portal.lensee-egypt.com"
        ]);
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
            ProductionConfigurationValidator.ValidateCorsAllowedOrigins([origin]));

        Assert.Contains(origin, exception.Message);
    }
}
