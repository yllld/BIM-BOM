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
        public int MeshSourceTriangleCount { get; set; }
        public int MeshCreatedTriangleCount { get; set; }
        public int MeshSkippedTriangleCount { get; set; }
        public int MeshSourceVertexCount { get; set; }
        public int MeshSourceNormalCount { get; set; }
        public int MeshOutputNormalCount { get; set; }
        public string MeshSourceNormalDistribution { get; set; }
        public string MeshOutputNormalDistribution { get; set; }
        public string MeshCreationPath { get; set; }
        public string MeshFallbackReason { get; set; }
        public string MeshFreeFormFailureReason { get; set; }
        public string MeshDirectMeshFailureReason { get; set; }
        public int MeshSolidFaceCount { get; set; }
        public int MeshFreeFormPlanarFaceCount { get; set; }
        public int MeshFreeFormReferenceFaceCount { get; set; }
        public int MeshBoundaryEdgeCount { get; set; }
        public int MeshBoundaryLoopCount { get; set; }
        public int MeshNonManifoldEdgeCount { get; set; }
        public int MeshOrientationFlipCount { get; set; }
        public int MeshOrientationConflictCount { get; set; }
        public int MeshPlanarCapCount { get; set; }
        public int MeshNonPlanarBoundaryLoopCount { get; set; }
        public int MeshOpenBoundaryChainCount { get; set; }
        public bool MeshTopologyRepairApplied { get; set; }
        public string MeshTopologyRepairFailureReason { get; set; }
        public IList<string> Warnings { get; private set; }
        public GeometryObjectInfo Source { get; set; }
    }
}
