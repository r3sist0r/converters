using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using IxMilia.Dxf;
using IxMilia.Dxf.Entities;
using IxMilia.Dxf.Objects;

namespace IxMilia.Converters
{
    using AttributeGeneratorFunc = Func<string, IDictionary<string, string>>;
    public struct DxfToSvgConverterOptions
    {
        public ConverterDxfRect DxfSource { get; }
        public ConverterSvgRect SvgDestination { get; }
        public string SvgId { get; }
        public IEnumerable<string> Layers { get; }
        public AttributeGeneratorFunc AttributesGenerator { get; }
        public DxfToSvgConverterOptions(ConverterDxfRect dxfSource, ConverterSvgRect svgDestination, string svgId = null,
                                        IEnumerable<string> layers = null, AttributeGeneratorFunc attributeGenerator = null)
        {
            DxfSource = dxfSource;
            SvgDestination = svgDestination;
            SvgId = svgId;
            Layers = layers;
            AttributesGenerator = attributeGenerator;
        }
    }

    public class DxfToSvgConverter : IConverter<DxfFile, XElement, DxfToSvgConverterOptions>
    {
        public static XNamespace Xmlns = "http://www.w3.org/2000/svg";

        public XElement Convert(DxfFile source, DxfToSvgConverterOptions options)
        {
            // adapted from https://github.com/ixmilia/bcad/blob/main/src/IxMilia.BCad.FileHandlers/Plotting/Svg/SvgPlotter.cs
            var world = new XElement(Xmlns + "g");
            IEnumerable<DxfLayer> targetLayers;
            if (options.Layers != null)
                targetLayers = source.Layers.Where(l => options.Layers.Contains(l.Name));
            else
                targetLayers = source.Layers;

            foreach (var layer in targetLayers)
            {
                var autoColor = DxfColor.FromIndex(0);
                world.Add(new XComment($" layer '{layer.Name}' "));
                var g = new XElement(Xmlns + "g",
                    new XAttribute("stroke", (layer.Color ?? autoColor).ToRGBString()),
                    new XAttribute("fill", (layer.Color ?? autoColor).ToRGBString()),
                    new XAttribute("class", $"dxf-layer {layer.Name}"));

                var entities = DxfExtensions.AssociateEntitiesWithDescriptions(source.Entities.Where(e => e.Layer == layer.Name));
                foreach (var entity in entities)
                {
                    var element = entity.Item2.ToXElement();
                    if (element != null)
                    {
                        if ((entity.Item1 != null) && (options.AttributesGenerator!=null))
                        {
                            var attributes = options.AttributesGenerator(entity.Item1);
                            foreach(KeyValuePair<string,string> entry in attributes)
                            {
                                element.SetAttributeValue(entry.Key, entry.Value);
                            }
                        }
                        g.Add(element);
                    }
                }
                world.Add(g);
            }

            var dxfar = options.DxfSource.Width / options.DxfSource.Height;
            var svgar = options.SvgDestination.ElementWidth / options.SvgDestination.ElementHeight;
            var scale = svgar < dxfar
                ? options.SvgDestination.ElementWidth / options.DxfSource.Width
                : options.SvgDestination.ElementHeight / options.DxfSource.Height;

            var root = new XElement(Xmlns + "svg",
                new XAttribute("width", options.SvgDestination.ElementWidth.ToDisplayString()),
                new XAttribute("height", options.SvgDestination.ElementHeight.ToDisplayString()),
                new XAttribute("viewBox", $"0 0 {options.SvgDestination.ElementWidth.ToDisplayString()} {options.SvgDestination.ElementHeight.ToDisplayString()}"),
                new XAttribute("version", "1.1"),
                new XAttribute("class", "dxf-drawing"),
                new XComment(" this group corrects for the y-axis going in different directions "),
                new XElement(Xmlns + "g",
                    new XAttribute("transform", $"translate(0 {options.SvgDestination.ElementHeight.ToDisplayString()}) scale(1 -1)"),
                    new XComment(" this group handles display panning "),
                    new XElement(Xmlns + "g",
                        new XAttribute("transform", "translate(0 0)"),
                        new XAttribute("class", "svg-translate"),
                        new XComment(" this group handles display scaling "),
                        new XElement(Xmlns + "g",
                            new XAttribute("transform", $"scale({scale.ToDisplayString()} {scale.ToDisplayString()})"),
                            new XAttribute("class", "svg-scale"),
                            new XComment(" this group handles initial translation offset "),
                            new XElement(Xmlns + "g",
                                new XAttribute("transform", $"translate({(-options.DxfSource.Left).ToDisplayString()} {(-options.DxfSource.Bottom).ToDisplayString()})"),
                                world)))));

            var layerNames = source.Layers.OrderBy(l => l.Name).Select(l => l.Name).ToArray();
            root = TransformToHtmlDiv(root, options.SvgId, layerNames, -options.DxfSource.Left, -options.DxfSource.Bottom, scale, scale);
            return root;
        }

