using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DWGOptimizer.Contracts;

namespace DWGOptimizer.AutoCAD2022
{
    internal sealed class DwgOptimizerService
    {
        private readonly DwgAnalyzer _analyzer = new DwgAnalyzer();

        public OptimizationReport Optimize(Database sourceDatabase, string sourcePath, AnalysisReport before, OptimizationRequest request)
        {
            var report = new OptimizationReport
            {
                Before = before,
                Profile = request.Profile,
                CompletedAtUtc = DateTime.UtcNow
            };
            report.OutputPath = OutputPathService.GetOutputPath(sourcePath, request.Profile);

            bool fatalBlocker = before.Findings.Any(x => x.Severity == FindingSeverity.Blocker
                && x.Code != "UNITS_UNKNOWN" && x.Code != "XREF_MISSING" && x.Code != "XREF_CIRCULAR");
            bool xrefBlocker = before.Findings.Any(x => x.Severity == FindingSeverity.Blocker
                && (x.Code == "XREF_MISSING" || x.Code == "XREF_CIRCULAR"));
            if (fatalBlocker || (xrefBlocker && !request.ContinueWithoutMissingXrefs))
            {
                report.Errors.Add("Оптимизация остановлена: анализ содержит блокирующие проблемы DWG.");
                ReportWriter.Write(report);
                return report;
            }

            if (!before.UnitsKnown && string.IsNullOrWhiteSpace(request.UnitsOverride))
            {
                report.Errors.Add("Не заданы единицы DWG при INSUNITS=0.");
                ReportWriter.Write(report);
                return report;
            }

            string outputPath = report.OutputPath;
            report.OutputPath = outputPath;
            using (Database working = sourceDatabase.Wblock())
            {
                if (!before.UnitsKnown)
                {
                    UnitsValue units;
                    if (!Enum.TryParse(request.UnitsOverride, true, out units))
                    {
                        report.Errors.Add("Неизвестные единицы: " + request.UnitsOverride);
                        ReportWriter.Write(report);
                        return report;
                    }

                    working.Insunits = units;
                    report.Operations.Add(Applied("SET_UNITS", "Назначить единицы", 1, units.ToString()));
                }

                Audit(working, report);
                BindXrefs(working, report, request.ContinueWithoutMissingXrefs);

                if (request.Profile != OptimizationProfile.Safe)
                {
                    RemovePaperSpaceLayouts(working, report);
                    FilterForRevit(working, report);
                    CleanSolids(working, report, 0.1, 0.1);
                    ConvertMeshesToSolids(working, report, request.MaxDeviationMillimeters);
                }

                if (request.Profile == OptimizationProfile.Aggressive)
                {
                    ExplodeBlocks(working, report);
                    FilterForRevit(working, report);
                    ReduceSubdividedMeshes(working, report, request.MaxDeviationMillimeters);
                    MergeIntersectingSolids(working, report, 1.0, request.MaxDeviationMillimeters);
                }

                if (request.NormalizeOrigin && before.Bounds.IsValid && before.Findings.Any(x => x.Code == "FAR_FROM_ORIGIN"))
                {
                    NormalizeOrigin(working, before.Bounds, report);
                }

                Purge(working, report);
                working.SaveAs(outputPath, DwgVersion.Current);
            }

            report.OutputSizeBytes = new FileInfo(outputPath).Length;
            report.OutputSha256 = DwgAnalyzer.ComputeSha256(outputPath);
            using (var resultDatabase = new Database(false, true))
            {
                resultDatabase.ReadDwgFile(outputPath, FileOpenMode.OpenForReadAndAllShare, false, null);
                report.After = _analyzer.Analyze(resultDatabase, outputPath);
            }

            ValidateOutput(before, request, report);
            if (File.Exists(sourcePath) && !string.IsNullOrWhiteSpace(before.SourceSha256))
            {
                string sourceHashAfter = DwgAnalyzer.ComputeSha256(sourcePath);
                if (!string.Equals(before.SourceSha256, sourceHashAfter, StringComparison.OrdinalIgnoreCase))
                    report.Errors.Add("Контрольная сумма исходного DWG изменилась; результат помечен ошибкой.");
            }
            report.Success = report.Errors.Count == 0;
            report.CompletedAtUtc = DateTime.UtcNow;
            ReportWriter.Write(report);
            return report;
        }

