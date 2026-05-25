using Autodesk.Revit.DB;

namespace FamilyConverter.Revit2021.Utils
{
    public static class ToleranceUtils
    {
        public static bool AlmostEqual(double a, double b, double tolerance)
        {
            return System.Math.Abs(a - b) <= tolerance;
        }

        public static bool AlmostSamePoint(XYZ a, XYZ b, double tolerance)
        {
            if (a == null || b == null)
            {
                return false;
            }

            return a.DistanceTo(b) <= tolerance;
        }

        public static bool AlmostParallel(XYZ a, XYZ b, double dotTolerance)
        {
            if (a == null || b == null)
            {
                return false;
            }

            XYZ an = a.Normalize();
            XYZ bn = b.Normalize();
            return System.Math.Abs(an.DotProduct(bn)) >= dotTolerance;
        }

        public static bool AlmostOpposite(XYZ a, XYZ b, double dotTolerance)
        {
            if (a == null || b == null)
            {
                return false;
            }

            XYZ an = a.Normalize();
            XYZ bn = b.Normalize();
            return an.DotProduct(bn) <= -dotTolerance;
        }
    }
}
