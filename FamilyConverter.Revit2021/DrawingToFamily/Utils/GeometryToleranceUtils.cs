using System;
using Autodesk.Revit.DB;

namespace FamilyConverter.Revit2021.DrawingToFamily.Utils
{
    public static class GeometryToleranceUtils
    {
        public static bool IsHorizontal(XYZ a, XYZ b, double toleranceFeet)
        {
            return a != null && b != null && Math.Abs(a.Y - b.Y) <= toleranceFeet && Math.Abs(a.X - b.X) > toleranceFeet;
        }

        public static bool IsVertical(XYZ a, XYZ b, double toleranceFeet)
        {
            return a != null && b != null && Math.Abs(a.X - b.X) <= toleranceFeet && Math.Abs(a.Y - b.Y) > toleranceFeet;
        }

        public static XYZ Flatten(XYZ point)
        {
            return point == null ? XYZ.Zero : new XYZ(point.X, point.Y, 0);
        }

        public static double Dot2D(XYZ a, XYZ b)
        {
            if (a == null || b == null)
            {
                return 0;
            }

            return a.X * b.X + a.Y * b.Y;
        }

        public static XYZ Perpendicular2D(XYZ vector)
        {
            if (vector == null)
            {
                return XYZ.BasisY;
            }

            XYZ perpendicular = new XYZ(-vector.Y, vector.X, 0);
            return perpendicular.GetLength() < 1e-9 ? XYZ.BasisY : perpendicular.Normalize();
        }
    }
}
