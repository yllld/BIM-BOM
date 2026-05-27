using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace FamilyConverter.Revit2021.DrawingToFamily.Utils
{
    public static class CurveLoopUtils
    {
        public static XYZ GetEndPoint(Curve curve, int index)
        {
            try
            {
                return curve.GetEndPoint(index);
            }
            catch
            {
                IList<XYZ> points = curve.Tessellate();
                if (points == null || points.Count == 0)
                {
                    return null;
                }

                return index == 0 ? points[0] : points[points.Count - 1];
            }
        }

        public static double AreaFeet2(IList<Curve> curves)
        {
            double area = 0;
            if (curves == null)
            {
                return 0;
            }

            foreach (Curve curve in curves)
            {
                XYZ start = GetEndPoint(curve, 0);
                XYZ end = GetEndPoint(curve, 1);
                if (start == null || end == null)
                {
                    continue;
                }

                area += start.X * end.Y - end.X * start.Y;
            }

            return area * 0.5;
        }

        public static CurveArrArray ToCurveArrArray(IList<Curve> curves)
        {
            var result = new CurveArrArray();
            var array = new CurveArray();
            foreach (Curve curve in curves)
            {
                array.Append(curve);
            }

            result.Append(array);
            return result;
        }
    }
}
