using System;
using System.IO;
using System.Linq;
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
    public class ImportConvertCommand : IExternalCommand
    {
        private const long LargeFileWarningBytes = 100L * 1024L * 1024L;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            var logger = new LoggingService();
            ProgressWindow progressWindow = null;
            Document targetDocument = uidoc == null ? null : uidoc.Document;
            Document sourceDocument = null;

            try
            {
                logger.Info("Import Convert command invoked.");

                if (targetDocument == null || !targetDocument.IsFamilyDocument)
                {
                    TaskDialog.Show(ProductInfo.Name, "Команда Import Convert работает только в открытом семействе Revit.");
                    return Result.Cancelled;
                }

                string filePath = SelectCadFile();
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    return Result.Cancelled;
                }

                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length >= LargeFileWarningBytes && !ConfirmLargeFile(fileInfo))
                {
                    return Result.Cancelled;
                }

                logger.Info("Import Convert started. File: " + fileInfo.Name + ", bytes: " + fileInfo.Length);
                progressWindow = CreateProgressWindow(uiapp);
                progressWindow.SetActive(0, "Импортируем файл САПР от начала координат");

                sourceDocument = uiapp.Application.NewProjectDocument(UnitSystem.Metric);
                View sourceView = FindOrCreateImportView(sourceDocument);
                var importService = new CadImportService();
                ImportInstance importInstance = importService.ImportAtOrigin(sourceDocument, sourceView, filePath);
                progressWindow.Complete(0);

                ConversionOptions defaults = ConversionOptions.CreateDefaults();
                defaults.DeleteSourceDwgOnSuccess = false;
                var extraction = new GeometryExtractionService(new LayerService(), new UnitService());
                progressWindow.SetActive(1, "Анализируем импортированную геометрию");
                var previewObjects = extraction.Extract(sourceDocument, importInstance, defaults);
                progressWindow.Complete(1);
                progressWindow.Close();
                progressWindow = null;

                if (previewObjects.Count == 0)
                {
                    TaskDialog.Show(
                        ProductInfo.Name,
                        "В выбранном файле не найдены Solid или Mesh, которые можно преобразовать. Служебный импорт будет удалён.");
                    return Result.Cancelled;
                }

                var mainWindow = new MainWindow(importInstance, previewObjects, defaults, new AiConfigService());
                new System.Windows.Interop.WindowInteropHelper(mainWindow).Owner = uiapp.MainWindowHandle;
                if (mainWindow.ShowDialog() != true)
                {
                    return Result.Cancelled;
                }

                ConversionOptions options = mainWindow.Options;
                options.DeleteSourceDwgOnSuccess = false;
                progressWindow = CreateProgressWindow(uiapp);
                progressWindow.Complete(0);
                progressWindow.SetActive(1, "Извлекаем геометрию с выбранными настройками");
                var geometryObjects = extraction.Extract(sourceDocument, importInstance, options);
                progressWindow.Complete(1);

                var conversionService = new ConversionService(
                    extraction,
                    new GeometryAnalysisService(new UnitService()),
                    new ExtrusionCreationService(new SubcategoryService()),
                    new FreeFormCreationService(new SubcategoryService()),
                    new ReportService(new UnitService()),
                    new AiConfigService(),
                    logger);

                progressWindow.SetActive(2, "Строим геометрию Revit от начала координат");
                ConversionSummary summary = conversionService.Convert(targetDocument, importInstance, geometryObjects, options);
                progressWindow.Complete(2);

                foreach (ConversionResult result in summary.Results)
                {
                    result.Source = null;
                }

                previewObjects.Clear();
                geometryObjects.Clear();

                string cleanupError;
                if (TryCloseTemporaryDocument(sourceDocument, out cleanupError))
                {
                    sourceDocument = null;
                    summary.SourceDwgDeleted = true;
                    summary.Messages.Add("Временный документ импорта закрыт без сохранения.");
                }
                else
                {
                    summary.Messages.Add(cleanupError);
                }

                progressWindow.SetActive(3, "Открываем отчёт");
                progressWindow.Complete(3);
                progressWindow.Close();
                progressWindow = null;

                var reportWindow = new ReportWindow(summary);
                new System.Windows.Interop.WindowInteropHelper(reportWindow).Owner = uiapp.MainWindowHandle;
                reportWindow.ShowDialog();
                logger.Info("Import Convert completed. File: " + fileInfo.Name);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                logger.Error("Import Convert failed.", ex);
                message = ex.Message;
                TaskDialog.Show(ProductInfo.Name, "Import Convert завершился с ошибкой:\n" + ex.Message);
                return Result.Failed;
            }
            finally
            {
                if (progressWindow != null)
                {
                    progressWindow.Close();
                }

                if (sourceDocument != null)
                {
                    string cleanupError;
                    if (!TryCloseTemporaryDocument(sourceDocument, out cleanupError))
                    {
                        logger.Warning(cleanupError);
                        TaskDialog.Show(ProductInfo.Name, cleanupError);
                    }
                }
            }
        }

        private static string SelectCadFile()
        {
            const string filter = "Поддерживаемые файлы (*.dwg;*.dxf;*.sat)|*.dwg;*.dxf;*.sat|AutoCAD (*.dwg;*.dxf)|*.dwg;*.dxf|ACIS SAT (*.sat)|*.sat";
            using (var dialog = new Autodesk.Revit.UI.FileOpenDialog(filter))
            {
                dialog.Title = "Import Convert — выбрать файл САПР";
                dialog.ShowPreview = false;
                if (dialog.Show() != ItemSelectionDialogResult.Confirmed)
                {
                    return null;
                }

                ModelPath selectedPath = dialog.GetSelectedModelPath();
                return selectedPath == null
                    ? null
                    : ModelPathUtils.ConvertModelPathToUserVisiblePath(selectedPath);
            }
        }

        private static bool ConfirmLargeFile(FileInfo fileInfo)
        {
            var dialog = new TaskDialog(ProductInfo.Name)
            {
                MainInstruction = "Выбран большой файл САПР",
                MainContent = string.Format(
                    "Размер файла: {0:0.0} МБ.\n\nMVP обрабатывает файл внутри текущего Revit. Во время импорта Revit может долго не отвечать и потреблять много памяти. Продолжить?",
                    fileInfo.Length / 1024d / 1024d),
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No
            };

            return dialog.Show() == TaskDialogResult.Yes;
        }

        private static View FindOrCreateImportView(Document document)
        {
            View3D view = new FilteredElementCollector(document)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(x => !x.IsTemplate);

            if (view != null)
            {
                return view;
            }

            ViewFamilyType viewFamilyType = new FilteredElementCollector(document)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);

            if (viewFamilyType == null)
            {
                throw new InvalidOperationException("Во временном документе Revit не найден тип 3D-вида для импорта.");
            }

            using (var transaction = new Transaction(document, ProductInfo.Name + ": создать служебный 3D-вид"))
            {
                transaction.Start();
                view = View3D.CreateIsometric(document, viewFamilyType.Id);
                transaction.Commit();
            }

            return view;
        }

        private static bool TryCloseTemporaryDocument(Document document, out string error)
        {
            error = null;
            if (document == null)
            {
                return true;
            }

            try
            {
                if (!document.Close(false))
                {
                    error = "Revit не смог закрыть временный документ импорта. Закройте несохранённый проект вручную.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = "Не удалось закрыть временный документ импорта: " + ex.Message;
                return false;
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
