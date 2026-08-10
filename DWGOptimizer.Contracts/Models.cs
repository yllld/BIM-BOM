using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace DWGOptimizer.Contracts
{
    public enum OptimizationProfile
    {
        Safe,
        Balanced,
        Aggressive
    }

    public enum FindingSeverity
    {
        Info,
        Warning,
        Blocker
    }

    [DataContract]
    public class BoundsInfo
    {
        [DataMember(Order = 1)] public double MinX { get; set; }
        [DataMember(Order = 2)] public double MinY { get; set; }
        [DataMember(Order = 3)] public double MinZ { get; set; }
        [DataMember(Order = 4)] public double MaxX { get; set; }
        [DataMember(Order = 5)] public double MaxY { get; set; }
        [DataMember(Order = 6)] public double MaxZ { get; set; }
        [DataMember(Order = 7)] public bool IsValid { get; set; }
        [DataMember(Order = 8)] public double DistanceFromOrigin { get; set; }
    }

    [DataContract]
    public class GeometryCounts
    {
        [DataMember(Order = 1)] public int TotalEntities { get; set; }
        [DataMember(Order = 2)] public int Solid3d { get; set; }
        [DataMember(Order = 3)] public int Surface { get; set; }
        [DataMember(Order = 4)] public int SubDMesh { get; set; }
        [DataMember(Order = 5)] public int Body { get; set; }
        [DataMember(Order = 6)] public int Region { get; set; }
        [DataMember(Order = 7)] public int BlockReference { get; set; }
        [DataMember(Order = 8)] public int Proxy { get; set; }
        [DataMember(Order = 9)] public int Curves2d { get; set; }
        [DataMember(Order = 10)] public int Annotation { get; set; }
        [DataMember(Order = 11)] public int Other { get; set; }
        [DataMember(Order = 12)] public long MeshVertices { get; set; }
        [DataMember(Order = 13)] public long MeshFaces { get; set; }
        [DataMember(Order = 14)] public long SolidFaces { get; set; }
        [DataMember(Order = 15)] public long SolidEdges { get; set; }
        [DataMember(Order = 16)] public int InvalidGeometry { get; set; }
        [DataMember(Order = 17)] public int TinyGeometry { get; set; }
        [DataMember(Order = 18)] public int Curves3d { get; set; }
    }

    [DataContract]
    public class XrefInfo
    {
        [DataMember(Order = 1)] public string Name { get; set; }
        [DataMember(Order = 2)] public string Path { get; set; }
        [DataMember(Order = 3)] public bool IsResolved { get; set; }
        [DataMember(Order = 4)] public bool IsCircular { get; set; }
        [DataMember(Order = 5)] public int ReferenceCount { get; set; }
    }

    [DataContract]
    public class Finding
    {
        [DataMember(Order = 1)] public string Code { get; set; }
        [DataMember(Order = 2)] public FindingSeverity Severity { get; set; }
        [DataMember(Order = 3)] public string Message { get; set; }
        [DataMember(Order = 4)] public string Handle { get; set; }
    }

    [DataContract]
    public class OperationResult
    {
        [DataMember(Order = 1)] public string Code { get; set; }
        [DataMember(Order = 2)] public string Description { get; set; }
        [DataMember(Order = 3)] public bool Applied { get; set; }
        [DataMember(Order = 4)] public bool RolledBack { get; set; }
        [DataMember(Order = 5)] public string Message { get; set; }
        [DataMember(Order = 6)] public int AffectedObjects { get; set; }
    }

    [DataContract]
    public class AnalysisReport
    {
        public AnalysisReport()
        {
            Counts = new GeometryCounts();
            Bounds = new BoundsInfo();
            Findings = new List<Finding>();
            Xrefs = new List<XrefInfo>();
        }

        [DataMember(Order = 1)] public int SchemaVersion { get; set; } = 1;
        [DataMember(Order = 2)] public string ToolVersion { get; set; }
        [DataMember(Order = 3)] public string SourcePath { get; set; }
        [DataMember(Order = 4)] public long SourceSizeBytes { get; set; }
        [DataMember(Order = 5)] public string SourceSha256 { get; set; }
        [DataMember(Order = 6)] public string DwgVersion { get; set; }
        [DataMember(Order = 7)] public string Units { get; set; }
        [DataMember(Order = 8)] public bool UnitsKnown { get; set; }
        [DataMember(Order = 9)] public GeometryCounts Counts { get; set; }
        [DataMember(Order = 10)] public BoundsInfo Bounds { get; set; }
        [DataMember(Order = 11)] public IList<XrefInfo> Xrefs { get; set; }
        [DataMember(Order = 12)] public IList<Finding> Findings { get; set; }
        [DataMember(Order = 13)] public int ReadinessScore { get; set; }
        [DataMember(Order = 14)] public OptimizationProfile RecommendedProfile { get; set; }
        [DataMember(Order = 15)] public DateTime AnalyzedAtUtc { get; set; }
        [DataMember(Order = 16)] public string Scope { get; set; }
    }

    [DataContract]
    public class OptimizationRequest
    {
        [DataMember(Order = 1)] public OptimizationProfile Profile { get; set; }
        [DataMember(Order = 2)] public bool NormalizeOrigin { get; set; }
        [DataMember(Order = 3)] public bool ContinueWithoutMissingXrefs { get; set; }
        [DataMember(Order = 4)] public string UnitsOverride { get; set; }
        [DataMember(Order = 5)] public double MaxDeviationMillimeters { get; set; } = 0.5;
    }

    [DataContract]
    public class OptimizationReport
    {
        public OptimizationReport()
        {
            Operations = new List<OperationResult>();
            Errors = new List<string>();
            CompletedAtUtc = DateTime.UtcNow;
        }

        [DataMember(Order = 1)] public int SchemaVersion { get; set; } = 1;
        [DataMember(Order = 2)] public AnalysisReport Before { get; set; }
        [DataMember(Order = 3)] public AnalysisReport After { get; set; }
        [DataMember(Order = 4)] public OptimizationProfile Profile { get; set; }
        [DataMember(Order = 5)] public IList<OperationResult> Operations { get; set; }
        [DataMember(Order = 6)] public double ShiftX { get; set; }
        [DataMember(Order = 7)] public double ShiftY { get; set; }
        [DataMember(Order = 8)] public double ShiftZ { get; set; }
        [DataMember(Order = 9)] public string OutputPath { get; set; }
        [DataMember(Order = 10)] public long OutputSizeBytes { get; set; }
        [DataMember(Order = 11)] public string OutputSha256 { get; set; }
        [DataMember(Order = 12)] public string JsonReportPath { get; set; }
        [DataMember(Order = 13)] public string HtmlReportPath { get; set; }
        [DataMember(Order = 14)] public bool Success { get; set; }
        [DataMember(Order = 15)] public IList<string> Errors { get; set; }
        [DataMember(Order = 16)] public DateTime CompletedAtUtc { get; set; }
    }

    [DataContract]
    public class BatchJob
    {
        [DataMember(Order = 1)] public string Id { get; set; }
        [DataMember(Order = 2)] public string SourcePath { get; set; }
        [DataMember(Order = 3)] public OptimizationRequest Request { get; set; }
        [DataMember(Order = 4)] public string StatusPath { get; set; }
    }

    [DataContract]
    public class BatchManifest
    {
        public BatchManifest()
        {
            Jobs = new List<BatchJob>();
        }

        [DataMember(Order = 1)] public string PluginPath { get; set; }
        [DataMember(Order = 2)] public string AutoCadCoreConsolePath { get; set; }
        [DataMember(Order = 3)] public IList<BatchJob> Jobs { get; set; }
    }

    [DataContract]
    public class BatchJobStatus
    {
        [DataMember(Order = 1)] public string JobId { get; set; }
        [DataMember(Order = 2)] public string State { get; set; }
        [DataMember(Order = 3)] public string Message { get; set; }
        [DataMember(Order = 4)] public string OutputPath { get; set; }
        [DataMember(Order = 5)] public int ExitCode { get; set; }
        [DataMember(Order = 6)] public DateTime UpdatedAtUtc { get; set; }
    }
}
