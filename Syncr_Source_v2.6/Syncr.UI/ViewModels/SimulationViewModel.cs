using Syncr.Core.Models;
using Syncr.Core.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace Syncr.UI.ViewModels
{
    // Model for a single row in the live stream log
    public class SimulationStreamEntry
    {
        public string Timestamp { get; set; } = "";
        public string TagName { get; set; } = "";
        public string CurrentValue { get; set; } = "";
        public string DisplayValue { get; set; } = ""; // Fix for build error
        public string OverrideValue { get; set; } = "";
    }

    public class SimulatedTagViewModel : ViewModelBase
    {
        private readonly ModbusService _service;
        private readonly string _machineName;
        private readonly ushort _address;

        public string Name { get; }
        public string SiUnit { get; }
        public string TagColor { get; }

        private string _currentValue = "0";
        public string CurrentValue
        {
            get => _currentValue;
            set { _currentValue = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayValue)); }
        }

        // Shows value + unit together: "23.50 V"
        public string DisplayValue => string.IsNullOrEmpty(SiUnit) ? CurrentValue : $"{CurrentValue} {SiUnit}";

        private string _value = "0";
        public string Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(); }
        }

        public SimpleCommand ApplyOverrideCommand { get; }

        public SimulatedTagViewModel(string name, ushort address, string machineName, ModbusService service, string siUnit = "", string tagColor = "#00FFFF")
        {
            Name = name;
            _address = address;
            _machineName = machineName;
            _service = service;
            SiUnit = siUnit;
            TagColor = tagColor;

            ApplyOverrideCommand = new SimpleCommand(ApplyOverride);
        }

        private void ApplyOverride()
        {
            if (double.TryParse(Value, out double val))
            {

            }
        }
    }

    public class SimulationViewModel : ViewModelBase
    {
        private readonly ModbusService? _modbusService;
        private readonly ModbusSlaveService? _modbusSlaveService;

        public ObservableCollection<string> MachineNames { get; } = new ObservableCollection<string>();
        public ObservableCollection<SimulatedTagViewModel> SimulatedTags { get; } = new ObservableCollection<SimulatedTagViewModel>();
        public ObservableCollection<SimulationStreamEntry> StreamLog { get; } = new ObservableCollection<SimulationStreamEntry>();

        private string? _selectedMachine;
        public string? SelectedMachine
        {
            get => _selectedMachine;
            set
            {
                _selectedMachine = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCurrentMachineSimulating));
                UpdateTags();
            }
        }

        public bool IsCurrentMachineSimulating
        {
            get => _selectedMachine != null && (_modbusService?.IsMockMachineActive(_selectedMachine) ?? false);
            set
            {
                if (_selectedMachine != null && _modbusService != null)
                {
                    _modbusService.SetMockMachineState(_selectedMachine, value);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsSimulating));
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(StatusColor));
                }
            }
        }

        public SimpleCommand ToggleAllMachinesCommand { get; }

        private void ToggleAllMachines()
        {
            if (_modbusService == null) return;
            bool enable = !IsSimulating;
            foreach (var name in MachineNames)
            {
                _modbusService.SetMockMachineState(name, enable);
            }
            OnPropertyChanged(nameof(IsCurrentMachineSimulating));
            OnPropertyChanged(nameof(IsSimulating));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
        }

        private bool _useBuiltInSlave;
        public bool UseBuiltInSlave
        {
            get => _useBuiltInSlave;
            set { _useBuiltInSlave = value; OnPropertyChanged(); }
        }

        public bool IsMachineSimulating(string machineName) => _modbusService?.IsMockMachineActive(machineName) ?? false;

        public event Action? RequestScrollToBottom;

        public SimpleCommand StartSimulationCommand { get; }
        public SimpleCommand StopSimulationCommand { get; }
        public SimpleCommand TogglePauseScrollCommand { get; }

        private bool _isScrollPaused;
        public string PauseButtonLabel => _isScrollPaused ? "Resume Scroll" : "Pause Scroll";

        public bool IsSimulating => (_modbusService?.ActiveMockMachinesCount ?? 0) > 0;

        public string StatusText => (_modbusService?.ActiveMockMachinesCount ?? 0) > 0 
            ? $"RUNNING ({_modbusService?.ActiveMockMachinesCount ?? 0})" 
            : "STOPPED";
        
        public string StatusColor => (_modbusService?.ActiveMockMachinesCount ?? 0) > 0 ? "#2ecc71" : "#e74c3c";

        public SimulationViewModel(ModbusService? modbusService, ModbusSlaveService? modbusSlaveService)
        {
            _modbusService = modbusService;
            _modbusSlaveService = modbusSlaveService;

            if (_modbusService != null)
            {
                _modbusService.OnConfigChanged += OnConfigChanged;
                _modbusService.OnDataReceived += OnDataReceived;
                LoadMachines();
            }

            ToggleAllMachinesCommand = new SimpleCommand(ToggleAllMachines);
            StartSimulationCommand   = new SimpleCommand(() => { if (_selectedMachine != null && _modbusService != null) { _modbusService.SetMockMachineState(_selectedMachine, true); OnPropertyChanged(nameof(IsCurrentMachineSimulating)); OnPropertyChanged(nameof(IsSimulating)); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(StatusColor)); } });
            StopSimulationCommand    = new SimpleCommand(() => { if (_selectedMachine != null && _modbusService != null) { _modbusService.SetMockMachineState(_selectedMachine, false); OnPropertyChanged(nameof(IsCurrentMachineSimulating)); OnPropertyChanged(nameof(IsSimulating)); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(StatusColor)); } });
            TogglePauseScrollCommand = new SimpleCommand(() => { _isScrollPaused = !_isScrollPaused; OnPropertyChanged(nameof(PauseButtonLabel)); });
        }

        private void OnConfigChanged()
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(LoadMachines);
        }

        private void OnDataReceived(MachineDataPoint point)
        {
            if (point.MachineName != SelectedMachine) return;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                foreach (var kvp in point.Values)
                {
                    var tagVm = SimulatedTags.FirstOrDefault(t => t.Name == kvp.Key);
                    if (tagVm != null)
                    {
                        tagVm.CurrentValue = kvp.Value.ToString("F2");

                        // Add entry to Live Stream Log
                        StreamLog.Insert(0, new SimulationStreamEntry
                        {
                            Timestamp = point.Timestamp.ToString("HH:mm:ss.ff"),
                            TagName = kvp.Key,
                            CurrentValue = tagVm.CurrentValue,
                            DisplayValue = tagVm.DisplayValue,
                            OverrideValue = tagVm.Value
                        });

                        // Keep log to max 50 entries for performance
                        if (StreamLog.Count > 50) StreamLog.RemoveAt(StreamLog.Count - 1);
                        if (!_isScrollPaused) RequestScrollToBottom?.Invoke();
                    }
                }
            });
        }

        private void LoadMachines()
        {
            MachineNames.Clear();
            if (_modbusService?.Config != null)
            {
                foreach (var m in _modbusService.Config)
                {
                    MachineNames.Add(m.Name);
                }
            }

            if (MachineNames.Count > 0 && (string.IsNullOrEmpty(SelectedMachine) || !MachineNames.Contains(SelectedMachine)))
                SelectedMachine = MachineNames[0];
            else if (MachineNames.Count == 0)
            {
                SelectedMachine = null;
                SimulatedTags.Clear();
            }
        }

        private void UpdateTags()
        {
            SimulatedTags.Clear();
            StreamLog.Clear();
            if (_modbusService?.Config == null || SelectedMachine == null) return;
            var machine = _modbusService.Config.FirstOrDefault(m => m.Name == SelectedMachine);
            if (machine != null)
            {
                foreach (var tag in machine.Tags)
                    SimulatedTags.Add(new SimulatedTagViewModel(
                        tag.Name, tag.Address, SelectedMachine, _modbusService,
                        tag.SiUnit ?? "", tag.Color ?? "#00FFFF"));
            }
        }
    }
}
