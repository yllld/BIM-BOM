using System;
using System.Globalization;
using System.Windows;
using FamilyConverter.Revit2021.Models;

namespace FamilyConverter.Revit2021.UI
{
    public partial class ConversionSettingsWindow : Window
    {
        public ConversionSettingsWindow(ConversionOptions defaults)
        {
            InitializeComponent();
            Options = defaults.Clone();

            CreateExtrusionsCheckBox.IsChecked = Options.CreateNativeExtrusions;
            TryExtrusionBeforeFreeFormCheckBox.IsChecked = Options.TryExtrusionBeforeFreeForm;
            UseFreeFormCheckBox.IsChecked = Options.UseFreeFormFallback;
            DeleteSourceCheckBox.IsChecked = Options.DeleteSourceDwgOnSuccess;
            CreateSubcategoriesCheckBox.IsChecked = Options.CreateSubcategoriesByLayer;
            JsonReportCheckBox.IsChecked = Options.CreateJsonReport;
            CsvReportCheckBox.IsChecked = Options.CreateCsvReport;
            MinVolumeTextBox.Text = Options.MinSolidVolumeMm3.ToString(CultureInfo.InvariantCulture);
            BBoxToleranceTextBox.Text = Options.BoundingBoxToleranceMm.ToString(CultureInfo.InvariantCulture);
            VolumeToleranceTextBox.Text = Options.VolumeTolerancePercent.ToString(CultureInfo.InvariantCulture);
            LoopToleranceTextBox.Text = Options.LoopClosureToleranceMm.ToString(CultureInfo.InvariantCulture);
            ConfidenceTextBox.Text = Options.MinExtrusionConfidence.ToString(CultureInfo.InvariantCulture);
        }

        public ConversionOptions Options { get; private set; }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            Options.CreateNativeExtrusions = CreateExtrusionsCheckBox.IsChecked == true;
            Options.TryExtrusionBeforeFreeForm = TryExtrusionBeforeFreeFormCheckBox.IsChecked == true;
            Options.UseFreeFormFallback = UseFreeFormCheckBox.IsChecked == true;
            Options.SuperTurboMode = false;
            Options.CollectUnsupportedGeometry = true;
            Options.ReadLayerNames = true;
            Options.ValidateCreatedGeometry = true;
            Options.DeleteSourceDwgOnSuccess = DeleteSourceCheckBox.IsChecked == true;
            Options.CreateSubcategoriesByLayer = CreateSubcategoriesCheckBox.IsChecked == true;
            Options.CreateJsonReport = JsonReportCheckBox.IsChecked == true;
            Options.CreateCsvReport = CsvReportCheckBox.IsChecked == true;
            Options.MinSolidVolumeMm3 = ParseNonNegative(MinVolumeTextBox.Text, 1.0);
            Options.MinSolidMaxDimensionMm = 0.0;
            Options.BoundingBoxToleranceMm = ParseNonNegative(BBoxToleranceTextBox.Text, 2.0);
            Options.VolumeTolerancePercent = ParseNonNegative(VolumeToleranceTextBox.Text, 2.0);
            Options.LoopClosureToleranceMm = ParseNonNegative(LoopToleranceTextBox.Text, 0.5);
            Options.MinExtrusionConfidence = Clamp(ParseDouble(ConfidenceTextBox.Text, 0.85), 0.0, 1.0);

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

        private static double ParseNonNegative(string text, double fallback)
        {
            return Math.Max(0, ParseDouble(text, fallback));
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
