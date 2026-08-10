using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using WpfPoint = System.Windows.Point;

namespace FamilyConverter.Revit2021.DrawingToFamily.UI
{
    public class PreviewHitTestService
    {
        public PreviewContourItem HitTest(IEnumerable<PreviewContourItem> items, WpfPoint screenPoint, PreviewTransformService transform, double tolerancePixels)
        {
            if (items == null || transform == null)
            {
                return null;
            }

            PreviewContourItem best = null;
            double bestDistance = double.MaxValue;
            foreach (PreviewContourItem item in items.Where(x => x != null && x.IsIncluded))
            {
                double distance = DistanceToContour(item, screenPoint, transform);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = item;
                }
            }

            return bestDistance <= tolerancePixels ? best : null;
        }

        private static double DistanceToContour(PreviewContourItem item, WpfPoint screenPoint, PreviewTransformService transform)
        {
            if (item == null || item.Contour == null || item.Contour.Curves == null)
            {
                return double.MaxValue;
            }

            double best = double.MaxValue;
            foreach (Curve curve in item.Contour.Curves)
            {
                IList<XYZ> points = Tessellate(curve);
                for (int i = 1; i < points.Count; i++)
                {
                    WpfPoint a = transform.ModelToScreen(points[i - 1]);
                    WpfPoint b = transform.ModelToScreen(points[i]);
                    best = Math.Min(best, DistanceToSegment(screenPoint, a, b));
                }
            }

            return best;
        }

        private static IList<XYZ> Tessellate(Curve curve)
        {
            if (curve == null)
            {
                return new List<XYZ>();
            }

            try
            {
                return curve.Tessellate();
            }
            catch
            {
                var result = new List<XYZ>();
                try
                {
                    result.Add(curve.GetEndPoint(0));
                    result.Add(curve.GetEndPoint(1));
                }
                catch
                {
                }

                return result;
            }
        }

        private static double DistanceToSegment(WpfPoint p, WpfPoint a, WpfPoint b)
        {
            Vector ab = b - a;
            double length2 = ab.X * ab.X + ab.Y * ab.Y;
            if (length2 < 1e-9)
            {
                return (p - a).Length;
            }

            double t = ((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / length2;
            t = Math.Max(0, Math.Min(1, t));
            WpfPoint projection = new WpfPoint(a.X + ab.X * t, a.Y + ab.Y * t);
            return (p - projection).Length;
        }
    }
}
