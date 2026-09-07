using System;
using Unity.Mathematics;

namespace MeasureItCS2
{
    /// <summary>
    /// Pure math helpers ported from CS1's MeasureInfo.cs (elevation/relief/length/
    /// distance/slope/direction) plus the ConvertDistance/DisplayDistance logic that used
    /// to live in ModManager.cs. Kept dependency-free (no ECS/UnityEngine types besides
    /// Unity.Mathematics) so it's easy to unit test.
    ///
    /// Note: CS2's game grid is 1 unit = 1 meter, same as CS1's "Metre" unit, so Meters
    /// here is the 1:1 passthrough case (equivalent to CS1's UnitOfDistance == 1).
    /// </summary>
    public static class MeasureMath
    {
        private const float MetersPerFoot = 0.3048f;
        private const float MetersPerMile = 1609.344f;
        private const float MetersPerKilometer = 1000f;

        private static readonly string[] Cardinals =
        {
            "N", "NE", "E", "SE", "S", "SW", "W", "NW", "N"
        };

        public static float ConvertDistance(DistanceUnit unit, float meters)
        {
            switch (unit)
            {
                case DistanceUnit.Meters:
                    return meters;
                case DistanceUnit.Kilometers:
                    return meters / MetersPerKilometer;
                case DistanceUnit.Feet:
                    return meters / MetersPerFoot;
                case DistanceUnit.Miles:
                    return meters / MetersPerMile;
                default:
                    return meters;
            }
        }

        public static string UnitSymbol(DistanceUnit unit)
        {
            switch (unit)
            {
                case DistanceUnit.Meters: return "m";
                case DistanceUnit.Kilometers: return "km";
                case DistanceUnit.Feet: return "ft";
                case DistanceUnit.Miles: return "mi";
                default: return "?";
            }
        }

        public static string UnitSymbol(SlopeUnit unit)
        {
            return unit == SlopeUnit.Degree ? "°" : "%";
        }

        public static string UnitSymbol(DirectionUnit unit)
        {
            return "°";
        }

        /// <summary>Formats a raw meters value for display, e.g. "128.4".</summary>
        public static string DisplayDistance(DistanceUnit unit, float meters)
        {
            return Round(ConvertDistance(unit, meters)).ToString("0.##");
        }

        public static float ConvertSlope(SlopeUnit unit, float slopeRatio)
        {
            return unit == SlopeUnit.Degree
                ? math.degrees(math.atan(slopeRatio))
                : slopeRatio * 100f;
        }

        public static string DisplaySlope(SlopeUnit unit, float slopeRatio)
        {
            return Round(ConvertSlope(unit, slopeRatio)).ToString("0.##");
        }

        public static string DisplayDirection(DirectionUnit unit, float directionDegrees)
        {
            if (unit == DirectionUnit.Point)
            {
                int index = (int)math.round((directionDegrees % 360f) / 45f);
                return Cardinals[index];
            }

            return Round(directionDegrees).ToString("0.##");
        }

        /// <summary>Flat (XZ-plane) distance between two points - "Length" in CS1.</summary>
        public static float LengthXZ(float3 a, float3 b)
        {
            float2 d = new float2(b.x - a.x, b.z - a.z);
            return math.length(d);
        }

        /// <summary>True 3D straight-line distance - "Distance" in CS1.</summary>
        public static float Distance3D(float3 a, float3 b)
        {
            return math.distance(a, b);
        }

        /// <summary>Vertical difference between two points - "Relief" in CS1.</summary>
        public static float Relief(float3 a, float3 b)
        {
            return b.y - a.y;
        }

        /// <summary>Average incline over the flat length - "Slope" in CS1 (rise/run ratio; convert with ConvertSlope for display).</summary>
        public static float SlopeRatio(float relief, float lengthXZ)
        {
            return lengthXZ > 0f ? relief / lengthXZ : 0f;
        }

        /// <summary>
        /// Cartographic bearing from "from" to "to" in the XZ plane, 0-360, matching the
        /// CalculateAngle() logic from CS1's MeasureInfo.cs.
        /// </summary>
        public static float Direction(float3 from, float3 to)
        {
            float2 a = new float2(0f, -1f); // Vector3.down projected onto XZ, matches CS1's reference vector
            float2 b = new float2(to.x - from.x, to.z - from.z);

            if (math.lengthsq(b) < 1e-8f)
            {
                return 0f;
            }

            float angle = math.degrees(math.acos(math.clamp(math.dot(math.normalize(a), math.normalize(b)), -1f, 1f)));
            float cross = a.x * b.y - a.y * b.x;

            if (cross > 0f)
            {
                angle = 360f - angle;
            }

            return angle;
        }

        private static float Round(float value)
        {
            return (float)Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// Single source of truth for the MeasureColorOption -> actual color mapping.
        /// Returns a hex string (e.g. "#FFD900") so both MeasureToolSystem (which
        /// needs a UnityEngine.Color for drawing) and MeasureUISystem (which sends a
        /// hex string to the React label overlay) derive from the exact same values,
        /// rather than keeping two separate color lists that could drift apart - which
        /// is exactly what happened before this was factored out (the point/line color
        /// was configurable via settings, but the floating number labels' color was
        /// hardcoded separately in SCSS and never followed the setting).
        /// </summary>
        public static string ColorHex(MeasureColorOption option)
        {
            switch (option)
            {
                case MeasureColorOption.Yellow: return "#FFD900";
                case MeasureColorOption.Magenta: return "#FF00FF";
                case MeasureColorOption.Red: return "#FF2626";
                case MeasureColorOption.Green: return "#33FF33";
                case MeasureColorOption.Cyan: return "#00E5FF";
                case MeasureColorOption.White: return "#FFFFFF";
                case MeasureColorOption.Orange: return "#FF8000";
                default: return "#FFD900";
            }
        }
    }
}
