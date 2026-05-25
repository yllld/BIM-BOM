using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using FamilyConverter.Revit2021.Models;
using FamilyConverter.Revit2021.Utils;

namespace FamilyConverter.Revit2021.Services
{
    public class GeometryAnalysisService
    {
        private readonly UnitService _unitService;

        public GeometryAnalysisService(UnitService unitService)
        {
            _unitService = unitService;
        }

        public PrismaticCandidate Analyze(GeometryObjectInfo info, ConversionOptions options)
        {
            var candidate = new PrismaticCandidate
            {
                Classification = GeometryClassification.Invalid,
                Confidence = 0,
                IsProfileSafe = false
            };

            if (info == null || info.Solid == null || info.Solid.Faces.Size == 0 || info.Solid.Volume <= 0)
            {
                candidate.Warnings.Add("Solid отсутствует или имеет нулевой объем.");
                return candidate;
            }

            Solid solid = info.Solid;
            List<PlanarFace> planarFaces = solid.Faces.Cast<Face>().OfType<PlanarFace>().ToList();
            int curvedFaceCount = solid.Faces.Size - planarFaces.Count;
            if (planarFaces.Count < 2)
            {
                candidate.Classification = curvedFaceCount > 0 ? GeometryClassification.CylinderLike : GeometryClassification.Complex;
                candidate.Warnings.Add("Недостаточно плоских граней для надежного профиля выдавливания.");
                return candidate;
            }

            double closureToleranceFeet = _unitService.MmToFeet(options.LoopClosureToleranceMm);
            var pairs = FindOppositePlanarFacePairs(planarFaces, closureToleranceFeet, options.VolumeTolerancePercent);
            if (pairs.Count == 0)
            {
                candidate.Classification = curvedFaceCount > 0 ? GeometryClassification.CylinderLike : GeometryClassification.Complex;
                candidate.Warnings.Add("Не найдена надежная пара противоположных плоских граней.");
                return candidate;
            }

            FacePair best = pairs.OrderByDescending(x => x.Score).First();
            var warnings = new List<string>();
            IList<IList<Curve>> loops = CurveLoopUtils.ExtractLoops(best.BaseFace, closureToleranceFeet * 0.1, warnings);
            bool closed = CurveLoopUtils.AreLoopsClosed(loops, closureToleranceFeet, warnings);
            double depthFeet = best.DistanceFeet;

            XYZ normal = best.BaseFace.FaceNormal.Normalize();
            XYZ toTop = best.TopFace.Origin - best.BaseFace.Origin;
            if (normal.DotProduct(toTop) < 0)
            {
                normal = normal.Negate();
            }

            Plane plane = Plane.CreateByNormalAndOrigin(normal, best.BaseFace.Origin);
            candidate.BaseFace = best.BaseFace;
            candidate.TopFace = best.TopFace;
            candidate.SketchPlane = plane;
            candidate.DepthFeet = depthFeet;
            candidate.IsProfileSafe = closed && loops.Count > 0 && depthFeet > closureToleranceFeet;
            foreach (IList<Curve> loop in loops)
            {
                candidate.ProfileLoops.Add(loop);
            }

            foreach (string warning in warnings)
            {
                candidate.Warnings.Add(warning);
            }

            candidate.Classification = ClassifyByBoundingBoxAndFaces(info, planarFaces.Count, curvedFaceCount);
            candidate.Confidence = ComputeConfidence(candidate, info, planarFaces.Count, curvedFaceCount, best);

            if (!candidate.IsProfileSafe)
            {
                candidate.Confidence = Math.Min(candidate.Confidence, 0.49);
            }

            if (curvedFaceCount > 0 && candidate.Classification != GeometryClassification.CylinderLike)
            {
                candidate.Warnings.Add("Solid содержит криволинейные грани; Extrusion может быть неточным.");
                candidate.Confidence = Math.Min(candidate.Confidence, 0.75);
            }

            return candidate;
        }

