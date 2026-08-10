using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Autodesk.AutoCAD.BoundaryRepresentation;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DWGOptimizer.Contracts;

namespace DWGOptimizer.AutoCAD2022
{
    internal sealed class DwgAnalyzer
    {
        public AnalysisReport Analyze(Database database, string sourcePath)
        {
            if (database == null)
            {
                throw new ArgumentNullException("database");
            }

            var report = new AnalysisReport
            {
                ToolVersion = ProductInfo.Version,
                SourcePath = sourcePath,
                SourceSizeBytes = File.Exists(sourcePath) ? new FileInfo(sourcePath).Length : 0,
                SourceSha256 = File.Exists(sourcePath) ? ComputeSha256(sourcePath) : null,
                DwgVersion = database.OriginalFileVersion.ToString(),
                Units = database.Insunits.ToString(),
                UnitsKnown = database.Insunits != UnitsValue.Undefined,
                AnalyzedAtUtc = DateTime.UtcNow
            };

            report.Scope = "ModelSpace";

            if (database.NeedsRecovery)
            {
                report.Findings.Add(new Finding
                {
                    Code = "NEEDS_RECOVERY",
                    Severity = FindingSeverity.Blocker,
                    Message = "База DWG требует RECOVER. Оптимизация заблокирована до восстановления исходного файла."
                });
            }

            if (!report.UnitsKnown)
            {
                report.Findings.Add(new Finding
                {
                    Code = "UNITS_UNKNOWN",
                    Severity = FindingSeverity.Blocker,
                    Message = "INSUNITS=0. Перед оптимизацией необходимо выбрать единицы исходного DWG."
                });
            }

            using (Transaction transaction = database.TransactionManager.StartOpenCloseTransaction())
            {
                var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
                var modelSpace = (BlockTableRecord)transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                var recursionStack = new HashSet<ObjectId>();

                foreach (ObjectId objectId in modelSpace)
                {
                    Entity entity = transaction.GetObject(objectId, OpenMode.ForRead, false) as Entity;
                    if (entity != null)
                    {
                        VisitEntity(database, transaction, entity, Matrix3d.Identity, report, recursionStack, 0);
                    }
                }

                CollectXrefs(database, transaction, blockTable, report);
                transaction.Commit();
            }

            double oneMillimeter = GeometrySupport.MillimetersToDrawingUnits(1.0, database.Insunits);
            if (report.Bounds.IsValid && GeometrySupport.Diagonal(report.Bounds) < oneMillimeter)
            {
                report.Findings.Add(new Finding
                {
                    Code = "MODEL_TINY",
                    Severity = FindingSeverity.Warning,
                    Message = "Общий габарит модели меньше 1 мм; вероятна ошибка единиц."
                });
            }

            double oneKilometer = GeometrySupport.MillimetersToDrawingUnits(1000000.0, database.Insunits);
            if (report.Bounds.DistanceFromOrigin > oneKilometer)
            {
                report.Findings.Add(new Finding
                {
                    Code = "FAR_FROM_ORIGIN",
                    Severity = FindingSeverity.Warning,
                    Message = "Модель расположена дальше 1 км от WCS 0,0,0. Рекомендуется перенос в начало координат."
                });
            }

            if (report.Counts.Proxy > 0)
            {
                report.Findings.Add(new Finding
                {
                    Code = "PROXY_OBJECTS",
                    Severity = FindingSeverity.Warning,
                    Message = "Найдены Proxy-объекты: " + report.Counts.Proxy + ". Revit может их пропустить."
                });
            }

            if (report.Counts.MeshFaces > 1000000)
            {
                report.Findings.Add(new Finding
                {
                    Code = "HEAVY_MESH",
                    Severity = FindingSeverity.Warning,
                    Message = "Mesh содержит более 1 000 000 граней; импорт и конвертация в Revit будут тяжёлыми."
                });
            }

            ReadinessScorer.Score(report);
            return report;
        }

