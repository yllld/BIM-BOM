using System;
using Autodesk.Revit.DB;
using FamilyConverter.Revit2021.DrawingToFamily.Models;
using FamilyConverter.Revit2021.DrawingToFamily.Utils;

namespace FamilyConverter.Revit2021.DrawingToFamily.Services
{
    public static class ContourSignatureService
    {
        public static string Create(RecognizedContour contour)
        {
            if (contour == null)
            {
                return string.Empty;
            }

            BoundingBoxXYZ box = contour.BoundingBox;
            if (box == null)
            {
                return string.Format(
                    "{0}|{1}|empty|{2}",
                    contour.SourceProjection,
                    contour.SourceLayer ?? "-",
                    contour.Curves == null ? 0 : contour.Curves.Count);
            }

            return string.Format(
                "{0}|{1}|{2}|{3}|{4}|{5}|{6}",
                contour.SourceProjection,
                contour.SourceLayer ?? "-",
                Round(UnitUtilsExtensions.FeetToMm(box.Min.X)),
                Round(UnitUtilsExtensions.FeetToMm(box.Min.Y)),
                Round(UnitUtilsExtensions.FeetToMm(box.Max.X)),
                Round(UnitUtilsExtensions.FeetToMm(box.Max.Y)),
                contour.Curves == null ? 0 : contour.Curves.Count);
        }

        private static int Round(double value)
        {
            return (int)Math.Round(value);
        }
    }
}
