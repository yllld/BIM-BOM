using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using FamilyConverter.Revit2021.Models;

namespace FamilyConverter.Revit2021.Services
{
    public class MeshDirectShapeCreationResult
    {
        public Element Element { get; set; }
        public ConversionMethod Method { get; set; }
        public int SourceTriangleCount { get; set; }
        public int CreatedTriangleCount { get; set; }
        public int SkippedTriangleCount { get; set; }
        public int GeometryObjectCount { get; set; }
        public int SourceVertexCount { get; set; }
        public int SourceNormalCount { get; set; }
        public int OutputNormalCount { get; set; }
        public string SourceNormalDistribution { get; set; }
        public string OutputNormalDistribution { get; set; }
        public string CreationPath { get; set; }
        public string FallbackReason { get; set; }
        public string FreeFormFailureReason { get; set; }
        public string DirectMeshFailureReason { get; set; }
        public int SolidFaceCount { get; set; }
        public int FreeFormPlanarFaceCount { get; set; }
        public int FreeFormReferenceFaceCount { get; set; }
        public int BoundaryEdgeCount { get; set; }
        public int BoundaryLoopCount { get; set; }
        public int NonManifoldEdgeCount { get; set; }
        public int OrientationFlipCount { get; set; }
        public int OrientationConflictCount { get; set; }
        public int PlanarCapCount { get; set; }
        public int NonPlanarBoundaryLoopCount { get; set; }
        public int OpenBoundaryChainCount { get; set; }
        public bool TopologyRepairApplied { get; set; }
        public string TopologyRepairFailureReason { get; set; }
    }

    public class MeshDirectShapeCreationService
    {
        private class FaceReferenceStats
        {
            public int PlanarFaceCount { get; set; }
            public int ReferencePlanarFaceCount { get; set; }
        }

        private const double PointToleranceFeet = 1e-9;
        private const double TriangleAreaToleranceFeet2 = 1e-12;
        private readonly FreeFormCreationService _freeFormService;
        private readonly MeshTopologyRepairService _topologyRepairService;

        public MeshDirectShapeCreationService(FreeFormCreationService freeFormService)
        {
            _freeFormService = freeFormService;
            _topologyRepairService = new MeshTopologyRepairService();
        }

        public MeshDirectShapeCreationResult Create(
            Document document,
            GeometryObjectInfo source,
            ConversionOptions options)
        {
            if (document == null || !document.IsFamilyDocument)
            {
                throw new InvalidOperationException("DirectShape Mesh можно создавать только внутри документа семейства.");
            }

            if (source == null || source.Mesh == null)
            {
                throw new InvalidOperationException("Mesh для DirectShape не найден.");
            }

            Mesh mesh = source.Mesh;
            Transform transform = source.Transform ?? Transform.Identity;
            string freeFormFailure;
            int solidFaceCount;
            MeshTopologyRepairAnalysis topology;
            MeshDirectShapeCreationResult freeFormResult = TryCreateHybridFreeForm(
                document,
                source,
                options,
                mesh,
                transform,
                out freeFormFailure,
                out solidFaceCount,
                out topology);

            if (freeFormResult != null)
            {
                return freeFormResult;
            }

            string directMeshFailure;
            MeshDirectShapeCreationResult directResult = TryCreateFromTransformedMesh(
                document,
                source,
                mesh,
                transform,
                out directMeshFailure);

            if (directResult != null)
            {
                directResult.CreationPath = "DirectMeshFallback";
                directResult.FallbackReason = freeFormFailure;
                directResult.FreeFormFailureReason = freeFormFailure;
                directResult.SolidFaceCount = solidFaceCount;
                ApplyTopologyDiagnostics(directResult, topology, false);
                return directResult;
            }

            MeshDirectShapeCreationResult tessellatedResult = CreateFromTessellatedFaces(
                document,
                source,
                mesh,
                transform,
                CombineFallbackReasons(freeFormFailure, directMeshFailure));
            tessellatedResult.FreeFormFailureReason = freeFormFailure;
            tessellatedResult.DirectMeshFailureReason = directMeshFailure;
            tessellatedResult.SolidFaceCount = solidFaceCount;
            ApplyTopologyDiagnostics(tessellatedResult, topology, false);
            return tessellatedResult;
        }

