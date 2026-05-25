using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using FamilyConverter.Revit2021.Models;
using FamilyConverter.Revit2021.Services;

namespace FamilyConverter.Revit2021.UI
{
    public partial class MainWindow : Window
    {
        private readonly AiConfigService _aiConfigService;

        public MainWindow(ImportInstance importInstance, IList<GeometryObjectInfo> previewObjects, ConversionOptions defaults, AiConfigService aiConfigService)
        {
            InitializeComponent();
            _aiConfigService = aiConfigService;
            Options = defaults.Clone();

            SettingsButton.Content = CreateIcon(EmbeddedIcons.CreateSettings());
            AiButton.Content = CreateIcon(EmbeddedIcons.CreateAi());

            previewObjects = previewObjects ?? new List<GeometryObjectInfo>();
            ElementIdText.Text = importInstance.Id.IntegerValue.ToString(CultureInfo.InvariantCulture);
            CategoryText.Text = importInstance.Category == null ? "Unknown" : importInstance.Category.Name;
            SolidCountText.Text = previewObjects.Count(x => x.Solid != null).ToString(CultureInfo.InvariantCulture);
            MeshCountText.Text = previewObjects.Count(x => x.Mesh != null).ToString(CultureInfo.InvariantCulture);
            CurveCountText.Text = previewObjects.Count(x => x.Curve != null).ToString(CultureInfo.InvariantCulture);
            LayersText.Text = new LayerService().JoinTopLayers(previewObjects.Select(x => x.LayerName), 12);

            UpdateSummaries();
        }

        public ConversionOptions Options { get; private set; }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new ConversionSettingsWindow(Options);
            window.Owner = this;
            if (window.ShowDialog() == true)
            {
                Options = window.Options;
                UpdateSummaries();
            }
        }

        private void AiButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new AiSettingsWindow(Options, _aiConfigService);
            window.Owner = this;
            if (window.ShowDialog() == true)
            {
                Options = window.Options;
                UpdateSummaries();
            }
        }

        private void ConvertButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void UpdateSummaries()
        {
            ModeSummaryText.Text = string.Format(
                "Extrusion: {0} · FreeForm fallback: {1} · Подкатегории: {2}",
                OnOff(Options.CreateNativeExtrusions),
                OnOff(Options.UseFreeFormFallback),
                OnOff(Options.CreateSubcategoriesByLayer));

            ToleranceSummaryText.Text = string.Format(
                "Минимальный Solid: {0:0.###} мм3 · Габариты: {1:0.###} мм · Объем: {2:0.###}% · Контур: {3:0.###} мм · Уверенность: {4:0.##}",
                Options.MinSolidVolumeMm3,
                Options.BoundingBoxToleranceMm,
                Options.VolumeTolerancePercent,
                Options.LoopClosureToleranceMm,
                Options.MinExtrusionConfidence);

            AiSummaryText.Text = Options.UseAiAdvisor
                ? "AI: включен · " + Options.AiConfigPath
                : "AI: выключен";

            ReportSummaryText.Text = string.Format(
                "Отчеты: JSON {0}, CSV {1} · Удаление DWG: {2}",
                OnOff(Options.CreateJsonReport),
                OnOff(Options.CreateCsvReport),
                OnOff(Options.DeleteSourceDwgOnSuccess));
        }

        private static Image CreateIcon(System.Windows.Media.ImageSource source)
        {
            return new Image
            {
                Source = source,
                Width = 28,
                Height = 28,
                Stretch = System.Windows.Media.Stretch.Uniform
            };
        }

        private static string OnOff(bool value)
        {
            return value ? "вкл" : "выкл";
        }
    }
}
