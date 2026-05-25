using System;
using System.Collections.Generic;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FamilyConverter.Revit2021.Models;
using FamilyConverter.Revit2021.Utils;

namespace FamilyConverter.Revit2021.Services
{
    public class ConversionService
    {
        private readonly GeometryExtractionService _extractionService;
        private readonly GeometryAnalysisService _analysisService;
        private readonly ExtrusionCreationService _extrusionService;
        private readonly FreeFormCreationService _freeFormService;
        private readonly ReportService _reportService;
        private readonly AiConfigService _aiConfigService;
        private readonly LoggingService _logger;

        public ConversionService(
            GeometryExtractionService extractionService,
            GeometryAnalysisService analysisService,
            ExtrusionCreationService extrusionService,
            FreeFormCreationService freeFormService,
            ReportService reportService,
            AiConfigService aiConfigService,
            LoggingService logger)
        {
            _extractionService = extractionService;
            _analysisService = analysisService;
            _extrusionService = extrusionService;
            _freeFormService = freeFormService;
            _reportService = reportService;
            _aiConfigService = aiConfigService;
            _logger = logger;
        }

        public ConversionSummary Convert(UIApplication uiapp, ImportInstance importInstance, IList<GeometryObjectInfo> geometryObjects, ConversionOptions options)
        {
            Document document = uiapp.ActiveUIDocument.Document;
            var summary = new ConversionSummary();

            if (geometryObjects == null || geometryObjects.Count == 0)
            {
                summary.Messages.Add("Геометрия DWG не содержит пригодных Solid-объектов.");
            }

            AiConfig aiConfig = null;
            IAiGeometryAdvisor aiAdvisor = new LocalRuleGeometryAdvisor();
            if (options.UseAiAdvisor)
            {
                string aiMessage;
                if (_aiConfigService.TryLoad(options.AiConfigPath, out aiConfig, out aiMessage))
                {
                    aiAdvisor = new HttpJsonAiGeometryAdvisor(aiConfig);
                    summary.Messages.Add("AI-советник включен: " + aiConfig.Provider + ".");
                }
                else
                {
                    summary.Messages.Add(aiMessage);
                    _logger.Warning(aiMessage);
                }
            }

            using (var group = new TransactionGroup(document, ProductInfo.Name))
            {
                group.Start();

                foreach (GeometryObjectInfo info in geometryObjects)
                {
                    ConversionResult result = CreateBaseResult(info);
                    summary.Results.Add(result);

                    try
                    {
                        if (info.Solid != null)
                        {
                            if (options.SuperTurboMode)
                            {
                                ProcessSolidSuperTurbo(document, info, options, result);
                            }
                            else
                            {
                                ProcessSolid(document, info, options, aiAdvisor, aiConfig, result);
                            }
                        }
                        else if (info.Mesh != null)
                        {
                            result.LocalClassification = GeometryClassification.MeshUnsupported;
                            result.FinalMethod = ConversionMethod.Skip;
                            result.Status = ConversionStatus.Skipped;
                            result.Message = "Mesh-геометрия в MVP не преобразуется и будет отражена в отчете.";
                        }
                        else if (info.Curve != null)
                        {
                            result.LocalClassification = GeometryClassification.Unknown;
                            result.FinalMethod = ConversionMethod.Skip;
                            result.Status = ConversionStatus.Skipped;
                            result.Message = "Curve-геометрия в MVP не преобразуется.";
                        }
                        else
                        {
                            result.LocalClassification = GeometryClassification.Unknown;
                            result.FinalMethod = ConversionMethod.Skip;
                            result.Status = ConversionStatus.Skipped;
                            result.Message = "UnknownGeometry: тип геометрии не поддержан в MVP.";
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Status = ConversionStatus.Failed;
                        result.FinalMethod = ConversionMethod.None;
                        result.Message = "Ошибка обработки объекта.";
                        result.Exception = ex.Message;
                        _logger.Error("Ошибка обработки " + info.ObjectId, ex);
                    }
                }

                TryDeleteSourceDwg(document, importInstance, options, summary);
                group.Assimilate();
            }

            try
            {
                _reportService.CreateReports(document, summary, options);
            }
            catch (Exception ex)
            {
                summary.Messages.Add("Не удалось создать отчет: " + ex.Message);
                _logger.Error("Ошибка создания отчета.", ex);
            }

            return summary;
        }

        private ConversionResult CreateBaseResult(GeometryObjectInfo info)
        {
            var result = new ConversionResult
            {
                ObjectId = info.ObjectId,
                LayerName = info.LayerName,
                SourceGeometryType = info.GeometryType,
                LocalClassification = GeometryClassification.Unknown,
                LocalConfidence = 0,
                FinalMethod = ConversionMethod.None,
                Status = ConversionStatus.Skipped,
                Source = info
            };

            foreach (string warning in info.Warnings)
            {
                result.Warnings.Add(warning);
            }

            return result;
        }

        private void ProcessSolid(
            Document document,
            GeometryObjectInfo info,
            ConversionOptions options,
            IAiGeometryAdvisor aiAdvisor,
            AiConfig aiConfig,
            ConversionResult result)
        {
            PrismaticCandidate candidate = _analysisService.Analyze(info, options);
            result.LocalClassification = candidate.Classification;
            result.LocalConfidence = candidate.Confidence;
            foreach (string warning in candidate.Warnings)
            {
                result.Warnings.Add(warning);
            }

            AiGeometryResponse aiResponse = null;
            bool useHttpAi = options.UseAiAdvisor && aiConfig != null && candidate.Confidence >= 0.50 && candidate.Confidence < options.MinExtrusionConfidence;
            if (useHttpAi)
            {
                try
                {
                    AiGeometryRequest request = _analysisService.CreateAiRequest(info, candidate);
                    aiResponse = aiAdvisor.AnalyzeAsync(request, CancellationToken.None).GetAwaiter().GetResult();
                    result.AiUsed = true;
                    result.AiProvider = aiResponse.provider;
                    result.AiModel = aiResponse.model;
                    result.AiRecommendation = aiResponse.recommended_method;
                    result.AiConfidence = aiResponse.confidence;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add("AI вернул ошибку или некорректный JSON. Продолжаем локально: " + ex.Message);
                    _logger.Warning("AI warning: " + ex.Message);
                }
            }

            ConversionMethod method = DecideMethod(candidate, options, aiResponse);
            if (method == ConversionMethod.Extrusion)
            {
                if (TryCreateExtrusion(document, info, candidate, options, result))
                {
                    return;
                }

                if (options.UseFreeFormFallback)
                {
                    result.Warnings.Add("Создание Extrusion не удалось, выполняется fallback в FreeFormElement.");
                    result.FallbackUsed = true;
                    TryCreateFreeForm(document, info, options, result);
                    return;
                }
            }

            if (method == ConversionMethod.FreeForm)
            {
                result.FallbackUsed = true;
                if (string.IsNullOrWhiteSpace(result.ExtrusionFailedReason))
                {
                    result.ExtrusionFailedReason = "Локальная классификация " + candidate.Classification + " с уверенностью " + candidate.Confidence.ToString("0.###") + " не прошла порог безопасного Extrusion " + options.MinExtrusionConfidence.ToString("0.###") + ".";
                }
                TryCreateFreeForm(document, info, options, result);
                return;
            }

            result.FinalMethod = ConversionMethod.Skip;
            result.Status = ConversionStatus.Skipped;
            result.Message = "Solid пропущен: профиль не признан безопасным, FreeForm fallback выключен или рекомендация требует Skip.";
        }

        private void ProcessSolidSuperTurbo(Document document, GeometryObjectInfo info, ConversionOptions options, ConversionResult result)
        {
            result.LocalClassification = GeometryClassification.Complex;
            result.LocalConfidence = 0;
            result.FinalMethod = ConversionMethod.FreeForm;
            result.FallbackUsed = true;
            result.ExtrusionFailedReason = "Turbo FreeForm: Extrusion and geometry analysis skipped. Solid is converted directly to FreeFormElement.";
            TryCreateFreeForm(document, info, options, result);
        }

        private static ConversionMethod DecideMethod(PrismaticCandidate candidate, ConversionOptions options, AiGeometryResponse aiResponse)
        {
            if (candidate == null || candidate.Classification == GeometryClassification.Invalid)
            {
                return options.UseFreeFormFallback ? ConversionMethod.FreeForm : ConversionMethod.Skip;
            }

            if (!options.CreateNativeExtrusions)
            {
                return options.UseFreeFormFallback ? ConversionMethod.FreeForm : ConversionMethod.Skip;
            }

            if (candidate.Classification == GeometryClassification.CylinderLike
                || candidate.Classification == GeometryClassification.Complex)
            {
                return options.UseFreeFormFallback ? ConversionMethod.FreeForm : ConversionMethod.Skip;
            }

            if (candidate.Classification == GeometryClassification.ProfileLike
                && candidate.Confidence < options.MinExtrusionConfidence)
            {
                if (options.TryExtrusionBeforeFreeForm && candidate.IsProfileSafe && candidate.Confidence >= 0.50)
                {
                    return ConversionMethod.Extrusion;
                }

                return options.UseFreeFormFallback ? ConversionMethod.FreeForm : ConversionMethod.Skip;
            }

            if (aiResponse != null && aiResponse.confidence >= 0.80)
            {
                string recommended = (aiResponse.recommended_method ?? string.Empty).ToLowerInvariant();
                if (recommended == "skip")
                {
                    return ConversionMethod.Skip;
                }
                if (recommended == "freeform")
                {
                    return options.UseFreeFormFallback ? ConversionMethod.FreeForm : ConversionMethod.Skip;
                }
                if (recommended == "extrusion" && candidate.IsProfileSafe && candidate.Confidence >= options.MinExtrusionConfidence)
                {
                    return ConversionMethod.Extrusion;
                }
            }

            if (options.CreateNativeExtrusions && candidate.IsProfileSafe && candidate.Confidence >= options.MinExtrusionConfidence)
            {
                return ConversionMethod.Extrusion;
            }

            return options.UseFreeFormFallback ? ConversionMethod.FreeForm : ConversionMethod.Skip;
        }

        private bool TryCreateExtrusion(Document document, GeometryObjectInfo info, PrismaticCandidate candidate, ConversionOptions options, ConversionResult result)
        {
            try
            {
                Element element = CreateInTransaction(document, ProductInfo.Name + ": Extrusion", () => _extrusionService.Create(document, info, candidate, options, false));
                CompleteCreatedResult(document, info, element, options, result, ConversionMethod.Extrusion, "Создан нативный Extrusion.");
                if (ShouldFallbackAfterExtrusionValidation(result, options))
                {
                    DeleteCreatedElement(document, element);
                    result.CreatedElementId = null;
                    result.ExtrusionFailedReason = BuildValidationFallbackReason(result);
                    result.Warnings.Add("Extrusion удален после проверки: геометрия отличается от исходного Solid сильнее допуска. Выполняется fallback в FreeFormElement.");
                    return false;
                }
                return true;
            }
            catch (Exception firstException)
            {
                result.Warnings.Add("Не удалось создать Extrusion по полному профилю: " + firstException.Message);
                result.ExtrusionFailedReason = firstException.Message;

                if (candidate.ProfileLoops.Count > 1)
                {
                    try
                    {
                        Element element = CreateInTransaction(document, ProductInfo.Name + ": Extrusion outer loop", () => _extrusionService.Create(document, info, candidate, options, true));
                        result.Warnings.Add("Внутренние контуры не были использованы: Revit не принял полный профиль.");
                        CompleteCreatedResult(document, info, element, options, result, ConversionMethod.Extrusion, "Создан нативный Extrusion по внешнему контуру.");
                        if (ShouldFallbackAfterExtrusionValidation(result, options))
                        {
                            DeleteCreatedElement(document, element);
                            result.CreatedElementId = null;
                            result.ExtrusionFailedReason = BuildValidationFallbackReason(result);
                            result.Warnings.Add("Extrusion по внешнему контуру удален после проверки: геометрия отличается от исходного Solid сильнее допуска. Выполняется fallback в FreeFormElement.");
                            return false;
                        }
                        return true;
                    }
                    catch (Exception secondException)
                    {
                        result.Warnings.Add("Не удалось создать Extrusion по внешнему контуру: " + secondException.Message);
                        result.ExtrusionFailedReason = secondException.Message;
                        result.Exception = secondException.Message;
                    }
                }
                else
                {
                    result.Exception = firstException.Message;
                }
            }

            return false;
        }

        private static bool ShouldFallbackAfterExtrusionValidation(ConversionResult result, ConversionOptions options)
        {
            return options.UseFreeFormFallback
                && result.Status == ConversionStatus.Warning
                && (result.ValidationBoundingBoxDeviationMm > options.BoundingBoxToleranceMm
                    || result.ValidationVolumeDeviationPercent > options.VolumeTolerancePercent);
        }

        private static string BuildValidationFallbackReason(ConversionResult result)
        {
            return "Extrusion не прошел валидацию: отклонение габаритов "
                + result.ValidationBoundingBoxDeviationMm.ToString("0.###")
                + " мм, отклонение объема "
                + result.ValidationVolumeDeviationPercent.ToString("0.###")
                + " %. Использован FreeForm fallback.";
        }

        private static void DeleteCreatedElement(Document document, Element element)
        {
            if (document == null || element == null || element.Id == ElementId.InvalidElementId)
            {
                return;
            }

            CreateInTransaction(document, ProductInfo.Name + ": rollback invalid Extrusion", () =>
            {
                document.Delete(element.Id);
                return null;
            });
        }

        private bool TryCreateFreeForm(Document document, GeometryObjectInfo info, ConversionOptions options, ConversionResult result)
        {
            try
            {
                Element element = CreateInTransaction(document, ProductInfo.Name + ": FreeFormElement", () => _freeFormService.Create(document, info, options));
                CompleteCreatedResult(document, info, element, options, result, ConversionMethod.FreeForm, "Создан FreeFormElement: геометрия перенесена как непараметрическая форма.");
                return true;
            }
            catch (Exception ex)
            {
                result.FinalMethod = ConversionMethod.FreeForm;
                result.Status = ConversionStatus.Failed;
                result.Message = "FreeFormElement создать не удалось.";
                result.Exception = ex.Message;
                _logger.Error("Ошибка создания FreeFormElement для " + info.ObjectId, ex);
                return false;
            }
        }

        private static Element CreateInTransaction(Document document, string name, Func<Element> create)
        {
            var transaction = new Transaction(document, name);
            transaction.Start();
            try
            {
                Element element = create();
                transaction.Commit();
                return element;
            }
            catch
            {
                try
                {
                    if (transaction.GetStatus() == TransactionStatus.Started)
                    {
                        transaction.RollBack();
                    }
                }
                catch
                {
                    // Nothing else can be done here.
                }

                throw;
            }
        }

        private static void CompleteCreatedResult(
            Document document,
            GeometryObjectInfo source,
            Element element,
            ConversionOptions options,
            ConversionResult result,
            ConversionMethod method,
            string successMessage)
        {
            result.FinalMethod = method;
            result.CreatedElementId = element == null ? null : element.Id;
            result.Message = successMessage;
            result.Exception = null;

            if (!options.ValidateCreatedGeometry)
            {
                result.ValidationBoundingBoxDeviationMm = 0;
                result.ValidationVolumeDeviationPercent = 0;
                result.Status = element == null ? ConversionStatus.Failed : ConversionStatus.Success;
                return;
            }

            BoundingBoxXYZ createdBox = element == null ? null : element.get_BoundingBox(null);
            double createdVolume = GeometryUtils.GetElementSolidVolume(element);
            result.ValidationBoundingBoxDeviationMm = GeometryUtils.BoundingBoxDeviationMm(source.BoundingBox, createdBox, new UnitService());
            result.ValidationVolumeDeviationPercent = GeometryUtils.VolumeDeviationPercent(source.VolumeFeet3, createdVolume);

            bool bboxOk = source.BoundingBox == null || result.ValidationBoundingBoxDeviationMm <= options.BoundingBoxToleranceMm;
            bool volumeOk = source.VolumeFeet3 <= 1e-12 || createdVolume <= 1e-12 || result.ValidationVolumeDeviationPercent <= options.VolumeTolerancePercent;

            if (bboxOk && volumeOk)
            {
                result.Status = ConversionStatus.Success;
            }
            else
            {
                result.Status = ConversionStatus.Warning;
                if (!bboxOk)
                {
                    result.Warnings.Add("Отклонение габаритов больше допуска: " + result.ValidationBoundingBoxDeviationMm.ToString("0.###") + " мм.");
                }
                if (!volumeOk)
                {
                    result.Warnings.Add("Отклонение объема больше допуска: " + result.ValidationVolumeDeviationPercent.ToString("0.###") + " %.");
                }
            }
        }

        private void TryDeleteSourceDwg(Document document, ImportInstance importInstance, ConversionOptions options, ConversionSummary summary)
        {
            if (!options.DeleteSourceDwgOnSuccess)
            {
                return;
            }

            bool canDelete = summary.HasCreatedElements
                && summary.FailedCount == 0
                && summary.HasCriticalWarnings == false
                && summary.SkippedCount == 0;

            if (!canDelete)
            {
                summary.Messages.Add("Исходный DWG не удален, так как часть объектов обработана с ошибками, предупреждениями или была пропущена.");
                return;
            }

            try
            {
                CreateInTransaction(document, ProductInfo.Name + ": удалить исходный DWG", () =>
                {
                    document.Delete(importInstance.Id);
                    return null;
                });
                summary.SourceDwgDeleted = true;
                summary.Messages.Add("Исходный DWG удален после успешного преобразования.");
            }
            catch (Exception ex)
            {
                summary.Messages.Add("Не удалось удалить исходный DWG: " + ex.Message);
                _logger.Error("Ошибка удаления ImportInstance.", ex);
            }
        }
    }
}
