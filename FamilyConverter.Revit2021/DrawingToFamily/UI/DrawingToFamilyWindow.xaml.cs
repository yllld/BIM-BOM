using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using FamilyConverter.Revit2021.DrawingToFamily.Models;
using FamilyConverter.Revit2021.DrawingToFamily.Services;

namespace FamilyConverter.Revit2021.DrawingToFamily.UI
{
    public enum DrawingToFamilyWindowAction
    {
        None,
        PickPlan,
        PickFront,
        PickSide,
        PickIsometric,
        Build,
        Cancel
    }

    public partial class DrawingToFamilyWindow : Window
    {
        private readonly DrawingToFamilyPreview _preview;
        private readonly ProjectionRegionPicker _projectionPicker;
        private readonly DrawingToFamilyLogger _logger;

        public DrawingToFamilyWindow(DrawingToFamilyPreview preview, ProjectionRegionPicker projectionPicker, DrawingToFamilyLogger logger)
            : this(preview, projectionPicker, logger, null)
        {
        }

        public DrawingToFamilyWindow(DrawingToFamilyPreview preview, ProjectionRegionPicker projectionPicker, DrawingToFamilyLogger logger, DrawingToFamilySettings existingSettings)
        {
            _logger = logger;
            LogStage("WPF window constructor start");
            InitializeComponent();
            DataContext = this;
            _preview = preview;
            _projectionPicker = projectionPicker;
            RoleOptions = new ObservableCollection<string>(LayerRoleOption.All());
            Settings = existingSettings ?? new DrawingToFamilySettings();
            RequestedAction = DrawingToFamilyWindowAction.None;

            ImportNameText.Text = preview.ImportName;
            ObjectSummaryText.Text = string.Format("Объектов: {0}; слоёв: {1}", preview.ObjectCount, preview.LayerCount);
            BoundsText.Text = preview.BoundingBoxText;

            if (Settings.Layers.Count == 0)
            {
                foreach (DwgLayerInfo layer in preview.Layers)
                {
                    Settings.Layers.Add(layer);
                }
            }

            LayerGrid.ItemsSource = Settings.Layers;
            ClosureToleranceTextBox.Text = Settings.ClosureToleranceMm.ToString(CultureInfo.InvariantCulture);
            MinimumSizeTextBox.Text = Settings.MinimumElementSizeMm.ToString(CultureInfo.InvariantCulture);
            UseIsoCheckBox.IsChecked = Settings.UseIsometricReference;
            UpdateProjectionTexts();
            LogData("WPF preview entities", preview.ObjectCount);
            LogData("WPF preview layers", preview.LayerCount);
            LogStage("WPF initial reanalyze start");
            Reanalyze(false);
            LogStage("WPF window constructor end");
        }

        public ObservableCollection<string> RoleOptions { get; private set; }
        public DrawingToFamilySettings Settings { get; private set; }
        public IList<RecognizedContour> PreviewContours { get; private set; }
        public DrawingToFamilyWindowAction RequestedAction { get; private set; }

        private void PickPlanButton_Click(object sender, RoutedEventArgs e)
        {
            RequestProjectionPick(DrawingToFamilyWindowAction.PickPlan);
        }

        private void PickFrontButton_Click(object sender, RoutedEventArgs e)
        {
            RequestProjectionPick(DrawingToFamilyWindowAction.PickFront);
        }

        private void PickSideButton_Click(object sender, RoutedEventArgs e)
        {
            RequestProjectionPick(DrawingToFamilyWindowAction.PickSide);
        }

        private void PickIsoButton_Click(object sender, RoutedEventArgs e)
        {
            Settings.UseIsometricReference = true;
            UseIsoCheckBox.IsChecked = true;
            RequestProjectionPick(DrawingToFamilyWindowAction.PickIsometric);
        }

        private void ClearProjectionButton_Click(object sender, RoutedEventArgs e)
        {
            Settings.PlanRegion = null;
            Settings.FrontRegion = null;
            Settings.SideRegion = null;
            Settings.IsometricRegion = null;
            Settings.UseIsometricReference = false;
            UseIsoCheckBox.IsChecked = false;
            UpdateProjectionTexts();
            Reanalyze(false);
        }

        private void ReanalyzeButton_Click(object sender, RoutedEventArgs e)
        {
            Reanalyze(true);
        }

