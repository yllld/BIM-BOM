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
                var buttonData = new PushButtonData(
                    "FamilyConverterCommand",
                    "Simple\nConvert",
                    assemblyPath,
                    typeof(Command).FullName);

                PushButton button = panel.AddItem(buttonData) as PushButton;
                if (button != null)
                {
                    button.ToolTip = "Обычная конвертация выбранного импортированного 3D DWG в геометрию семейства Revit.";
                    SetButtonImage(button);
                }

                var turboButtonData = new PushButtonData(
                    "FamilyConverterTurboCommand",
                    "Turbo\nFreeForm",
                    assemblyPath,
                    typeof(TurboCommand).FullName);

                PushButton turboButton = panel.AddItem(turboButtonData) as PushButton;
                if (turboButton != null)
                {
                    turboButton.ToolTip = "Сверхбыстрый режим для тяжелых DWG: Solid сразу создаются как FreeFormElement без предпросмотра и Extrusion.";
                    SetButtonImage(turboButton);
                }
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

        private static void SetButtonImage(PushButton button)
        {
            try
            {
                button.LargeImage = RibbonIcon.Create(32);
                button.Image = RibbonIcon.Create(16);
            }
            catch
            {
                // Broken icon rendering must not prevent the add-in from loading.
            }
        }
    }
}
