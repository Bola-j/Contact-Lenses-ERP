using Lensee.Host.Infrastructure;
using Xunit;

namespace Lensee.Tests;

public sealed class PasswordHasherTests
{
    [Fact]
    public void Verify_ReturnsTrue_ForOriginalPassword()
    {
        var hasher = new PasswordHasher();

        var hash = hasher.Hash("Admin123!");

        Assert.True(hasher.Verify("Admin123!", hash));
    }

    [Fact]
    public void Verify_ReturnsFalse_ForDifferentPassword()
    {
        var hasher = new PasswordHasher();

        var hash = hasher.Hash("Admin123!");

        Assert.False(hasher.Verify("Wrong123!", hash));
    }
}