        private void BuildButton_Click(object sender, RoutedEventArgs e)
        {
            LogStage("WPF Build clicked");
            if (!UpdateSettingsFromFields())
            {
                return;
            }

            LogStage("WPF Build apply layer roles");
            ApplyLayerRolesToPreview();
            LogStage("WPF Build refresh projection entities start");
            RefreshProjectionEntities();
            LogStage("WPF Build refresh projection entities end");
            if (Settings.PlanRegion == null || !Settings.PlanRegion.IsValid)
            {
                MessageBox.Show(this, "Не выбран вид сверху.", ProductInfo.Name);
                return;
            }

            if (Settings.FrontRegion == null || !Settings.FrontRegion.IsValid)
            {
                MessageBox.Show(this, "Не выбран вид спереди.", ProductInfo.Name);
                return;
            }

            if (Settings.PlanRegion.EntityCount == 0)
            {
                MessageBox.Show(this, "В выбранной области вида сверху нет линий для построения.", ProductInfo.Name);
                return;
            }

            if (Settings.FrontRegion.EntityCount == 0)
            {
                MessageBox.Show(this, "В выбранной области вида спереди нет линий для определения высоты.", ProductInfo.Name);
                return;
            }

            if (Settings.UseIsometricReference
                && (Settings.IsometricRegion == null || !Settings.IsometricRegion.IsValid))
            {
                MessageBox.Show(this, "Включён ISO-режим, но не выбран 3D/ISO вид.", ProductInfo.Name);
                return;
            }

            LogStage("WPF Build skipping WPF Reanalyze; command will analyze after window closes");
            LogRegion("WPF Build Plan", Settings.PlanRegion);
            LogRegion("WPF Build Front", Settings.FrontRegion);
            LogRegion("WPF Build Side", Settings.SideRegion);
            LogRegion("WPF Build ISO", Settings.IsometricRegion);
            RequestedAction = DrawingToFamilyWindowAction.Build;
            LogStage("WPF Build requested; close window");
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            RequestedAction = DrawingToFamilyWindowAction.Cancel;
            LogStage("WPF Cancel requested; close window");
            Close();
        }

        private void RequestProjectionPick(DrawingToFamilyWindowAction action)
        {
            if (!UpdateSettingsFromFields())
            {
                return;
            }

            ApplyLayerRolesToPreview();
            RefreshProjectionEntities();
            RequestedAction = action;
            LogStage("WPF projection pick requested: " + action);
            Close();
        }

