using System.Linq;
using System.Windows;
using FamilyConverter.Revit2021.Models;

namespace FamilyConverter.Revit2021.UI
{
    public partial class ReportWindow : Window
    {
        public ReportWindow(ConversionSummary summary)
        {
            InitializeComponent();
            SummaryText.Text = string.Format(
                "Extrusion: {0} • FreeFormElement: {1} • Пропущено: {2} • Ошибки: {3} • Предупреждения: {4}",
                summary.ExtrusionCount,
                summary.FreeFormCount,
                summary.SkippedCount,
                summary.FailedCount,
                summary.WarningCount);

            ReportPathsText.Text = string.Format(
                "JSON: {0}\nCSV: {1}",
                string.IsNullOrWhiteSpace(summary.JsonReportPath) ? "-" : summary.JsonReportPath,
                string.IsNullOrWhiteSpace(summary.CsvReportPath) ? "-" : summary.CsvReportPath);

            MessagesText.Text = summary.Messages.Count == 0 ? string.Empty : string.Join("\n", summary.Messages);
            ResultsGrid.ItemsSource = summary.Results.ToList();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
