using Autodesk.Revit.DB;

namespace FamilyConverter.Revit2021.Services
{
    public class UnitService
    {
        public double FeetToMm(double feet)
        {
            return UnitUtils.ConvertFromInternalUnits(feet, DisplayUnitType.DUT_MILLIMETERS);
        }

        public double MmToFeet(double millimeters)
        {
            return UnitUtils.ConvertToInternalUnits(millimeters, DisplayUnitType.DUT_MILLIMETERS);
        }

        public double CubicFeetToCubicMm(double cubicFeet)
        {
            double scale = FeetToMm(1.0);
            return cubicFeet * scale * scale * scale;
        }

        public double CubicMmToCubicFeet(double cubicMillimeters)
        {
            double scale = FeetToMm(1.0);
            return cubicMillimeters / (scale * scale * scale);
        }

        public double SquareFeetToSquareMm(double squareFeet)
        {
            double scale = FeetToMm(1.0);
            return squareFeet * scale * scale;
        }

        public double SquareMmToSquareFeet(double squareMillimeters)
        {
            double scale = FeetToMm(1.0);
            return squareMillimeters / (scale * scale);
        }
    }
}
