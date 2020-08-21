using IxMilia.Dxf;
using IxMilia.Dxf.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace IxMilia.Converters
{
    public class Spline
    {
        public int Degree { get; }
        public IList<DxfControlPoint> ControlPoints { get; }
        public IList<double> Knots { get; }
        public Spline(DxfSpline sp)
        {
            Degree = sp.DegreeOfCurve;
            ControlPoints = sp.ControlPoints;
            Knots = sp.KnotValues;
        }

        public Spline(DxfHatch.SplineBoundaryPathEdge edge_spline)
        {
            Degree = edge_spline.Degree;
            ControlPoints = edge_spline.ControlPoints;
            Knots = edge_spline.Knots;
        }
    }

    // Algorithm implementation borrowed from https://github.com/bjnortier/dxf/blob/master/src/util/toPiecewiseBezier.js
    public class PiecewiseBezier
    {
        int k;
        IList<DxfPoint> controls;
        IList<double> knots;

        public PiecewiseBezier(Spline spline)
        {
            k = spline.Degree + 1;

            CheckPinned(k, spline.Knots);
            var insertions = ComputeInsertions(k, spline.Knots);

            Tuple<IList<DxfPoint>, IList<double>> seed = new Tuple<IList<DxfPoint>, IList<double>>(spline.ControlPoints.Select(cp => cp.Point).ToList(), spline.Knots);
            var result = insertions.Aggregate(seed,
                (acc, tNew) => { return InsertKnot(k, acc.Item1, acc.Item2, tNew); });

            controls = result.Item1;
            knots = result.Item2;
        }

        public SvgPath ToSvgPath()
        {
            // Based on https://github.com/bjnortier/dxf/blob/master/src/toSVG.js
            IList<SvgPathSegment> paths = new List<SvgPathSegment>();
            int controlPointIndex = 0;
            int knotIndex = k;
            DxfPoint? last = null;
            while (knotIndex < knots.Count - k + 1)
            {
                var m = Multiplicity(knots, knotIndex);
                var cp = controls.Skip(controlPointIndex).Take(k).ToArray();
                if (k == 4)
                {
                    if ((last==null) || (last!=cp[0]))
                        paths.Add(new SvgMoveToPath(cp[0]));
                    paths.Add(new SvgCurveToPath(cp[1], cp[2], cp[3]));
                    last = cp[3];
                }
                else if (k == 3)
                {
                    if ((last == null) || (last != cp[0]))
                        paths.Add(new SvgMoveToPath(cp[0]));
                    paths.Add(new SvgQuadraticCurveToPath(cp[1], cp[2]));
                    last = cp[2];
                }
                controlPointIndex += m;
                knotIndex += m;
            }
            return new SvgPath(paths);
        }

        static public int Multiplicity(IList<double> knots, int index)
        {
            int m = 1;
            for (int i = index + 1; i < knots.Count; ++i)
            { 
                if (knots[i] == knots[index])
                    ++m;
                else
                    break;
            }
            return m;
        }

        static private IList<double> ComputeInsertions(int k, IList<double> knots)
        {
            IList<double> inserts = new List<double>();
            int i = k;
            while (i < (knots.Count - k))
            {
                double knot = knots[i];
                int m = Multiplicity(knots, i);
                for (int j = 0; j < (k - m - 1); ++j)
                    inserts.Add(knot);
                i += m;
            }
            return inserts;
        }

        static private void CheckPinned(int k, IList<double> knots)
        {
            // Pinned at the start
            for (int i = 1; i < k; ++i)
            {
                if (knots[i] != knots[0])
                    throw new InvalidOperationException($"not pinned.order: ${k} knots: ${knots}");
            }
            // Pinned at the end
            for (int i = knots.Count - 2; i > knots.Count - k - 1; --i) 
            {
                if (knots[i] != knots[knots.Count - 1])
                    throw new InvalidOperationException($"not pinned. order: ${k} knots: ${knots}");
            }
        }

        static private Tuple<IList<DxfPoint>, IList<double>> InsertKnot(int k, IList<DxfPoint> controlPoints, IList<double> knots, double newKnot)
        {
            var x = knots;
            var b = controlPoints;
            var n = controlPoints.Count;
            int i = 0;
            bool foundIndex = false;
            for (int j = 0; j < n + k; j++)
            {
                if (newKnot > x[j] && newKnot <= x[j + 1]) {
                    i = j;
                    foundIndex = true;
                    break;
                }
            }
            if (!foundIndex) {
                throw new InvalidOperationException("invalid new knot");
            }

            double[] xHat = new double[n + k + 1];
            for (int j = 0; j < n + k + 1; j++)
            {
                if (j <= i)
                    xHat[j] = x[j];
                else if (j == i + 1)
                    xHat[j] = newKnot;
                else
                    xHat[j] = x[j - 1];
            }

            double alpha;
            DxfPoint[] bHat = new DxfPoint[n + 1];
            for (int j = 0; j<n + 1; j++)
            {
                if (j <= i - k + 1)
                    alpha = 1;
                else if (i - k + 2 <= j && j <= i)
                {
                    if (x[j + k - 1] - x[j] == 0)
                        alpha = 0;
                    else
                        alpha = (newKnot - x[j]) / (x[j + k - 1] - x[j]);
                }
                else
                    alpha = 0;

                if (alpha == 0)
                    bHat[j] = b[j - 1];
                else if (alpha == 1)
                    bHat[j] = b[j];
                else
                {
                    bHat[j] = new DxfPoint(
                        x: (1 - alpha) * b[j - 1].X + alpha * b[j].X,
                        y: (1 - alpha) * b[j - 1].Y + alpha * b[j].Y,
                        z: (1 - alpha) * b[j - 1].Z + alpha * b[j].Z);
                }
            }

            return new Tuple<IList<DxfPoint>, IList<double>>(bHat, xHat);
        }
    }
}
