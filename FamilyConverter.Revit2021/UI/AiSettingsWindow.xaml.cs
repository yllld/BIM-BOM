using System.Windows;
using FamilyConverter.Revit2021.Models;
using FamilyConverter.Revit2021.Services;
using Microsoft.Win32;

namespace FamilyConverter.Revit2021.UI
{
    public partial class AiSettingsWindow : Window
    {
        private readonly AiConfigService _aiConfigService;

        public AiSettingsWindow(ConversionOptions defaults, AiConfigService aiConfigService)
        {
            InitializeComponent();
            _aiConfigService = aiConfigService;
            Options = defaults.Clone();

            UseAiCheckBox.IsChecked = Options.UseAiAdvisor;
            AiConfigPathTextBox.Text = Options.AiConfigPath;
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

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            Options.UseAiAdvisor = UseAiCheckBox.IsChecked == true;
            Options.AiConfigPath = AiConfigPathTextBox.Text;

            DialogResult = true;
            Close();
        }
    }
}