        private MeshDirectShapeCreationResult TryCreateFreeForm(
            Document document,
            GeometryObjectInfo source,
            ConversionOptions options,
            Mesh mesh,
            Transform transform,
            out string failureReason,
            out int solidFaceCount)
        {
            failureReason = null;
            solidFaceCount = 0;

            try
            {
                using (var builder = new TessellatedShapeBuilder())
                {
                    int acceptedTriangles;
                    Solid solid = BuildSolidFromMesh(builder, mesh, transform, out acceptedTriangles);
                    if (solid == null || solid.Faces.Size == 0)
                    {
                        throw new InvalidOperationException("TessellatedShapeBuilder не вернул Solid.");
                    }

                    solidFaceCount = solid.Faces.Size;
                    using (var subTransaction = new SubTransaction(document))
                    {
                        subTransaction.Start();
                        try
                        {
                            Element element = _freeFormService.Create(document, solid, source, options);
                            document.Regenerate();

                            FaceReferenceStats faceStats = GetFaceReferenceStats(element);
                            if (faceStats.ReferencePlanarFaceCount == 0)
                            {
                                throw new InvalidOperationException(
                                    "FreeForm создан, но Revit не вернул ни одной планарной грани со ссылкой для коннектора.");
                            }

                            subTransaction.Commit();
                            return new MeshDirectShapeCreationResult
                            {
                                Element = element,
                                Method = ConversionMethod.FreeForm,
                                SourceTriangleCount = mesh.NumTriangles,
                                CreatedTriangleCount = acceptedTriangles,
                                SkippedTriangleCount = 0,
                                GeometryObjectCount = 1,
                                SourceVertexCount = mesh.Vertices.Count,
                                SourceNormalCount = mesh.NumberOfNormals,
                                OutputNormalCount = 0,
                                SourceNormalDistribution = mesh.DistributionOfNormals.ToString(),
                                OutputNormalDistribution = "SolidFaces",
                                CreationPath = "FreeFormFromClosedMesh",
                                SolidFaceCount = solidFaceCount,
                                FreeFormPlanarFaceCount = faceStats.PlanarFaceCount,
                                FreeFormReferenceFaceCount = faceStats.ReferencePlanarFaceCount
                            };
                        }
                        catch
                        {
                            TryRollBack(subTransaction);
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                failureReason = BuildFallbackReason(ex);
                return null;
            }
        }

        private MeshDirectShapeCreationResult TryCreateHybridFreeForm(
            Document document,
            GeometryObjectInfo source,
            ConversionOptions options,
            Mesh mesh,
            Transform transform,
            out string failureReason,
            out int solidFaceCount,
            out MeshTopologyRepairAnalysis topology)
        {
            topology = _topologyRepairService.Analyze(mesh, transform, ReadValidTriangle);

            string initialFailure;
            MeshDirectShapeCreationResult initialResult = TryCreateFreeForm(
                document,
                source,
                options,
                mesh,
                transform,
                out initialFailure,
                out solidFaceCount);

            if (initialResult != null)
            {
                ApplyTopologyDiagnostics(initialResult, topology, false);
                failureReason = null;
                return initialResult;
            }

            if (!topology.CanAttemptRepair)
            {
                failureReason = CombineRepairReasons(initialFailure, topology.FailureReason);
                return null;
            }

            string repairFailure;
            MeshDirectShapeCreationResult repairedResult = TryCreateRepairedFreeForm(
                document,
                source,
                options,
                mesh,
                topology,
                out repairFailure,
                out solidFaceCount);

            if (repairedResult != null)
            {
                ApplyTopologyDiagnostics(repairedResult, topology, true);
                failureReason = null;
                return repairedResult;
            }

            topology.FailureReason = repairFailure;
            failureReason = CombineRepairReasons(initialFailure, repairFailure);
            return null;
        }

        private MeshDirectShapeCreationResult TryCreateRepairedFreeForm(
            Document document,
            GeometryObjectInfo source,
            ConversionOptions options,
            Mesh mesh,
            MeshTopologyRepairAnalysis topology,
            out string failureReason,
            out int solidFaceCount)
        {
            failureReason = null;
            solidFaceCount = 0;

            try
            {
                using (var builder = new TessellatedShapeBuilder())
                {
                    int acceptedTriangles;
                    Solid solid = BuildSolidFromRepairedMesh(builder, topology, out acceptedTriangles);
                    solidFaceCount = solid.Faces.Size;

                    using (var subTransaction = new SubTransaction(document))
                    {
                        subTransaction.Start();
                        try
                        {
                            Element element = _freeFormService.Create(document, solid, source, options);
                            document.Regenerate();

                            FaceReferenceStats faceStats = GetFaceReferenceStats(element);
                            if (faceStats.ReferencePlanarFaceCount == 0)
                            {
                                throw new InvalidOperationException(
                                    "Repaired FreeForm has no referenceable planar face for a face-hosted connector.");
                            }

                            subTransaction.Commit();
                            return new MeshDirectShapeCreationResult
                            {
                                Element = element,
                                Method = ConversionMethod.FreeForm,
                                SourceTriangleCount = mesh.NumTriangles,
                                CreatedTriangleCount = acceptedTriangles,
                                SkippedTriangleCount = 0,
                                GeometryObjectCount = 1,
                                SourceVertexCount = mesh.Vertices.Count,
                                SourceNormalCount = mesh.NumberOfNormals,
                                OutputNormalCount = 0,
                                SourceNormalDistribution = mesh.DistributionOfNormals.ToString(),
                                OutputNormalDistribution = "SolidFaces",
                                CreationPath = "FreeFormFromRepairedMesh",
                                SolidFaceCount = solidFaceCount,
                                FreeFormPlanarFaceCount = faceStats.PlanarFaceCount,
                                FreeFormReferenceFaceCount = faceStats.ReferencePlanarFaceCount
                            };
                        }
                        catch
                        {
                            TryRollBack(subTransaction);
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                failureReason = BuildFallbackReason(ex);
                return null;
            }
        }

        private static Solid BuildSolidFromRepairedMesh(
            TessellatedShapeBuilder builder,
            MeshTopologyRepairAnalysis topology,
            out int acceptedTriangles)
        {
            if (topology == null || topology.Triangles == null || topology.Triangles.Count == 0)
            {
                throw new InvalidOperationException("Topology repair did not return mesh triangles.");
            }

            acceptedTriangles = 0;
            builder.OpenConnectedFaceSet(true);
            foreach (IList<XYZ> triangle in topology.Triangles)
            {
                builder.AddFace(new TessellatedFace(triangle, ElementId.InvalidElementId));
                acceptedTriangles++;
            }

            foreach (IList<XYZ> capLoop in topology.CapLoops)
            {
                builder.AddFace(new TessellatedFace(capLoop, ElementId.InvalidElementId));
            }

            builder.CloseConnectedFaceSet();
            builder.Target = TessellatedShapeBuilderTarget.Solid;
            builder.Fallback = TessellatedShapeBuilderFallback.Abort;
            builder.Build();

            TessellatedShapeBuilderResult buildResult = builder.GetBuildResult();
            IList<GeometryObject> geometryObjects = buildResult == null
                ? null
                : buildResult.GetGeometricalObjects();

            if (geometryObjects == null || geometryObjects.Count != 1)
            {
                throw new InvalidOperationException(
                    "Topology repair expected one closed Solid, but received "
                    + (geometryObjects == null ? 0 : geometryObjects.Count)
                    + " geometry objects.");
            }

            Solid solid = geometryObjects[0] as Solid;
            if (solid == null || solid.Faces.Size == 0 || solid.Volume <= 0)
            {
                throw new InvalidOperationException("Repaired Mesh did not produce a closed volumetric Solid.");
            }

            return solid;
        }

        private static Solid BuildSolidFromMesh(
            TessellatedShapeBuilder builder,
            Mesh mesh,
            Transform transform,
            out int acceptedTriangles)
        {
            acceptedTriangles = 0;
            builder.OpenConnectedFaceSet(true);

            for (int index = 0; index < mesh.NumTriangles; index++)
            {
                IList<XYZ> vertices;
                if (!TryGetValidTriangle(mesh, index, transform, out vertices))
                {
                    throw new InvalidOperationException(
                        "Mesh содержит недопустимый или вырожденный треугольник "
                        + index
                        + " и не может быть безопасно замкнут в Solid.");
                }

                builder.AddFace(new TessellatedFace(vertices, ElementId.InvalidElementId));
                acceptedTriangles++;
            }

            builder.CloseConnectedFaceSet();
            builder.Target = TessellatedShapeBuilderTarget.Solid;
            builder.Fallback = TessellatedShapeBuilderFallback.Abort;
            builder.Build();

            TessellatedShapeBuilderResult buildResult = builder.GetBuildResult();
            IList<GeometryObject> geometryObjects = buildResult == null
                ? null
                : buildResult.GetGeometricalObjects();

            if (geometryObjects == null || geometryObjects.Count != 1)
            {
                throw new InvalidOperationException(
                    "Для FreeForm ожидался один замкнутый Solid, получено объектов: "
                    + (geometryObjects == null ? 0 : geometryObjects.Count)
                    + ".");
            }

            Solid solid = geometryObjects[0] as Solid;
            if (solid == null || solid.Faces.Size == 0 || solid.Volume <= 0)
            {
                throw new InvalidOperationException("Результат Mesh→Solid не является замкнутым объёмным телом.");
            }

            return solid;
        }

        private static MeshDirectShapeCreationResult TryCreateFromTransformedMesh(
            Document document,
            GeometryObjectInfo source,
            Mesh mesh,
            Transform transform,
            out string failureReason)
        {
            failureReason = null;

            try
            {
                using (Mesh transformedMesh = mesh.get_Transformed(transform))
                {
                    if (transformedMesh == null)
                    {
                        throw new InvalidOperationException("Revit API вернул пустой трансформированный Mesh.");
                    }

                    var geometryObjects = new List<GeometryObject> { transformedMesh };
                    using (var subTransaction = new SubTransaction(document))
                    {
                        subTransaction.Start();
                        try
                        {
                            DirectShape directShape = CreateDirectShape(document, source);
                            if (!directShape.IsValidShape(geometryObjects))
                            {
                                throw new InvalidOperationException("DirectShape отклонил исходный Mesh как недопустимую форму.");
                            }

                            directShape.SetShape(geometryObjects);
                            TrySetElementName(directShape, source);
                            subTransaction.Commit();

                            return new MeshDirectShapeCreationResult
                            {
                                Element = directShape,
                                Method = ConversionMethod.DirectShape,
                                SourceTriangleCount = mesh.NumTriangles,
                                CreatedTriangleCount = transformedMesh.NumTriangles,
                                SkippedTriangleCount = 0,
                                GeometryObjectCount = 1,
                                SourceVertexCount = mesh.Vertices.Count,
                                SourceNormalCount = mesh.NumberOfNormals,
                                OutputNormalCount = transformedMesh.NumberOfNormals,
                                SourceNormalDistribution = mesh.DistributionOfNormals.ToString(),
                                OutputNormalDistribution = transformedMesh.DistributionOfNormals.ToString(),
                                CreationPath = "DirectMesh"
                            };
                        }
                        catch
                        {
                            TryRollBack(subTransaction);
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                failureReason = BuildFallbackReason(ex);
                return null;
            }
        }

        private static MeshDirectShapeCreationResult CreateFromTessellatedFaces(
            Document document,
            GeometryObjectInfo source,
            Mesh mesh,
            Transform transform,
            string directMeshFailure)
        {
            int acceptedTriangles = 0;
            int skippedTriangles = 0;

            using (var builder = new TessellatedShapeBuilder())
            {
                builder.OpenConnectedFaceSet(false);

                for (int index = 0; index < mesh.NumTriangles; index++)
                {
                    IList<XYZ> vertices;
                    if (!TryGetValidTriangle(mesh, index, transform, out vertices))
                    {
                        skippedTriangles++;
                        continue;
                    }

                    try
                    {
                        var face = new TessellatedFace(vertices, ElementId.InvalidElementId);
                        if (!face.IsValidObject)
                        {
                            skippedTriangles++;
                            continue;
                        }

                        builder.AddFace(face);
                        acceptedTriangles++;
                    }
                    catch
                    {
                        skippedTriangles++;
                    }
                }

                builder.CloseConnectedFaceSet();

                if (acceptedTriangles == 0)
                {
                    throw new InvalidOperationException("Mesh не содержит допустимых треугольников для DirectShape.");
                }

                builder.Target = TessellatedShapeBuilderTarget.AnyGeometry;
                builder.Fallback = TessellatedShapeBuilderFallback.Mesh;
                builder.Build();

                TessellatedShapeBuilderResult buildResult = builder.GetBuildResult();
                IList<GeometryObject> geometryObjects = buildResult == null
                    ? null
                    : buildResult.GetGeometricalObjects();

                if (geometryObjects == null || geometryObjects.Count == 0)
                {
                    throw new InvalidOperationException("TessellatedShapeBuilder не создал геометрию из Mesh.");
                }

                DirectShape directShape = CreateDirectShape(document, source);
                directShape.SetShape(geometryObjects);
                TrySetElementName(directShape, source);

                return new MeshDirectShapeCreationResult
                {
                    Element = directShape,
                    Method = ConversionMethod.DirectShape,
                    SourceTriangleCount = mesh.NumTriangles,
                    CreatedTriangleCount = acceptedTriangles,
                    SkippedTriangleCount = skippedTriangles,
                    GeometryObjectCount = geometryObjects.Count,
                    SourceVertexCount = mesh.Vertices.Count,
                    SourceNormalCount = mesh.NumberOfNormals,
                    OutputNormalCount = CountNormals(geometryObjects),
                    SourceNormalDistribution = mesh.DistributionOfNormals.ToString(),
                    OutputNormalDistribution = GetNormalDistribution(geometryObjects),
                    CreationPath = "TessellatedFallback",
                    FallbackReason = directMeshFailure
                };
            }
        }

        private static DirectShape CreateDirectShape(Document document, GeometryObjectInfo source)
        {
            ElementId categoryId = ResolveDirectShapeCategory(document);
            DirectShape directShape = DirectShape.CreateElement(document, categoryId);
            directShape.ApplicationId = ProductInfo.VendorId + "." + ProductInfo.Name;
            directShape.ApplicationDataId = source.ObjectId;
            return directShape;
        }

        private static void TrySetElementName(DirectShape directShape, GeometryObjectInfo source)
        {
            try
            {
                directShape.SetName(BuildElementName(source));
            }
            catch
            {
                // A name is helpful for diagnostics but must not block geometry creation.
            }
        }

        private static void TryRollBack(SubTransaction subTransaction)
        {
            try
            {
                if (subTransaction != null && subTransaction.GetStatus() == TransactionStatus.Started)
                {
                    subTransaction.RollBack();
                }
            }
            catch
            {
                // The outer transaction will still protect the document if rollback cannot be completed here.
            }
        }

        private static FaceReferenceStats GetFaceReferenceStats(Element element)
        {
            var stats = new FaceReferenceStats();
            if (element == null)
            {
                return stats;
            }

            var options = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = true,
                DetailLevel = ViewDetailLevel.Fine
            };

            GeometryElement geometry = element.get_Geometry(options);
            CountReferenceFaces(geometry, stats);
            return stats;
        }

        private static void CountReferenceFaces(GeometryElement geometry, FaceReferenceStats stats)
        {
            if (geometry == null || stats == null)
            {
                return;
            }

            foreach (GeometryObject geometryObject in geometry)
            {
                Solid solid = geometryObject as Solid;
                if (solid != null)
                {
                    foreach (Face face in solid.Faces)
                    {
                        if (!(face is PlanarFace))
                        {
                            continue;
                        }

                        stats.PlanarFaceCount++;
                        try
                        {
                            if (face.Reference != null)
                            {
                                stats.ReferencePlanarFaceCount++;
                            }
                        }
                        catch
                        {
                            // A non-referenceable face is still counted as planar for diagnostics.
                        }
                    }

                    continue;
                }

                GeometryInstance instance = geometryObject as GeometryInstance;
                if (instance != null)
                {
                    try
                    {
                        CountReferenceFaces(instance.GetInstanceGeometry(), stats);
                    }
                    catch
                    {
                        // Created FreeForm geometry is normally top-level; nested geometry is best-effort only.
                    }
                }
            }
        }

        private static int CountNormals(IList<GeometryObject> geometryObjects)
        {
            int count = 0;
            if (geometryObjects == null)
            {
                return count;
            }

            foreach (GeometryObject geometryObject in geometryObjects)
            {
                Mesh mesh = geometryObject as Mesh;
                if (mesh != null)
                {
                    count += mesh.NumberOfNormals;
                }
            }

            return count;
        }

        private static string GetNormalDistribution(IList<GeometryObject> geometryObjects)
        {
            var values = new List<string>();
            if (geometryObjects == null)
            {
                return string.Empty;
            }

            foreach (GeometryObject geometryObject in geometryObjects)
            {
                Mesh mesh = geometryObject as Mesh;
                if (mesh == null)
                {
                    continue;
                }

                string value = mesh.DistributionOfNormals.ToString();
                if (!values.Contains(value))
                {
                    values.Add(value);
                }
            }

            return string.Join(",", values);
        }

        private static string BuildFallbackReason(Exception exception)
        {
            if (exception == null)
            {
                return "Неизвестная ошибка прямого переноса Mesh.";
            }

            string message = exception.Message;
            if (string.IsNullOrWhiteSpace(message))
            {
                message = exception.GetType().Name;
            }

            return message.Replace(Environment.NewLine, " ").Trim();
        }

        private static string CombineFallbackReasons(string freeFormFailure, string directMeshFailure)
        {
            var reasons = new List<string>();
            if (!string.IsNullOrWhiteSpace(freeFormFailure))
            {
                reasons.Add("FreeForm: " + freeFormFailure);
            }

            if (!string.IsNullOrWhiteSpace(directMeshFailure))
            {
                reasons.Add("DirectMesh: " + directMeshFailure);
            }

            return string.Join(" | ", reasons);
        }

        private static string CombineRepairReasons(string initialFailure, string repairFailure)
        {
            var reasons = new List<string>();
            if (!string.IsNullOrWhiteSpace(initialFailure))
            {
                reasons.Add("Initial Solid: " + initialFailure);
            }

            if (!string.IsNullOrWhiteSpace(repairFailure))
            {
                reasons.Add("Topology repair: " + repairFailure);
            }

            return string.Join(" | ", reasons);
        }

        private static void ApplyTopologyDiagnostics(
            MeshDirectShapeCreationResult result,
            MeshTopologyRepairAnalysis topology,
            bool repairApplied)
        {
            if (result == null || topology == null)
            {
                return;
            }

            result.BoundaryEdgeCount = topology.BoundaryEdgeCount;
            result.BoundaryLoopCount = topology.BoundaryLoopCount;
            result.NonManifoldEdgeCount = topology.NonManifoldEdgeCount;
            result.OrientationFlipCount = topology.OrientationFlipCount;
            result.OrientationConflictCount = topology.OrientationConflictCount;
            result.PlanarCapCount = topology.CapLoops == null ? 0 : topology.CapLoops.Count;
            result.NonPlanarBoundaryLoopCount = topology.NonPlanarBoundaryLoopCount;
            result.OpenBoundaryChainCount = topology.OpenBoundaryChainCount;
            result.TopologyRepairApplied = repairApplied;
            result.TopologyRepairFailureReason = repairApplied ? null : topology.FailureReason;
        }

        private static IList<XYZ> ReadValidTriangle(Mesh mesh, int triangleIndex, Transform transform)
        {
            IList<XYZ> vertices;
            return TryGetValidTriangle(mesh, triangleIndex, transform, out vertices)
                ? vertices
                : null;
        }

        private static bool TryGetValidTriangle(Mesh mesh, int triangleIndex, Transform transform, out IList<XYZ> vertices)
        {
            vertices = null;

            try
            {
                MeshTriangle triangle = mesh.get_Triangle(triangleIndex);
                XYZ first = TransformPoint(triangle.get_Vertex(0), transform);
                XYZ second = TransformPoint(triangle.get_Vertex(1), transform);
                XYZ third = TransformPoint(triangle.get_Vertex(2), transform);

                if (!IsValidPoint(first) || !IsValidPoint(second) || !IsValidPoint(third))
                {
                    return false;
                }

                if (first.DistanceTo(second) <= PointToleranceFeet
                    || second.DistanceTo(third) <= PointToleranceFeet
                    || third.DistanceTo(first) <= PointToleranceFeet)
                {
                    return false;
                }

                double doubledArea = second.Subtract(first).CrossProduct(third.Subtract(first)).GetLength();
                if (doubledArea <= TriangleAreaToleranceFeet2)
                {
                    return false;
                }

                vertices = new List<XYZ> { first, second, third };
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static XYZ TransformPoint(XYZ point, Transform transform)
        {
            return transform == null || transform.IsIdentity ? point : transform.OfPoint(point);
        }

        private static bool IsValidPoint(XYZ point)
        {
            return point != null
                && IsFinite(point.X)
                && IsFinite(point.Y)
                && IsFinite(point.Z)
                && XYZ.IsWithinLengthLimits(point);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static ElementId ResolveDirectShapeCategory(Document document)
        {
            Category familyCategory = document.OwnerFamily == null
                ? null
                : document.OwnerFamily.FamilyCategory;

            if (familyCategory != null && DirectShape.IsValidCategoryId(familyCategory.Id, document))
            {
                return familyCategory.Id;
            }

            var genericModelId = new ElementId(BuiltInCategory.OST_GenericModel);
            if (DirectShape.IsValidCategoryId(genericModelId, document))
            {
                return genericModelId;
            }

            throw new InvalidOperationException("Категория текущего семейства не поддерживает DirectShape.");
        }

        private static string BuildElementName(GeometryObjectInfo source)
        {
            string layerName = string.IsNullOrWhiteSpace(source.LayerName) ? "Unknown layer" : source.LayerName.Trim();
            return "DWG Mesh - " + layerName;
        }
    }
}
