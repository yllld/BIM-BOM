using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using FamilyConverter.Revit2021.Services;

namespace FamilyConverter.Revit2021.Utils
{
    public static class CurveLoopUtils
    {
        public static IList<IList<Curve>> ExtractLoops(PlanarFace face, double minSegmentLengthFeet, IList<string> warnings)
        {
            var loops = new List<IList<Curve>>();
            if (face == null)
            {
                return loops;
            }

            foreach (EdgeArray edgeArray in face.EdgeLoops)
            {
                var loop = new List<Curve>();
                foreach (Edge edge in edgeArray)
                {
                    try
                    {
                        Curve curve = edge.AsCurveFollowingFace(face);
                        if (curve != null && SafeLength(curve) > minSegmentLengthFeet)
                        {
                            loop.Add(curve);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (warnings != null)
                        {
                            warnings.Add("Не удалось прочитать ребро профиля: " + ex.Message);
                        }
                    }
                }

                if (loop.Count > 0)
                {
                    loops.Add(loop);
                }
            }

            return loops;
        }

        public static bool AreLoopsClosed(IList<IList<Curve>> loops, double toleranceFeet, IList<string> warnings)
        {
            if (loops == null || loops.Count == 0)
            {
                if (warnings != null)
                {
                    warnings.Add("Профиль не содержит контуров.");
                }
                return false;
            }

            bool allClosed = true;
            foreach (IList<Curve> loop in loops)
            {
                if (!IsLoopClosed(loop, toleranceFeet))
                {
                    allClosed = false;
                    if (warnings != null)
                    {
                        warnings.Add("Найден незамкнутый контур профиля.");
                    }
                }
            }

            return allClosed;
        }

        public static bool IsLoopClosed(IList<Curve> loop, double toleranceFeet)
        {
            if (loop == null || loop.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < loop.Count; i++)
            {
                Curve current = loop[i];
                Curve next = loop[(i + 1) % loop.Count];
                XYZ end = GetEndPoint(current, 1);
                XYZ start = GetEndPoint(next, 0);
                if (end == null || start == null || end.DistanceTo(start) > toleranceFeet)
                {
                    return false;
                }
            }

            return true;
        }

        public static CurveArrArray ToCurveArrArray(IList<IList<Curve>> loops, bool outerOnly)
        {
            var result = new CurveArrArray();
            if (loops == null)
            {
                return result;
            }

            int count = outerOnly ? Math.Min(1, loops.Count) : loops.Count;
            for (int i = 0; i < count; i++)
            {
                var curveArray = new CurveArray();
                foreach (Curve curve in loops[i])
                {
                    curveArray.Append(curve);
                }

                result.Append(curveArray);
            }

            return result;
        }

        public static int CountLineEdges(Solid solid)
        {
            int count = 0;
            if (solid == null)
            {
                return count;
            }

            foreach (Edge edge in solid.Edges)
            {
                if (edge.AsCurve() is Line)
                {
                    count++;
                }
            }

            return count;
        }

        public static int CountArcEdges(Solid solid)
        {
            int count = 0;
            if (solid == null)
            {
                return count;
            }

            foreach (Edge edge in solid.Edges)
            {
                if (edge.AsCurve() is Arc)
                {
                    count++;
                }
            }

            return count;
        }

        public static double SafeLength(Curve curve)
        {
            if (curve == null)
            {
                return 0;
            }

            try
            {
                return curve.Length;
            }
            catch
            {
                try
                {
                    return curve.GetEndPoint(0).DistanceTo(curve.GetEndPoint(1));
                }
                catch
                {
                    return 0;
                }
            }
        }

        public static XYZ GetEndPoint(Curve curve, int index)
        {
            try
            {
                return curve.GetEndPoint(index);
            }
            catch
            {
                IList<XYZ> points = curve.Tessellate();
                if (points.Count == 0)
                {
                    return null;
                }

                return index == 0 ? points[0] : points[points.Count - 1];
            }
        }

        public static string CountCurveKinds(Solid solid)
        {
            int lines = CountLineEdges(solid);
            int arcs = CountArcEdges(solid);
            int total = solid == null ? 0 : solid.Edges.Size;
            return string.Format("Line: {0}, Arc: {1}, Other: {2}", lines, arcs, Math.Max(0, total - lines - arcs));
        }
    }
}
