using Avalonia;
using Avalonia.Styling;
using Syncr.Core;
using Syncr.Core.Models;
using Syncr.Core.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace Syncr.UI.ViewModels
{
    public class ConfigViewModel : ViewModelBase
    {
        private readonly ConfigService _configService;
        private readonly Action? _closeAction;
        private readonly ModbusService? _modbusService;

        public ObservableCollection<MachineConfig> Machines { get; set; }

        public SimpleCommand SaveCommand { get; }
        public SimpleCommand AddMachineCommand { get; }
        public SimpleCommand<MachineConfig> RemoveMachineCommand { get; }
        public SimpleCommand<MachineConfig> EditTagsCommand { get; }
        public SimpleCommand<MachineConfig> AutoDetectBaudCommand { get; }
        public SimpleCommand CheckForUpdatesCommand { get; }
        public SimpleCommand InstallUpdateCommand { get; }
        public SimpleCommand ToggleThemeCommand { get; }

        public event Action<MachineConfig>? RequestEditTags;

        private AppConfig _appConfig;

        private string _baudStatus = "";
        public string BaudStatus
        {
            get => _baudStatus;
            set { _baudStatus = value; OnPropertyChanged(); }
        }

        private bool _isScanningBaud;
        public bool IsScanningBaud
        {
            get => _isScanningBaud;
            set { _isScanningBaud = value; OnPropertyChanged(); }
        }

        private bool _isDarkTheme = true;
        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set
            {
                _isDarkTheme = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ThemeToggleText));
                ApplyTheme(value);
            }
        }
        public string ThemeToggleText => IsDarkTheme ? "Dark Mode" : "Light Mode";

        private void ApplyTheme(bool dark)
        {
            if (Application.Current != null)
            {
                Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            }
        }

        private readonly UpdateService _updateService = new UpdateService();

        public string AppVersionDisplay => SyncrVersion.Display;

        private string _updateStatusText = "Click 'Check for Updates' to verify version";
        public string UpdateStatusText
        {
            get => _updateStatusText;
            set { _updateStatusText = value; OnPropertyChanged(); }
        }

        private bool _updateAvailable;
        public bool UpdateAvailable
        {
            get => _updateAvailable;
            set { _updateAvailable = value; OnPropertyChanged(); }
        }

        private bool _isCheckingUpdates;
        public bool IsCheckingUpdates
        {
            get => _isCheckingUpdates;
            set { _isCheckingUpdates = value; OnPropertyChanged(); }
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

        public ConfigViewModel(Action? closeAction, ModbusService? modbusService = null)
        {
            _closeAction = closeAction;
            _modbusService = modbusService;
            _configService = new ConfigService();
            _appConfig = _configService.LoadConfig();
            Machines = new ObservableCollection<MachineConfig>(_appConfig.Machines);
            IsDarkTheme = _appConfig.IsDarkTheme;

            SaveCommand = new SimpleCommand(Save);
            AddMachineCommand = new SimpleCommand(AddMachine);
            RemoveMachineCommand = new SimpleCommand<MachineConfig>(RemoveMachine);
            EditTagsCommand = new SimpleCommand<MachineConfig>(EditTags);
            AutoDetectBaudCommand = new SimpleCommand<MachineConfig>(async m => await RunAutoBaud(m));
            ToggleThemeCommand = new SimpleCommand(() => IsDarkTheme = !IsDarkTheme);

            _updateService.OnLog += (msg) => UpdateStatusText = msg;
            _updateService.OnUpdateAvailable += (rel) =>
            {
                UpdateAvailable = true;
                UpdateStatusText = $"Update available: {rel.Version} ({rel.PublishedAt:d MMM yyyy})";
            };
            _updateService.OnDownloadProgress += (pct) =>
            {
                UpdateProgress = pct;
                UpdateStatusText = $"Downloading... {pct * 100:F0}%";
            };

            CheckForUpdatesCommand = new SimpleCommand(async () =>
            {
                IsCheckingUpdates = true;
                UpdateStatusText = "Checking GitHub repository for updates...";
                bool found = await _updateService.CheckForUpdateAsync();
                IsCheckingUpdates = false;
                if (!found && string.IsNullOrEmpty(_updateService.CheckError))
                    UpdateStatusText = "SYNCR is up to date!";
                else if (!string.IsNullOrEmpty(_updateService.CheckError))
                    UpdateStatusText = $"Update check error: {_updateService.CheckError}";
            });

            InstallUpdateCommand = new SimpleCommand(async () =>
            {
                if (_updateService.LatestRelease == null) return;
                IsUpdating = true;
                UpdateStatusText = "Downloading update package...";
                string? zip = await _updateService.DownloadUpdateAsync(_updateService.LatestRelease.DownloadUrl);
                if (zip == null)
                {
                    IsUpdating = false;
                    UpdateStatusText = "Download failed — check internet connection";
                    return;
                }
                UpdateStatusText = "Applying update — SYNCR will restart...";
                _updateService.ApplyUpdateAndRestart(zip);
            });
        }

        private void EditTags(MachineConfig machine)
        {
            RequestEditTags?.Invoke(machine);
        }

        private void AddMachine()
        {
            Machines.Add(new MachineConfig { Name = $"Machine {Machines.Count + 1}" });
        }

        private void RemoveMachine(MachineConfig machine)
        {
            if (Machines.Contains(machine)) Machines.Remove(machine);
        }

        private async Task RunAutoBaud(MachineConfig machine)
        {
            if (machine == null || IsScanningBaud) return;
            IsScanningBaud = true;
            BaudStatus = "Scanning...";

            var svc = _modbusService ?? new ModbusService(new AppConfig { Machines = new List<MachineConfig> { machine } });
            var progress = new Progress<string>(s => BaudStatus = s);

            int result = await svc.AutoDetectBaudRateAsync(machine, progress);

            BaudStatus = result > 0 ? $"{result} baud" : "Not found";
            IsScanningBaud = false;
        }

        private void Save()
        {
            _appConfig.Machines = new List<MachineConfig>(Machines);
            _appConfig.IsDarkTheme = IsDarkTheme;
            _configService.SaveConfig(_appConfig);
            _closeAction?.Invoke();
        }
    }
}
