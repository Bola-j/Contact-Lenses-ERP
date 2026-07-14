using Lensee.SharedKernel.Abstractions;

namespace Lensee.Host.Infrastructure;

public sealed class SystemClock : IClock
{
    private static readonly TimeZoneInfo EgyptTimeZone = ResolveEgyptTimeZone();

    public DateTime UtcNow => DateTime.UtcNow;

    public DateTime EgyptNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, EgyptTimeZone);

    private static TimeZoneInfo ResolveEgyptTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        }
    }
}
