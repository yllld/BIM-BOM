using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace FamilyConverter.Revit2021.DrawingToFamily.Models
{
    public class DrawingProjectionRegion
    {
        public DrawingProjectionRegion()
        {
            Id = Guid.NewGuid();
            Entities = new List<DwgCurveEntity>();
            PickedPoints = new List<XYZ>();
            Type = ProjectionType.Unknown;
            LocalXAxis = XYZ.BasisX;
            LocalYAxis = XYZ.BasisY;
        }

        public Guid Id { get; set; }
        public ProjectionType Type { get; set; }
        public IList<XYZ> PickedPoints { get; private set; }
        public XYZ Origin { get; set; }
        public XYZ LocalXAxis { get; set; }
        public XYZ LocalYAxis { get; set; }
        public double LocalMinU { get; set; }
        public double LocalMinV { get; set; }
        public BoundingBoxXYZ BoundingBox { get; set; }
        public IList<DwgCurveEntity> Entities { get; private set; }
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }
        public int EntityCount { get; set; }
        public bool IsValid { get; set; }
        public string WarningMessage { get; set; }

        public string StatusText
        {
            get
            {
                if (!IsValid)
                {
                    return "не выбрано";
                }

                return string.Format(
                    "выбрано: {0} объектов, {1:0.#} x {2:0.#} мм",
                    EntityCount,
                    WidthMm,
                    HeightMm);
            }
        }

        public string DisplayName
        {
            get { return StatusText; }
        }
    }
}