        private bool UpdateSettingsFromFields()
        {
            Settings.ClosureToleranceMm = ParsePositive(ClosureToleranceTextBox.Text, 2.0);
            Settings.MinimumElementSizeMm = ParsePositive(MinimumSizeTextBox.Text, 10.0);
            Settings.UseIsometricReference = UseIsoCheckBox.IsChecked == true;
            ClosureToleranceTextBox.Text = Settings.ClosureToleranceMm.ToString(CultureInfo.InvariantCulture);
            MinimumSizeTextBox.Text = Settings.MinimumElementSizeMm.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        private void Reanalyze(bool showMessage)
        {
            if (!UpdateSettingsFromFields())
            {
                return;
            }

            ApplyLayerRolesToPreview();
            LogStage("WPF Reanalyze start");
            RefreshProjectionEntities();
            var warnings = new List<string>();
            var contours = new List<RecognizedContour>();
            var service = new ContourRecognitionService();
            foreach (DrawingProjectionRegion region in SelectedRegions())
            {
                LogRegion("WPF Reanalyze region before", region);
                if (region.Type == ProjectionType.Isometric)
                {
                    LogStage("WPF Reanalyze skips ISO contours; ISO is used only as detail reference.");
                    continue;
                }

                contours.AddRange(service.Recognize(region, Settings, warnings));
                LogStage("WPF Reanalyze region done: " + region.Type);
            }

            PreviewContours = contours;
            int solid = contours.Count(x => x.Type == ContourType.SolidProfile);
            int voids = contours.Count(x => x.Type == ContourType.VoidProfile);
            int open = contours.Count(x => x.Type == ContourType.OpenCurve || x.Type == ContourType.ReferenceCurve);
            int invalid = contours.Count(x => x.Type == ContourType.Invalid);

            AnalysisSummaryText.Text = string.Format(
                "Анализ: контуров {0}; solid {1}; void {2}; open/reference {3}; invalid {4}.",
                contours.Count,
                solid,
                voids,
                open,
                invalid);
            LogData("WPF Reanalyze contours", contours.Count);
            LogData("WPF Reanalyze solid", solid);
            LogData("WPF Reanalyze void", voids);
            LogData("WPF Reanalyze open", open);
            LogData("WPF Reanalyze invalid", invalid);

            ProjectionWarningText.Text = warnings.Count == 0 ? string.Empty : warnings[0];
            UpdateProjectionTexts();

            if (showMessage)
            {
                MessageBox.Show(this, AnalysisSummaryText.Text, ProductInfo.Name);
            }
            LogStage("WPF Reanalyze end");
        }

        private void RefreshProjectionEntities()
        {
            if (_projectionPicker == null)
            {
                return;
            }

            foreach (DrawingProjectionRegion region in SelectedRegions())
            {
                LogStage("WPF RefreshRegionEntities start: " + region.Type);
                _projectionPicker.RefreshRegionEntities(region, Settings);
                LogRegion("WPF RefreshRegionEntities end", region);
            }
        }

        private IEnumerable<DrawingProjectionRegion> SelectedRegions()
        {
            if (Settings.PlanRegion != null && Settings.PlanRegion.IsValid)
            {
                yield return Settings.PlanRegion;
            }
            if (Settings.FrontRegion != null && Settings.FrontRegion.IsValid)
            {
                yield return Settings.FrontRegion;
            }
            if (Settings.SideRegion != null && Settings.SideRegion.IsValid)
            {
                yield return Settings.SideRegion;
            }
            if (Settings.UseIsometricReference
                && Settings.IsometricRegion != null
                && Settings.IsometricRegion.IsValid)
            {
                yield return Settings.IsometricRegion;
            }
        }

        private void ApplyLayerRolesToPreview()
        {
            var map = Settings.Layers.ToDictionary(x => x.LayerName ?? "Unknown", x => x, StringComparer.OrdinalIgnoreCase);
            LogData("WPF ApplyLayerRoles layer count", map.Count);
            foreach (DwgCurveEntity entity in _preview.Entities)
            {
                DwgLayerInfo layer;
                if (map.TryGetValue(entity.LayerName ?? "Unknown", out layer))
                {
                    entity.RecognitionRole = layer.EffectiveRole;
                    entity.IsIgnored = layer.EffectiveRole == RecognitionRole.Ignored;
                    entity.IsSmallObject = entity.LengthMm < Settings.MinimumElementSizeMm;
                }
            }
        }

        private void LogRegion(string label, DrawingProjectionRegion region)
        {
            if (_logger == null || region == null)
            {
                return;
            }

            _logger.Info(string.Format(
                "{0}: type={1}; valid={2}; entities={3}; size={4:0.#} x {5:0.#} mm",
                label,
                region.Type,
                region.IsValid,
                region.EntityCount,
                region.WidthMm,
                region.HeightMm));
        }

        private void LogStage(string message)
        {
            if (_logger != null)
            {
                _logger.Stage(message);
            }
        }

        private void LogData(string name, object value)
        {
            if (_logger != null)
            {
                _logger.Data(name, value);
            }
        }

        private void LogWarning(string message)
        {
            if (_logger != null)
            {
                _logger.Warning(message);
            }
        }

        private void LogError(string message, Exception exception)
        {
            if (_logger != null)
            {
                _logger.Error(message, exception);
            }
        }

        private void UpdateProjectionTexts()
        {
            PlanStatusText.Text = Settings.PlanRegion == null ? "не выбрано" : Settings.PlanRegion.StatusText;
            FrontStatusText.Text = Settings.FrontRegion == null ? "не выбрано" : Settings.FrontRegion.StatusText;
            SideStatusText.Text = Settings.SideRegion == null ? "не выбрано" : Settings.SideRegion.StatusText;
            IsoStatusText.Text = Settings.IsometricRegion == null
                ? "не выбрано"
                : Settings.IsometricRegion.StatusText + " (только детализация, не основной габарит)";
        }

        private static double ParseDouble(string text, double fallback)
        {
            double value;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            {
                return value;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }

            return fallback;
        }

        private static double ParsePositive(string text, double fallback)
        {
            return Math.Max(0.001, ParseDouble(text, fallback));
        }
    }

    public class LayerColorBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string text = value as string;
            if (string.IsNullOrWhiteSpace(text) || text == "-")
            {
                return new SolidColorBrush(Color.FromRgb(214, 212, 222));
            }

            try
            {
                return (SolidColorBrush)new BrushConverter().ConvertFromString(text);
            }
            catch
            {
                return new SolidColorBrush(Color.FromRgb(214, 212, 222));
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}

