using System;
using Autodesk.Revit.DB;

namespace FamilyConverter.Revit2021.DrawingToFamily.Models
{
    public class DrawingDiagnosticIssue
    {
        public DrawingDiagnosticIssue()
        {
            Id = Guid.NewGuid();
            Severity = DrawingDiagnosticSeverity.Info;
        }

        public Guid Id { get; set; }
        public DrawingDiagnosticSeverity Severity { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
        public string LayerName { get; set; }
        public Guid? GeometryId { get; set; }
        public Guid? ContourId { get; set; }
        public ProjectionType Projection { get; set; }
        public XYZ Location { get; set; }
        public double ValueMm { get; set; }
        public double ToleranceMm { get; set; }
        public string SuggestedAction { get; set; }

        public string ShortTarget
        {
            get
            {
                if (ContourId.HasValue)
                {
                    return "contour " + ShortId(ContourId.Value);
                }

                if (GeometryId.HasValue)
                {
                    return "geometry " + ShortId(GeometryId.Value);
                }

                return "-";
            }
        }

        private static string ShortId(Guid id)
        {
            return id.ToString("N").Substring(0, 8);
        }
    }
}
