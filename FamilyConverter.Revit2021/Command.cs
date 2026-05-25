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
    public class Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            var logger = new LoggingService();
            ProgressWindow progressWindow = null;

            try
            {
                logger.Info("Запуск команды Simple Convert.");
                var selectionService = new SelectionService();
                ImportInstance importInstance;
                string validationMessage;

                if (!selectionService.TryGetSingleImportInstance(uidoc, out importInstance, out validationMessage))
                {
                    TaskDialog.Show(ProductInfo.Name, validationMessage);
                    return Result.Cancelled;
                }

                Document doc = uidoc.Document;
                logger.Info("Revit: " + uiapp.Application.VersionName + ", ImportInstanceId: " + importInstance.Id.IntegerValue);

                ConversionOptions defaults = ConversionOptions.CreateDefaults();
                var extraction = new GeometryExtractionService(new LayerService(), new UnitService());
                progressWindow = CreateProgressWindow(uiapp);
                progressWindow.SetActive(0, "Анализируем выбранный DWG");
                var previewObjects = extraction.Extract(doc, importInstance, defaults);
                progressWindow.Complete(0);
                progressWindow.Close();
                progressWindow = null;
                logger.Info("Предварительно найдено объектов геометрии: " + previewObjects.Count);

                var mainWindow = new MainWindow(importInstance, previewObjects, defaults, new AiConfigService());
                new System.Windows.Interop.WindowInteropHelper(mainWindow).Owner = uiapp.MainWindowHandle;
                bool? dialogResult = mainWindow.ShowDialog();
                if (dialogResult != true)
                {
                    return Result.Cancelled;
                }

                ConversionOptions options = mainWindow.Options;
                progressWindow = CreateProgressWindow(uiapp);
                progressWindow.Complete(0);
                progressWindow.SetActive(1, "Извлекаем геометрию с текущими настройками");
                var geometryObjects = extraction.Extract(doc, importInstance, options);
                progressWindow.Complete(1);
                var conversionService = new ConversionService(
                    extraction,
                    new GeometryAnalysisService(new UnitService()),
                    new ExtrusionCreationService(new SubcategoryService()),
                    new FreeFormCreationService(new SubcategoryService()),
                    new ReportService(new UnitService()),
                    new AiConfigService(),
                    logger);

                progressWindow.SetActive(2, "Создаем геометрию Revit и отчеты");
                ConversionSummary summary = conversionService.Convert(uiapp, importInstance, geometryObjects, options);
                progressWindow.Complete(2);
                progressWindow.SetActive(3, "Открываем отчет");
                progressWindow.Complete(3);
                progressWindow.Close();
                progressWindow = null;
                logger.Info("Конвертация завершена. JSON: " + (summary.JsonReportPath ?? "-") + ", CSV: " + (summary.CsvReportPath ?? "-"));

                var reportWindow = new ReportWindow(summary);
                new System.Windows.Interop.WindowInteropHelper(reportWindow).Owner = uiapp.MainWindowHandle;
                reportWindow.ShowDialog();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                if (progressWindow != null)
                {
                    progressWindow.Close();
                }

                logger.Error("Критическая ошибка команды.", ex);
                message = ex.Message;
                TaskDialog.Show(ProductInfo.Name, "Команда завершилась с ошибкой:\n" + ex.Message);
                return Result.Failed;
            }
        }

        private static ProgressWindow CreateProgressWindow(UIApplication uiapp)
        {
            var progressWindow = new ProgressWindow();
            new System.Windows.Interop.WindowInteropHelper(progressWindow).Owner = uiapp.MainWindowHandle;
            progressWindow.Show();
            return progressWindow;
        }
    }
}
