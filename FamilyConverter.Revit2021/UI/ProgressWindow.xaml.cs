using System.Globalization;
using System.Windows;
using System.Windows.Threading;

namespace FamilyConverter.Revit2021.UI
{
    public partial class ProgressWindow : Window
    {
        public ProgressWindow()
        {
            InitializeComponent();
            Reset();
        }

        public void Reset()
        {
            CurrentStatusText.Text = "Готовим конвертацию...";
            SecondaryStatusText.Text = "Собираем входные данные и держим допуски под рукой.";
            StageText.Text = string.Format(CultureInfo.InvariantCulture, "Этап 0 из {0}", ProgressStatusTextProvider.StageCount);
            UpdateProgress(0);
            Refresh();
        }

        public void SetActive(int index, string status)
        {
            int stage = ProgressStatusTextProvider.NormalizeStage(index);
            CurrentStatusText.Text = ProgressStatusTextProvider.GetActivePhrase(stage);
            SecondaryStatusText.Text = string.IsNullOrWhiteSpace(status)
                ? ProgressStatusTextProvider.GetDefaultDetail(stage)
                : status;
            StageText.Text = string.Format(CultureInfo.InvariantCulture, "Этап {0} из {1}", stage + 1, ProgressStatusTextProvider.StageCount);
            UpdateProgress(ProgressStatusTextProvider.GetActivePercent(stage));
            Refresh();
        }

        public void Complete(int index)
        {
            int stage = ProgressStatusTextProvider.NormalizeStage(index);
            CurrentStatusText.Text = ProgressStatusTextProvider.GetCompletePhrase(stage);
            SecondaryStatusText.Text = ProgressStatusTextProvider.GetDefaultDetail(stage);
            StageText.Text = string.Format(CultureInfo.InvariantCulture, "Этап {0} из {1}", stage + 1, ProgressStatusTextProvider.StageCount);
            UpdateProgress(ProgressStatusTextProvider.GetCompletePercent(stage));
            Refresh();
        }

        private void UpdateProgress(double value)
        {
            if (value < 0)
            {
                value = 0;
            }
            else if (value > 100)
            {
                value = 100;
            }

            OverallProgressBar.IsIndeterminate = false;
            OverallProgressBar.Value = value;
            ProgressPercentText.Text = value.ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        private void Refresh()
        {
            UpdateLayout();
            Dispatcher.Invoke(DispatcherPriority.Background, new DispatcherOperationCallback(delegate { return null; }), null);
        }
    }
}
