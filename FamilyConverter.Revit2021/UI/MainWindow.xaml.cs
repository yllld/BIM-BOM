using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;
using FamilyConverter.Revit2021.Models;
using FamilyConverter.Revit2021.Services;
using Microsoft.Win32;

namespace FamilyConverter.Revit2021.UI
{
    public partial class MainWindow : Window
    {
        private readonly AiConfigService _aiConfigService;

        public MainWindow(ImportInstance importInstance, IList<GeometryObjectInfo> previewObjects, ConversionOptions defaults, AiConfigService aiConfigService)
        {
            InitializeComponent();
            _aiConfigService = aiConfigService;
            Options = defaults;

            previewObjects = previewObjects ?? new List<GeometryObjectInfo>();
            ElementIdText.Text = importInstance.Id.IntegerValue.ToString(CultureInfo.InvariantCulture);
            CategoryText.Text = importInstance.Category == null ? "Unknown" : importInstance.Category.Name;
            SolidCountText.Text = previewObjects.Count(x => x.Solid != null).ToString(CultureInfo.InvariantCulture);
            MeshCountText.Text = previewObjects.Count(x => x.Mesh != null).ToString(CultureInfo.InvariantCulture);
            CurveCountText.Text = previewObjects.Count(x => x.Curve != null).ToString(CultureInfo.InvariantCulture);
            LayersText.Text = new LayerService().JoinTopLayers(previewObjects.Select(x => x.LayerName), 12);

            CreateExtrusionsCheckBox.IsChecked = defaults.CreateNativeExtrusions;
            TryExtrusionBeforeFreeFormCheckBox.IsChecked = defaults.TryExtrusionBeforeFreeForm;
            UseFreeFormCheckBox.IsChecked = defaults.UseFreeFormFallback;
            DeleteSourceCheckBox.IsChecked = defaults.DeleteSourceDwgOnSuccess;
            CreateSubcategoriesCheckBox.IsChecked = defaults.CreateSubcategoriesByLayer;
            JsonReportCheckBox.IsChecked = defaults.CreateJsonReport;
            CsvReportCheckBox.IsChecked = defaults.CreateCsvReport;
            UseAiCheckBox.IsChecked = defaults.UseAiAdvisor;
            AiConfigPathTextBox.Text = defaults.AiConfigPath;
            MinVolumeTextBox.Text = defaults.MinSolidVolumeMm3.ToString(CultureInfo.InvariantCulture);
            BBoxToleranceTextBox.Text = defaults.BoundingBoxToleranceMm.ToString(CultureInfo.InvariantCulture);
            VolumeToleranceTextBox.Text = defaults.VolumeTolerancePercent.ToString(CultureInfo.InvariantCulture);
            LoopToleranceTextBox.Text = defaults.LoopClosureToleranceMm.ToString(CultureInfo.InvariantCulture);
            ConfidenceTextBox.Text = defaults.MinExtrusionConfidence.ToString(CultureInfo.InvariantCulture);
        }

        public ConversionOptions Options { get; private set; }

        private void BrowseAiConfigButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) == true)
            {
                AiConfigPathTextBox.Text = dialog.FileName;
            }
        }

        private void ValidateAiConfigButton_Click(object sender, RoutedEventArgs e)
        {
            string message;
            bool ok = _aiConfigService.ValidateConfigFile(AiConfigPathTextBox.Text, out message);
            AiConfigStatusText.Text = ok ? "Конфиг корректен." : message;
        }

        private void ConvertButton_Click(object sender, RoutedEventArgs e)
        {
            Options = new ConversionOptions
            {
                CreateNativeExtrusions = CreateExtrusionsCheckBox.IsChecked == true,
                TryExtrusionBeforeFreeForm = TryExtrusionBeforeFreeFormCheckBox.IsChecked == true,
                UseFreeFormFallback = UseFreeFormCheckBox.IsChecked == true,
                DeleteSourceDwgOnSuccess = DeleteSourceCheckBox.IsChecked == true,
                CreateSubcategoriesByLayer = CreateSubcategoriesCheckBox.IsChecked == true,
                CreateJsonReport = JsonReportCheckBox.IsChecked == true,
                CreateCsvReport = CsvReportCheckBox.IsChecked == true,
                UseAiAdvisor = UseAiCheckBox.IsChecked == true,
                AiConfigPath = AiConfigPathTextBox.Text,
                MinSolidVolumeMm3 = ParseDouble(MinVolumeTextBox.Text, 1.0),
                BoundingBoxToleranceMm = ParseDouble(BBoxToleranceTextBox.Text, 2.0),
                VolumeTolerancePercent = ParseDouble(VolumeToleranceTextBox.Text, 2.0),
                LoopClosureToleranceMm = ParseDouble(LoopToleranceTextBox.Text, 0.5),
                MinExtrusionConfidence = Clamp(ParseDouble(ConfidenceTextBox.Text, 0.85), 0.0, 1.0)
            };

            DialogResult = true;
            Close();
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

        private static double Clamp(double value, double min, double max)
        {
            if (value < min)
            {
                return min;
            }
            if (value > max)
            {
                return max;
            }

            return value;
        }
    }
}
