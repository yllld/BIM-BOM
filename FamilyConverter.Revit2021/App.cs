using System;
using System.Reflection;
using Autodesk.Revit.UI;
using FamilyConverter.Revit2021.UI;

namespace FamilyConverter.Revit2021
{
    public class App : IExternalApplication
    {
        private const string TabName = "ENECA_MEP";
        private const string PanelName = "DWG Converter";

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                try
                {
                    application.CreateRibbonTab(TabName);
                }
                catch
                {
                    // Revit throws when the tab already exists. Reusing it is expected.
                }

                RibbonPanel panel = GetOrCreatePanel(application);
                string assemblyPath = Assembly.GetExecutingAssembly().Location;
                var buttonData = new PushButtonData(
                    "FamilyConverterCommand",
                    "Family\nGeometry",
                    assemblyPath,
                    typeof(Command).FullName);

                PushButton button = panel.AddItem(buttonData) as PushButton;
                if (button != null)
                {
                    button.ToolTip = "Преобразовать выбранный импортированный 3D DWG в геометрию семейства Revit.";
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
                TaskDialog.Show("Family Converter", "Не удалось создать кнопку Family Converter:\n" + ex.Message);
            }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        private static RibbonPanel GetOrCreatePanel(UIControlledApplication application)
        {
            foreach (RibbonPanel panel in application.GetRibbonPanels(TabName))
            {
                if (string.Equals(panel.Name, PanelName, StringComparison.OrdinalIgnoreCase))
                {
                    return panel;
                }
            }

            return application.CreateRibbonPanel(TabName, PanelName);
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
