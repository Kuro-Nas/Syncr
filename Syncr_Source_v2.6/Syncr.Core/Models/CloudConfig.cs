using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Syncr.Core.Models
{
    public class CloudConfig : INotifyPropertyChanged
    {
        private bool _isEnabled;
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(); }
        }

        private string _supabaseUrl = "";
        public string SupabaseUrl
        {
            get => _supabaseUrl;
            set { _supabaseUrl = value; OnPropertyChanged(); }
        }

        private string _supabaseKey = "";
        public string SupabaseKey
        {
            get => _supabaseKey;
            set { _supabaseKey = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
