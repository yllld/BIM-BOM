using System.Collections.Generic;
using System.Linq;

namespace FamilyConverter.Revit2021.Models
{
    public class ConversionSummary
    {
        public ConversionSummary()
        {
            Results = new List<ConversionResult>();
            Messages = new List<string>();
        }

        public IList<ConversionResult> Results { get; private set; }
        public IList<string> Messages { get; private set; }
        public string JsonReportPath { get; set; }
        public string CsvReportPath { get; set; }
        public bool SourceDwgDeleted { get; set; }

        public int ExtrusionCount { get { return Results.Count(x => x.FinalMethod == ConversionMethod.Extrusion && x.CreatedElementId != null); } }
        public int FreeFormCount { get { return Results.Count(x => x.FinalMethod == ConversionMethod.FreeForm && x.CreatedElementId != null); } }
        public int DirectShapeCount { get { return Results.Count(x => x.FinalMethod == ConversionMethod.DirectShape && x.CreatedElementId != null); } }
        public int SkippedCount { get { return Results.Count(x => x.Status == ConversionStatus.Skipped); } }
        public int FailedCount { get { return Results.Count(x => x.Status == ConversionStatus.Failed); } }
        public int WarningCount { get { return Results.Count(x => x.Status == ConversionStatus.Warning) + Results.Sum(x => x.Warnings.Count); } }
        public bool HasCreatedElements { get { return Results.Any(x => x.CreatedElementId != null); } }
        public bool HasCriticalWarnings { get { return Results.Any(x => x.Status == ConversionStatus.Warning); } }
    }
}
