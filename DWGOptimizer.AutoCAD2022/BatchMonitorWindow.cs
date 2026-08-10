using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DWGOptimizer.Contracts;

namespace DWGOptimizer.AutoCAD2022
{
    internal sealed class BatchMonitorWindow : Window
    {
        private readonly BatchManifest _manifest;
        private readonly string _manifestPath;
        private readonly ListBox _jobs;
        private readonly TextBlock _summary;
        private readonly DispatcherTimer _timer;

        public BatchMonitorWindow(BatchManifest manifest, string manifestPath)
        {
            _manifest = manifest;
            _manifestPath = manifestPath;
            Title = "DWG Revit Optimizer — очередь";
            Width = 720;
            Height = 440;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var root = new DockPanel { Margin = new Thickness(14) };
            Content = root;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);
            var reports = new Button { Content = "Отчёты", Width = 100, Margin = new Thickness(5) };
            reports.Click += (s, e) => Process.Start(new ProcessStartInfo("explorer.exe", "\"" + OutputPathService.GetReportsDirectory() + "\"") { UseShellExecute = true });
            buttons.Children.Add(reports);
            var cancel = new Button { Content = "Отменить очередь", Width = 140, Margin = new Thickness(5) };
            cancel.Click += (s, e) =>
            {
                File.WriteAllText(_manifestPath + ".cancel", DateTime.UtcNow.ToString("O"));
                cancel.IsEnabled = false;
                cancel.Content = "Отмена запрошена";
            };
            buttons.Children.Add(cancel);
            var content = new StackPanel();
            root.Children.Add(content);
            _summary = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) };
            content.Children.Add(_summary);
            content.Children.Add(new TextBlock { Text = "Файлы обрабатываются последовательно. Текущий процесс Core Console завершается безопасно; отмена применяется перед следующим файлом.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
            _jobs = new ListBox { MinHeight = 280 };
            content.Children.Add(_jobs);
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _timer.Tick += (s, e) => Refresh();
            Closed += (s, e) => _timer.Stop();
            Refresh();
            _timer.Start();
        }

        private void Refresh()
        {
            _jobs.Items.Clear();
            int completed = 0, failed = 0, running = 0, cancelled = 0;
            foreach (BatchJob job in _manifest.Jobs)
            {
                string state = "Pending";
                string message = string.Empty;
                if (File.Exists(job.StatusPath))
                {
                    try
                    {
                        BatchJobStatus status = JsonFile.Read<BatchJobStatus>(job.StatusPath);
                        state = status.State ?? state;
                        message = status.Message ?? string.Empty;
                    }
                    catch { state = "Reading"; }
                }
                if (state == "Completed") completed++;
                else if (state == "Failed") failed++;
                else if (state == "Running") running++;
                else if (state == "Cancelled") cancelled++;
                _jobs.Items.Add("[" + state + "] " + Path.GetFileName(job.SourcePath) + (string.IsNullOrWhiteSpace(message) ? string.Empty : " — " + message));
            }
            _summary.Text = string.Format("Всего: {0}   Готово: {1}   Выполняется: {2}   Ошибки: {3}   Отменено: {4}",
                _manifest.Jobs.Count, completed, running, failed, cancelled);
            if (completed + failed + cancelled == _manifest.Jobs.Count) _timer.Stop();
        }
    }
}