        public AiGeometryRequest CreateAiRequest(GeometryObjectInfo info, PrismaticCandidate candidate)
        {
            var request = new AiGeometryRequest
            {
                object_id = info.ObjectId,
                layer_name = info.LayerName,
                bbox_mm = GeometryUtils.BoundingBoxToMmString(info.BoundingBox, _unitService),
                volume_mm3 = info.VolumeMm3,
                local_classification = candidate.Classification.ToString(),
                local_confidence = candidate.Confidence,
                Source = info
            };

            request.face_summary["face_count"] = info.FaceCount;
            request.face_summary["planar_face_count"] = info.Solid == null ? 0 : info.Solid.Faces.Cast<Face>().OfType<PlanarFace>().Count();
            request.face_summary["curved_face_count"] = info.Solid == null ? 0 : info.Solid.Faces.Size - info.Solid.Faces.Cast<Face>().OfType<PlanarFace>().Count();
            request.face_summary["candidate_opposite_face_pairs"] = candidate.BaseFace == null ? 0 : 1;
            request.edge_summary["edge_count"] = info.EdgeCount;
            request.edge_summary["line_count"] = CurveLoopUtils.CountLineEdges(info.Solid);
            request.edge_summary["arc_count"] = CurveLoopUtils.CountArcEdges(info.Solid);
            request.edge_summary["curve_kinds"] = CurveLoopUtils.CountCurveKinds(info.Solid);
            request.candidate_methods.Add("extrusion");
            request.candidate_methods.Add("freeform");
            request.candidate_methods.Add("skip");

            foreach (string warning in info.Warnings)
            {
                request.warnings.Add(warning);
            }
            foreach (string warning in candidate.Warnings)
            {
                request.warnings.Add(warning);
            }

            return request;
        }

        private static List<FacePair> FindOppositePlanarFacePairs(IList<PlanarFace> faces, double toleranceFeet, double volumeTolerancePercent)
        {
            var pairs = new List<FacePair>();
            for (int i = 0; i < faces.Count; i++)
            {
                for (int j = i + 1; j < faces.Count; j++)
                {
                    PlanarFace a = faces[i];
                    PlanarFace b = faces[j];
                    if (!ToleranceUtils.AlmostOpposite(a.FaceNormal, b.FaceNormal, 0.98))
                    {
                        continue;
                    }

                    double areaMax = Math.Max(Math.Abs(a.Area), Math.Abs(b.Area));
                    if (areaMax <= 1e-9)
                    {
                        continue;
                    }

                    double areaDeltaPercent = Math.Abs(a.Area - b.Area) / areaMax * 100.0;
                    if (areaDeltaPercent > Math.Max(10.0, volumeTolerancePercent * 5.0))
                    {
                        continue;
                    }

                    double distance = Math.Abs((b.Origin - a.Origin).DotProduct(a.FaceNormal.Normalize()));
                    if (distance <= toleranceFeet)
                    {
                        continue;
                    }

                    pairs.Add(new FacePair
                    {
                        BaseFace = a,
                        TopFace = b,
                        DistanceFeet = distance,
                        Score = a.Area + b.Area - areaDeltaPercent
                    });
                }
            }

            return pairs;
        }

        private static GeometryClassification ClassifyByBoundingBoxAndFaces(GeometryObjectInfo info, int planarFaceCount, int curvedFaceCount)
        {
            if (curvedFaceCount > 0 && planarFaceCount <= 2)
            {
                return GeometryClassification.CylinderLike;
            }

            if (info.BoundingBox == null)
            {
                return GeometryClassification.Prism;
            }

            double dx = Math.Abs(info.BoundingBox.Max.X - info.BoundingBox.Min.X);
            double dy = Math.Abs(info.BoundingBox.Max.Y - info.BoundingBox.Min.Y);
            double dz = Math.Abs(info.BoundingBox.Max.Z - info.BoundingBox.Min.Z);
            double[] dims = { dx, dy, dz };
            Array.Sort(dims);
            double max = Math.Max(dims[2], 1e-9);
            double min = dims[0];

            if (planarFaceCount == 6 && curvedFaceCount == 0)
            {
                return min / max < 0.15 ? GeometryClassification.Plate : GeometryClassification.Box;
            }

            if (curvedFaceCount == 0)
            {
                return GeometryClassification.Prism;
            }

            return GeometryClassification.ProfileLike;
        }

        private static double ComputeConfidence(PrismaticCandidate candidate, GeometryObjectInfo info, int planarFaceCount, int curvedFaceCount, FacePair best)
        {
            double confidence = 0.50;
            if (candidate.IsProfileSafe)
            {
                confidence += 0.25;
            }

            if (curvedFaceCount == 0)
            {
                confidence += 0.10;
            }

            if (planarFaceCount >= 6)
            {
                confidence += 0.05;
            }

            if (best.DistanceFeet > 1e-6)
            {
                confidence += 0.05;
            }

            if (info.EdgeCount > 0 && info.EdgeCount <= 48)
            {
                confidence += 0.05;
            }

            return Math.Max(0, Math.Min(0.98, confidence));
        }

        private class FacePair
        {
            public PlanarFace BaseFace { get; set; }
            public PlanarFace TopFace { get; set; }
            public double DistanceFeet { get; set; }
            public double Score { get; set; }
        }
    }
}
