using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace FamilyConverter.Revit2021.Models
{
    public class ConversionResult
    {
        public ConversionResult()
        {
            Warnings = new List<string>();
        }

        public string ObjectId { get; set; }
        public string LayerName { get; set; }
        public string SourceGeometryType { get; set; }
        public GeometryClassification LocalClassification { get; set; }
        public double LocalConfidence { get; set; }
        public bool AiUsed { get; set; }
        public string AiProvider { get; set; }
        public string AiModel { get; set; }
        public string AiRecommendation { get; set; }
        public double AiConfidence { get; set; }
        public ConversionMethod FinalMethod { get; set; }
        public ConversionStatus Status { get; set; }
        public ElementId CreatedElementId { get; set; }
        public string Message { get; set; }
        public string Exception { get; set; }
        public bool FallbackUsed { get; set; }
        public string ExtrusionFailedReason { get; set; }
        public double ValidationBoundingBoxDeviationMm { get; set; }
        public double ValidationVolumeDeviationPercent { get; set; }
        public IList<string> Warnings { get; private set; }
        public GeometryObjectInfo Source { get; set; }
    }
}
