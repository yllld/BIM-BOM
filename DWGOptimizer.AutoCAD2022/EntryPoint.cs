using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;
using DWGOptimizer.Contracts;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: ExtensionApplication(typeof(DWGOptimizer.AutoCAD2022.EntryPoint))]
[assembly: CommandClass(typeof(DWGOptimizer.AutoCAD2022.Commands))]

namespace DWGOptimizer.AutoCAD2022
{
    public sealed class EntryPoint : IExtensionApplication
    {
        public void Initialize()
        {
            try { RibbonBuilder.EnsureRibbon(); } catch { }
            AcApp.Idle += OnIdle;
        }

        public void Terminate() { AcApp.Idle -= OnIdle; }

        private static void OnIdle(object sender, EventArgs e)
        {
            AcApp.Idle -= OnIdle;
            try { RibbonBuilder.EnsureRibbon(); } catch { }
        }
    }

    public sealed class Commands
    {
        [CommandMethod(ProductInfo.CommandAnalyze, CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void AnalyzeAndOptimize() { RunInteractive(); }

        internal static void RunInteractive()
        {
            Document document = AcApp.DocumentManager.MdiActiveDocument;
            if (document == null) return;
            Editor editor = document.Editor;
            string sourcePath = document.Name;
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                editor.WriteMessage("\nСначала сохраните текущий DWG.");
                return;
            }

            try
            {
                using (Database selected = SelectionSnapshot.TryCreate(document))
                {
                    Database source = selected ?? document.Database;
                    var analysis = new DwgAnalyzer().Analyze(source, sourcePath);
                    analysis.Scope = selected == null ? "ModelSpace" : "Selection";
                    var window = new AnalysisWindow(analysis);
                    if (AcApp.ShowModalWindow(window) != true) return;
                    OptimizationReport result = new DwgOptimizerService().Optimize(source, sourcePath, analysis, window.Request);
                    if (!result.Success)
                    {
                        AcApp.ShowAlertDialog(ProductInfo.Name + ":\n" + string.Join("\n", result.Errors));
                        return;
                    }
                    AcApp.ShowAlertDialog("RevitReady-копия создана:\n" + result.OutputPath + "\n\nОтчёт:\n" + result.HtmlReportPath);
                }
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage("\nDWG Revit Optimizer: " + ex);
                AcApp.ShowAlertDialog("Ошибка оптимизации: " + ex.Message);
            }
        }

        [CommandMethod(ProductInfo.CommandBatch, CommandFlags.Modal)]
        public void BatchQueue()
        {
            using (var dialog = new System.Windows.Forms.OpenFileDialog
            {
                Filter = "AutoCAD DWG (*.dwg)|*.dwg",
                Multiselect = true,
                Title = "DWG Revit Optimizer — пакетная очередь"
            })
            {
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                var options = new BatchOptionsWindow();
                if (AcApp.ShowModalWindow(options) != true) return;
                string installDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string queueDir = Path.Combine(OutputPathService.GetQueuesDirectory(),
                    DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(queueDir);
                string workerPlugin = Path.Combine(installDir, "DWGOptimizer.CoreConsole2022.dll");
                if (!File.Exists(workerPlugin)) throw new FileNotFoundException("Не найден Core Console worker.", workerPlugin);
                var manifest = new BatchManifest
                {
                    PluginPath = workerPlugin,
                    AutoCadCoreConsolePath = @"C:\Program Files\Autodesk\AutoCAD 2022\accoreconsole.exe"
                };
                foreach (string file in dialog.FileNames)
                {
                    string id = Guid.NewGuid().ToString("N");
                    manifest.Jobs.Add(new BatchJob
                    {
                        Id = id,
                        SourcePath = file,
                        StatusPath = Path.Combine(queueDir, id + ".status.json"),
                        Request = options.Request
                    });
                }
                string manifestPath = Path.Combine(queueDir, "batch.json");
                JsonFile.Write(manifestPath, manifest);
                string runner = Path.Combine(installDir, "DWGOptimizer.BatchRunner.exe");
                if (!File.Exists(runner)) throw new FileNotFoundException("Не найден обработчик пакетной очереди.", runner);
                Process.Start(new ProcessStartInfo(runner, "\"" + manifestPath + "\"") { UseShellExecute = true });
                AcApp.ShowModelessWindow(new BatchMonitorWindow(manifest, manifestPath));
            }
        }

        [CommandMethod(ProductInfo.CommandReports, CommandFlags.Modal)]
        public void Reports()
        {
            string path = OutputPathService.GetReportsDirectory();
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
        }

    }

    internal static class SelectionSnapshot
    {
        public static Database TryCreate(Document document)
        {
            PromptSelectionResult selection = document.Editor.SelectImplied();
            if (selection.Status != PromptStatus.OK || selection.Value.Count == 0) return null;
            var ids = new ObjectIdCollection(selection.Value.GetObjectIds());
            return document.Database.Wblock(ids, Autodesk.AutoCAD.Geometry.Point3d.Origin);
        }
    }

    internal static class RibbonBuilder
    {
        private static readonly BitmapImage SmallIcon = LoadIcon(16);
        private static readonly BitmapImage LargeIcon = LoadIcon(32);

        public static void EnsureRibbon()
        {
            RibbonControl ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;
            RibbonTab tab = ribbon.Tabs.FirstOrDefault(x => x.Id == "BIM_BOM_DWG_OPTIMIZER");
            if (tab == null)
            {
                tab = new RibbonTab { Title = "BIM BOM", Id = "BIM_BOM_DWG_OPTIMIZER" };
                ribbon.Tabs.Add(tab);
            }
            if (tab.Panels.Any(x => x.Source != null && x.Source.Id == "BIM_BOM_REVIT_PREP")) return;
            var panelSource = new RibbonPanelSource { Title = "Revit Prep", Id = "BIM_BOM_REVIT_PREP" };
            var panel = new RibbonPanel { Source = panelSource };
            panelSource.Items.Add(Button("Analyze & Optimize", ProductInfo.CommandAnalyze));
            panelSource.Items.Add(Button("Batch Queue", ProductInfo.CommandBatch));
            panelSource.Items.Add(Button("Reports", ProductInfo.CommandReports));
            tab.Panels.Add(panel);
        }

        private static RibbonButton Button(string text, string command)
        {
            return new RibbonButton
            {
                Text = text, ShowText = true, Size = RibbonItemSize.Large,
                ShowImage = true, Image = SmallIcon, LargeImage = LargeIcon,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                CommandHandler = new RibbonHandler(command)
            };
        }

        private static BitmapImage LoadIcon(int size)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri("pack://application:,,,/DWGOptimizer.AutoCAD2022;component/Assets/financial-growth-analysis-" + size + ".png", UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
    }

    internal sealed class RibbonHandler : ICommand
    {
        private readonly string _command;
        public RibbonHandler(string command) { _command = command; }
        public bool CanExecute(object parameter) { return true; }
        public event EventHandler CanExecuteChanged { add { } remove { } }
        public void Execute(object parameter)
        {
            var commands = new Commands();
            if (_command == ProductInfo.CommandAnalyze) Commands.RunInteractive();
            else if (_command == ProductInfo.CommandBatch) commands.BatchQueue();
            else if (_command == ProductInfo.CommandReports) commands.Reports();
        }
    }
}
