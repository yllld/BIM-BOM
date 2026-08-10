using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DWGOptimizer.Contracts;

namespace DWGOptimizer.AutoCAD2022
{
    internal sealed class AnalysisWindow : Window
    {
        private readonly ComboBox _profile;
        private readonly ComboBox _units;
        private readonly CheckBox _normalize;
        private readonly CheckBox _continueWithoutXrefs;
        private readonly AnalysisReport _analysis;

        public OptimizationRequest Request { get; private set; }

        public AnalysisWindow(AnalysisReport analysis)
        {
            _analysis = analysis;
            Title = "DWG Revit Optimizer — анализ";
            Width = 680;
            Height = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResizeWithGrip;

            var root = new DockPanel { Margin = new Thickness(18) };
            Content = root;

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);
            var cancel = new Button { Content = "Отмена", Width = 100, Margin = new Thickness(8) };
            cancel.Click += (s, e) => { DialogResult = false; Close(); };
            buttons.Children.Add(cancel);
            var apply = new Button { Content = "Создать копию", Width = 130, Margin = new Thickness(8), IsDefault = true };
            apply.Click += Apply;
            buttons.Children.Add(apply);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            root.Children.Add(scroll);
            var content = new StackPanel();
            scroll.Content = content;
            content.Children.Add(new TextBlock
            {
                Text = "Revit Readiness: " + analysis.ReadinessScore + "/100",
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 12)
            });
            content.Children.Add(new TextBlock
            {
                Text = string.Format("Объектов: {0:N0}   Solid: {1:N0}   Mesh: {2:N0}   Mesh-граней: {3:N0}",
                    analysis.Counts.TotalEntities, analysis.Counts.Solid3d, analysis.Counts.SubDMesh, analysis.Counts.MeshFaces),
                Margin = new Thickness(0, 0, 0, 12)
            });
            double removable = analysis.Counts.TotalEntities == 0 ? 0 :
                100d * (analysis.Counts.Curves2d + analysis.Counts.Annotation) / analysis.Counts.TotalEntities;
            content.Children.Add(new TextBlock
            {
                Text = "Оценка нерелевантной для 3D части: " + removable.ToString("0") + "% (фактический размер зависит от структуры блоков и ACIS).",
                Margin = new Thickness(0, 0, 0, 12)
            });

            content.Children.Add(Label("Профиль оптимизации"));
            _profile = new ComboBox { Margin = new Thickness(0, 2, 0, 12) };
            _profile.ItemsSource = Enum.GetValues(typeof(OptimizationProfile));
            _profile.SelectedItem = analysis.RecommendedProfile;
            content.Children.Add(_profile);

            content.Children.Add(Label("Единицы (обязательно при INSUNITS=0)"));
            _units = new ComboBox { Margin = new Thickness(0, 2, 0, 12), IsEnabled = !analysis.UnitsKnown };
            _units.ItemsSource = new[] { "Millimeters", "Centimeters", "Meters", "Inches", "Feet" };
            content.Children.Add(_units);

            bool far = analysis.Findings.Any(x => x.Code == "FAR_FROM_ORIGIN");
            bool xrefBlocker = analysis.Findings.Any(x => x.Code == "XREF_MISSING" || x.Code == "XREF_CIRCULAR");
            _normalize = new CheckBox
            {
                Content = "Перенести нижний центр габаритов в WCS 0,0,0",
                IsChecked = false,
                IsEnabled = far,
                Margin = new Thickness(0, 4, 0, 8)
            };
            content.Children.Add(_normalize);
            _continueWithoutXrefs = new CheckBox
            {
                Content = "Продолжить без отсутствующих/циклических XREF",
                IsChecked = false,
                Visibility = xrefBlocker ? Visibility.Visible : Visibility.Collapsed,
                Margin = new Thickness(0, 4, 0, 12)
            };
            content.Children.Add(_continueWithoutXrefs);

            content.Children.Add(Label("Риски и замечания"));
            var findings = new ListBox { MinHeight = 210 };
            foreach (Finding finding in analysis.Findings)
                findings.Items.Add("[" + finding.Severity + "] " + finding.Message);
            if (analysis.Findings.Count == 0) findings.Items.Add("Критических замечаний не найдено.");
            content.Children.Add(findings);
            content.Children.Add(new TextBlock
            {
                Text = "Safe сохраняет форму; Balanced убирает нерелевантную 2D-часть и безопасно очищает 3D; Aggressive дополнительно раскрывает блоки, объединяет тела и упрощает Mesh с контролем отклонений.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 12, 0, 0)
            });
        }

        private static TextBlock Label(string text)
        {
            return new TextBlock { Text = text, FontWeight = FontWeights.SemiBold };
        }

        private void Apply(object sender, RoutedEventArgs e)
        {
            if (!_analysis.UnitsKnown && _units.SelectedItem == null)
            {
                MessageBox.Show(this, "Выберите единицы исходного DWG.", ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Request = new OptimizationRequest
            {
                Profile = (OptimizationProfile)_profile.SelectedItem,
                NormalizeOrigin = _normalize.IsChecked == true,
                ContinueWithoutMissingXrefs = _continueWithoutXrefs.IsChecked == true,
                UnitsOverride = _analysis.UnitsKnown ? null : _units.SelectedItem.ToString()
            };
            DialogResult = true;
            Close();
        }
    }
}
