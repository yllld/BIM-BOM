using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
        private ObservableCollection<PreviewContourItem> _previewItems;
        private PreviewContourItem _selectedPreviewContour;
        private bool _updatingPreviewSelection;

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
            SuppressRevitWarningsCheckBox.IsChecked = Settings.SuppressRevitWarnings;
            SelectBuildProfileProjection(Settings.BuildProfileProjection);
            PreviewProjectionComboBox.SelectedIndex = 0;
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

            if (Settings.BuildProfileProjection == ProjectionType.Side
                && (Settings.SideRegion == null || !Settings.SideRegion.IsValid || Settings.SideRegion.EntityCount == 0))
            {
                MessageBox.Show(this, "Выбран профиль по виду сбоку/слева, но область вида сбоку не выбрана или пуста.", ProductInfo.Name);
                return;
            }

            if (Settings.BuildProfileProjection == ProjectionType.Front
                && (Settings.FrontRegion == null || !Settings.FrontRegion.IsValid || Settings.FrontRegion.EntityCount == 0))
            {
                MessageBox.Show(this, "Выбран профиль по виду спереди, но область вида спереди не выбрана или пуста.", ProductInfo.Name);
                return;
            }

            if (Settings.BuildProfileProjection == ProjectionType.Plan
                && (Settings.PlanRegion == null || !Settings.PlanRegion.IsValid || Settings.PlanRegion.EntityCount == 0))
            {
                MessageBox.Show(this, "Выбран профиль по плану, но область вида сверху не выбрана или пуста.", ProductInfo.Name);
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
            PersistManualContourOverrides();
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
            PersistManualContourOverrides();
            RequestedAction = action;
            LogStage("WPF projection pick requested: " + action);
            Close();
        }

        private bool UpdateSettingsFromFields()
        {
            Settings.ClosureToleranceMm = ParsePositive(ClosureToleranceTextBox.Text, 2.0);
            Settings.MinimumElementSizeMm = ParsePositive(MinimumSizeTextBox.Text, 10.0);
            Settings.UseIsometricReference = UseIsoCheckBox.IsChecked == true;
            Settings.SuppressRevitWarnings = SuppressRevitWarningsCheckBox.IsChecked == true;
            Settings.BuildProfileProjection = GetSelectedBuildProfileProjection();
            ClosureToleranceTextBox.Text = Settings.ClosureToleranceMm.ToString(CultureInfo.InvariantCulture);
            MinimumSizeTextBox.Text = Settings.MinimumElementSizeMm.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        private void SelectBuildProfileProjection(ProjectionType projection)
        {
            string tag = projection.ToString();
            foreach (object item in BuildProfileProjectionComboBox.Items)
            {
                var comboBoxItem = item as ComboBoxItem;
                if (comboBoxItem != null
                    && string.Equals(comboBoxItem.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
                {
                    BuildProfileProjectionComboBox.SelectedItem = comboBoxItem;
                    return;
                }
            }

            BuildProfileProjectionComboBox.SelectedIndex = 0;
        }

        private ProjectionType GetSelectedBuildProfileProjection()
        {
            var item = BuildProfileProjectionComboBox.SelectedItem as ComboBoxItem;
            string tag = item == null ? null : item.Tag as string;
            ProjectionType projection;
            return Enum.TryParse(tag, true, out projection) ? projection : ProjectionType.Unknown;
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
            RebuildPreviewItems(contours);
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
            RefreshPreviewCanvas(false);

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

        private void RebuildPreviewItems(IList<RecognizedContour> contours)
        {
            var overrides = Settings.ManualContourOverrides.ToDictionary(x => x.Signature ?? string.Empty, x => x, StringComparer.Ordinal);
            _previewItems = new ObservableCollection<PreviewContourItem>();
            foreach (RecognizedContour contour in contours ?? new List<RecognizedContour>())
            {
                var item = new PreviewContourItem(contour);
                ContourManualOverride manual;
                if (overrides.TryGetValue(item.Signature, out manual))
                {
                    item.Role = manual.OverrideType;
                    item.IsIncluded = manual.IsIncluded;
                }

                _previewItems.Add(item);
            }

            PreviewContourGrid.ItemsSource = _previewItems;
            if (_selectedPreviewContour != null)
            {
                _selectedPreviewContour = _previewItems.FirstOrDefault(x => x.Signature == _selectedPreviewContour.Signature);
                if (_selectedPreviewContour != null)
                {
                    _selectedPreviewContour.IsSelected = true;
                    PreviewContourGrid.SelectedItem = _selectedPreviewContour;
                }
            }
        }

        private void RefreshPreviewCanvas(bool fit)
        {
            if (PreviewCanvas == null)
            {
                return;
            }

            PreviewCanvas.Items = _previewItems == null ? new List<PreviewContourItem>() : _previewItems.ToList();
            PreviewCanvas.Projection = GetSelectedPreviewProjection();
            PreviewCanvas.ShowSolid = PreviewSolidCheckBox == null || PreviewSolidCheckBox.IsChecked == true;
            PreviewCanvas.ShowVoid = PreviewVoidCheckBox == null || PreviewVoidCheckBox.IsChecked == true;
            PreviewCanvas.ShowReference = PreviewReferenceCheckBox == null || PreviewReferenceCheckBox.IsChecked == true;
            PreviewCanvas.ShowOpen = PreviewOpenCheckBox == null || PreviewOpenCheckBox.IsChecked == true;
            PreviewCanvas.ShowInvalid = PreviewInvalidCheckBox == null || PreviewInvalidCheckBox.IsChecked == true;
            PreviewCanvas.ShowDisabled = PreviewDisabledCheckBox == null || PreviewDisabledCheckBox.IsChecked == true;
            PreviewCanvas.ShowOnlyIncluded = PreviewOnlyIncludedCheckBox != null && PreviewOnlyIncludedCheckBox.IsChecked == true;
            PreviewCanvas.ShowOnlyProblems = PreviewOnlyProblemsCheckBox != null && PreviewOnlyProblemsCheckBox.IsChecked == true;
            PreviewCanvas.InvalidateVisual();
            if (fit)
            {
                PreviewCanvas.FitToView();
            }

            UpdatePreviewStats();
        }

        private void UpdatePreviewStats()
        {
            IList<PreviewContourItem> items = _previewItems == null ? new List<PreviewContourItem>() : _previewItems.ToList();
            int shown = items.Count(x => x.Projection == GetSelectedPreviewProjection());
            int disabled = items.Count(x => !x.IsIncluded);
            int manual = items.Count(x => x.IsManual);
            PreviewStatsText.Text = string.Format(
                "Preview: contours {0}; shown projection {1}; solid {2}; void {3}; reference/open {4}; invalid {5}; disabled {6}; manual changes {7}. Wheel: zoom, Ctrl+left or middle mouse: pan.",
                items.Count,
                shown,
                items.Count(x => x.Role == ContourType.SolidProfile),
                items.Count(x => x.Role == ContourType.VoidProfile),
                items.Count(x => x.Role == ContourType.ReferenceCurve || x.Role == ContourType.OpenCurve),
                items.Count(x => x.Role == ContourType.Invalid),
                disabled,
                manual);
        }

        private ProjectionType GetSelectedPreviewProjection()
        {
            var item = PreviewProjectionComboBox == null ? null : PreviewProjectionComboBox.SelectedItem as ComboBoxItem;
            string tag = item == null ? null : item.Tag as string;
            ProjectionType projection;
            return Enum.TryParse(tag, true, out projection) ? projection : ProjectionType.Plan;
        }

        private void SelectPreviewContour(PreviewContourItem item)
        {
            if (item == null)
            {
                return;
            }

            _updatingPreviewSelection = true;
            foreach (PreviewContourItem previewItem in _previewItems ?? new ObservableCollection<PreviewContourItem>())
            {
                previewItem.IsSelected = false;
            }

            _selectedPreviewContour = item;
            item.IsSelected = true;
            PreviewContourGrid.SelectedItem = item;
            PreviewContourGrid.ScrollIntoView(item);
            SelectedContourIncludedCheckBox.IsChecked = item.IsIncluded;
            SelectRoleCombo(item.IsManual ? item.Role : ContourType.Unknown);
            SelectedContourText.Text = BuildSelectedContourText(item);
            _updatingPreviewSelection = false;
            RefreshPreviewCanvas(false);
        }

        private string BuildSelectedContourText(PreviewContourItem item)
        {
            if (item == null || item.Contour == null)
            {
                return "No contour selected";
            }

            return string.Format(
                "ID: {0}\nLayer: {1}\nProjection: {2}\nAuto role: {3}\nCurrent role: {4}\nIncluded: {5}\nClosed: {6}\nArea: {7:0.#} mm2\nSize: {8:0.#} x {9:0.#} mm\nSegments: {10}\nProblem: {11}",
                item.ShortId,
                item.LayerName,
                item.Projection,
                item.AutoType,
                item.Role,
                item.IsIncluded,
                item.Contour.IsClosed,
                item.Contour.AreaMm2,
                item.Contour.WidthMm,
                item.Contour.HeightMm,
                item.Contour.Curves == null ? 0 : item.Contour.Curves.Count,
                string.IsNullOrWhiteSpace(item.Contour.ReasonIfInvalid) ? "-" : item.Contour.ReasonIfInvalid);
        }

        private void SelectRoleCombo(ContourType role)
        {
            string tag = role == ContourType.Unknown ? "Auto" : role.ToString();
            foreach (object comboItem in SelectedContourRoleComboBox.Items)
            {
                var item = comboItem as ComboBoxItem;
                if (item != null && string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
                {
                    SelectedContourRoleComboBox.SelectedItem = item;
                    return;
                }
            }
        }

        private void PersistManualContourOverrides()
        {
            Settings.ManualContourOverrides.Clear();
            foreach (PreviewContourItem item in _previewItems ?? new ObservableCollection<PreviewContourItem>())
            {
                if (!item.IsManual)
                {
                    continue;
                }

                Settings.ManualContourOverrides.Add(new ContourManualOverride
                {
                    Signature = item.Signature,
                    Projection = item.Projection,
                    LayerName = item.LayerName,
                    AutoType = item.AutoType,
                    OverrideType = item.Role,
                    IsIncluded = item.IsIncluded,
                    Reason = "Changed in visual preview"
                });
            }
        }

        private void PreviewProjectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshPreviewCanvas(true);
        }

        private void PreviewFilterChanged(object sender, RoutedEventArgs e)
        {
            RefreshPreviewCanvas(false);
        }

        private void PreviewFitButton_Click(object sender, RoutedEventArgs e)
        {
            PreviewCanvas.FitToView();
        }

        private void PreviewResetButton_Click(object sender, RoutedEventArgs e)
        {
            PreviewCanvas.ResetView();
        }

        private void PreviewZoomSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            PreviewCanvas.ZoomSelected();
        }

        private void PreviewCanvas_ContourSelected(object sender, PreviewContourSelectedEventArgs e)
        {
            SelectPreviewContour(e == null ? null : e.Contour);
        }

        private void PreviewContourGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingPreviewSelection)
            {
                return;
            }

            SelectPreviewContour(PreviewContourGrid.SelectedItem as PreviewContourItem);
        }

        private void SelectedContourRoleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingPreviewSelection || _selectedPreviewContour == null)
            {
                return;
            }

            var item = SelectedContourRoleComboBox.SelectedItem as ComboBoxItem;
            string tag = item == null ? "Auto" : item.Tag as string;
            if (string.Equals(tag, "Auto", StringComparison.OrdinalIgnoreCase))
            {
                _selectedPreviewContour.Role = _selectedPreviewContour.AutoType;
            }
            else if (string.Equals(tag, "Ignore", StringComparison.OrdinalIgnoreCase))
            {
                _selectedPreviewContour.Role = ContourType.ReferenceCurve;
                _selectedPreviewContour.IsIncluded = false;
                SelectedContourIncludedCheckBox.IsChecked = false;
            }
            else
            {
                ContourType role;
                if (Enum.TryParse(tag, true, out role))
                {
                    if (_selectedPreviewContour.AutoType == ContourType.Invalid
                        && (role == ContourType.SolidProfile || role == ContourType.VoidProfile))
                    {
                        MessageBox.Show(this, "Invalid contour was forced to a build role. Check the problem text and report before trusting the result.", ProductInfo.Name);
                    }

                    _selectedPreviewContour.Role = role;
                }
            }

            SelectedContourText.Text = BuildSelectedContourText(_selectedPreviewContour);
            PreviewContourGrid.Items.Refresh();
            RefreshPreviewCanvas(false);
        }

        private void SelectedContourIncludedCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_updatingPreviewSelection || _selectedPreviewContour == null)
            {
                return;
            }

            _selectedPreviewContour.IsIncluded = SelectedContourIncludedCheckBox.IsChecked == true;
            SelectedContourText.Text = BuildSelectedContourText(_selectedPreviewContour);
            PreviewContourGrid.Items.Refresh();
            RefreshPreviewCanvas(false);
        }

        private void ResetSelectedContourOverrideButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPreviewContour == null)
            {
                return;
            }

            _selectedPreviewContour.Role = _selectedPreviewContour.AutoType;
            _selectedPreviewContour.IsIncluded = true;
            SelectPreviewContour(_selectedPreviewContour);
        }

        private void ResetAllContourOverridesButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (PreviewContourItem item in _previewItems ?? new ObservableCollection<PreviewContourItem>())
            {
                item.Role = item.AutoType;
                item.IsIncluded = true;
            }

            if (_selectedPreviewContour != null)
            {
                SelectPreviewContour(_selectedPreviewContour);
            }

            PreviewContourGrid.Items.Refresh();
            RefreshPreviewCanvas(false);
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