        private static void VisitEntity(
            Database database,
            Transaction transaction,
            Entity entity,
            Matrix3d transform,
            AnalysisReport report,
            ISet<ObjectId> recursionStack,
            int depth)
        {
            if (depth > 64)
            {
                report.Findings.Add(new Finding
                {
                    Code = "BLOCK_DEPTH",
                    Severity = FindingSeverity.Blocker,
                    Message = "Глубина вложенности блоков превышает 64.",
                    Handle = entity.Handle.ToString()
                });
                return;
            }

            report.Counts.TotalEntities++;
            CountEntity(entity, report);

            try
            {
                BoundsInfo bounds = GeometrySupport.ToBounds(entity.GeometricExtents, transform);
                if (!bounds.IsValid)
                {
                    report.Counts.InvalidGeometry++;
                    report.Findings.Add(new Finding
                    {
                        Code = "INVALID_COORDINATES",
                        Severity = FindingSeverity.Warning,
                        Message = "Объект содержит некорректные или бесконечные координаты.",
                        Handle = entity.Handle.ToString()
                    });
                }
                GeometrySupport.Merge(report.Bounds, bounds);
                double tinyThreshold = GeometrySupport.MillimetersToDrawingUnits(0.01, database.Insunits);
                if (bounds.IsValid && GeometrySupport.Diagonal(bounds) <= tinyThreshold)
                {
                    report.Counts.TinyGeometry++;
                }
            }
            catch (System.Exception ex)
            {
                report.Counts.InvalidGeometry++;
                report.Findings.Add(new Finding
                {
                    Code = "INVALID_EXTENTS",
                    Severity = FindingSeverity.Warning,
                    Message = "Не удалось получить габариты объекта: " + ex.Message,
                    Handle = entity.Handle.ToString()
                });
            }

            var solid = entity as Solid3d;
            if (solid != null)
            {
                CountBrep(solid, report);
            }

            var mesh = entity as SubDMesh;
            if (mesh != null)
            {
                report.Counts.MeshVertices += mesh.NumberOfVertices;
                report.Counts.MeshFaces += mesh.NumberOfFaces;
            }

            var blockReference = entity as BlockReference;
            if (blockReference == null || blockReference.BlockTableRecord.IsNull || recursionStack.Contains(blockReference.BlockTableRecord))
            {
                return;
            }

            recursionStack.Add(blockReference.BlockTableRecord);
            try
            {
                var block = transaction.GetObject(blockReference.BlockTableRecord, OpenMode.ForRead, false) as BlockTableRecord;
                if (block == null)
                {
                    return;
                }

                Matrix3d childTransform = transform * blockReference.BlockTransform;
                foreach (ObjectId childId in block)
                {
                    Entity child = transaction.GetObject(childId, OpenMode.ForRead, false) as Entity;
                    if (child != null)
                    {
                        VisitEntity(database, transaction, child, childTransform, report, recursionStack, depth + 1);
                    }
                }
            }
            finally
            {
                recursionStack.Remove(blockReference.BlockTableRecord);
            }
        }

        private static void CountEntity(Entity entity, AnalysisReport report)
        {
            if (entity is Solid3d) report.Counts.Solid3d++;
            else if (entity is Autodesk.AutoCAD.DatabaseServices.Surface) report.Counts.Surface++;
            else if (entity is SubDMesh) report.Counts.SubDMesh++;
            else if (entity is Body) report.Counts.Body++;
            else if (entity is Region) report.Counts.Region++;
            else if (entity is BlockReference) report.Counts.BlockReference++;
            else if (entity is ProxyEntity || entity.IsAProxy) report.Counts.Proxy++;
            else if (GeometrySupport.IsAnnotation(entity)) report.Counts.Annotation++;
            else if (entity is Curve)
            {
                try
                {
                    Extents3d extents = entity.GeometricExtents;
                    if (entity is Polyline3d || Math.Abs(extents.MaxPoint.Z - extents.MinPoint.Z) > 1e-9) report.Counts.Curves3d++;
                    else report.Counts.Curves2d++;
                }
                catch { report.Counts.Curves2d++; }
            }
            else report.Counts.Other++;
        }

        private static void CountBrep(Solid3d solid, AnalysisReport report)
        {
            try
            {
                using (var brep = new Brep(solid))
                {
                    foreach (Autodesk.AutoCAD.BoundaryRepresentation.Face face in brep.Faces)
                    {
                        report.Counts.SolidFaces++;
                    }

                    foreach (Edge edge in brep.Edges)
                    {
                        report.Counts.SolidEdges++;
                    }
                }
            }
            catch (System.Exception ex)
            {
                report.Counts.InvalidGeometry++;
                report.Findings.Add(new Finding
                {
                    Code = "INVALID_BREP",
                    Severity = FindingSeverity.Warning,
                    Message = "ACIS/BRep объекта не читается: " + ex.Message,
                    Handle = solid.Handle.ToString()
                });
            }
        }

        private static void CollectXrefs(Database database, Transaction transaction, BlockTable blockTable, AnalysisReport report)
        {
            foreach (ObjectId id in blockTable)
            {
                var block = transaction.GetObject(id, OpenMode.ForRead, false) as BlockTableRecord;
                if (block == null || !block.IsFromExternalReference)
                {
                    continue;
                }

                bool circular = IsSamePath(block.PathName, report.SourcePath);
                bool resolved = block.XrefStatus == XrefStatus.Resolved;
                report.Xrefs.Add(new XrefInfo
                {
                    Name = block.Name,
                    Path = block.PathName,
                    IsResolved = resolved,
                    IsCircular = circular,
                    ReferenceCount = block.GetBlockReferenceIds(true, false).Count
                });

                if (!resolved)
                {
                    report.Findings.Add(new Finding
                    {
                        Code = circular ? "XREF_CIRCULAR" : "XREF_MISSING",
                        Severity = FindingSeverity.Blocker,
                        Message = circular
                            ? "Обнаружена циклическая XREF: " + block.Name
                            : "XREF не разрешена: " + block.Name + " — " + block.PathName
                    });
                }
            }
        }

        internal static bool IsSamePath(string candidate, string source)
        {
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(source)) return false;
            try
            {
                string sourceFull = Path.GetFullPath(source);
                string candidateFull = Path.IsPathRooted(candidate)
                    ? Path.GetFullPath(candidate)
                    : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFull), candidate));
                return string.Equals(sourceFull, candidateFull, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static string ComputeSha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
            }
        }
    }
}
