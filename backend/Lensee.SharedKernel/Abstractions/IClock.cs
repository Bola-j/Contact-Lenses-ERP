namespace Lensee.SharedKernel.Abstractions;

public interface IClock
{
    DateTime UtcNow { get; }

    DateTime EgyptNow { get; }
}
