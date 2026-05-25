using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FamilyConverter.Revit2021.UI
{
    public partial class ProgressWindow : Window
    {
        private readonly ProgressBar[] _bars;
        private readonly TextBlock[] _labels;
        private readonly Brush _activeBrush;
        private readonly Brush _mutedBrush;

        public ProgressWindow()
        {
            InitializeComponent();
            _bars = new[] { Phase0Bar, Phase1Bar, Phase2Bar, Phase3Bar };
            _labels = new[] { Phase0Text, Phase1Text, Phase2Text, Phase3Text };
            _activeBrush = new SolidColorBrush(Color.FromRgb(0x25, 0x15, 0x3D));
            _mutedBrush = new SolidColorBrush(Color.FromRgb(0x66, 0x5E, 0x73));
            Reset();
        }

        public void Reset()
        {
            for (int i = 0; i < _bars.Length; i++)
            {
                SetPending(i);
            }

            Refresh();
        }

        public void SetActive(int index, string status)
        {
            CurrentStatusText.Text = status;
            for (int i = 0; i < _bars.Length; i++)
            {
                if (i < index)
                {
                    SetComplete(i);
                }
                else if (i == index)
                {
                    _bars[i].IsIndeterminate = true;
                    _bars[i].Value = 0;
                    _labels[i].Foreground = _activeBrush;
                    _labels[i].FontWeight = FontWeights.SemiBold;
                }
                else
                {
                    SetPending(i);
                }
            }

            Refresh();
        }

        public void Complete(int index)
        {
            SetComplete(index);
            Refresh();
        }

        private void SetComplete(int index)
        {
            _bars[index].IsIndeterminate = false;
            _bars[index].Value = 100;
            _labels[index].Foreground = _activeBrush;
            _labels[index].FontWeight = FontWeights.Normal;
        }

        private void SetPending(int index)
        {
            _bars[index].IsIndeterminate = false;
            _bars[index].Value = 0;
            _labels[index].Foreground = _mutedBrush;
            _labels[index].FontWeight = FontWeights.Normal;
        }

        private void Refresh()
        {
            UpdateLayout();
            Dispatcher.Invoke(DispatcherPriority.Background, new DispatcherOperationCallback(delegate { return null; }), null);
        }
    }
}
