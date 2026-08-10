using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace FamilyConverter.Revit2021.DrawingToFamily.Models
{
    public class DrawingToFamilyPreview
    {
        public DrawingToFamilyPreview()
        {
            Entities = new List<DwgCurveEntity>();
            Layers = new List<DwgLayerInfo>();
            ProjectionRegions = new List<DrawingProjectionRegion>();
        }

        public string ImportName { get; set; }
        public int ObjectCount { get; set; }
        public int LayerCount { get; set; }
        public BoundingBoxXYZ BoundingBox { get; set; }
        public string BoundingBoxText { get; set; }
        public IList<DwgCurveEntity> Entities { get; private set; }
        public IList<DwgLayerInfo> Layers { get; private set; }
        public IList<DrawingProjectionRegion> ProjectionRegions { get; private set; }
    }
}
