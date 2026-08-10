using System;
using System.Reflection;
using Autodesk.Revit.UI;
using FamilyConverter.Revit2021.UI;

namespace FamilyConverter.Revit2021
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                try
                {
                    application.CreateRibbonTab(ProductInfo.TabName);
                }
                catch
                {
                    // Revit throws when the tab already exists. Reusing it is expected.
                }

                RibbonPanel panel = GetOrCreatePanel(application);
                string assemblyPath = Assembly.GetExecutingAssembly().Location;

                AddButton(
                    panel,
                    assemblyPath,
                    "FamilyConverterCommand",
                    "Simple\nConvert",
                    typeof(Command),
                    RibbonIconKind.SimpleConvert,
                    "Обычная конвертация выбранного импортированного 3D DWG в геометрию семейства Revit.");

                AddButton(
                    panel,
                    assemblyPath,
                    "FamilyConverterImportConvertCommand",
                    "Import\nConvert",
                    typeof(ImportConvertCommand),
                    RibbonIconKind.SimpleConvert,
                    "Выбрать DWG, DXF или SAT, автоматически импортировать от начала координат, проверить найденную геометрию и построить её в текущем семействе.");

                AddButton(
                    panel,
                    assemblyPath,
                    "FamilyConverterTurboCommand",
                    "Turbo\nFreeForm",
                    typeof(TurboCommand),
                    RibbonIconKind.TurboFreeForm,
                    "Сверхбыстрый режим для тяжелых DWG: Solid сразу создаются как FreeFormElement без предпросмотра и Extrusion.");

                panel.AddSeparator();

                AddButton(
                    panel,
                    assemblyPath,
                    "FamilyConverterReportsFolderCommand",
                    "Reports\nFolder",
                    typeof(OpenReportsFolderCommand),
                    RibbonIconKind.ReportsFolder,
                    "Открыть папку отчетов DWG Converter для текущего семейства.");

                AddButton(
                    panel,
                    assemblyPath,
                    "FamilyConverterDonateCommand",
                    "Donate",
                    typeof(DonateCommand),
                    RibbonIconKind.Donate,
                    "Заглушка будущей ссылки на донат.");

                AddButton(
                    panel,
                    assemblyPath,
                    "FamilyConverterSupportCommand",
                    "Tech\nSupport",
                    typeof(SupportCommand),
                    RibbonIconKind.Support,
                    "Заглушка будущей ссылки на техническую поддержку.");
            }
            catch (Exception ex)
            {
                TaskDialog.Show(ProductInfo.Name, "Не удалось создать кнопки DWG Converter:\n" + ex.Message);
            }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        private static RibbonPanel GetOrCreatePanel(UIControlledApplication application)
        {
            foreach (RibbonPanel panel in application.GetRibbonPanels(ProductInfo.TabName))
            {
                if (string.Equals(panel.Name, ProductInfo.PanelName, StringComparison.OrdinalIgnoreCase))
                {
                    return panel;
                }
            }

            return application.CreateRibbonPanel(ProductInfo.TabName, ProductInfo.PanelName);
        }

        private static PushButton AddButton(
            RibbonPanel panel,
            string assemblyPath,
            string name,
            string text,
            Type commandType,
            RibbonIconKind iconKind,
            string tooltip)
        {
            var buttonData = new PushButtonData(name, text, assemblyPath, commandType.FullName);
            PushButton button = panel.AddItem(buttonData) as PushButton;
            if (button != null)
            {
                button.ToolTip = tooltip;
                SetButtonImage(button, iconKind);
            }

            return button;
        }

        private static void SetButtonImage(PushButton button, RibbonIconKind iconKind)
        {
            try
            {
                button.LargeImage = RibbonIcon.Create(iconKind, 32);
                button.Image = RibbonIcon.Create(iconKind, 16);
            }
            catch
            {
                // Broken icon rendering must not prevent the add-in from loading.
            }
        }
    }
}
