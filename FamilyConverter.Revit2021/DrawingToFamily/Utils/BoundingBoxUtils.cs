using System;
using Autodesk.Revit.DB;

namespace FamilyConverter.Revit2021.DrawingToFamily.Utils
{
    public static class BoundingBoxUtils
    {
        public static BoundingBoxXYZ Empty()
        {
            return null;
        }

        public static BoundingBoxXYZ FromPoint(XYZ point)
        {
            return new BoundingBoxXYZ { Min = point, Max = point };
        }

        public static BoundingBoxXYZ AddPoint(BoundingBoxXYZ box, XYZ point)
        {
            if (point == null)
            {
                return box;
            }

            if (box == null)
            {
                return FromPoint(point);
            }

            box.Min = new XYZ(
                Math.Min(box.Min.X, point.X),
                Math.Min(box.Min.Y, point.Y),
                Math.Min(box.Min.Z, point.Z));
            box.Max = new XYZ(
                Math.Max(box.Max.X, point.X),
                Math.Max(box.Max.Y, point.Y),
                Math.Max(box.Max.Z, point.Z));
            return box;
        }

        public static BoundingBoxXYZ Union(BoundingBoxXYZ first, BoundingBoxXYZ second)
        {
            if (first == null)
            {
                return Clone(second);
            }
            if (second == null)
            {
                return Clone(first);
            }

            BoundingBoxXYZ result = Clone(first);
            AddPoint(result, second.Min);
            AddPoint(result, second.Max);
            return result;
        }

        public static BoundingBoxXYZ Clone(BoundingBoxXYZ source)
        {
            if (source == null)
            {
                return null;
            }

            return new BoundingBoxXYZ
            {
                Min = new XYZ(source.Min.X, source.Min.Y, source.Min.Z),
                Max = new XYZ(source.Max.X, source.Max.Y, source.Max.Z)
            };
        }

        public static bool IntersectsExpanded(BoundingBoxXYZ first, BoundingBoxXYZ second, double expandFeet)
        {
            if (first == null || second == null)
            {
                return false;
            }

            return first.Min.X - expandFeet <= second.Max.X
                && first.Max.X + expandFeet >= second.Min.X
                && first.Min.Y - expandFeet <= second.Max.Y
                && first.Max.Y + expandFeet >= second.Min.Y;
        }

        public static bool Contains2D(BoundingBoxXYZ outer, BoundingBoxXYZ inner, double toleranceFeet)
        {
            if (outer == null || inner == null)
            {
                return false;
            }

            return inner.Min.X >= outer.Min.X - toleranceFeet
                && inner.Max.X <= outer.Max.X + toleranceFeet
                && inner.Min.Y >= outer.Min.Y - toleranceFeet
                && inner.Max.Y <= outer.Max.Y + toleranceFeet;
        }

        public static XYZ Center(BoundingBoxXYZ box)
        {
            if (box == null)
            {
                return XYZ.Zero;
            }

            return new XYZ(
                (box.Min.X + box.Max.X) * 0.5,
                (box.Min.Y + box.Max.Y) * 0.5,
                (box.Min.Z + box.Max.Z) * 0.5);
        }

        public static double WidthMm(BoundingBoxXYZ box)
        {
            return box == null ? 0 : UnitUtilsExtensions.FeetToMm(Math.Abs(box.Max.X - box.Min.X));
        }

        public static double HeightMm(BoundingBoxXYZ box)
        {
            return box == null ? 0 : UnitUtilsExtensions.FeetToMm(Math.Abs(box.Max.Y - box.Min.Y));
        }

        public static double DepthMm(BoundingBoxXYZ box)
        {
            return box == null ? 0 : UnitUtilsExtensions.FeetToMm(Math.Abs(box.Max.Z - box.Min.Z));
        }

        public static double SizeMm(BoundingBoxXYZ box, int axis)
        {
            if (box == null)
            {
                return 0;
            }

            if (axis == 0)
            {
                return WidthMm(box);
            }
            if (axis == 1)
            {
                return HeightMm(box);
            }

            return DepthMm(box);
        }

        public static string ToMmString(BoundingBoxXYZ box)
        {
            if (box == null)
            {
                return "-";
            }

            return string.Format(
                "X {0:0.#}..{1:0.#}, Y {2:0.#}..{3:0.#}, Z {4:0.#}..{5:0.#} mm",
                UnitUtilsExtensions.FeetToMm(box.Min.X),
                UnitUtilsExtensions.FeetToMm(box.Max.X),
                UnitUtilsExtensions.FeetToMm(box.Min.Y),
                UnitUtilsExtensions.FeetToMm(box.Max.Y),
                UnitUtilsExtensions.FeetToMm(box.Min.Z),
                UnitUtilsExtensions.FeetToMm(box.Max.Z));
        }
    }
}
