using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace FamilyConverter.Revit2021.DrawingToFamily.Models
{
    public class RecognizedContour
    {
        public RecognizedContour()
        {
            Id = Guid.NewGuid();
            SourceEntities = new List<DwgCurveEntity>();
            Curves = new List<Curve>();
            Type = ContourType.Unknown;
            SourceProjection = ProjectionType.Unknown;
        }

        public Guid Id { get; set; }
        public ProjectionType SourceProjection { get; set; }
        public string SourceLayer { get; set; }
        public IList<DwgCurveEntity> SourceEntities { get; private set; }
        public IList<Curve> Curves { get; private set; }
        public ContourType Type { get; set; }
        public int NestingLevel { get; set; }
        public Guid? ParentContourId { get; set; }
        public BoundingBoxXYZ BoundingBox { get; set; }
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }
        public double AreaMm2 { get; set; }
        public bool IsClosed { get; set; }
        public bool IsValidForRevit { get; set; }
        public bool IsBuilt { get; set; }
        public string ReasonIfInvalid { get; set; }
        public string BuildResult { get; set; }

        public bool IsValidForExtrusion
        {
            get { return IsValidForRevit; }
            set { IsValidForRevit = value; }
        }
    }
}
