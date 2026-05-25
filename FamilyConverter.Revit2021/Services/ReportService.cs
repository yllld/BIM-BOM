using System.Collections.Generic;
using System.IO;
using System.Text;
using Autodesk.Revit.DB;
using FamilyConverter.Revit2021.Models;
using FamilyConverter.Revit2021.Utils;
using Newtonsoft.Json;

namespace FamilyConverter.Revit2021.Services
{
    public class ReportService
    {
        private readonly UnitService _unitService;

        public ReportService(UnitService unitService)
        {
            _unitService = unitService;
        }

        public void CreateReports(Document document, ConversionSummary summary, ConversionOptions options)
        {
            if (summary == null)
            {
                return;
            }

            string directory = FileNameUtils.GetReportDirectory(document);
            string timestamp = FileNameUtils.Timestamp();
            IList<ReportRow> rows = BuildRows(summary.Results);

            if (options.CreateJsonReport)
            {
                string path = Path.Combine(directory, "DWG_Conversion_Report_" + timestamp + ".json");
                File.WriteAllText(path, JsonConvert.SerializeObject(rows, Formatting.Indented), new UTF8Encoding(true));
                summary.JsonReportPath = path;
            }

            if (options.CreateCsvReport)
            {
                string path = Path.Combine(directory, "DWG_Conversion_Report_" + timestamp + ".csv");
                File.WriteAllText(path, BuildCsv(rows), new UTF8Encoding(true));
                summary.CsvReportPath = path;
            }
        }

        private IList<ReportRow> BuildRows(IEnumerable<ConversionResult> results)
        {
            var rows = new List<ReportRow>();
            foreach (ConversionResult result in results)
            {
                GeometryObjectInfo source = result.Source;
                rows.Add(new ReportRow
                {
                    object_id = result.ObjectId,
                    layer = result.LayerName,
                    source_geometry_type = result.SourceGeometryType,
                    bbox_mm = source == null ? string.Empty : GeometryUtils.BoundingBoxToMmString(source.BoundingBox, _unitService),
                    volume_mm3 = source == null ? 0 : source.VolumeMm3,
                    face_count = source == null ? 0 : source.FaceCount,
                    edge_count = source == null ? 0 : source.EdgeCount,
                    local_classification = result.LocalClassification.ToString(),
                    local_confidence = result.LocalConfidence,
                    ai_used = result.AiUsed,
                    ai_provider = result.AiProvider,
                    ai_model = result.AiModel,
                    ai_recommendation = result.AiRecommendation,
                    ai_confidence = result.AiConfidence,
                    final_method = result.FinalMethod.ToString(),
                    result_status = result.Status.ToString(),
                    created_element_id = result.CreatedElementId == null ? string.Empty : result.CreatedElementId.IntegerValue.ToString(),
                    message = result.Message,
                    exception = result.Exception,
                    fallback_used = result.FallbackUsed,
                    extrusion_failed_reason = result.ExtrusionFailedReason,
                    validation_bbox_deviation_mm = result.ValidationBoundingBoxDeviationMm,
                    validation_volume_deviation_percent = result.ValidationVolumeDeviationPercent
                });
            }

            return rows;
        }

        private static string BuildCsv(IList<ReportRow> rows)
        {
            var builder = new StringBuilder();
            builder.AppendLine("object_id;layer;source_geometry_type;bbox_mm;volume_mm3;face_count;edge_count;local_classification;local_confidence;ai_used;ai_provider;ai_model;ai_recommendation;ai_confidence;final_method;result_status;created_element_id;message;exception;fallback_used;extrusion_failed_reason;validation_bbox_deviation_mm;validation_volume_deviation_percent");
            foreach (ReportRow row in rows)
            {
                builder.AppendLine(string.Join(";",
                    Escape(row.object_id),
                    Escape(row.layer),
                    Escape(row.source_geometry_type),
                    Escape(row.bbox_mm),
                    row.volume_mm3.ToString("0.###"),
                    row.face_count,
                    row.edge_count,
                    Escape(row.local_classification),
                    row.local_confidence.ToString("0.###"),
                    row.ai_used,
                    Escape(row.ai_provider),
                    Escape(row.ai_model),
                    Escape(row.ai_recommendation),
                    row.ai_confidence.ToString("0.###"),
                    Escape(row.final_method),
                    Escape(row.result_status),
                    Escape(row.created_element_id),
                    Escape(row.message),
                    Escape(row.exception),
                    row.fallback_used,
                    Escape(row.extrusion_failed_reason),
                    row.validation_bbox_deviation_mm.ToString("0.###"),
                    row.validation_volume_deviation_percent.ToString("0.###")));
            }

            return builder.ToString();
        }

        private static string Escape(object value)
        {
            string text = value == null ? string.Empty : value.ToString();
            text = text.Replace("\"", "\"\"");
            return "\"" + text + "\"";
        }
    }
}
