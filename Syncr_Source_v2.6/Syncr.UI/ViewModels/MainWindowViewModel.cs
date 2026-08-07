using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.Defaults;
using SkiaSharp;
using Syncr.Core;
using Syncr.Core.Models;
using Syncr.Core.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Syncr.UI.Views;
using System.Collections.Concurrent;

namespace Syncr.UI.ViewModels
{
    public class MachineStatusViewModel : ViewModelBase
    {
        private string _name = "";
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        private string _status = "STOPPED";
        public string Status
        {
            get => _status;
            set 
            { 
                _status = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(ToggleSwitchColor));
            }
        }

        private SKColor _statusColor = SKColors.Gray;
        public SKColor StatusColor
        {
            get => _statusColor;
            set 
            { 
                _statusColor = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(ToggleSwitchColor));
            }
        }

        public SKColor ToggleSwitchColor => 
            !IsPlotVisible ? SKColors.Gray : 
            (Status == "RUNNING" ? SKColor.Parse("#2ecc71") : SKColor.Parse("#e06c75"));

        private bool _isPlotVisible = true;
        public bool IsPlotVisible
        {
            get => _isPlotVisible;
            set 
            { 
                _isPlotVisible = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(ToggleSwitchColor));
                OnVisibilityChanged?.Invoke(this);
            }
        }

        public event Action<MachineStatusViewModel>? OnVisibilityChanged;

        public ObservableCollection<TagValueViewModel> Tags { get; } = new ObservableCollection<TagValueViewModel>();
    }

    public class TagValueViewModel : ViewModelBase
    {
        private string _name = "";
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        private string _value = "---";
        public string Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(); }
        }

        private string _siUnit = "";
        public string SiUnit
        {
            get => _siUnit;
            set { _siUnit = value; OnPropertyChanged(); }
        }

        // Tag display color (hex string from tag config) for Live Metrics card
        private string _tagColor = "#00FFFF";
        public string TagColor
        {
            get => _tagColor;
            set { _tagColor = value; OnPropertyChanged(); }
        }

        private string _machineName = "";
        public string MachineName
        {
            get => _machineName;
            set { _machineName = value; OnPropertyChanged(); }
        }

        private string _assignedGraphText = "";
        public string AssignedGraphText
        {
            get => _assignedGraphText;
            set { _assignedGraphText = value; OnPropertyChanged(); }
        }
    }

    public class GraphPanelViewModel : ViewModelBase
    {
        // Serial display number (stable, doesn't change on delete/add cycles)
        public int SerialNumber { get; set; }

        private string _badgeText = "";
        public string BadgeText
        {
            get => _badgeText;
            set { _badgeText = value; OnPropertyChanged(); }
        }

        private string _title = "";
        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        // Device (machine) name shown in graph header
        private string _deviceName = "";
        public string DeviceName
        {
            get => _deviceName;
            set { _deviceName = value; OnPropertyChanged(); }
        }

        public DateTime LastInteractionTime { get; set; } = DateTime.MinValue;

        // SI Unit shown if singular
        private string _unitLabel = "";
        public string UnitLabel
        {
            get => _unitLabel;
            set { _unitLabel = value; OnPropertyChanged(); }
        }

        // Live value shown in header
        private string _headerLiveValue = "";
        public string HeaderLiveValue
        {
            get => _headerLiveValue;
            set { _headerLiveValue = value; OnPropertyChanged(); }
        }

        private string _headerColor = "#22d3ee"; // Default Cyan
        public string HeaderColor
        {
            get => _headerColor;
            set { _headerColor = value; OnPropertyChanged(); }
        }

        public ObservableCollection<ISeries> Series { get; set; } = new ObservableCollection<ISeries>();
        public ObservableCollection<Axis> XAxes { get; set; } = new ObservableCollection<Axis>();
        public ObservableCollection<Axis> YAxes { get; set; } = new ObservableCollection<Axis>();
        
        public List<string> AssignedTags { get; set; } = new List<string>();
        public ObservableCollection<TagValueViewModel> HeaderMetrics { get; set; } = new ObservableCollection<TagValueViewModel>();

        private bool _isExpanded;
        public bool IsExpanded 
        {
            get => _isExpanded;
            set 
            {
                _isExpanded = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ExpandIcon));
            }
        }
        // Pi-compatible icons (no emoji — standard Unicode arrows work on all platforms)
        public string ExpandIcon => IsExpanded ? "\u25b4" : "\u25be"; // ▴ / ▾
        public string FocusIcon => "M4,4H10V6H6V10H4V4M20,4V10H18V6H14V4H20M20,20H14V18H18V14H20V20M4,20V14H6V18H10V20H4Z";

        /// <summary>Display name shown in Focus Mode selection list (e.g. "G1 – Voltage").</summary>
        public string FocusDisplayName => $"G{SerialNumber} \u2013 {Title}";

        private bool _isFocused;
        public bool IsFocused
        {
            get => _isFocused;
            set { _isFocused = value; OnPropertyChanged(); }
        }

        private double _gridWidth = double.NaN;
        public double GridWidth
        {
            get => _gridWidth;
            set { _gridWidth = value; OnPropertyChanged(); }
        }

        private double _chartHeight = 400;
        public double ChartHeight
        {
            get => _chartHeight;
            set { _chartHeight = value; OnPropertyChanged(); }
        }

        private double _focusWidth = 400;
        public double FocusWidth
        {
            get => _focusWidth;
            set { _focusWidth = value; OnPropertyChanged(); }
        }

        private double _focusHeight = 400;
        public double FocusHeight
        {
            get => _focusHeight;
            set { _focusHeight = value; OnPropertyChanged(); }
        }

        public SimpleCommand<Window>? AssignTagsCommand { get; set; }
        public SimpleCommand<GraphPanelViewModel>? DeleteGraphCommand { get; set; }
        public SimpleCommand<GraphPanelViewModel>? ToggleExpandCommand { get; set; }

        // Helper properties for X-Axis binding
        public Func<double, string> XAxisLabeler => value => 
        {
            if (value < DateTime.MinValue.Ticks || value > DateTime.MaxValue.Ticks) return "";
            var dt = new DateTime((long)value);
            return dt.Date != DateTime.Today ? dt.ToString("MMM dd HH:mm") : dt.ToString("HH:mm:ss");
        };
        public double XAxisUnitWidth => TimeSpan.FromSeconds(1).Ticks;
        public double XAxisMinStep => TimeSpan.FromSeconds(1).Ticks;
    }

    public class MainWindowViewModel : ViewModelBase
    {
        private bool _isFocusModeActive;
        public bool IsFocusModeActive
        {
            get => _isFocusModeActive;
            set { _isFocusModeActive = value; OnPropertyChanged(); }
        }
        
        public ObservableCollection<GraphPanelViewModel> FocusGraphs { get; set; } = new ObservableCollection<GraphPanelViewModel>();
        public List<List<GraphPanelViewModel>> FocusRows { get; set; } = new List<List<GraphPanelViewModel>>();

        // Tiling Logic Properties (v3.5 Centered Formatting)
        public double FocusItemHeight
        {
            get
            {
                if (FocusGraphs.Count == 0) return 400;
                int rowCount = (int)Math.Ceiling(FocusGraphs.Count / 2.0);
                
                // Minimized offsets (Margins/Padding/Controls recovery)
                double offset = FocusGraphs.Count <= 2 ? 40 : 65; 
                return (AvailableGraphHeight - offset) / (rowCount == 0 ? 1 : rowCount);
            }
        }

        public double FocusItemWidth
        {
            get
            {
                if (FocusGraphs == null || FocusGraphs.Count == 0) return 400;
                
                // Total Space Recovery: Sidebar(280) + RightPanel(250) + Margin(25) = 555px
                double fullWidth = AvailableGraphWidth + 555; 
                double overlayMargins = 30; // 10 Margin * 2 + 5 Padding * 2
                double usableSpace = fullWidth - overlayMargins;

                if (FocusGraphs.Count == 1) return usableSpace - 10;
                // Strict 2-column layout
                return (usableSpace / 2) - 15; 
            }
        }

        private void UpdateFocusLayout()
        {
            if (FocusGraphs == null) return;
            
            double height = FocusItemHeight;
            double baseWidth = FocusItemWidth;

            // Update row-grouping for centering orbits
            var rows = new List<List<GraphPanelViewModel>>();
            for (int i = 0; i < FocusGraphs.Count; i += 2)
            {
                var row = FocusGraphs.Skip(i).Take(2).ToList();
                rows.Add(row);
            }
            FocusRows = rows;
            OnPropertyChanged(nameof(FocusRows));

            foreach (var panel in FocusGraphs)
            {
                panel.FocusHeight = height;
                panel.FocusWidth = baseWidth;
            }
        }

        public SimpleCommand<object> EnterFocusModeCommand { get; set; }
        public SimpleCommand<object> ExitFocusModeCommand { get; set; }
        public SimpleCommand<GraphPanelViewModel> ToggleGraphFocusCommand { get; set; }
        public SimpleCommand<GraphPanelViewModel> MoveFocusGraphUpCommand { get; set; }
        public SimpleCommand<GraphPanelViewModel> MoveFocusGraphDownCommand { get; set; }
        public SimpleCommand<GraphPanelViewModel> ResetGraphZoomCommand { get; set; }
        public event Action? RequestResetInteraction;
        
        private readonly ModbusService _modbusService;
        private readonly ModbusSlaveService _modbusSlaveService;
        private readonly DataStore _dataStore;
        private readonly ConfigService _configService;
        private readonly SupabaseService _supabaseService;
        private AppConfig _appConfig;

        private string _statusMessage = "Initializing...";
        public string StatusMessage
        {
            get => _statusMessage;
            set 
            { 
                _statusMessage = value; 
                OnPropertyChanged(); 
                AddToLog(value);
            }
        }

        public ObservableCollection<string> StatusLog { get; } = new ObservableCollection<string>();
        public string FullStatusLog => string.Join(Environment.NewLine, StatusLog);

        private void AddToLog(string message)
        {
            Dispatcher.UIThread.Post(() => {
                if (string.IsNullOrEmpty(message)) return;
                StatusLog.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
                if (StatusLog.Count > 100) StatusLog.RemoveAt(0);
                OnPropertyChanged(nameof(FullStatusLog));
            });
        }

        private string _latency = "No Connection";
        public string Latency
        {
            get => _latency;
            set { _latency = value; OnPropertyChanged(); }
        }

        private string _connectionState = "Disconnected";
        public string ConnectionState
        {
            get => _connectionState;
            set { _connectionState = value; OnPropertyChanged(); }
        }

        // --- Terminal Visibility (v3.1) ---
        private bool _isTerminalVisible = false;
        public bool IsTerminalVisible
        {
            get => _isTerminalVisible;
            set 
            {
                _isTerminalVisible = value; 
                OnPropertyChanged();
                OnPropertyChanged(nameof(TerminalToggleIcon));
                OnPropertyChanged(nameof(TerminalToggleText));
            }
        }
        public string TerminalToggleIcon => IsTerminalVisible ? "M12 8l-6 6 1.41 1.41L12 10.83l4.59 4.58L18 14z" : "M16.59 8.59L12 13.17 7.41 8.59 6 10l6 6 6-6z";
        public string TerminalToggleText => IsTerminalVisible ? "Hide Logs" : "Show Logs";

        // --- Cloud Diagnostics (v3.1) ---
        private string _cloudSyncStatus = "Connecting...";
        public string CloudSyncStatus
        {
            get => _cloudSyncStatus;
            set { _cloudSyncStatus = value; OnPropertyChanged(); }
        }

        private SkiaSharp.SKColor _cloudSyncColor = SkiaSharp.SKColors.Yellow;
        public SkiaSharp.SKColor CloudSyncColor
        {
            get => _cloudSyncColor;
            set { _cloudSyncColor = value; OnPropertyChanged(); }
        }

        private int _queueLength = 0;
        public int QueueLength
        {
            get => _queueLength;
            set { _queueLength = value; OnPropertyChanged(); }
        }

        private string _lastSyncTime = "Never";
        public string LastSyncTime
        {
            get => _lastSyncTime;
            set { _lastSyncTime = value; OnPropertyChanged(); }
        }

        private int _totalSyncs;
        public int TotalSyncs
        {
            get => _totalSyncs;
            set { _totalSyncs = value; OnPropertyChanged(); }
        }

        private int _sessionRetries;
        public int SessionRetries
        {
            get => _sessionRetries;
            set { _sessionRetries = value; OnPropertyChanged(); }
        }

        private string _dataRate = "0.0 pts/s";
        public string DataRate
        {
            get => _dataRate;
            set { _dataRate = value; OnPropertyChanged(); }
        }

        // --- Health Badges (v3.2) ---
        private SkiaSharp.SKColor _hardwareHealthColor = SkiaSharp.SKColors.Gray;
        public SkiaSharp.SKColor HardwareHealthColor
        {
            get => _hardwareHealthColor;
            set { _hardwareHealthColor = value; OnPropertyChanged(); }
        }

        private SkiaSharp.SKColor _cloudHealthColor = SkiaSharp.SKColors.Gray;
        public SkiaSharp.SKColor CloudHealthColor
        {
            get => _cloudHealthColor;
            set { _cloudHealthColor = value; OnPropertyChanged(); }
        }

        private SkiaSharp.SKColor _syncHealthColor = SkiaSharp.SKColors.Gray;
        public SkiaSharp.SKColor SyncHealthColor
        {
            get => _syncHealthColor;
            set { _syncHealthColor = value; OnPropertyChanged(); }
        }

        private int _pointsSinceLastRateCheck = 0;
        private DateTime _lastRateCheck = DateTime.Now;

        private bool _isTerminalExpanded;
        public bool IsTerminalExpanded
        {
            get => _isTerminalExpanded;
            set 
            { 
                _isTerminalExpanded = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(TerminalHeight));
            }
        }
        public int TerminalHeight => IsTerminalExpanded ? 500 : 200;
        private bool _isTerminalEnlarged;
        public bool IsTerminalEnlarged
        {
            get => _isTerminalEnlarged;
            set { _isTerminalEnlarged = value; OnPropertyChanged(); }
        }


        private bool _isMetricsEnlarged;
        public bool IsMetricsEnlarged
        {
            get => _isMetricsEnlarged;
            set { _isMetricsEnlarged = value; OnPropertyChanged(); }
        }

        public SimpleCommand ToggleEnlargeMetricsCommand { get; private set; }

        /// <summary>Flat list of all tags across all machines for the Live Metrics panel.</summary>
        public IEnumerable<TagValueViewModel> AllTags =>
            Machines?.SelectMany(m => m.Tags) ?? Enumerable.Empty<TagValueViewModel>();

        public SimpleCommand ToggleTerminalCommand { get; }
        public SimpleCommand ToggleEnlargeTerminalCommand { get; }

        private DateTime _lastDataReceived = DateTime.MinValue;
        private readonly ConcurrentDictionary<string, DateTime> _machineLastDataTime = new();
        private System.Timers.Timer? _heartbeatTimer = null;
        private bool _isActionsExpanded = true;

        public bool IsActionsExpanded
        {
            get => _isActionsExpanded;
            set { _isActionsExpanded = value; OnPropertyChanged(nameof(IsActionsExpanded)); }
        }

        private DispatcherTimer? _uiHeartbeatTimer = null;
        private readonly ConcurrentDictionary<string, string> _uiValueBuffer = new();
        
        private DateTime _lastChartUpdate = DateTime.MinValue;
        private const int MinChartUpdateIntervalMs = 200; // Throttle chart redraws to 5Hz for Pi
        private const int MaxDataPoints = 1200; // Increased to 1200 to allow 10 minutes of history at 2Hz

        private string _cloudStatus = "Cloud: Disabled";
        public string CloudStatus
        {
            get => _cloudStatus;
            set { _cloudStatus = value; OnPropertyChanged(); }
        }

        private SKColor _cloudStatusColor = SKColors.Gray;
        public SKColor CloudStatusColor
        {
            get => _cloudStatusColor;
            set { _cloudStatusColor = value; OnPropertyChanged(); }
        }

        public ObservableCollection<MachineStatusViewModel> Machines { get; } = new ObservableCollection<MachineStatusViewModel>();

        private MachineStatusViewModel? _selectedMachine;
        public MachineStatusViewModel? SelectedMachine
        {
            get => _selectedMachine;
            set 
            { 
                _selectedMachine = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(SelectedMachineTags));
            }
        }

        /// <summary>Tags belonging to the currently selected machine for the right metrics panel.</summary>
        public IEnumerable<TagValueViewModel> SelectedMachineTags =>
            SelectedMachine?.Tags ?? Enumerable.Empty<TagValueViewModel>();


        private double _availableGraphWidth = 1000;
        public double AvailableGraphWidth
        {
            get => _availableGraphWidth;
            set { _availableGraphWidth = value; UpdateGraphGridWidths(); UpdateFocusLayout(); }
        }

        private double _availableGraphHeight = 500;
        public double AvailableGraphHeight
        {
            get => _availableGraphHeight;
            set { _availableGraphHeight = value; UpdateGraphChartHeights(); UpdateFocusLayout(); }
        }

        public ObservableCollection<GraphPanelViewModel> GraphPanels { get; set; } = new ObservableCollection<GraphPanelViewModel>();
        
        public bool CanAddGraph => GraphPanels.Count < 64;
        public SimpleCommand AddGraphCommand { get; }
        // Tracks which serial numbers are in use so deleting graph 3 and adding new one reuses 3, not 5
        private readonly HashSet<int> _usedSerials = new HashSet<int>();
        private int GetNextSerial()
        {
            int n = 1;
            while (_usedSerials.Contains(n)) n++;
            _usedSerials.Add(n);
            return n;
        }

        private string _currentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        public string CurrentTime
        {
            get => _currentTime;
            set
            {
                _currentTime = value;
                OnPropertyChanged();
            }
        }
        public SimpleCommand ToggleActionsCommand { get; }
        public SimpleCommand OpenConfigCommand { get; }
        public SimpleCommand OpenSimulationCommand { get; }
        public SimpleCommand OpenDataFolderCommand { get; }
        public SimpleCommand OpenCloudConfigCommand { get; }
        public SimpleCommand InstallUpdateCommand   { get; private set; }


        private readonly UpdateService _updateService = new UpdateService();

        private bool _updateAvailable;
        public bool UpdateAvailable
        {
            get => _updateAvailable;
            set { _updateAvailable = value; OnPropertyChanged(); }
        }

        private string _updateBannerText = "";
        public string UpdateBannerText
        {
            get => _updateBannerText;
            set { _updateBannerText = value; OnPropertyChanged(); }
        }

        private bool _isUpdating;
        public bool IsUpdating
        {
            get => _isUpdating;
            set { _isUpdating = value; OnPropertyChanged(); }
        }

        private double _updateProgress;
        public double UpdateProgress
        {
            get => _updateProgress;
            set { _updateProgress = value; OnPropertyChanged(); }
        }

        private string _updateProgressText = "";
        public string UpdateProgressText
        {
            get => _updateProgressText;
            set { _updateProgressText = value; OnPropertyChanged(); }
        }

        public string AppVersionDisplay => SyncrVersion.Display;


        public MainWindowViewModel()
        {
            _configService = new ConfigService();
            _dataStore = new DataStore();
            _appConfig = _configService.LoadConfig();

            // Apply saved theme preference on launch
            if (Avalonia.Application.Current != null)
            {
                Avalonia.Application.Current.RequestedThemeVariant = _appConfig.IsDarkTheme 
                    ? Avalonia.Styling.ThemeVariant.Dark 
                    : Avalonia.Styling.ThemeVariant.Light;
            }
            
            _modbusService = new ModbusService(_appConfig, useMock: false);
            _modbusService.OnDataReceived += OnDataReceived;
            _modbusService.OnConnectionError += OnConnectionError;

            _modbusSlaveService = new ModbusSlaveService(_appConfig, _modbusService);
            _modbusSlaveService.OnDataWritten += OnDataReceived;
            _modbusSlaveService.OnLog += (msg) => StatusMessage = msg;

            _supabaseService = new SupabaseService(_appConfig.Cloud);
            _supabaseService.OnStatusChanged += OnCloudStatusChanged;
            _supabaseService.OnTelemetryUpdated += UpdateCloudTelemetry;

            OpenConfigCommand = new SimpleCommand(OpenConfig);
            OpenSimulationCommand = new SimpleCommand(OpenSimulation);
            OpenDataFolderCommand = new SimpleCommand(OpenDataFolder);
            OpenCloudConfigCommand = new SimpleCommand(OpenCloudConfig);


            _updateService.OnLog += (msg) => StatusMessage = msg;
            _updateService.OnUpdateAvailable += (rel) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    UpdateAvailable   = true;
                    UpdateBannerText  = $"Update available: {rel.Version} - Click to install";
                    StatusMessage     = $"Update available: {rel.Version}  ({rel.PublishedAt:d MMM yyyy})";
                });
            };
            _updateService.OnDownloadProgress += (pct) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    UpdateProgress     = pct;
                    UpdateProgressText = $"Downloading... {pct * 100:F0}%";
                });
            };

            InstallUpdateCommand = new SimpleCommand(async () =>
            {
                if (_updateService.LatestRelease == null) return;
                IsUpdating        = true;
                UpdateProgressText = "Starting download...";
                string? zip = await _updateService.DownloadUpdateAsync(
                    _updateService.LatestRelease.DownloadUrl);
                if (zip == null)
                {
                    IsUpdating        = false;
                    UpdateProgressText = "Download failed — check Status Log";
                    return;
                }
                UpdateProgressText = "Applying update...";
                // This will exit the process — updater script restarts SYNCR
                _updateService.ApplyUpdateAndRestart(zip);
            });


            _ = Task.Run(async () =>
            {
                await Task.Delay(5000);
                await _updateService.CheckForUpdateAsync();
            });


            ToggleTerminalCommand = new SimpleCommand(() => 
            {
                IsTerminalVisible = !IsTerminalVisible;
                if (!IsTerminalVisible) IsTerminalEnlarged = false;
            });
            ToggleEnlargeTerminalCommand = new SimpleCommand(() => 
            {
                IsTerminalEnlarged = !IsTerminalEnlarged;
                if (IsTerminalEnlarged) IsTerminalVisible = true;
            });

            ToggleEnlargeMetricsCommand = new SimpleCommand(() =>
            {
                IsMetricsEnlarged = !IsMetricsEnlarged;
            });

            ToggleActionsCommand = new SimpleCommand(() => IsActionsExpanded = !IsActionsExpanded);

            AddGraphCommand = new SimpleCommand(AddNewGraphPanel);

            FocusGraphs.CollectionChanged += (s, e) => 
            {
                OnPropertyChanged(nameof(FocusItemWidth));
                OnPropertyChanged(nameof(FocusItemHeight));
                UpdateFocusLayout();
            };

            ExitFocusModeCommand = new SimpleCommand<object>(_ => IsFocusModeActive = false);
            EnterFocusModeCommand = new SimpleCommand<object>(p => 
            {
                IsFocusModeActive = true;
                if (p is GraphPanelViewModel gp && !FocusGraphs.Contains(gp))
                {
                    gp.IsFocused = true;
                    FocusGraphs.Add(gp);
                }
            });
            ToggleGraphFocusCommand = new SimpleCommand<GraphPanelViewModel>(p => 
            {
                if (FocusGraphs.Contains(p))
                {
                    p.IsFocused = false;
                    FocusGraphs.Remove(p);
                }
                else
                {
                    p.IsFocused = true;
                    FocusGraphs.Add(p);
                }
            });

            MoveFocusGraphUpCommand = new SimpleCommand<GraphPanelViewModel>(p => 
            {
                int idx = FocusGraphs.IndexOf(p);
                if (idx > 0) FocusGraphs.Move(idx, idx - 1);
            });

            MoveFocusGraphDownCommand = new SimpleCommand<GraphPanelViewModel>(p => 
            {
                int idx = FocusGraphs.IndexOf(p);
                if (idx < FocusGraphs.Count - 1) FocusGraphs.Move(idx, idx + 1);
            });

            ResetGraphZoomCommand = new SimpleCommand<GraphPanelViewModel>(p => 
            {
                if (p.XAxes.Count > 0)
                {
                    var axis = p.XAxes[0];
                    var now = DateTime.Now.Ticks;
                    axis.MaxLimit = now;
                    axis.MinLimit = now - TimeSpan.FromMinutes(5).Ticks;
                    // Reset interaction time to force auto-scroll to resume for THIS graph
                    p.LastInteractionTime = DateTime.MinValue;
                    RequestResetInteraction?.Invoke();
                }
            });

            // Restore graph layout or create default
            if (_appConfig.GraphLayout != null && _appConfig.GraphLayout.Count > 0)
                RestoreGraphLayout();
            else
            {
                // First-run default: single graph showing all tags
                AddNewGraphPanel();
                GraphPanels[0].AssignedTags = GetAllTagPaths();
                GraphPanels[0].BadgeText = "Graph 1";
                GraphPanels[0].IsExpanded = true;
            }
            UpdateGraphGridWidths();
            UpdateGraphChartHeights();
            RefreshGraphAssociations();


            StartService();
            StartClock();
            StartHeartbeatMonitor();
            StartUiHeartbeat();
        }

        private void StartUiHeartbeat()
        {
            _uiHeartbeatTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _uiHeartbeatTimer.Tick += (s, e) => ProcessUiHeartbeat();
            _uiHeartbeatTimer.Start();
        }

        private void ProcessUiHeartbeat()
        {
            if (_uiValueBuffer.IsEmpty) return;

            // Flush the buffer to the UI models in one go
            foreach (var kvp in _uiValueBuffer)
            {
                var parts = kvp.Key.Split(':');
                if (parts.Length < 2) continue;
                var machineName = parts[0];
                var tagName = parts[1];

                var machine = Machines.FirstOrDefault(m => m.Name == machineName);
                if (machine == null) continue;

                var existingTag = machine.Tags.FirstOrDefault(t => t.Name == tagName);
                if (existingTag != null)
                {
                    existingTag.Value = kvp.Value;
                }
            }

            _uiValueBuffer.Clear();
            OnPropertyChanged(nameof(AllTags));
        }

        private List<string> GetAllTagPaths()
        {
            var list = new List<string>();
            foreach (var m in _appConfig.Machines)
            {
                foreach (var t in m.Tags)
                {
                    list.Add($"{m.Name} - {t.Name}");
                }
            }
            return list;
        }



        /// <summary>Saves all current graph panels to AppConfig and persists to disk.</summary>
        private void SaveGraphLayout()
        {
            try
            {
                _appConfig.GraphLayout = GraphPanels.Select(p => new Syncr.Core.Models.GraphLayoutConfig
                {
                    SerialNumber = p.SerialNumber,
                    Title        = p.Title,
                    BadgeText    = p.BadgeText,
                    IsExpanded   = p.IsExpanded,
                    IsFocused    = p.IsFocused,
                    AssignedTags = new System.Collections.Generic.List<string>(p.AssignedTags)
                }).ToList();
                _configService.SaveConfig(_appConfig);
            }
            catch { /* Non-critical — don't crash on save failure */ }
        }

        /// <summary>Recreates graph panels from the saved layout config on startup.</summary>
        private void RestoreGraphLayout()
        {
            if (_appConfig.GraphLayout == null) return;

            foreach (var saved in _appConfig.GraphLayout)
            {
                // Honour the saved serial number to keep stable IDs
                _usedSerials.Add(saved.SerialNumber);
                var panel = new GraphPanelViewModel
                {
                    SerialNumber = saved.SerialNumber,
                    Title        = saved.Title,
                    BadgeText    = saved.BadgeText,
                    IsExpanded   = saved.IsExpanded,
                    IsFocused    = saved.IsFocused,
                    AssignedTags = new System.Collections.Generic.List<string>(saved.AssignedTags)
                };
                panel.XAxes.Add(CreateTimeAxis());
                panel.YAxes.Add(CreateValueAxis());

                // Restore HeaderMetrics placeholders (values will fill when data arrives)
                foreach (var path in saved.AssignedTags)
                {
                    var pts   = path.Split(" - ", 2);
                    var mName = pts.Length == 2 ? pts[0] : "";
                    var tName = pts.Length == 2 ? pts[1] : pts[0];
                    var tDef  = GetMachineConfig(mName)?.Tags.FirstOrDefault(t => t.Name == tName);
                    panel.HeaderMetrics.Add(new TagValueViewModel
                    {
                        Name        = tName,
                        MachineName = mName,
                        SiUnit      = tDef?.SiUnit ?? "",
                        TagColor    = string.IsNullOrEmpty(tDef?.Color) ? "#22d3ee" : tDef!.Color,
                        Value       = "—"
                    });
                }

                WireGraphPanelCommands(panel);
                GraphPanels.Add(panel);

                // Populate FocusGraphs for saved favorites
                if (panel.IsFocused) FocusGraphs.Add(panel);
            }

            // Ensure expanded graph (if any) is at the top (Fix v2.3 Logic)
            var expanded = GraphPanels.FirstOrDefault(p => p.IsExpanded);
            if (expanded != null)
            {
                int oldIdx = GraphPanels.IndexOf(expanded);
                if (oldIdx > 0) GraphPanels.Move(oldIdx, 0);
            }

            OnPropertyChanged(nameof(CanAddGraph));
        }

        private Axis CreateTimeAxis()
        {
            bool isDark = _appConfig?.IsDarkTheme ?? true;
            var labelColor = isDark ? SkiaSharp.SKColor.Parse("#94A3B8") : SkiaSharp.SKColor.Parse("#6B5D45");
            var gridColor  = isDark ? SkiaSharp.SKColor.Parse("#334155") : SkiaSharp.SKColor.Parse("#C7B78E");

            return new Axis
            {
                LabelsPaint = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(labelColor),
                SeparatorsPaint = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(gridColor) { StrokeThickness = 1 },
                Labeler = value => 
                {
                    if (value < DateTime.MinValue.Ticks || value > DateTime.MaxValue.Ticks) return "";
                    var dt = new DateTime((long)value);
                    if (dt.Date != DateTime.Today) return dt.ToString("MMM dd HH:mm");
                    return dt.ToString("HH:mm:ss");
                },
                LabelsRotation = 0,
                UnitWidth = TimeSpan.FromSeconds(1).Ticks, 
                MinStep = TimeSpan.FromSeconds(1).Ticks,
                MinLimit = null,
                MaxLimit = null
            };
        }

        private Axis CreateValueAxis()
        {
            bool isDark = _appConfig?.IsDarkTheme ?? true;
            var labelColor = isDark ? SkiaSharp.SKColor.Parse("#94A3B8") : SkiaSharp.SKColor.Parse("#6B5D45");
            var gridColor  = isDark ? SkiaSharp.SKColor.Parse("#334155") : SkiaSharp.SKColor.Parse("#C7B78E");

            return new Axis
            {
                LabelsPaint = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(labelColor),
                SeparatorsPaint = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(gridColor) { StrokeThickness = 1 },
                MinLimit = null,
                MaxLimit = null,
                Labeler = val => Math.Abs(val) >= 1000 ? $"{val / 1000.0:F1}k" : val.ToString("F1")
            };
        }

        private void AddNewGraphPanel()
        {
            // Collapse all existing graphs before adding the new one
            foreach (var existing in GraphPanels)
                existing.IsExpanded = false;

            int serial = GetNextSerial();
            var panel = new GraphPanelViewModel
            {
                SerialNumber = serial,
                Title = $"Graph {serial}"
            };
            panel.XAxes.Add(CreateTimeAxis());
            panel.YAxes.Add(CreateValueAxis());

            WireGraphPanelCommands(panel);
            GraphPanels.Add(panel);
            panel.BadgeText = $"Graph {serial}";
            OnPropertyChanged(nameof(CanAddGraph));
            UpdateGraphGridWidths();
            UpdateGraphChartHeights();
            RefreshGraphAssociations();
            SaveGraphLayout();
        }

        private bool _isTagPickerOpen = false;

        /// <summary>Attaches all interactive commands to a graph panel (used by both AddNewGraphPanel and RestoreGraphLayout).</summary>
        private void WireGraphPanelCommands(GraphPanelViewModel panel)
        {
            panel.AssignTagsCommand = new SimpleCommand<Window>(async w =>
            {
                if (_isTagPickerOpen || w == null) return;
                _isTagPickerOpen = true;
                try
                {
                    var vm = new GraphTagPickerViewModel(GetAllTagPaths(), panel.AssignedTags);
                    var picker = new GraphTagPickerWindow { DataContext = vm };
                    await picker.ShowDialog(w);
                    if (vm.Confirmed)
                    {
                        panel.AssignedTags = vm.SelectedTagPaths;
                        // Cleanup removed series
                        var toRemove = panel.Series.Where(s => s.Name != null && !panel.AssignedTags.Contains(s.Name)).ToList();
                        foreach (var s in toRemove) panel.Series.Remove(s);

                        // Refined naming (v2.1)
                        if (panel.AssignedTags.Count == 1)
                        {
                            var parts = panel.AssignedTags[0].Split(" - ", 2);
                            panel.Title = parts.Length == 2 ? parts[1] : panel.AssignedTags[0];
                            panel.DeviceName = parts.Length == 2 ? parts[0] : "";
                            var tagDef = GetMachineConfig(panel.DeviceName)?.Tags.FirstOrDefault(t => t.Name == (parts.Length == 2 ? parts[1] : parts[0]));
                            panel.UnitLabel = tagDef?.SiUnit ?? "";
                        }
                        else
                        {
                            var firstParts = panel.AssignedTags.Count > 0 ? panel.AssignedTags[0].Split(" - ", 2) : new[] { "" };
                            panel.DeviceName = firstParts.Length == 2 ? firstParts[0] : "";
                            panel.UnitLabel = "";
                        }

                        // Re-populate HeaderMetrics for multi-metric display
                        panel.HeaderMetrics.Clear();
                        foreach (var path in panel.AssignedTags)
                        {
                            var pts  = path.Split(" - ", 2);
                            var mName = pts.Length == 2 ? pts[0] : "";
                            var tName = pts.Length == 2 ? pts[1] : pts[0];
                            var tDef  = GetMachineConfig(mName)?.Tags.FirstOrDefault(t => t.Name == tName);
                            panel.HeaderMetrics.Add(new TagValueViewModel
                            {
                                Name        = tName,
                                MachineName = mName,
                                SiUnit      = tDef?.SiUnit ?? "",
                                TagColor    = string.IsNullOrEmpty(tDef?.Color) ? "#22d3ee" : tDef!.Color,
                                Value       = "0.00"
                            });
                        }

                        RefreshGraphAssociations();
                        SaveGraphLayout(); // Persist tag assignments
                    }
                }
                finally
                {
                    _isTagPickerOpen = false;
                }
            });

            panel.DeleteGraphCommand = new SimpleCommand<GraphPanelViewModel>(p =>
            {
                if (GraphPanels.Count > 1)
                {
                    _usedSerials.Remove(p.SerialNumber);
                    GraphPanels.Remove(p);
                    OnPropertyChanged(nameof(CanAddGraph));
                    UpdateGraphGridWidths();
                    UpdateGraphChartHeights();
                    SaveGraphLayout(); // Persist deletion
                }
            });

            panel.ToggleExpandCommand = new SimpleCommand<GraphPanelViewModel>(p =>
            {
                bool wasExpanded = p.IsExpanded;
                foreach (var graph in GraphPanels) graph.IsExpanded = false;
                if (!wasExpanded)
                {
                    p.IsExpanded = true;
                    int oldIndex = GraphPanels.IndexOf(p);
                    if (oldIndex > 0) GraphPanels.Move(oldIndex, 0);
                }
                UpdateGraphGridWidths();
                UpdateGraphChartHeights();
                SaveGraphLayout(); // Persist expand state
            });
        }

        private void RefreshGraphAssociations()
        {
            foreach (var machine in Machines)
            {
                foreach (var tag in machine.Tags)
                {
                    var fullPath = $"{machine.Name} - {tag.Name}";
                    var associatedGraphs = GraphPanels
                        .Where(p => p.AssignedTags.Contains(fullPath))
                        .Select(p => $"G{p.SerialNumber}");
                    
                    tag.AssignedGraphText = string.Join(", ", associatedGraphs);
                }
            }
        }

        private void UpdateGraphGridWidths()
        {
            if (GraphPanels == null) return;
            
            foreach (var p in GraphPanels)
            {
                if (GraphPanels.Count == 1 || p.IsExpanded)
                {
                    p.GridWidth = Math.Max(450, AvailableGraphWidth - 20); 
                }
                else
                {
                    p.GridWidth = Math.Max(380, ((AvailableGraphWidth - 20) / 2) - 8); 
                }
            }
        }

        private void UpdateGraphChartHeights()
        {
            if (GraphPanels == null) return;

            foreach (var p in GraphPanels)
            {
                if (GraphPanels.Count == 1)
                {
                    // Single graph: fill all available vertical space minus the graph header (~55px)
                    p.ChartHeight = Math.Max(200, AvailableGraphHeight - 55);
                }
                else if (p.IsExpanded)
                {
                    // Expanded graph in multi-graph mode: take most of the vertical space
                    p.ChartHeight = Math.Max(200, AvailableGraphHeight - 55);
                }
                else
                {
                    // Non-expanded in multi-graph: split roughly in half
                    p.ChartHeight = Math.Max(150, (AvailableGraphHeight / 2) - 60);
                }
            }
        }

        private void StartHeartbeatMonitor()
        {
            _heartbeatTimer = new System.Timers.Timer(1000); 
            _heartbeatTimer.Elapsed += (s, e) => CheckConnectionState();
            _heartbeatTimer.Start();
        }

        private void CheckConnectionState()
        {
            Dispatcher.UIThread.Post(() =>
            {
                var now = DateTime.Now;
                var timeSinceLastData = now - _lastDataReceived;
                
                if (_lastDataReceived == DateTime.MinValue)
                {
                    ConnectionState = "No Connection";
                    HardwareHealthColor = SkiaSharp.SKColors.Gray;
                    Latency = "--";
                }
                else if (timeSinceLastData.TotalSeconds > 10)
                {
                    ConnectionState = "Disconnected";
                    HardwareHealthColor = SkiaSharp.SKColors.OrangeRed;
                    Latency = "Timeout";
                }
                else
                {
                    ConnectionState = "Connected";
                    HardwareHealthColor = SkiaSharp.SKColors.LimeGreen;
                }

                // Per-Machine status evaluation
                foreach (var m in Machines)
                {
                    if (m.Name == "No Machines") continue;
                    if (_machineLastDataTime.TryGetValue(m.Name, out var lastTime))
                    {
                        if ((now - lastTime).TotalSeconds > 10)
                        {
                            m.Status = "TIMEOUT";
                            m.StatusColor = SKColors.Orange;
                        }
                    }
                    else if (_lastDataReceived == DateTime.MinValue)
                    {
                        m.Status = "STOPPED";
                        m.StatusColor = SKColors.Gray;
                    }
                }

                // Update Data Rate (v3.2)
                var elapsedSeconds = (now - _lastRateCheck).TotalSeconds;
                if (elapsedSeconds >= 1.0)
                {
                    double rate = _pointsSinceLastRateCheck / elapsedSeconds;
                    DataRate = $"{rate:F1} pts/s";
                    _pointsSinceLastRateCheck = 0;
                    _lastRateCheck = now;
                }

                // Cloud/Sync Health
                CloudHealthColor = _supabaseService.IsCloudConnected ? SkiaSharp.SKColors.Turquoise : SkiaSharp.SKColors.OrangeRed;
                SyncHealthColor = (_supabaseService.IsCloudConnected && _supabaseService.PendingQueueCount == 0) ? SkiaSharp.SKColors.LimeGreen : 
                                 (_supabaseService.PendingQueueCount > 100 ? SkiaSharp.SKColors.OrangeRed : SkiaSharp.SKColors.Orange);

            }, DispatcherPriority.Background);
        }

        private void OnCloudStatusChanged(string status)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                CloudStatus = $"Cloud: {status}";
                StatusMessage = $"[CLOUD] {status}"; // Send to Terminal Log
                if (status == "Connected" || status == "Data Pushed") CloudStatusColor = SKColors.Green;
                else if (status.StartsWith("Error") || status.StartsWith("Push Error")) CloudStatusColor = SKColors.Red;
                else CloudStatusColor = SKColors.Gray;
            });
            UpdateCloudTelemetry();
        }

        private void UpdateCloudTelemetry()
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                CloudSyncStatus = _supabaseService.IsCloudConnected ? "Online" : "Offline";
                CloudSyncColor = _supabaseService.IsCloudConnected ? SkiaSharp.SKColors.LimeGreen : SkiaSharp.SKColors.OrangeRed;
                QueueLength = _supabaseService.PendingQueueCount;
                LastSyncTime = _supabaseService.LastSyncTime?.ToString("HH:mm:ss") ?? "Never";
                TotalSyncs = _supabaseService.TotalSessionSyncs;
                SessionRetries = _supabaseService.RetryCount;
            });
        }

        private void InitializeMachines()
        {
            // Read directly from _appConfig which is always authoritative.
            // _modbusService.Config is the same reference, but reading from _appConfig
            // is explicit and avoids any confusion about the service state.
            var machines = _appConfig?.Machines;

            Console.WriteLine($"[InitializeMachines] Found {machines?.Count ?? 0} machine(s) in config.");

            Dispatcher.UIThread.Post(() =>
            {
                Machines.Clear();

                if (machines != null && machines.Count > 0)
                {
                    foreach (var config in machines)
                    {
                        var vm = new MachineStatusViewModel { Name = config.Name };
                        vm.OnVisibilityChanged += OnMachineVisibilityChanged;

                        // Pre-populate tags so they show up in Live Metrics immediately
                        foreach (var tag in config.Tags)
                        {
                            vm.Tags.Add(new TagValueViewModel
                            {
                                Name        = tag.Name,
                                Value       = "—",
                                SiUnit      = tag.SiUnit ?? "",
                                TagColor    = string.IsNullOrEmpty(tag.Color) ? "#00FFFF" : tag.Color,
                                MachineName = config.Name
                            });
                        }

                        Machines.Add(vm);
                    }
                }

                if (Machines.Count > 0)
                {
                    SelectedMachine = Machines[0];
                }
                else
                {
                    var placeholder = new MachineStatusViewModel { Name = "No Machines" };
                    Machines.Add(placeholder);
                    SelectedMachine = placeholder;
                }

                OnPropertyChanged(nameof(AllTags));
            });
        }

        private void OnMachineVisibilityChanged(MachineStatusViewModel machine)
        {
            if (machine == null) return;
            
            var now = DateTime.Now;
            if ((now - _lastChartUpdate).TotalMilliseconds < MinChartUpdateIntervalMs) return;
            _lastChartUpdate = now;
            
            foreach (var panel in GraphPanels)
            {
                foreach (var series in panel.Series)
                {
                    if (series.Name != null && series.Name.StartsWith(machine.Name + " -"))
                    {
                        series.IsVisible = machine.IsPlotVisible;
                    }
                }
            }
        }

        private void StartClock()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, e) => CurrentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            timer.Start();
        }

        private void OpenConfig() => RequestOpenConfig?.Invoke();
        private void OpenSimulation() => RequestOpenSimulation?.Invoke(_modbusService, _modbusSlaveService);
        private void OpenCloudConfig() => RequestOpenCloudConfig?.Invoke(_appConfig.Cloud);

        private void OpenDataFolder()
        {
            try
            {
                var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
                if (!System.IO.Directory.Exists(path)) System.IO.Directory.CreateDirectory(path);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch { }
        }

        public event Action? RequestOpenConfig;
        public event Action<ModbusService, ModbusSlaveService>? RequestOpenSimulation;
        public event Action<CloudConfig>? RequestOpenCloudConfig;

        private void StartService()
        {
            StatusMessage = "Starting Services...";

            // v2.6.4 Boot Fix: UpdateConfig populates _modbusService.Config,
            // then InitializeMachines reads it. Previously InitializeMachines was
            // called BEFORE this, so on cold-boot Config was empty and no machines
            // appeared until the user opened Settings and clicked Save & Close.
            if (_appConfig.Machines != null && _appConfig.Machines.Count > 0)
            {
                _modbusService.UpdateConfig(_appConfig);
                StatusMessage = $"Auto-started polling for {_appConfig.Machines.Count} machine(s)";
            }
            else
            {
                _modbusService.Start();
            }

            // Reinitialize machine list NOW that Config is fully populated
            InitializeMachines();

            _modbusSlaveService.Start();
            StatusMessage = "Running";
        }

        private void OnDataReceived(MachineDataPoint data)
        {
            var now = DateTime.Now;
            _lastDataReceived = now;
            _machineLastDataTime[data.MachineName] = now;
            _pointsSinceLastRateCheck++;
            _ = _dataStore.SaveDataAsync(data);

            // v2.0 Sync Logic: Always try to push if cloud is enabled. 
            // The SupabaseService handles its own queuing if the network is down.
            _ = _supabaseService.PushDataAsync(data);
            
            DataReceived?.Invoke(data);
            
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                Latency = $"{(int)data.LatencyMs}ms";
                ConnectionState = "Connected";
                StatusMessage = "Running";
            });

            var machine = Machines.FirstOrDefault(m => m.Name == data.MachineName);
            var machineConfig = _appConfig.Machines.FirstOrDefault(m => m.Name == data.MachineName);

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (machine != null)
                {
                    machine.Status = "RUNNING";
                    machine.StatusColor = SKColors.Green;

                    // Buffer UI Card Updates instead of direct dispatch
                    foreach (var kvp in data.Values)
                    {
                        var parts = kvp.Key.Split(':');
                        var tagName = parts.Length > 1 ? parts[1] : parts[0];
                        var tagValue = kvp.Value.ToString("F2");
                        
                        _uiValueBuffer[$"{data.MachineName}:{tagName}"] = tagValue;
                    }
                }

                // Throttle chart
                if ((now - _lastChartUpdate).TotalMilliseconds < MinChartUpdateIntervalMs) return;
                _lastChartUpdate = now;

                if (machineConfig != null)
                {
                    UpdateCharts(data, machineConfig);
                }
            }, DispatcherPriority.Background);
            
            StatusMessage = "Running";
        }

        private void OnConnectionError(string message)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = message;
                ConnectionState = "Disconnected";

                if (!string.IsNullOrEmpty(message) && message.StartsWith("Connection Error: "))
                {
                    var parts = message.Substring("Connection Error: ".Length).Split(" - ");
                    if (parts.Length > 0)
                    {
                        var machineName = parts[0].Trim();
                        var machine = Machines.FirstOrDefault(m => m.Name == machineName);
                        if (machine != null)
                        {
                            machine.Status = "ERROR";
                            machine.StatusColor = SKColors.OrangeRed;
                        }
                    }
                }
            });
        }

        private void UpdateCharts(MachineDataPoint data, MachineConfig machineConfig)
        {
            try 
            {
                var machineStatus = Machines.FirstOrDefault(m => m.Name == data.MachineName);
                bool isPlotVisible = machineStatus?.IsPlotVisible ?? true;
                long timestampTicks = data.Timestamp.Ticks;

                foreach (var kvp in data.Values)
                {
                    var parts = kvp.Key.Split(':');
                    var tagName = parts.Length > 1 ? parts[1] : parts[0];
                    var fullTagPath = $"{data.MachineName} - {tagName}";

                    var tagDef = machineConfig?.Tags.FirstOrDefault(t => t.Name == tagName);
                    if (tagDef == null || !tagDef.IsPlotted) continue;

                    SKColor color = SKColors.Cyan;
                    if (!string.IsNullOrEmpty(tagDef.Color) && SKColor.TryParse(tagDef.Color, out var parsed))
                        color = parsed;

                    string unitSuffix = string.IsNullOrWhiteSpace(tagDef.SiUnit) ? "" : $" {tagDef.SiUnit}";

                    // Distribute to all panels that listen to this tag
                    foreach (var panel in GraphPanels)
                    {
                        if (!panel.AssignedTags.Contains(fullTagPath)) continue;

                        var series = panel.Series.FirstOrDefault(s => s.Name == fullTagPath) as LineSeries<ObservablePoint>;
                        if (series == null)
                        {
                            var values = new ObservableCollection<ObservablePoint>();
                            series = new LineSeries<ObservablePoint>
                            {
                                Name = fullTagPath,
                                Values = values,
                                Fill = null,
                                Stroke = new SolidColorPaint(color) { StrokeThickness = 2 },
                                GeometryFill = new SolidColorPaint(color),
                                GeometryStroke = new SolidColorPaint(color),
                                GeometrySize = 3,
                                LineSmoothness = 0,
                                IsVisible = isPlotVisible,
                                YToolTipLabelFormatter = chartPoint => $"{tagName}: {chartPoint.Model?.Y}{unitSuffix}"
                            };
                            panel.Series.Add(series);
                        }
                        else
                        {
                            bool colorChanged = false;
                            if (series.Stroke is SolidColorPaint currentPaint)
                            {
                                if (currentPaint.Color != color) colorChanged = true;
                            }
                            else colorChanged = true;

                            if (colorChanged && series != null)
                            {
                                var oldValues = series.Values as ObservableCollection<ObservablePoint>;
                                if (oldValues != null)
                                {
                                    panel.Series.Remove(series);
                                    series = new LineSeries<ObservablePoint>
                                    {
                                        Name = fullTagPath,
                                        Values = oldValues,
                                        Fill = null,
                                        Stroke = new SolidColorPaint(color) { StrokeThickness = 2 },
                                        GeometryFill = new SolidColorPaint(color),
                                        GeometryStroke = new SolidColorPaint(color),
                                        GeometrySize = 3,
                                        LineSmoothness = 0,
                                        IsVisible = isPlotVisible,
                                        YToolTipLabelFormatter = cp => $"{tagName}: {cp.Model?.Y}{unitSuffix}"
                                    };
                                    panel.Series.Add(series);
                                }
                            }
                        }

                        // Update multi-metric header list
                        var metric = panel.HeaderMetrics.FirstOrDefault(m => m.Name == tagName && m.MachineName == data.MachineName);
                        if (metric == null)
                        {
                            // Initialize metric entry for multi-tag graphs (v3.4 Fix)
                            metric = new TagValueViewModel 
                            { 
                                Name = tagName, 
                                MachineName = data.MachineName,
                                SiUnit = tagDef?.SiUnit ?? ""
                            };
                            panel.HeaderMetrics.Add(metric);
                        }
                        
                        metric.TagColor = string.IsNullOrEmpty(tagDef?.Color) ? "#00FFFF" : tagDef!.Color;
                        metric.Value = kvp.Value.ToString("F2");

                        if (panel.AssignedTags.Count > 0 && panel.AssignedTags[0] == fullTagPath)
                        {
                            panel.HeaderLiveValue = kvp.Value.ToString("F2");
                            panel.UnitLabel = tagDef?.SiUnit ?? "";
                            panel.HeaderColor = string.IsNullOrEmpty(tagDef?.Color) ? "#22d3ee" : tagDef!.Color;
                            panel.DeviceName = data.MachineName; // Auto-sync source branding
                        }

                        if (series != null)
                        {
                            var pValues = series.Values as ObservableCollection<ObservablePoint>;
                            if (pValues != null)
                            {
                                pValues.Add(new ObservablePoint(timestampTicks, kvp.Value));
                                if (pValues.Count > MaxDataPoints) pValues.RemoveAt(0);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Chart Error: {ex.Message}";
            }
        }

        public event Action<MachineDataPoint>? DataReceived;

        public MachineConfig? GetMachineConfig(string name)
        {
            return _modbusService.Config.FirstOrDefault(m => m.Name == name);
        }

        public void ReloadConfig()
        {
            _appConfig = _configService.LoadConfig();
            _modbusService.UpdateConfig(_appConfig);
            _supabaseService.ResetConfig(_appConfig.Cloud);
            _modbusSlaveService.Stop();
            _modbusSlaveService.ResetConfig(_appConfig);
            _modbusSlaveService.Start(); 

            // Apply updated theme preference
            if (Avalonia.Application.Current != null)
            {
                Avalonia.Application.Current.RequestedThemeVariant = _appConfig.IsDarkTheme 
                    ? Avalonia.Styling.ThemeVariant.Dark 
                    : Avalonia.Styling.ThemeVariant.Light;
            }

            // Re-apply graph axes theme colors across all active graph panels
            UpdateGraphAxesColors();

            InitializeMachines();
        }

        private void UpdateGraphAxesColors()
        {
            bool isDark = _appConfig?.IsDarkTheme ?? true;
            var labelColor = isDark ? SkiaSharp.SKColor.Parse("#94A3B8") : SkiaSharp.SKColor.Parse("#6B5D45");
            var gridColor  = isDark ? SkiaSharp.SKColor.Parse("#334155") : SkiaSharp.SKColor.Parse("#C7B78E");

            foreach (var panel in GraphPanels)
            {
                foreach (var axis in panel.XAxes)
                {
                    axis.LabelsPaint = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(labelColor);
                    axis.SeparatorsPaint = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(gridColor) { StrokeThickness = 1 };
                }
                foreach (var axis in panel.YAxes)
                {
                    axis.LabelsPaint = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(labelColor);
                    axis.SeparatorsPaint = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(gridColor) { StrokeThickness = 1 };
                }
            }
        }

        public void SaveCloudConfig()
        {
            _configService.SaveConfig(_appConfig);
        }
    }
}
