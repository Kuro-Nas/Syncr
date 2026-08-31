using Syncr.Core.Models;
using Syncr.Core.Services;
using System;
using System.Threading.Tasks;

namespace Syncr.UI.ViewModels
{
    public class CloudConfigViewModel : ViewModelBase
    {
        private readonly CloudConfig _config;
        private readonly SupabaseService? _supabaseService;
        private readonly Action? _closeAction;

        public bool IsEnabled
        {
            get => _config.IsEnabled;
            set { _config.IsEnabled = value; OnPropertyChanged(); }
        }

        public string SupabaseUrl
        {
            get => _config.SupabaseUrl;
            set { _config.SupabaseUrl = value; OnPropertyChanged(); }
        }

        public string SupabaseKey
        {
            get => _config.SupabaseKey;
            set { _config.SupabaseKey = value; OnPropertyChanged(); }
        }

        // Test connection feedback
        private string _testStatus = "";
        public string TestStatus
        {
            get => _testStatus;
            set { _testStatus = value; OnPropertyChanged(); }
        }

        private bool _isTesting;
        public bool IsTesting
        {
            get => _isTesting;
            set { _isTesting = value; OnPropertyChanged(); }
        }

        public SimpleCommand SaveCommand        { get; }
        public SimpleCommand TestConnCommand    { get; }

        public CloudConfigViewModel(CloudConfig config, Action? closeAction, SupabaseService? supabaseService = null)
        {
            _config           = config;
            _closeAction      = closeAction;
            _supabaseService  = supabaseService;
            SaveCommand       = new SimpleCommand(Save);
            TestConnCommand   = new SimpleCommand(async () => await TestConnection());
        }

        private async Task TestConnection()
        {
            if (IsTesting || _supabaseService == null) return;
            IsTesting  = true;
            TestStatus = "Testing...";

            // Re-init with current form values before testing
            _config.SupabaseUrl = SupabaseUrl;
            _config.SupabaseKey = SupabaseKey;

            await Task.Delay(500); // Give service time to re-init

            var res = await _supabaseService.TestConnectionAsync();
            TestStatus = res.ok ? "Connected" : $"Error: {res.error}";
            IsTesting  = false;
        }

        private void Save()
        {
            _closeAction?.Invoke();
        }
    }
}
