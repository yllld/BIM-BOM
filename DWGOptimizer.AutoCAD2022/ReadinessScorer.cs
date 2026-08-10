using System.Linq;
using DWGOptimizer.Contracts;

namespace DWGOptimizer.AutoCAD2022
{
    internal static class ReadinessScorer
    {
        public static void Score(AnalysisReport report)
        {
            int score = 100;
            score -= report.Findings.Count(x => x.Severity == FindingSeverity.Blocker) * 30;
            score -= report.Findings.Count(x => x.Severity == FindingSeverity.Warning) * 5;
            score -= report.Counts.Proxy > 0 ? 10 : 0;
            score -= report.Counts.InvalidGeometry > 0 ? 15 : 0;
            score -= report.Counts.MeshFaces > 1000000 ? 20 : report.Counts.MeshFaces > 250000 ? 10 : 0;
            report.ReadinessScore = score < 0 ? 0 : score;

            report.RecommendedProfile = report.Counts.MeshFaces > 1000000 || report.Counts.TotalEntities > 200000
                ? OptimizationProfile.Aggressive
                : report.Counts.Annotation + report.Counts.Curves2d > report.Counts.TotalEntities / 3
                    ? OptimizationProfile.Balanced
                    : OptimizationProfile.Safe;
        }
    }
}
