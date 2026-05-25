using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace FamilyConverter.Revit2021.Models
{
    public class PrismaticCandidate
    {
        public PrismaticCandidate()
        {
            ProfileLoops = new List<IList<Curve>>();
            Warnings = new List<string>();
        }

        public GeometryClassification Classification { get; set; }
        public PlanarFace BaseFace { get; set; }
        public PlanarFace TopFace { get; set; }
        public IList<IList<Curve>> ProfileLoops { get; private set; }
        public Plane SketchPlane { get; set; }
        public double DepthFeet { get; set; }
        public double Confidence { get; set; }
        public bool IsProfileSafe { get; set; }
        public IList<string> Warnings { get; private set; }
    }
}
