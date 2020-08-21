using IxMilia.Dxf;
using System;
using System.Collections.Generic;
using System.Linq;

namespace IxMilia.Converters
{
    public class SvgPath
    {
        public List<SvgPathSegment> Segments { get; }

        public SvgPath(IEnumerable<SvgPathSegment> segments)
        {
            Segments = segments.ToList();
        }

        public override string ToString()
        {
            return string.Join(" ", Segments);
        }

        public static SvgPath FromEllipse(double centerX, double centerY, double majorAxisX, double majorAxisY, double minorAxisRatio, double startAngle, double endAngle)
        {
            // large arc and counterclockwise computations all rely on the end angle being greater than the start
            while (endAngle < startAngle)
            {
                endAngle += Math.PI * 2.0;
            }

            var axisAngle = Math.Atan2(majorAxisY, majorAxisY);
            var majorAxisLength = Math.Sqrt(majorAxisX * majorAxisX + majorAxisY * majorAxisY);
            var minorAxisLength = majorAxisLength * minorAxisRatio;

            var startSin = Math.Sin(startAngle);
            var startCos = Math.Cos(startAngle);
            var startX = centerX + startCos * majorAxisLength;
            var startY = centerY + startSin * minorAxisLength;

            var endSin = Math.Sin(endAngle);
            var endCos = Math.Cos(endAngle);
            var endX = centerX + endCos * majorAxisLength;
            var endY = centerY + endSin * minorAxisLength;

            var enclosedAngle = endAngle - startAngle;
            var isLargeArc = (endAngle - startAngle) > Math.PI;
            var isCounterClockwise = endAngle > startAngle;

            var segments = new List<SvgPathSegment>();
            segments.Add(new SvgMoveToPath(startX, startY));
            var oneDegreeInRadians = Math.PI / 180.0;
            if (Math.Abs(Math.PI - enclosedAngle) <= oneDegreeInRadians)
            {
                // really close to a semicircle; split into to half arcs to avoid rendering artifacts
                var midAngle = (startAngle + endAngle) / 2.0;
                var midSin = Math.Sin(midAngle);
                var midCos = Math.Cos(midAngle);
                var midX = centerX + midCos * majorAxisLength;
                var midY = centerY + midSin * minorAxisLength;
                segments.Add(new SvgArcToPath(majorAxisLength, minorAxisLength, axisAngle, false, isCounterClockwise, midX, midY));
                segments.Add(new SvgArcToPath(majorAxisLength, minorAxisLength, axisAngle, false, isCounterClockwise, endX, endY));
            }
            else
            {
                // can be contained by just one arc
                segments.Add(new SvgArcToPath(majorAxisLength, minorAxisLength, axisAngle, isLargeArc, isCounterClockwise, endX, endY));
            }

            return new SvgPath(segments);
        }
    }

    public struct Location
    {
        public uint X;
        public uint Y;
        public Location(DxfPoint point)
        {
            X = (uint)point.X;
            Y = (uint)point.Y;
        }

        public Location(double x, double y)
        {
            X = (uint)x;
            Y = (uint)y;
        }

        public static bool operator ==(Location p1, Location p2)
        {
            return p1.X == p2.X && p1.Y == p2.Y;
        }

        public static bool operator !=(Location p1, Location p2)
        {
            return !(p1 == p2);
        }

        public override string ToString()
        {
            return string.Format("{0} {1}", X, Y);
        }
    }

    public abstract class SvgPathSegment
    {
        public abstract Location GetEndPoint();
    }

    public class SvgCurveToPath : SvgPathSegment
    {
        public Location CP1 { get; }
        public Location CP2 { get; }
        
        public Location CP3 { get; }

        public override Location GetEndPoint() { return CP3; }

        public SvgCurveToPath(DxfPoint c1, DxfPoint c2, DxfPoint c3)
        {
            CP1 = new Location(c1);
            CP2 = new Location(c2);
            CP3 = new Location(c3);
        }

        public override string ToString()
        {
            return string.Join(" ", new[]
            {
                "C", // CurveTo
                CP1.ToString(),
                CP2.ToString(),
                CP3.ToString()
            });
        }
    }

    public class SvgQuadraticCurveToPath : SvgPathSegment
    {
        public Location CP1 { get; }
        public Location CP2 { get; }

        public override Location GetEndPoint() { return CP2; }
        public SvgQuadraticCurveToPath(DxfPoint c1, DxfPoint c2)
        {
            CP1 = new Location(c1);
            CP2 = new Location(c2);
        }

        public override string ToString()
        {
            return string.Join(" ", new[]
            {
                "Q", // Quadratic Bezier curve
                CP1.ToString(),
                CP2.ToString()
            });
        }
    }

    public class SvgMoveToPath : SvgPathSegment
    {
        public Location Location { get; }

        public override Location GetEndPoint() { return Location; }
        public SvgMoveToPath(double locationX, double locationY)
        {
            Location = new Location(locationX,locationY);
        }
        public SvgMoveToPath(DxfPoint point)
        {
            Location = new Location(point);
        }

        public override string ToString()
        {
            return string.Join(" ", new[]
            {
                "M", // move absolute
                Location.ToString()
            });
        }
    }

    public class SvgLineToPath : SvgPathSegment
    {
        public Location Location { get; }

        public override Location GetEndPoint() { return Location; }
        public SvgLineToPath(double locationX, double locationY)
        {
            Location = new Location(locationX,locationY);
        }

        public override string ToString()
        {
            return string.Join(" ", new[]
            {
                "L", // line absolute
                Location.ToString()
            });
        }
    }

    public class SvgClosePath: SvgPathSegment
    {
        public override Location GetEndPoint() { return new Location(); }
        public override string ToString()
        {
            return "Z";
        }
    }

    public class SvgArcToPath : SvgPathSegment
    {
        public double RadiusX { get; }
        public double RadiusY { get; }
        public double XAxisRotation { get; }
        public bool IsLargeArc { get; }
        public bool IsCounterClockwiseSweep { get; }
        public Location EndPoint { get; }

        public override Location GetEndPoint() { return EndPoint; }
        public SvgArcToPath(double radiusX, double radiusY, double xAxisRotation, bool isLargeArc, bool isCounterClockwiseSweep, double endPointX, double endPointY)
        {
            RadiusX = radiusX;
            RadiusY = radiusY;
            XAxisRotation = xAxisRotation;
            IsLargeArc = isLargeArc;
            IsCounterClockwiseSweep = isCounterClockwiseSweep;
            EndPoint = new Location(endPointX,endPointY);
        }

        public override string ToString()
        {
            return string.Join(" ", new object[]
            {
                "A", // arc absolute
                RadiusX.ToDisplayString(),
                RadiusY.ToDisplayString(),
                XAxisRotation.ToDisplayString(),
                IsLargeArc ? 1 : 0,
                IsCounterClockwiseSweep ? 1 : 0,
                EndPoint.ToString()
            });
        }
    }
}
