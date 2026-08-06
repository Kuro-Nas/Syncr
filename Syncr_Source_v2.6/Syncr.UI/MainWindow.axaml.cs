using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Syncr.Core.Models;
using Syncr.UI.ViewModels;
using System;
using System.Collections.Generic;
using SkiaSharp;

namespace Syncr.UI.Views
{
    public partial class MainWindow : Window
    {
        private MainWindowViewModel _viewModel;
        private readonly Dictionary<string, LiveChartsCore.ISeries> _machineSeries = new();
        private DateTime _lastInteractionTime = DateTime.Now;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainWindowViewModel();
            DataContext = _viewModel;

            // Intercept all interactions before LiveCharts can swallow them (Fix for graph snap-back)
            this.AddHandler(Avalonia.Input.InputElement.PointerWheelChangedEvent, (s, e) => UserInteracted(), Avalonia.Interactivity.RoutingStrategies.Tunnel);
            this.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, (s, e) => UserInteracted(), Avalonia.Interactivity.RoutingStrategies.Tunnel);

            var scrollViewer = this.FindControl<ScrollViewer>("GraphScrollViewer");
            if (scrollViewer != null)
            {
                scrollViewer.GetObservable(BoundsProperty).Subscribe(bounds => 
                {
                    if (bounds.Width > 0)
                    {
                        _viewModel.AvailableGraphWidth = bounds.Width;
                    }
                    if (bounds.Height > 0)
                    {
                        _viewModel.AvailableGraphHeight = bounds.Height;
                    }
                });
            }

            _viewModel.DataReceived += OnDataReceived;
            _viewModel.RequestOpenConfig += async () => 
            {
                await new ConfigWindow().ShowDialog(this);
                _viewModel.ReloadConfig();
            };
            _viewModel.RequestOpenSimulation += (service, slaveService) => 
            {
                var vm = new SimulationViewModel(service, slaveService);
                var win = new SimulationWindow { DataContext = vm };
                win.ShowDialog(this);
            };
            _viewModel.RequestOpenCloudConfig += async (config) =>
            {
                var vm = new CloudConfigViewModel(config, () => { });
                var win = new CloudConfigWindow { DataContext = vm };
                // Hook up close action to window close
                var closeActionField = typeof(CloudConfigViewModel).GetField("_closeAction", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                closeActionField?.SetValue(vm, (Action)(() => 
                {
                    win.Close();
                    _viewModel.SaveCloudConfig();
                }));
                
                await win.ShowDialog(this);
            };

            _viewModel.RequestResetInteraction += () => 
            {
                _lastInteractionTime = DateTime.MinValue; // Force immediate reset
            };
        }

        private void OnChartPointerPressed(object sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            UserInteracted(sender);
        }

        private void OnChartPointerWheelChanged(object sender, Avalonia.Input.PointerWheelEventArgs e)
        {
            UserInteracted(sender);
        }

        private void OnChartPointerMoved(object sender, Avalonia.Input.PointerEventArgs e)
        {
            // Only consider move as interaction if mouse is pressed (panning)
            var props = e.GetCurrentPoint(this).Properties;
            if (props.IsLeftButtonPressed || props.IsRightButtonPressed || props.IsMiddleButtonPressed)
            {
                UserInteracted(sender);
            }
        }

        private void UserInteracted(object? sender = null)
        {
            _lastInteractionTime = DateTime.Now;
            
            // If the sender is a control within a graph panel, update that specific panel's timer
            if (sender is Avalonia.Controls.Control control && control.DataContext is GraphPanelViewModel panel)
            {
                panel.LastInteractionTime = DateTime.Now;
            }
            // If sender is null (from global tunnel), try to find the panel under the pointer
            else
            {
                // We'll update the global timer, and individual charts will also update via their direct handlers
            }

            UpdateLevelOfDetail();
        }

        private void UpdateLevelOfDetail()
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var now = DateTime.Now.Ticks;
                foreach (var panel in _viewModel.GraphPanels)
                {
                    if (panel.XAxes.Count > 0)
                    {
                        var axis = panel.XAxes[0];
                        double currentRange = (axis.MaxLimit ?? now) - (axis.MinLimit ?? (now - TimeSpan.FromMinutes(5).Ticks));
                        
                        // If zoomed in to see less than 60 seconds of data, show dots (size 6)
                        // Otherwise hide them (size 0) for max FPS
                        bool isZoomedIn = currentRange < TimeSpan.FromMinutes(1).Ticks;
                        int targetSize = isZoomedIn ? 6 : 0;

                        foreach (var series in panel.Series)
                        {
                            if (series is LiveChartsCore.SkiaSharpView.LineSeries<LiveChartsCore.Defaults.ObservablePoint> lineSeries)
                            {
                                if (lineSeries.GeometrySize != targetSize)
                                {
                                    lineSeries.GeometrySize = targetSize;
                                }
                            }
                        }
                    }
                }
            });
        }

        private void OnDataReceived(MachineDataPoint data)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var now = DateTime.Now;
                var nowTicks = now.Ticks;
                var globalTimeSinceInteraction = now - _lastInteractionTime;

                foreach (var panel in _viewModel.GraphPanels)
                {
                    if (panel.XAxes.Count > 0)
                    {
                        var axis = panel.XAxes[0];
                        var panelTimeSinceInteraction = now - panel.LastInteractionTime;
                        
                        // If user hasn't touched THIS graph for more than 5 minutes (300s), 
                        // reset to live view.
                        if (panelTimeSinceInteraction.TotalSeconds > 300)
                        {
                            axis.MaxLimit = nowTicks;
                            axis.MinLimit = nowTicks - TimeSpan.FromMinutes(5).Ticks;
                        }
                    }
                }
                UpdateLevelOfDetail();
            });
        }
    }
}