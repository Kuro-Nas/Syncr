using Avalonia.Controls;
using Syncr.UI.ViewModels;

namespace Syncr.UI.Views
{
    public partial class ConfigWindow : Window
    {
        public ConfigWindow()
        {
            InitializeComponent();
            var vm = new ConfigViewModel(Close);
            vm.RequestEditTags += (machine) =>
            {
                var editorVm = new TagEditorViewModel(machine, this, vm.Machines);
                var win = new TagEditorWindow { DataContext = editorVm };
                win.ShowDialog(this);
            };
            DataContext = vm;
        }
    }
}
