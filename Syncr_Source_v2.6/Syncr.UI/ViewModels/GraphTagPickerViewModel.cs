using System.Collections.ObjectModel;
using System.Linq;
using System;
using Avalonia.Controls;
using System.Collections.Generic;

namespace Syncr.UI.ViewModels
{
    public class SelectableTag : ViewModelBase
    {
        public string MachineName { get; set; } = "";
        public string TagName { get; set; } = "";
        public string FullPath => $"{MachineName} - {TagName}";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }
    }

    public class GraphTagPickerViewModel : ViewModelBase
    {
        public ObservableCollection<SelectableTag> Tags { get; } = new ObservableCollection<SelectableTag>();

        public SimpleCommand<Window> SaveCommand { get; }
        public SimpleCommand<Window> CancelCommand { get; }
        public SimpleCommand SelectAllCommand { get; }
        public SimpleCommand DeselectAllCommand { get; }

        public bool Confirmed { get; private set; }
        public List<string> SelectedTagPaths { get; private set; } = new List<string>();

        public GraphTagPickerViewModel(IEnumerable<string> allTagPaths, IEnumerable<string> previouslySelected)
        {
            var selectedSet = new HashSet<string>(previouslySelected ?? Enumerable.Empty<string>());

            foreach (var path in allTagPaths)
            {
                var parts = path.Split(new[] { " - " }, 2, StringSplitOptions.None);
                Tags.Add(new SelectableTag
                {
                    MachineName = parts.Length > 0 ? parts[0] : "Unknown",
                    TagName = parts.Length > 1 ? parts[1] : path,
                    IsSelected = selectedSet.Contains(path)
                });
            }

            SaveCommand = new SimpleCommand<Window>(w => 
            {
                SelectedTagPaths = Tags.Where(t => t.IsSelected).Select(t => t.FullPath).ToList();
                Confirmed = true;
                w?.Close();
            });

            CancelCommand = new SimpleCommand<Window>(w => w?.Close());
            SelectAllCommand = new SimpleCommand(() => { foreach(var t in Tags) t.IsSelected = true; });
            DeselectAllCommand = new SimpleCommand(() => { foreach(var t in Tags) t.IsSelected = false; });
        }
    }
}
