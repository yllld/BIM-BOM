using System.Collections.Generic;

namespace FamilyConverter.Revit2021.DrawingToFamily.Models
{
    public class DrawingToFamilyResult
    {
        public DrawingToFamilyResult()
        {
            Warnings = new List<string>();
            Errors = new List<string>();
            CreatedElementIds = new List<int>();
            NativeFeatures = new List<NativeGeometryFeature>();
            Diagnostics = new List<DrawingDiagnosticIssue>();
        }

        public int ReadObjectCount { get; set; }
        public int LayerCount { get; set; }
        public int PlanObjectCount { get; set; }
        public int FrontObjectCount { get; set; }
        public int SideObjectCount { get; set; }
        public int ProcessedObjectCount { get; set; }
        public int UsedObjectCount { get; set; }
        public int ReferenceObjectCount { get; set; }
        public int SkippedObjects { get; set; }
        public int ContoursFound { get; set; }
        public int SolidContours { get; set; }
        public int VoidContours { get; set; }
        public int OpenContours { get; set; }
        public int InvalidContours { get; set; }
        public int OuterContours { get; set; }
        public int InnerContours { get; set; }
        public int HoleContours { get; set; }
        public int SkippedContours { get; set; }
        public int BuildCandidateCount { get; set; }
        public int NativeFeatureCount { get; set; }
        public int ManualContourOverrideCount { get; set; }
        public int DisabledContourCount { get; set; }
        public int BoxFeatureCount { get; set; }
        public int CylinderFeatureCount { get; set; }
        public int CreatedGeometryCount { get; set; }
        public int SolidExtrusionsCreated { get; set; }
        public int FreeFormElementsCreated { get; set; }
        public int VoidProfilesUsed { get; set; }
        public int ReferenceLinesCreated { get; set; }
        public int FailedBuildCandidates { get; set; }
        public bool FallbackUsed { get; set; }
        public double WidthMm { get; set; }
        public double DepthMm { get; set; }
        public double HeightMm { get; set; }
        public string ReportPath { get; set; }
        public string LogPath { get; set; }
        public IList<string> Warnings { get; private set; }
        public IList<string> Errors { get; private set; }
        public IList<int> CreatedElementIds { get; private set; }
        public IList<NativeGeometryFeature> NativeFeatures { get; private set; }
        public IList<DrawingDiagnosticIssue> Diagnostics { get; private set; }

        public string Status
        {
            get
            {
                if (Errors.Count > 0 && CreatedGeometryCount == 0 && ReferenceLinesCreated == 0)
                {
                    return "Ошибка";
                }

                if (FallbackUsed || Warnings.Count > 0 || Errors.Count > 0 || FailedBuildCandidates > 0)
                {
                    return "Частично успешно";
                }

                return "Успешно";
            }
        }
    }
}
