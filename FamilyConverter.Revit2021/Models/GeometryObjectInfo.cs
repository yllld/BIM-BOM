using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace FamilyConverter.Revit2021.Models
{
    public class GeometryObjectInfo
    {
        public GeometryObjectInfo()
        {
            Warnings = new List<string>();
        }

        public ElementId SourceImportInstanceId { get; set; }
        public int GeometryIndex { get; set; }
        public string GeometryType { get; set; }
        public Transform Transform { get; set; }
        public BoundingBoxXYZ BoundingBox { get; set; }
        public double VolumeFeet3 { get; set; }
        public double VolumeMm3 { get; set; }
        public int FaceCount { get; set; }
        public int EdgeCount { get; set; }
        public string LayerName { get; set; }
        public Solid Solid { get; set; }
        public Mesh Mesh { get; set; }
        public Curve Curve { get; set; }
        public GeometryObject RawObject { get; set; }
        public IList<string> Warnings { get; private set; }

        public string ObjectId
        {
            get { return SourceImportInstanceId.IntegerValue + ":" + GeometryIndex; }
        }
    }
}
