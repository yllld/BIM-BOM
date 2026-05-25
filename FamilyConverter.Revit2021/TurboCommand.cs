using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FamilyConverter.Revit2021.Models;
using FamilyConverter.Revit2021.Services;

namespace FamilyConverter.Revit2021
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TurboCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            var logger = new LoggingService();

            try
            {
                var selectionService = new SelectionService();
                ImportInstance importInstance;
                string validationMessage;

                if (!selectionService.TryGetSingleImportInstance(uidoc, out importInstance, out validationMessage))
                {
                    TaskDialog.Show("Family Converter Turbo", validationMessage);
                    return Result.Cancelled;
                }

                TaskDialog dialog = new TaskDialog("Family Converter Turbo");
                dialog.MainInstruction = "Super Turbo FreeForm";
                dialog.MainContent =
                    "Режим для очень тяжелых DWG.\n\n" +
                    "- без предпросмотра;\n" +
                    "- без Extrusion и AI;\n" +
                    "- без анализа Curve/Mesh;\n" +
                    "- Solid сразу создаются как FreeFormElement;\n" +
                    "- проверка созданной геометрии отключена для скорости.\n\n" +
                    "Revit может не отвечать во время операции. Дождитесь завершения.";
                dialog.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
                dialog.DefaultButton = TaskDialogResult.Ok;

                if (dialog.Show() != TaskDialogResult.Ok)
                {
                    return Result.Cancelled;
                }

                Document doc = uidoc.Document;
                ConversionOptions options = ConversionOptions.CreateSuperTurboDefaults();
                var extraction = new GeometryExtractionService(new LayerService(), new UnitService());
                logger.Info("Super Turbo extraction started. ImportInstanceId: " + importInstance.Id.IntegerValue);

                var geometryObjects = extraction.Extract(doc, importInstance, options);
                logger.Info("Super Turbo solids collected: " + geometryObjects.Count);

                var conversionService = new ConversionService(
                    extraction,
                    new GeometryAnalysisService(new UnitService()),
                    new ExtrusionCreationService(new SubcategoryService()),
                    new FreeFormCreationService(new SubcategoryService()),
                    new ReportService(new UnitService()),
                    new AiConfigService(),
                    logger);

                ConversionSummary summary = conversionService.Convert(uiapp, importInstance, geometryObjects, options);

                TaskDialog.Show("Family Converter Turbo", BuildSummary(summary));
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                logger.Error("Super Turbo command failed.", ex);
                message = ex.Message;
                TaskDialog.Show("Family Converter Turbo", "Команда завершилась с ошибкой:\n" + ex.Message);
                return Result.Failed;
            }
        }

        private static string BuildSummary(ConversionSummary summary)
        {
            return string.Format(
                "Super Turbo завершен.\n\nFreeFormElement: {0}\nПропущено: {1}\nОшибки: {2}\nПредупреждения: {3}\n\nJSON: {4}\nCSV: {5}",
                summary.FreeFormCount,
                summary.SkippedCount,
                summary.FailedCount,
                summary.WarningCount,
                string.IsNullOrWhiteSpace(summary.JsonReportPath) ? "-" : summary.JsonReportPath,
                string.IsNullOrWhiteSpace(summary.CsvReportPath) ? "-" : summary.CsvReportPath);
        }
    }
}
