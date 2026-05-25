using System;
using System.Globalization;
using System.Windows;
using FamilyConverter.Revit2021.Models;

namespace FamilyConverter.Revit2021.UI
{
    public partial class TurboSettingsWindow : Window
    {
        public TurboSettingsWindow(ConversionOptions defaults)
        {
            InitializeComponent();
            Options = defaults;

            MinVolumeTextBox.Text = defaults.MinSolidVolumeMm3.ToString(CultureInfo.InvariantCulture);
            MinMaxDimensionTextBox.Text = defaults.MinSolidMaxDimensionMm.ToString(CultureInfo.InvariantCulture);
            BBoxToleranceTextBox.Text = defaults.BoundingBoxToleranceMm.ToString(CultureInfo.InvariantCulture);
            VolumeToleranceTextBox.Text = defaults.VolumeTolerancePercent.ToString(CultureInfo.InvariantCulture);
            ValidateGeometryCheckBox.IsChecked = defaults.ValidateCreatedGeometry;
            JsonReportCheckBox.IsChecked = defaults.CreateJsonReport;
            CsvReportCheckBox.IsChecked = defaults.CreateCsvReport;
        }

        public ConversionOptions Options { get; private set; }

        private void RunButton_Click(object sender, RoutedEventArgs e)
        {
            Options.CreateNativeExtrusions = false;
            Options.TryExtrusionBeforeFreeForm = false;
            Options.UseFreeFormFallback = true;
            Options.SuperTurboMode = true;
            Options.CollectUnsupportedGeometry = false;
            Options.ReadLayerNames = false;
            Options.ValidateCreatedGeometry = ValidateGeometryCheckBox.IsChecked == true;
            Options.DeleteSourceDwgOnSuccess = false;
            Options.CreateSubcategoriesByLayer = false;
            Options.UseAiAdvisor = false;
            Options.MinSolidVolumeMm3 = ParseNonNegative(MinVolumeTextBox.Text, 50000.0);
            Options.MinSolidMaxDimensionMm = ParseNonNegative(MinMaxDimensionTextBox.Text, 25.0);
            Options.BoundingBoxToleranceMm = ParseNonNegative(BBoxToleranceTextBox.Text, 50.0);
            Options.VolumeTolerancePercent = ParseNonNegative(VolumeToleranceTextBox.Text, 25.0);
            Options.CreateJsonReport = JsonReportCheckBox.IsChecked == true;
            Options.CreateCsvReport = CsvReportCheckBox.IsChecked == true;

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
    }
}
