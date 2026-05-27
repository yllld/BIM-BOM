using System;
using System.Collections.Generic;

namespace FamilyConverter.Revit2021.DrawingToFamily.Models
{
    public class NativeGeometryFeature
    {
        public NativeGeometryFeature()
        {
            Id = Guid.NewGuid();
            SourceContourIds = new List<Guid>();
            Warnings = new List<string>();
            FeatureType = NativeFeatureType.Unknown;
            Axis = BuildDirection.ExtrudeZ_FromPlan;
            Confidence = 0.5;
            CanBuild = true;
        }

        public Guid Id { get; set; }
        public NativeFeatureType FeatureType { get; set; }
        public string Name { get; set; }
        public string SourceDescription { get; set; }
        public IList<Guid> SourceContourIds { get; private set; }

        public BuildDirection Axis { get; set; }
        public double XMinMm { get; set; }
        public double XMaxMm { get; set; }
        public double YMinMm { get; set; }
        public double YMaxMm { get; set; }
        public double ZMinMm { get; set; }
        public double ZMaxMm { get; set; }
        public double DiameterMm { get; set; }
        public double Confidence { get; set; }
        public bool CanBuild { get; set; }
        public bool IsBuilt { get; set; }
        public string BuildMethod { get; set; }
        public string BuildResult { get; set; }
        public string SkipReason { get; set; }
        public IList<string> Warnings { get; private set; }

        public double WidthMm
        {
            get { return Math.Abs(XMaxMm - XMinMm); }
        }

        public double DepthMm
        {
            get { return Math.Abs(YMaxMm - YMinMm); }
        }

        public double HeightMm
        {
            get { return Math.Abs(ZMaxMm - ZMinMm); }
        }

        public double VolumeScore
        {
            get
            {
                if (FeatureType == NativeFeatureType.Cylinder
                    || FeatureType == NativeFeatureType.VoidCylinder)
                {
                    double radius = DiameterMm * 0.5;
                    double length = WidthMm;
                    if (Axis == BuildDirection.ExtrudeY_FromFront)
                    {
                        length = DepthMm;
                    }
                    else if (Axis == BuildDirection.ExtrudeZ_FromPlan)
                    {
                        length = HeightMm;
                    }

                    return Math.PI * radius * radius * Math.Max(0, length);
                }

                return Math.Max(0, WidthMm) * Math.Max(0, DepthMm) * Math.Max(0, HeightMm);
            }
        }
    }
}
