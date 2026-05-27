using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace FamilyConverter.Revit2021.DrawingToFamily.Models
{
    public class DwgCurveEntity
    {
        public DwgCurveEntity()
        {
            Id = Guid.NewGuid();
            Points = new List<XYZ>();
            PointsMm = new List<XYZ>();
            Warnings = new List<string>();
            RecognitionRole = RecognitionRole.Unknown;
            LayerColorHex = "-";
        }

        public Guid Id { get; set; }
        public string LayerName { get; set; }
        public string EntityType { get; set; }
        public IList<XYZ> Points { get; private set; }
        public IList<XYZ> PointsMm { get; set; }
        public Curve Curve { get; set; }
        public BoundingBoxXYZ BoundingBox { get; set; }
        public double LengthMm { get; set; }
        public bool IsClosedCandidate { get; set; }
        public bool IsSmallObject { get; set; }
        public bool IsHorizontal { get; set; }
        public bool IsVertical { get; set; }
        public bool IsIgnored { get; set; }
        public RecognitionRole RecognitionRole { get; set; }
        public Color LayerColor { get; set; }
        public string LayerColorHex { get; set; }
        public ElementId GraphicsStyleId { get; set; }
        public Transform TotalTransform { get; set; }
        public string Warning { get; set; }
        public IList<string> Warnings { get; private set; }

        public string StyleColor
        {
            get { return LayerColorHex; }
            set { LayerColorHex = value; }
        }

        public Transform SourceTransform
        {
            get { return TotalTransform; }
            set { TotalTransform = value; }
        }
    }
}