        private static void ValidateOutput(AnalysisReport before, OptimizationRequest request, OptimizationReport report)
        {
            if (report.After == null)
            {
                report.Errors.Add("Не удалось повторно открыть и проверить выходной DWG.");
                return;
            }
            if (report.After.Findings.Any(x => x.Code == "NEEDS_RECOVERY"))
                report.Errors.Add("Выходной DWG требует RECOVER.");
            if (!request.NormalizeOrigin && request.Profile == OptimizationProfile.Safe
                && before.Bounds.IsValid && report.After.Bounds.IsValid)
            {
                double allowedMm = 0.001;
                double allowed = GeometrySupport.MillimetersToDrawingUnits(allowedMm,
                    ParseUnits(report.After.Units));
                if (BoundsDeviation(before.Bounds, report.After.Bounds) > allowed)
                    report.Errors.Add("Итоговые габариты вышли за допуск профиля " + allowedMm + " мм.");
            }
        }

        private static UnitsValue ParseUnits(string value)
        {
            UnitsValue units;
            return Enum.TryParse(value, true, out units) ? units : UnitsValue.Undefined;
        }

        private static void Audit(Database database, OptimizationReport report)
        {
            if (database.NeedsRecovery)
            {
                report.Operations.Add(Skipped(
                    "AUDIT",
                    "Проверить целостность базы DWG",
                    "Database.NeedsRecovery=true. Запустите RECOVER в AutoCAD; публичный Database API не позволяет безопасно выполнить полный AUDIT над side database."));
            }
            else
            {
                report.Operations.Add(Applied(
                    "AUDIT",
                    "Проверить целостность базы DWG",
                    1,
                    "Database.NeedsRecovery=false; структурный анализ объектов выполнен."));
            }
        }

        private static void BindXrefs(Database database, OptimizationReport report, bool allowMissing)
        {
            var ids = new ObjectIdCollection();
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in blockTable)
                {
                    var block = transaction.GetObject(id, OpenMode.ForRead, false) as BlockTableRecord;
                    if (block != null && block.IsFromExternalReference && block.XrefStatus == XrefStatus.Resolved)
                    {
                        ids.Add(id);
                    }
                }
                transaction.Commit();
            }

            if (ids.Count == 0)
            {
                report.Operations.Add(Skipped("BIND_XREF", "Включить XREF", "Разрешённые XREF отсутствуют."));
                return;
            }

