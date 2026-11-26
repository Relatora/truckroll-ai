using Truckroll.Api.Data;
using Truckroll.Data.Seed;

namespace Truckroll.Api.Common;

/// <summary>
/// "Today" for the business: the date in the company's time zone (Ontario), not the server's.
/// A furnace's warranty doesn't expire at midnight UTC.
/// </summary>
/// <remarks>
/// In the demo, <c>Demo:Today</c> pins the date so warranty states match the seeded data and the
/// screenshots. Everywhere else it's the real local date.
/// </remarks>
public sealed class CompanyClock(IConfiguration configuration)
{
    public static TimeZoneInfo Zone => DemoDataGenerator.LocalZone;

    public DateOnly Today => DataServiceExtensions.DemoToday(configuration);
}
