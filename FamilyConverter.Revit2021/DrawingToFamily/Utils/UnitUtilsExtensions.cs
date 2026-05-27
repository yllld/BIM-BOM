using Autodesk.Revit.DB;

namespace FamilyConverter.Revit2021.DrawingToFamily.Utils
{
    public static class UnitUtilsExtensions
    {
        public static double FeetToMm(double feet)
        {
            return UnitUtils.ConvertFromInternalUnits(feet, DisplayUnitType.DUT_MILLIMETERS);
        }

        public static double MmToFeet(double millimeters)
        {
            return UnitUtils.ConvertToInternalUnits(millimeters, DisplayUnitType.DUT_MILLIMETERS);
        }

        public static double SquareFeetToSquareMm(double squareFeet)
        {
            double scale = FeetToMm(1.0);
            return squareFeet * scale * scale;
        }
    }
}
