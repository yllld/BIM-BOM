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
                    validation_volume_deviation_percent = result.ValidationVolumeDeviationPercent,
                    mesh_source_triangle_count = result.MeshSourceTriangleCount,
                    mesh_created_triangle_count = result.MeshCreatedTriangleCount,
                    mesh_skipped_triangle_count = result.MeshSkippedTriangleCount,
                    mesh_source_vertex_count = result.MeshSourceVertexCount,
                    mesh_source_normal_count = result.MeshSourceNormalCount,
                    mesh_output_normal_count = result.MeshOutputNormalCount,
                    mesh_source_normal_distribution = result.MeshSourceNormalDistribution,
                    mesh_output_normal_distribution = result.MeshOutputNormalDistribution,
                    mesh_creation_path = result.MeshCreationPath,
                    mesh_fallback_reason = result.MeshFallbackReason,
                    mesh_freeform_failure_reason = result.MeshFreeFormFailureReason,
                    mesh_direct_mesh_failure_reason = result.MeshDirectMeshFailureReason,
                    mesh_solid_face_count = result.MeshSolidFaceCount,
                    mesh_freeform_planar_face_count = result.MeshFreeFormPlanarFaceCount,
                    mesh_freeform_reference_face_count = result.MeshFreeFormReferenceFaceCount,
                    mesh_boundary_edge_count = result.MeshBoundaryEdgeCount,
                    mesh_boundary_loop_count = result.MeshBoundaryLoopCount,
                    mesh_non_manifold_edge_count = result.MeshNonManifoldEdgeCount,
                    mesh_orientation_flip_count = result.MeshOrientationFlipCount,
                    mesh_orientation_conflict_count = result.MeshOrientationConflictCount,
                    mesh_planar_cap_count = result.MeshPlanarCapCount,
                    mesh_non_planar_boundary_loop_count = result.MeshNonPlanarBoundaryLoopCount,
                    mesh_open_boundary_chain_count = result.MeshOpenBoundaryChainCount,
                    mesh_topology_repair_applied = result.MeshTopologyRepairApplied,
                    mesh_topology_repair_failure_reason = result.MeshTopologyRepairFailureReason
                });
            }

            return rows;
        }

        private static string BuildCsv(IList<ReportRow> rows)
        {
            var builder = new StringBuilder();
            builder.AppendLine("object_id;layer;source_geometry_type;bbox_mm;volume_mm3;face_count;edge_count;local_classification;local_confidence;ai_used;ai_provider;ai_model;ai_recommendation;ai_confidence;final_method;result_status;created_element_id;message;exception;fallback_used;extrusion_failed_reason;validation_bbox_deviation_mm;validation_volume_deviation_percent;mesh_source_triangle_count;mesh_created_triangle_count;mesh_skipped_triangle_count;mesh_source_vertex_count;mesh_source_normal_count;mesh_output_normal_count;mesh_source_normal_distribution;mesh_output_normal_distribution;mesh_creation_path;mesh_fallback_reason;mesh_freeform_failure_reason;mesh_direct_mesh_failure_reason;mesh_solid_face_count;mesh_freeform_planar_face_count;mesh_freeform_reference_face_count;mesh_boundary_edge_count;mesh_boundary_loop_count;mesh_non_manifold_edge_count;mesh_orientation_flip_count;mesh_orientation_conflict_count;mesh_planar_cap_count;mesh_non_planar_boundary_loop_count;mesh_open_boundary_chain_count;mesh_topology_repair_applied;mesh_topology_repair_failure_reason");
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
                    row.validation_volume_deviation_percent.ToString("0.###"),
                    row.mesh_source_triangle_count,
                    row.mesh_created_triangle_count,
                    row.mesh_skipped_triangle_count,
                    row.mesh_source_vertex_count,
                    row.mesh_source_normal_count,
                    row.mesh_output_normal_count,
                    Escape(row.mesh_source_normal_distribution),
                    Escape(row.mesh_output_normal_distribution),
                    Escape(row.mesh_creation_path),
                    Escape(row.mesh_fallback_reason),
                    Escape(row.mesh_freeform_failure_reason),
                    Escape(row.mesh_direct_mesh_failure_reason),
                    row.mesh_solid_face_count,
                    row.mesh_freeform_planar_face_count,
                    row.mesh_freeform_reference_face_count,
                    row.mesh_boundary_edge_count,
                    row.mesh_boundary_loop_count,
                    row.mesh_non_manifold_edge_count,
                    row.mesh_orientation_flip_count,
                    row.mesh_orientation_conflict_count,
                    row.mesh_planar_cap_count,
                    row.mesh_non_planar_boundary_loop_count,
                    row.mesh_open_boundary_chain_count,
                    row.mesh_topology_repair_applied,
                    Escape(row.mesh_topology_repair_failure_reason)));
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
