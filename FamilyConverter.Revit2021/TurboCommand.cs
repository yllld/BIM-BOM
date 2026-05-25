using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FamilyConverter.Revit2021.Models;
using FamilyConverter.Revit2021.Services;
using FamilyConverter.Revit2021.UI;

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

                Document doc = uidoc.Document;
                ConversionOptions options = ConversionOptions.CreateSuperTurboDefaults();
                var settingsWindow = new TurboSettingsWindow(options);
                new System.Windows.Interop.WindowInteropHelper(settingsWindow).Owner = uiapp.MainWindowHandle;
                if (settingsWindow.ShowDialog() != true)
                {
                    return Result.Cancelled;
                }

                options = settingsWindow.Options;
                var extraction = new GeometryExtractionService(new LayerService(), new UnitService());
                logger.Info("Super Turbo extraction started. ImportInstanceId: " + importInstance.Id.IntegerValue
                    + ", MinVolumeMm3: " + options.MinSolidVolumeMm3
                    + ", MinMaxDimensionMm: " + options.MinSolidMaxDimensionMm);

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

                TaskDialog.Show("Family Converter Turbo", BuildSummary(summary, geometryObjects.Count, options));
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

        private static string BuildSummary(ConversionSummary summary, int collectedSolidCount, ConversionOptions options)
        {
            return string.Format(
                "Super Turbo завершен.\n\nПередано на FreeForm: {0}\nFreeFormElement: {1}\nПропущено: {2}\nОшибки: {3}\nПредупреждения: {4}\n\nПорог объема: {5:0.###} мм3\nПорог габарита: {6:0.###} мм\n\nJSON: {7}\nCSV: {8}",
                collectedSolidCount,
                summary.FreeFormCount,
                summary.SkippedCount,
                summary.FailedCount,
                summary.WarningCount,
                options.MinSolidVolumeMm3,
                options.MinSolidMaxDimensionMm,
                string.IsNullOrWhiteSpace(summary.JsonReportPath) ? "-" : summary.JsonReportPath,
                string.IsNullOrWhiteSpace(summary.CsvReportPath) ? "-" : summary.CsvReportPath);
        }
    }
}