        private static XElement TransformToHtmlDiv(XElement svg, string svgId, string[] layerNames, double defaultXTranslate, double defaultYTranslate, double defaultXScale, double defaultYScale)
        {
            if (string.IsNullOrWhiteSpace(svgId))
            {
                return svg;
            }

            var div = new XElement("div",
                new XAttribute("id", svgId),
                new XElement("style", GetCss()),
                new XElement("details",
                    new XElement("summary", "Controls"),
                    new XElement("button",
                        new XAttribute("class", "button-zoom-out"),
                        "Zoom out"),
                    new XElement("button",
                        new XAttribute("class", "button-zoom-in"),
                        "Zoom in"),
                    new XElement("button",
                        new XAttribute("class", "button-pan-left"),
                        "Pan left"),
                    new XElement("button",
                        new XAttribute("class", "button-pan-right"),
                        "Pan right"),
                    new XElement("button",
                        new XAttribute("class", "button-pan-up"),
                        "Pan up"),
                    new XElement("button",
                        new XAttribute("class", "button-pan-down"),
                        "Pan down"),
                    new XElement("button",
                        new XAttribute("class", "button-reset-view"),
                        "Reset")),
                    new XElement("details",
                        new XElement("summary", "Layers"),
                        new XElement("div",
                            new XAttribute("class", "layers-control"))
                        ),
                svg,
                new XElement("script",
                    new XAttribute("type", "text/javascript"),
                    new XRawText(GetJavascriptControls(svgId, layerNames, defaultXTranslate, defaultYTranslate, defaultXScale, defaultYScale))));
            return div;
        }

