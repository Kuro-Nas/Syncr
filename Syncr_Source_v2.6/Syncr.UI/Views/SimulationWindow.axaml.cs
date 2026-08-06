using Avalonia.Controls;
using Syncr.UI.ViewModels;

namespace Syncr.UI.Views
{
    public partial class SimulationWindow : Window
    {
        public SimulationWindow()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object? sender, System.EventArgs e)
        {
            if (DataContext is SimulationViewModel vm)
            {
                vm.RequestScrollToBottom += ScrollToBottom;
            }
        }

        private void ScrollToBottom()
        {
            var scrollViewer = this.FindControl<ScrollViewer>("StreamScrollViewer");
            scrollViewer?.ScrollToEnd();
        }

        private void OnCloseClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            this.Close();
        }

        private void OnTitleBarPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            // Only drag if it was a left click
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                this.BeginMoveDrag(e);
            }
        }
    }
}
