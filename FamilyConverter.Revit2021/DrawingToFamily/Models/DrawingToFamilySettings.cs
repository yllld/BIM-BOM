using System;
using System.Collections.Generic;

namespace FamilyConverter.Revit2021.DrawingToFamily.Models
{
    public class DrawingToFamilySettings
    {
        public DrawingToFamilySettings()
        {
            Layers = new List<DwgLayerInfo>();
            ManualContourOverrides = new List<ContourManualOverride>();
            MinimumElementSizeMm = 10.0;
            ClosureToleranceMm = 2.0;
            BuildGeometry = true;
            AllowFreeFormFallback = true;
            SuppressRevitWarnings = true;
            BuildProfileProjection = ProjectionType.Unknown;
            MaxBuildCandidates = 0;
            UseIsometricReference = false;
        }

        public IList<DwgLayerInfo> Layers { get; private set; }
        public IList<ContourManualOverride> ManualContourOverrides { get; private set; }
        public DrawingProjectionRegion PlanRegion { get; set; }
        public DrawingProjectionRegion FrontRegion { get; set; }
        public DrawingProjectionRegion SideRegion { get; set; }
        public DrawingProjectionRegion IsometricRegion { get; set; }
        public bool UseIsometricReference { get; set; }
        public double ClosureToleranceMm { get; set; }
        public double MinimumElementSizeMm { get; set; }
        public bool BuildGeometry { get; set; }
        public bool AllowFreeFormFallback { get; set; }
        public bool SuppressRevitWarnings { get; set; }
        public ProjectionType BuildProfileProjection { get; set; }
        public int MaxBuildCandidates { get; set; }

        public Guid PlanRegionId { get; set; }
        public Guid FrontRegionId { get; set; }
        public Guid? SideRegionId { get; set; }
        public Guid? IsometricRegionId { get; set; }

        public double IgnoreObjectsBelowMm
        {
            get { return MinimumElementSizeMm; }
            set { MinimumElementSizeMm = value; }
        }
    }
}