            try
            {
                database.BindXrefs(ids, true);
                report.Operations.Add(Applied("BIND_XREF", "Включить XREF", ids.Count, "XREF привязаны с сохранением префиксов слоёв."));
            }
            catch (Exception ex)
            {
                if (!allowMissing) throw;
                report.Operations.Add(Skipped("BIND_XREF", "Включить XREF", ex.Message));
            }
        }

        private static void FilterForRevit(Database database, OptimizationReport report)
        {
            int erased = 0;
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
                var modelSpace = (BlockTableRecord)transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in modelSpace)
                {
                    var entity = transaction.GetObject(id, OpenMode.ForRead, false) as Entity;
                    if (entity == null || GeometrySupport.IsUseful3d(entity)) continue;
                    entity.UpgradeOpen();
                    entity.Erase();
                    erased++;
                }
                transaction.Commit();
            }

            report.Operations.Add(Applied("FILTER_3D", "Оставить полезную 3D-геометрию Model Space", erased, "Удалены 2D, аннотации, изображения и неподдерживаемые объекты из выходной копии."));
        }

        private static void RemovePaperSpaceLayouts(Database database, OptimizationReport report)
        {
            int layoutsFound = 0;
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                var layouts = (DBDictionary)transaction.GetObject(database.LayoutDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry entry in layouts)
                {
                    var layout = transaction.GetObject(entry.Value, OpenMode.ForRead, false) as Layout;
                    if (layout == null || layout.ModelType) continue;
                    layoutsFound++;
                }
                transaction.Commit();
            }
            report.Operations.Add(Skipped("REMOVE_LAYOUTS", "Исключить Paper Space",
                "Обработка ограничена Model Space. Листы сохранены в копии (" + layoutsFound
                + "), поскольку физическое удаление Layout через side Database может повредить таблицу блоков AutoCAD."));
        }

        private static void CleanSolids(Database database, OptimizationReport report, double maxVolumeDeviationPercent, double maxBoundsDeviationMm)
        {
            int cleaned = 0;
            int rolledBack = 0;
            double tolerance = GeometrySupport.MillimetersToDrawingUnits(maxBoundsDeviationMm, database.Insunits);
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                foreach (Solid3d solid in GetModelSpaceEntities<Solid3d>(database, transaction))
                {
                    try
                    {
                        double beforeVolume = solid.MassProperties.Volume;
                        BoundsInfo beforeBounds = GeometrySupport.ToBounds(solid.GeometricExtents, Matrix3d.Identity);
                        using (var candidate = (Solid3d)solid.Clone())
                        {
                            candidate.CleanBody();
                            double afterVolume = candidate.MassProperties.Volume;
                            double deviation = PercentDeviation(beforeVolume, afterVolume);
                            BoundsInfo afterBounds = GeometrySupport.ToBounds(candidate.GeometricExtents, Matrix3d.Identity);
                            if (deviation <= maxVolumeDeviationPercent && BoundsDeviation(beforeBounds, afterBounds) <= tolerance)
                            {
                                solid.UpgradeOpen();
                                solid.CopyFrom(candidate);
                                cleaned++;
                            }
                            else
                            {
                                rolledBack++;
                            }
                        }
                    }
                    catch
                    {
                        rolledBack++;
                    }
                }
                transaction.Commit();
            }

            report.Operations.Add(new OperationResult
            {
                Code = "CLEAN_SOLID",
                Description = "Очистить ACIS-тела",
                Applied = cleaned > 0,
                RolledBack = rolledBack > 0,
                AffectedObjects = cleaned,
                Message = "Очищено: " + cleaned + "; отклонено проверкой: " + rolledBack + "."
            });
        }

        private static void ConvertMeshesToSolids(Database database, OptimizationReport report, double maxDeviationMm)
        {
            int converted = 0;
            int skipped = 0;
            const int maxFacesPerMesh = 25000;
            const long maxModelFacesForAutomaticConversion = 250000;
            const long conversionFaceBudget = 75000;
            long attemptedFaces = 0;
            double tolerance = GeometrySupport.MillimetersToDrawingUnits(maxDeviationMm, database.Insunits);
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
                var modelSpace = (BlockTableRecord)transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                List<SubDMesh> meshes = GetModelSpaceEntities<SubDMesh>(database, transaction).ToList();
                long totalFaces = meshes.Sum(x => (long)x.NumberOfFaces);
                if (totalFaces > maxModelFacesForAutomaticConversion)
                {
                    skipped = meshes.Count;
                    transaction.Commit();
                    report.Operations.Add(Skipped("MESH_TO_SOLID", "Преобразовать замкнутые Mesh в Solid",
                        "Набор содержит " + totalFaces.ToString("N0") + " граней. Автоматический Mesh→Solid пропущен для сохранения быстродействия; исходные Mesh сохранены."));
                    return;
                }

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                foreach (SubDMesh mesh in meshes.OrderBy(x => x.NumberOfFaces))
                {
                    int faces = mesh.NumberOfFaces;
                    if (!mesh.Watertight || faces > maxFacesPerMesh
                        || attemptedFaces + faces > conversionFaceBudget || stopwatch.Elapsed > TimeSpan.FromSeconds(20))
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        attemptedFaces += faces;
                        BoundsInfo beforeBounds = GeometrySupport.ToBounds(mesh.GeometricExtents, Matrix3d.Identity);
                        Solid3d solid = mesh.ConvertToSolid(false, true);
                        BoundsInfo afterBounds = GeometrySupport.ToBounds(solid.GeometricExtents, Matrix3d.Identity);
                        if (BoundsDeviation(beforeBounds, afterBounds) > tolerance)
                        {
                            solid.Dispose();
                            skipped++;
                            continue;
                        }

                        solid.SetPropertiesFrom(mesh);
                        modelSpace.AppendEntity(solid);
                        transaction.AddNewlyCreatedDBObject(solid, true);
                        mesh.UpgradeOpen();
                        mesh.Erase();
                        converted++;
                    }
                    catch
                    {
                        skipped++;
                    }
                }
                transaction.Commit();
            }

            report.Operations.Add(new OperationResult
            {
                Code = "MESH_TO_SOLID",
                Description = "Преобразовать замкнутые Mesh в Solid",
                Applied = converted > 0,
                RolledBack = skipped > 0,
                AffectedObjects = converted,
                Message = "Преобразовано: " + converted + "; оставлено Mesh: " + skipped + "."
            });
        }

        private static void ExplodeBlocks(Database database, OptimizationReport report)
        {
            int explodedCount = 0;
            for (int pass = 0; pass < 64; pass++)
            {
                int passCount = 0;
                using (Transaction transaction = database.TransactionManager.StartTransaction())
                {
                    var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
                    var modelSpace = (BlockTableRecord)transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    foreach (BlockReference block in GetModelSpaceEntities<BlockReference>(database, transaction).ToList())
                    {
                        try
                        {
                            var exploded = new DBObjectCollection();
                            block.Explode(exploded);
                            foreach (DBObject item in exploded)
                            {
                                var entity = item as Entity;
                                if (entity == null) { item.Dispose(); continue; }
                                modelSpace.AppendEntity(entity);
                                transaction.AddNewlyCreatedDBObject(entity, true);
                            }
                            block.UpgradeOpen();
                            block.Erase();
                            passCount++;
                        }
                        catch
                        {
                            // Unsupported or dynamic blocks remain intact.
                        }
                    }
                    transaction.Commit();
                }

                explodedCount += passCount;
                if (passCount == 0) break;
            }

            report.Operations.Add(Applied("EXPLODE_BLOCKS", "Раскрыть вложенные блоки", explodedCount, "Раскрыты поддерживаемые BlockReference в Model Space."));
        }

        private static void ReduceSubdividedMeshes(Database database, OptimizationReport report, double maxDeviationMm)
        {
            int reduced = 0;
            int rejected = 0;
            double tolerance = GeometrySupport.MillimetersToDrawingUnits(maxDeviationMm, database.Insunits);
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                foreach (SubDMesh mesh in GetModelSpaceEntities<SubDMesh>(database, transaction))
                {
                    if (mesh.SmoothLevel <= 0 || mesh.NumberOfSubDividedFaces <= mesh.NumberOfFaces)
                    {
                        continue;
                    }

                    try
                    {
                        BoundsInfo before = GeometrySupport.ToBounds(mesh.GeometricExtents, Matrix3d.Identity);
                        using (var candidate = new SubDMesh())
                        {
                            candidate.SetSubDMesh(mesh.Vertices, mesh.FaceArray, 0);
                            BoundsInfo after = GeometrySupport.ToBounds(candidate.GeometricExtents, Matrix3d.Identity);
                            if (BoundsDeviation(before, after) <= tolerance)
                            {
                                mesh.UpgradeOpen();
                                mesh.SetSubDMesh(candidate.Vertices, candidate.FaceArray, 0);
                                reduced++;
                            }
                            else
                            {
                                rejected++;
                            }
                        }
                    }
                    catch
                    {
                        rejected++;
                    }
                }
                transaction.Commit();
            }

            report.Operations.Add(new OperationResult
            {
                Code = "REDUCE_MESH",
                Description = "Упростить сглаженные Mesh",
                Applied = reduced > 0,
                RolledBack = rejected > 0,
                AffectedObjects = reduced,
                Message = "Упрощено: " + reduced + "; отклонено по допуску: " + rejected + "."
            });
        }

        private static void MergeIntersectingSolids(Database database, OptimizationReport report, double maxVolumeDeviationPercent, double maxBoundsDeviationMm)
        {
            int merged = 0;
            double tolerance = GeometrySupport.MillimetersToDrawingUnits(maxBoundsDeviationMm, database.Insunits);
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                List<Solid3d> solids = GetModelSpaceEntities<Solid3d>(database, transaction).ToList();
                for (int i = 0; i < solids.Count; i++)
                {
                    Solid3d first = solids[i];
                    if (first.IsErased) continue;
                    for (int j = i + 1; j < solids.Count; j++)
                    {
                        Solid3d second = solids[j];
                        if (second.IsErased || first.LayerId != second.LayerId || first.MaterialId != second.MaterialId) continue;
                        try
                        {
                            if (!first.CheckInterference(second)) continue;
                            double expectedVolume = first.MassProperties.Volume + second.MassProperties.Volume;
                            BoundsInfo expectedBounds = new BoundsInfo();
                            GeometrySupport.Merge(expectedBounds, GeometrySupport.ToBounds(first.GeometricExtents, Matrix3d.Identity));
                            GeometrySupport.Merge(expectedBounds, GeometrySupport.ToBounds(second.GeometricExtents, Matrix3d.Identity));
                            using (var candidate = (Solid3d)first.Clone())
                            using (var operand = (Solid3d)second.Clone())
                            {
                                candidate.BooleanOperation(BooleanOperationType.BoolUnite, operand);
                                double actual = candidate.MassProperties.Volume;
                                BoundsInfo actualBounds = GeometrySupport.ToBounds(candidate.GeometricExtents, Matrix3d.Identity);
                                if (PercentDeviation(expectedVolume, actual) > maxVolumeDeviationPercent
                                    || BoundsDeviation(expectedBounds, actualBounds) > tolerance) continue;
                                first.UpgradeOpen();
                                first.CopyFrom(candidate);
                                second.UpgradeOpen();
                                second.Erase();
                                merged++;
                            }
                        }
                        catch
                        {
                            // Invalid boolean candidates remain separate.
                        }
                    }
                }
                transaction.Commit();
            }

            report.Operations.Add(Applied("UNION_SOLIDS", "Объединить пересекающиеся Solid одного слоя и материала", merged, "Объединены только валидированные пары."));
        }

        private static void NormalizeOrigin(Database database, BoundsInfo bounds, OptimizationReport report)
        {
            double shiftX = -(bounds.MinX + bounds.MaxX) * 0.5;
            double shiftY = -(bounds.MinY + bounds.MaxY) * 0.5;
            double shiftZ = -bounds.MinZ;
            var displacement = Matrix3d.Displacement(new Vector3d(shiftX, shiftY, shiftZ));
            int moved = 0;
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                foreach (Entity entity in GetModelSpaceEntities<Entity>(database, transaction))
                {
                    try
                    {
                        entity.UpgradeOpen();
                        entity.TransformBy(displacement);
                        moved++;
                    }
                    catch
                    {
                        // Report final validation will expose unmoved invalid entities.
                    }
                }
                transaction.Commit();
            }

            report.ShiftX = shiftX;
            report.ShiftY = shiftY;
            report.ShiftZ = shiftZ;
            report.Operations.Add(Applied("NORMALIZE_ORIGIN", "Перенести нижний центр габаритов в 0,0,0", moved, "Вектор записан в JSON-отчёт."));
        }

        private static void Purge(Database database, OptimizationReport report)
        {
            int purged = 0;
            for (int pass = 0; pass < 16; pass++)
            {
                var candidates = new ObjectIdCollection();
                using (Transaction transaction = database.TransactionManager.StartTransaction())
                {
                    AddTableEntries(database.BlockTableId, transaction, candidates, id =>
                    {
                        var btr = transaction.GetObject(id, OpenMode.ForRead, false) as BlockTableRecord;
                        return btr != null && !btr.IsLayout && !btr.IsFromExternalReference;
                    });
                    AddTableEntries(database.LayerTableId, transaction, candidates, null);
                    AddTableEntries(database.LinetypeTableId, transaction, candidates, null);
                    AddTableEntries(database.TextStyleTableId, transaction, candidates, null);
                    AddTableEntries(database.DimStyleTableId, transaction, candidates, null);
                    AddTableEntries(database.RegAppTableId, transaction, candidates, null);
                    transaction.Commit();
                }

                database.Purge(candidates);
                if (candidates.Count == 0) break;
                using (Transaction transaction = database.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in candidates)
                    {
                        try
                        {
                            DBObject value = transaction.GetObject(id, OpenMode.ForWrite, false);
                            if (value != null && !value.IsErased) { value.Erase(); purged++; }
                        }
                        catch { }
                    }
                    transaction.Commit();
                }
            }

            report.Operations.Add(Applied("PURGE", "Удалить неиспользуемые определения и RegApps", purged, "PURGE повторён до устойчивого состояния."));
        }

        private static void AddTableEntries(ObjectId tableId, Transaction transaction, ObjectIdCollection result, Func<ObjectId, bool> predicate)
        {
            var table = transaction.GetObject(tableId, OpenMode.ForRead, false) as SymbolTable;
            if (table == null) return;
            foreach (ObjectId id in DwgAnalyzer.GetValidSymbolIds(table))
            {
                if (id.IsErased) continue;
                try
                {
                    if (predicate == null || predicate(id)) result.Add(id);
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    if (ex.ErrorStatus != Autodesk.AutoCAD.Runtime.ErrorStatus.WasErased) throw;
                }
            }
        }

        private static IEnumerable<T> GetModelSpaceEntities<T>(Database database, Transaction transaction) where T : Entity
        {
            var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
            var modelSpace = (BlockTableRecord)transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId id in modelSpace)
            {
                T value = transaction.GetObject(id, OpenMode.ForRead, false) as T;
                if (value != null && !value.IsErased) yield return value;
            }
        }

        private static double PercentDeviation(double before, double after)
        {
            if (Math.Abs(before) < 1e-12) return Math.Abs(after) < 1e-12 ? 0 : 100;
            return Math.Abs(after - before) / Math.Abs(before) * 100.0;
        }

        private static double BoundsDeviation(BoundsInfo first, BoundsInfo second)
        {
            if (first == null || second == null || !first.IsValid || !second.IsValid) return double.MaxValue;
            return new[]
            {
                Math.Abs(first.MinX - second.MinX), Math.Abs(first.MinY - second.MinY), Math.Abs(first.MinZ - second.MinZ),
                Math.Abs(first.MaxX - second.MaxX), Math.Abs(first.MaxY - second.MaxY), Math.Abs(first.MaxZ - second.MaxZ)
            }.Max();
        }

        private static OperationResult Applied(string code, string description, int count, string message)
        {
            return new OperationResult { Code = code, Description = description, Applied = count > 0, AffectedObjects = count, Message = message };
        }

        private static OperationResult Skipped(string code, string description, string message)
        {
            return new OperationResult { Code = code, Description = description, Applied = false, Message = message };
        }
    }
}
