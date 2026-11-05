namespace SkyWatch.Analytics;

internal static class GeoMath
{
    private const double EarthRadiusNm = 3_440.065;

    public static double DistanceNm(double lat1, double lon1, double lat2, double lon2)
    {
        double dLat = ToRadians(lat2 - lat1);
        double dLon = ToRadians(lon2 - lon1);

        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        return EarthRadiusNm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    /// <summary>Normalizes a heading delta to [-180, 180].</summary>
    public static double NormalizeTurn(double degrees)
    {
        double value = degrees % 360;

        if (value > 180)
        {
            value -= 360;
        }
        else if (value < -180)
        {
            value += 360;
        }

        return value;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
