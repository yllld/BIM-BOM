using System;
using System.Collections.Generic;

namespace FamilyConverter.Revit2021.DrawingToFamily.Models
{
    public class BuildCandidate
    {
        public BuildCandidate()
        {
            Id = Guid.NewGuid();
            VoidContours = new List<RecognizedContour>();
            Warnings = new List<string>();
        }

        public Guid Id { get; set; }
        public RecognizedContour PrimaryContour { get; set; }
        public RecognizedContour MatchedFrontContour { get; set; }
        public RecognizedContour MatchedSideContour { get; set; }
        public IList<RecognizedContour> VoidContours { get; private set; }
        public BuildDirection Direction { get; set; }
        public double WidthMm { get; set; }
        public double DepthMm { get; set; }
        public double HeightMm { get; set; }
        public double Confidence { get; set; }
        public IList<string> Warnings { get; private set; }
        public bool CanBuild { get; set; }
        public string SkipReason { get; set; }
        public bool IsBuilt { get; set; }
        public string BuildResult { get; set; }
    }
}
