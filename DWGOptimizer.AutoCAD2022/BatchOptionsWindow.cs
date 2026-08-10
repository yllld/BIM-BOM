using System;
using System.Windows;
using System.Windows.Controls;
using DWGOptimizer.Contracts;

namespace DWGOptimizer.AutoCAD2022
{
    internal sealed class BatchOptionsWindow : Window
    {
        private readonly ComboBox _profile;
        private readonly ComboBox _units;
        private readonly CheckBox _normalize;
        private readonly CheckBox _missingXrefs;
        public OptimizationRequest Request { get; private set; }

        public BatchOptionsWindow()
        {
            Title = "DWG Revit Optimizer — пакет";
            Width = 460;
            Height = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            var root = new StackPanel { Margin = new Thickness(20) };
            Content = root;
            root.Children.Add(new TextBlock { Text = "Профиль для всей очереди", FontWeight = FontWeights.SemiBold });
            _profile = new ComboBox { ItemsSource = Enum.GetValues(typeof(OptimizationProfile)), SelectedItem = OptimizationProfile.Balanced, Margin = new Thickness(0, 4, 0, 14) };
            root.Children.Add(_profile);
            root.Children.Add(new TextBlock { Text = "Единицы для файлов с INSUNITS=0 (необязательно)", FontWeight = FontWeights.SemiBold });
            _units = new ComboBox { ItemsSource = new[] { "Не назначать", "Millimeters", "Centimeters", "Meters", "Inches", "Feet" }, SelectedIndex = 0, Margin = new Thickness(0, 4, 0, 14) };
            root.Children.Add(_units);
            _normalize = new CheckBox { Content = "Переносить далёкие модели к WCS 0,0,0", Margin = new Thickness(0, 3, 0, 8) };
            root.Children.Add(_normalize);
            _missingXrefs = new CheckBox { Content = "Продолжать без отсутствующих XREF", Margin = new Thickness(0, 3, 0, 16) };
            root.Children.Add(_missingXrefs);
            root.Children.Add(new TextBlock { Text = "Каждый файл будет проанализирован отдельно. Блокирующие ошибки одного файла не остановят остальные.", TextWrapping = TextWrapping.Wrap });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            var cancel = new Button { Content = "Отмена", Width = 90, Margin = new Thickness(5) };
            cancel.Click += (s, e) => { DialogResult = false; Close(); };
            var start = new Button { Content = "Запустить", Width = 110, Margin = new Thickness(5), IsDefault = true };
            start.Click += (s, e) =>
            {
                Request = new OptimizationRequest
                {
                    Profile = (OptimizationProfile)_profile.SelectedItem,
                    UnitsOverride = _units.SelectedIndex <= 0 ? null : _units.SelectedItem.ToString(),
                    NormalizeOrigin = _normalize.IsChecked == true,
                    ContinueWithoutMissingXrefs = _missingXrefs.IsChecked == true
                };
                DialogResult = true;
                Close();
            };
            buttons.Children.Add(cancel);
            buttons.Children.Add(start);
            root.Children.Add(buttons);
        }
    }
}
