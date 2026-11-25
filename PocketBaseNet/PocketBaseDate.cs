using System.Globalization;

namespace PocketBaseNet;

internal static class PocketBaseDate
{
    /// <summary>JS <c>date.toISOString().replace("T", " ")</c>, e.g. <c>2024-01-02 03:04:05.678Z</c>.</summary>
    public static string Format(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>Unspecified kinds are treated as local time (same as <see cref="DateTime.ToUniversalTime"/>).</summary>
    public static string Format(DateTime value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    public static bool IsNumber(object value) => value is
        sbyte or byte or short or ushort or int or uint or long or ulong or
        float or double or decimal
#if NET5_0_OR_GREATER
        or Half
#endif
#if NET7_0_OR_GREATER
        or Int128 or UInt128
#endif
        ;
}