        private static string GetJavascriptControls(string svgId, string[] layerNames, double defaultXTranslate, double defaultYTranslate, double defaultXScale, double defaultYScale)
        {
            var assembly = typeof(DxfToSvgConverter).GetTypeInfo().Assembly;
            using (var jsStream = assembly.GetManifestResourceStream("IxMilia.Converters.SvgJavascriptControls.js"))
            using (var streamReader = new StreamReader(jsStream))
            {
                var contents = Environment.NewLine + streamReader.ReadToEnd();
                contents = contents
                    .Replace("$DRAWING-ID$", svgId)
                    .Replace("$LAYER-NAMES$", $"[{string.Join(", ", layerNames.Select(l => $"\"{l}\""))}]")
                    .Replace("$DEFAULT-X-TRANSLATE$", defaultXTranslate.ToDisplayString())
                    .Replace("$DEFAULT-Y-TRANSLATE$", defaultYTranslate.ToDisplayString())
                    .Replace("$DEFAULT-X-SCALE$", defaultXScale.ToDisplayString())
                    .Replace("$DEFAULT-Y-SCALE$", defaultYScale.ToDisplayString());
                return contents;
            }
        }

        private static string GetCss()
        {
            var assembly = typeof(DxfToSvgConverter).GetTypeInfo().Assembly;
            using (var jsStream = assembly.GetManifestResourceStream("IxMilia.Converters.SvgStyles.css"))
            using (var streamReader = new StreamReader(jsStream))
            {
                var contents = Environment.NewLine + streamReader.ReadToEnd();
                // perform replacements when necessary
                return contents;
            }
        }

        private class XRawText : XText
        {
            public XRawText(string text)
                : base(text)
            {
            }

            public override void WriteTo(XmlWriter writer)
            {
                writer.WriteRaw(Value);
            }
        }
    }

    public static class SvgExtensions
    {
        public static void SaveTo(this XElement document, Stream output)
        {
            var settings = new XmlWriterSettings()
            {
                Indent = true,
                IndentChars = "  "
            };
            using (var writer = XmlWriter.Create(output, settings))
            {
                document.WriteTo(writer);
            }
        }

        public static void SaveTo(this XElement document, string filePath)
        {
            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                document.SaveTo(fileStream);
            }
        }

        public static string ToRGBString(this DxfColor color)
        {
            var intValue = color.IsIndex
                ? color.ToRGB()
                : 0; // fall back to black
            var r = (intValue >> 16) & 0xFF;
            var g = (intValue >> 8) & 0xFF;
            var b = intValue & 0xFF;
            return $"#{r:X2}{g:X2}{b:X2}";
        }

        internal static string ToDisplayString(this double value)
        {
            return value.ToString("0.0##############", CultureInfo.InvariantCulture);
        }

        public static XElement ToXElement(this DxfEntity entity)
        {
            // elements are simply flattened in the z plane; the world transform in the main function handles the rest
            switch (entity)
            {
                case DxfArc arc:
                    return arc.ToXElement();
                case DxfCircle circle:
                    return circle.ToXElement();
                case DxfEllipse el:
                    return el.ToXElement();
                case DxfLine line:
                    return line.ToXElement();
                case DxfLwPolyline poly:
                    return poly.ToXElement();
                case DxfPolyline polyline:
                    return polyline.ToXElement();
                case DxfSpline spline:
                    return spline.ToXElement();
                case DxfHatch hatch:
                    return hatch.ToXElement();
                default:
                    return null;
            }
        }

        public static XElement ToXElement(this DxfArc arc)
        {
            var path = arc.GetSvgPath();
            return new XElement(DxfToSvgConverter.Xmlns + "path",
                new XAttribute("d", path.ToString()),
                new XAttribute("fill-opacity", 0))
                .AddStroke(arc.Color)
                .AddStrokeWidth(arc.Thickness)
                .AddVectorEffect();
        }

        public static XElement ToXElement(this DxfCircle circle)
        {
            return new XElement(DxfToSvgConverter.Xmlns + "ellipse",
                new XAttribute("cx", circle.Center.X.ToDisplayString()),
                new XAttribute("cy", circle.Center.Y.ToDisplayString()),
                new XAttribute("rx", circle.Radius.ToDisplayString()),
                new XAttribute("ry", circle.Radius.ToDisplayString()),
                new XAttribute("fill-opacity", 0))
                .AddStroke(circle.Color)
                .AddStrokeWidth(circle.Thickness)
                .AddVectorEffect();
        }

        public static XElement ToXElement(this DxfEllipse ellipse)
        {
            XElement baseShape;
            if (IsCloseTo(ellipse.StartParameter, 0.0) && IsCloseTo(ellipse.EndParameter, Math.PI * 2.0))
            {
                baseShape = new XElement(DxfToSvgConverter.Xmlns + "ellipse",
                    new XAttribute("cx", ellipse.Center.X.ToDisplayString()),
                    new XAttribute("cy", ellipse.Center.Y.ToDisplayString()),
                    new XAttribute("rx", ellipse.MajorAxis.Length.ToDisplayString()),
                    new XAttribute("ry", ellipse.MinorAxis().Length.ToDisplayString()));
            }
            else
            {
                var path = ellipse.GetSvgPath();
                baseShape = new XElement(DxfToSvgConverter.Xmlns + "path",
                    new XAttribute("d", path.ToString()));
            }

            baseShape.Add(new XAttribute("fill-opacity", 0));
            return baseShape
                .AddStroke(ellipse.Color)
                .AddStrokeWidth(1.0)
                .AddVectorEffect();
        }

        public static XElement ToXElement(this DxfLine line)
        {
            return new XElement(DxfToSvgConverter.Xmlns + "line",
                new XAttribute("x1", line.P1.X.ToDisplayString()),
                new XAttribute("y1", line.P1.Y.ToDisplayString()),
                new XAttribute("x2", line.P2.X.ToDisplayString()),
                new XAttribute("y2", line.P2.Y.ToDisplayString()))
                .AddStroke(line.Color)
                .AddStrokeWidth(line.Thickness)
                .AddVectorEffect();
        }

        public static XElement ToXElement(this DxfLwPolyline poly)
        {
            var path = poly.GetSvgPath();
            return new XElement(DxfToSvgConverter.Xmlns + "path",
                new XAttribute("d", path.ToString()),
                new XAttribute("fill-opacity", 0))
                .AddStroke(poly.Color)
                .AddStrokeWidth(1.0)
                .AddVectorEffect();
        }

        public static XElement ToXElement(this DxfPolyline polyline)
        {
            var path = polyline.GetSvgPath();
            return new XElement(DxfToSvgConverter.Xmlns + "path",
                new XAttribute("d", path.ToString()),
                new XAttribute("fill-opacity", 0))
                .AddStroke(polyline.Color)
                .AddStrokeWidth(1.0)
                .AddVectorEffect();
        }

        public static XElement ToXElement(this DxfSpline spline)
        {
            if (spline.DegreeOfCurve==3)
            {
                // Convert to SVG Bezier path
                // Based on https://github.com/bjnortier/dxf/blob/master/src/toSVG.js
                // TODO

            }
            return null;
        }

        internal static IEnumerable<SvgPathSegment> GetSvgSegmentsFromVertices(IEnumerable<DxfVertex> vertices, bool closed)
        {
            List<SvgPathSegment> segments = new List<SvgPathSegment>();

            var first = vertices.First();
            var last = first;
            segments.Add(new SvgMoveToPath(last.Location.X, last.Location.Y));
            foreach (var v in vertices.Skip(1))
            {
                segments.Add(FromVertices(last, v));
            }

            if (closed)
            {
                segments.Add(FromVertices(last, first));
            }
            return segments;
        }

        internal static IEnumerable<SvgPathSegment> GetSvgPathSegments(this DxfHatch.PolylineBoundaryPath poly)
        {
            return GetSvgSegmentsFromVertices(poly.Vertices,poly.IsClosed);
        }

        internal static IEnumerable<SvgPathSegment> GetSvgPathSegments(this IEnumerable<DxfHatch.LineBoundaryPathEdge> lineBoundaries)
        {
            List<SvgPathSegment> segments = new List<SvgPathSegment>();
            var first_point = lineBoundaries.First().StartPoint;
            segments.Add(new SvgMoveToPath(first_point.X, first_point.Y));

            // Assume line boundaries are continuous and closed
            foreach (var l in lineBoundaries.Take(lineBoundaries.Count()-1))
                segments.Add(new SvgLineToPath(l.EndPoint.X, l.EndPoint.Y));
            segments.Add(new SvgClosePath());
            return segments;
        }

        internal static IEnumerable<SvgPathSegment> GetSvgPathSegments(this DxfHatch.CircularArcBoundaryPathEdge circularBoundary)
        {
            // TODO
            return null;
        }

        internal static IEnumerable<SvgPathSegment> GetSvgPathSegments(this DxfHatch.EllipticArcBoundaryPathEdge ellipticArcBoundary)
        {
            // TODO
            return null;
        }

        internal static IEnumerable<SvgPathSegment> GetSvgPathSegments(this DxfHatch.SplineBoundaryPathEdge splineBoundary)
        {
            // TODO
            return null;
        }

        internal static IEnumerable<SvgPathSegment> GetSvgPathSegments(this DxfHatch.NonPolylineBoundaryPath non_poly)
        {
            List<SvgPathSegment> segments = new List<SvgPathSegment>();

            // Support a list of LineBoundaryPathEdges or a single item
            if (non_poly.Edges.Count == 1)
            {
                IEnumerable<SvgPathSegment> el = null;
                switch (non_poly.Edges.First())
                {
                    case DxfHatch.CircularArcBoundaryPathEdge circular:
                        el = GetSvgPathSegments(circular);
                        break;
                    case DxfHatch.EllipticArcBoundaryPathEdge elliptic:
                        el = GetSvgPathSegments(elliptic);
                        break;
                    case DxfHatch.SplineBoundaryPathEdge spline:
                        el = GetSvgPathSegments(spline);
                        break;
                }

                if (el != null)
                    segments.AddRange(el);
            }
            else
            {
                var lines = non_poly.Edges.Cast<DxfHatch.LineBoundaryPathEdge>();
                var s = GetSvgPathSegments(lines);
                segments.AddRange(s);
            }
            return segments;
        }

        internal static IEnumerable<SvgPathSegment> GetSvgPathSegments(this DxfHatch.BoundaryPathBase base_elem)
        {
            switch (base_elem)
            {
                case DxfHatch.NonPolylineBoundaryPath nonPoly:
                    return nonPoly.GetSvgPathSegments();
                case DxfHatch.PolylineBoundaryPath poly:
                    return poly.GetSvgPathSegments();
                default:
                    return null;
            }
        }

        public static XElement ToXElement(this DxfHatch hatch)
        {
            List<SvgPathSegment> segments = new List<SvgPathSegment>();

            if (hatch.HatchStyle == DxfHatchStyle.EntireArea)
            {
                Debug.Assert(hatch.BoundaryPaths.Count == 1);
                var s = GetSvgPathSegments(hatch.BoundaryPaths.First());
                if (s!=null)
                    segments.AddRange(s);
            }
            else if (hatch.HatchStyle == DxfHatchStyle.OutermostAreaOnly)
            {
                //Debug.Assert(hatch.BoundaryPaths.Count == 2);
                return null;
            }
            else // DxfHatchStyle.OddParity not supported
                return null;

            var path = new SvgPath(segments);
            return new XElement(DxfToSvgConverter.Xmlns + "path",
                new XAttribute("d", path.ToString()),
                new XAttribute("fill-opacity", 100),
                new XAttribute("fill",hatch.Color.ToRGBString()))
                .AddVectorEffect();
        }

        private static SvgPathSegment FromVertices(Tuple<double, double> last, Tuple<double,double> next, double bulge = 0.0)
        {
            var dx = next.Item1 - last.Item1;
            var dy = next.Item2 - last.Item2;
            var dist = Math.Sqrt(dx * dx + dy * dy);
            if (bulge == 0.0 || IsCloseTo(dist, 1.0e-10))
            {
                // line or a really short arc
                return new SvgLineToPath(next.Item1, next.Item2);
            }

            // given the following diagram:
            //
            //                p1
            //               -)
            //            -  |  )
            //        -      |    )
            //    -          |     )
            // O ------------|C----T
            //    -          |     )
            //        -      |    )
            //            -  |  )
            //               -)
            //               p2
            //
            // where O is the center of the circle, C is the midpoint between p1 and p2, calculate
            // the hypotenuse of the triangle Op1C to get the radius

            var includedAngle = Math.Atan(Math.Abs(bulge)) * 4.0;
            var isLargeArc = includedAngle > Math.PI;
            var isCounterClockwise = bulge > 0.0;

            // find radius
            var oppositeLength = dist / 2.0;
            var radius = oppositeLength / Math.Sin(includedAngle / 2.0);

            return new SvgArcToPath(radius, radius, 0.0, isLargeArc, isCounterClockwise, next.Item1, next.Item2);
        }
        private static SvgPathSegment FromVertices(DxfVertex last, DxfVertex next)
        {
            return FromVertices(new Tuple<double, double>(last.Location.X, last.Location.Y),
                new Tuple<double, double>(next.Location.X, next.Location.Y), last.Bulge);
        }

        private static SvgPathSegment FromPolylineVertices(DxfLwPolylineVertex last, DxfLwPolylineVertex next)
        {
            return FromVertices(new Tuple<double, double>(last.X, last.Y),
                new Tuple<double, double>(next.X, next.Y), last.Bulge);
        }

        internal static SvgPath GetSvgPath(this DxfArc arc)
        {
            var startAngle = arc.StartAngle * Math.PI / 180.0;
            var endAngle = arc.EndAngle * Math.PI / 180.0;
            return SvgPath.FromEllipse(arc.Center.X, arc.Center.Y, arc.Radius, 0.0, 1.0, startAngle, endAngle);
        }

        internal static SvgPath GetSvgPath(this DxfEllipse ellipse)
        {
            return SvgPath.FromEllipse(ellipse.Center.X, ellipse.Center.Y, ellipse.MajorAxis.X, ellipse.MajorAxis.Y, ellipse.MinorAxisRatio, ellipse.StartParameter, ellipse.EndParameter);
        }

        internal static SvgPath GetSvgPath(this DxfLwPolyline poly)
        {
            var first = poly.Vertices.First();
            var segments = new List<SvgPathSegment>();
            segments.Add(new SvgMoveToPath(first.X, first.Y));
            var last = first;
            foreach (var next in poly.Vertices.Skip(1))
            {
                segments.Add(FromPolylineVertices(last, next));
                last = next;
            }

            if (poly.IsClosed)
            {
                segments.Add(FromPolylineVertices(last, first));
            }

            return new SvgPath(segments);
        }

        internal static SvgPath GetSvgPath(this DxfPolyline poly)
        {
            return new SvgPath(GetSvgSegmentsFromVertices(poly.Vertices, poly.IsClosed));
        }

        private static XElement AddStroke(this XElement element, DxfColor color)
        {
            if (color.IsIndex)
            {
                var stroke = element.Attribute("stroke");
                var colorString = color.ToRGBString();
                if (stroke == null)
                {
                    element.Add(new XAttribute("stroke", colorString));
                }
                else
                {
                    stroke.Value = colorString;
                }
            }

            return element;
        }

        private static XElement AddStrokeWidth(this XElement element, double strokeWidth)
        {
            element.Add(new XAttribute("stroke-width", $"{Math.Max(strokeWidth, 1.0).ToDisplayString()}px"));
            return element;
        }

        private static XElement AddVectorEffect(this XElement element)
        {
            element.Add(new XAttribute("vector-effect", "non-scaling-stroke"));
            return element;
        }

        private static bool IsCloseTo(double a, double b)
        {
            return Math.Abs(a - b) < 1.0e-10;
        }
    }
}
