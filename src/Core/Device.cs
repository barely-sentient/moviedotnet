using Donkeh.Hosting;

namespace MovieDotNet.Core;

/// <summary>
/// Host form factor. Phones use the single-page shell; the environment override
/// makes that shell testable on the desktop host.
/// </summary>
public static class Device
{
    public static bool Mobile =>
        Host.IsMobile
        || Environment.GetEnvironmentVariable("MOVIEDOTNET_MOBILE") is "1" or "true";
}
