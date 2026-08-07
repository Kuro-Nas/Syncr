using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Syncr.Core.Models;
using Syncr.Core.Services;
using Syncr.UI.Views;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Syncr.UI.ViewModels
{
    public class TagItem : ViewModelBase
    {
        public ushort Address { get; set; }
        public string Name    { get; set; } = "";

        private ModbusFunctionCode _functionCode = ModbusFunctionCode.ReadHoldingRegisters;
        public ModbusFunctionCode FunctionCode
        {
            get => _functionCode;
            set { _functionCode = value; OnPropertyChanged(); }
        }

        private TagDataType _dataType = TagDataType.AutoDetect;
        public TagDataType DataType
        {
            get => _dataType;
            set { _dataType = value; OnPropertyChanged(); }
        }

        private double _scalingFactor = 1.0;
        public double ScalingFactor
        {
            get => _scalingFactor;
            set { _scalingFactor = value; OnPropertyChanged(); }
        }

        private string _siUnit = "";
        public string SiUnit
        {
            get => _siUnit;
            set { _siUnit = value; OnPropertyChanged(); }
        }

        private bool _isPlotted;
        public bool IsPlotted
        {
            get => _isPlotted;
            set { _isPlotted = value; OnPropertyChanged(); }
        }

        private string _color = "#00FFFF";
        public string Color
        {
            get => _color;
            set
            {
                if (_color != value)
                {
                    _color = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ColorBrush));
                    OnColorChanged?.Invoke(this);
                }
            }
        }

        public string ColorBrush => _color;

        public event Action<TagItem>? OnColorChanged;

        public static ObservableCollection<FunctionCodeOption> AllFunctionCodeOptions { get; } =
            new ObservableCollection<FunctionCodeOption>
            {
                new FunctionCodeOption { Code = ModbusFunctionCode.ReadCoils,            DisplayName = "FC01 — Coils (0xxxx Digital R/W)" },
                new FunctionCodeOption { Code = ModbusFunctionCode.ReadDiscreteInputs,   DisplayName = "FC02 — Discrete Inputs (1xxxx Digital Read)" },
                new FunctionCodeOption { Code = ModbusFunctionCode.ReadHoldingRegisters, DisplayName = "FC03 — Holding Regs (4xxxx Analog R/W)" },
                new FunctionCodeOption { Code = ModbusFunctionCode.ReadInputRegisters,   DisplayName = "FC04 — Input Regs (3xxxx Sensor Analog)" }
            };

        // All ModbusFunctionCode options for backwards compatibility
        public static ObservableCollection<ModbusFunctionCode> AllFunctionCodes { get; } =
            new ObservableCollection<ModbusFunctionCode>(
                (ModbusFunctionCode[])Enum.GetValues(typeof(ModbusFunctionCode)));

        // All TagDataType options for the UI combo box
        public static ObservableCollection<TagDataType> AllDataTypes { get; } =
            new ObservableCollection<TagDataType>(
                (TagDataType[])Enum.GetValues(typeof(TagDataType)));
    }

    public class FunctionCodeOption
    {
        public ModbusFunctionCode Code { get; set; }
        public string DisplayName { get; set; } = "";
        public override string ToString() => DisplayName;
    }

    public class TagEditorViewModel : ViewModelBase
    {
        private readonly MachineConfig _machine;
        private readonly Window _ownerWindow;
        public ObservableCollection<TagItem> Tags { get; }

        // ─── Copy Tags From ───────────────────────────────────────────────────────
        /// <summary>All other machines available to copy tags from.</summary>
        public ObservableCollection<MachineConfig> CopySourceMachines { get; } = new ObservableCollection<MachineConfig>();

        private MachineConfig? _selectedCopySource;
        public MachineConfig? SelectedCopySource
        {
            get => _selectedCopySource;
            set { _selectedCopySource = value; OnPropertyChanged(); }
        }

        /// <summary>True when there is at least one other machine to copy tags from.</summary>
        public bool HasCopySourceMachines => CopySourceMachines.Count > 0;

        // ─── Add-row fields ───────────────────────────────────────────────────────
        private string _newAddress = "";
        public string NewAddress
        {
            get => _newAddress;
            set { _newAddress = value; OnPropertyChanged(); }
        }

        private string _newName = "";
        public string NewName
        {
            get => _newName;
            set { _newName = value; OnPropertyChanged(); }
        }

        private ModbusFunctionCode _newFunctionCode = ModbusFunctionCode.ReadHoldingRegisters;
        public ModbusFunctionCode NewFunctionCode
        {
            get => _newFunctionCode;
            set { _newFunctionCode = value; OnPropertyChanged(); }
        }

        private TagDataType _newDataType = TagDataType.AutoDetect;
        public TagDataType NewDataType
        {
            get => _newDataType;
            set { _newDataType = value; OnPropertyChanged(); }
        }

        private double _newScalingFactor = 1.0;
        public double NewScalingFactor
        {
            get => _newScalingFactor;
            set { _newScalingFactor = value; OnPropertyChanged(); }
        }

        private string _newSiUnit = "";
        public string NewSiUnit
        {
            get => _newSiUnit;
            set { _newSiUnit = value; OnPropertyChanged(); }
        }

        private string _newColor = "#00FFFF";
        public string NewColor
        {
            get => _newColor;
            set { _newColor = value; OnPropertyChanged(); }
        }

        // ─── Selected Tag Row Pre-Fill (Requirement 3) ────────────────────────────
        private TagItem? _selectedTagItem;
        public TagItem? SelectedTagItem
        {
            get => _selectedTagItem;
            set
            {
                _selectedTagItem = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsEditingSelectedTag));
                OnPropertyChanged(nameof(AddOrUpdateBtnText));
                if (value != null)
                {
                    // Pre-fill input section with the selected tag's saved settings!
                    NewAddress       = value.Address.ToString();
                    NewName          = value.Name;
                    NewFunctionCode  = value.FunctionCode;
                    NewDataType      = value.DataType;
                    NewScalingFactor = value.ScalingFactor;
                    NewSiUnit        = value.SiUnit;
                    NewColor         = string.IsNullOrWhiteSpace(value.Color) ? "#00FFFF" : value.Color;
                }
            }
        }

        public bool IsEditingSelectedTag => SelectedTagItem != null;
        public string AddOrUpdateBtnText => IsEditingSelectedTag ? "✓ Update" : "＋ Add";
        public string AddOrUpdateBtnBackground => IsEditingSelectedTag ? "#2ecc71" : "#5e81f4";

        // ─── Master Library Import ───────────────────────────────────────────────
        public ObservableCollection<RegisterTemplate> MasterTemplates { get; } = new ObservableCollection<RegisterTemplate>();

        private RegisterTemplate? _selectedMasterTemplate;
        public RegisterTemplate? SelectedMasterTemplate
        {
            get => _selectedMasterTemplate;
            set { _selectedMasterTemplate = value; OnPropertyChanged(); }
        }

        // ─── Commands ─────────────────────────────────────────────────────────────
        public SimpleCommand AddTagCommand                           { get; }
        public SimpleCommand ClearSelectionCommand                   { get; }
        public SimpleCommand<TagItem> RemoveTagCommand              { get; }
        public SimpleCommand<Window> PickColorForExistingCommand    { get; }
        public SimpleCommand<Window> PickColorForNewCommand         { get; }
        public SimpleCommand ImportSelectedTemplateCommand           { get; }
        public SimpleCommand CopyTagsFromMachineCommand             { get; }
        public SimpleCommand<Window> SaveTagsCommand                 { get; }

        public TagEditorViewModel(MachineConfig machine, Window owner, IEnumerable<MachineConfig>? allMachines = null)
        {
            _machine     = machine;
            _ownerWindow = owner;
            Tags = new ObservableCollection<TagItem>();
            foreach (var t in machine.Tags)
            {
                var item = new TagItem
                {
                    Address       = t.Address,
                    Name          = t.Name,
                    FunctionCode  = t.FunctionCode,
                    DataType      = t.DataType,
                    ScalingFactor = t.ScalingFactor,
                    SiUnit        = t.SiUnit,
                    IsPlotted     = t.IsPlotted,
                    Color         = string.IsNullOrWhiteSpace(t.Color) ? "#00FFFF" : t.Color
                };
                item.OnColorChanged += _ => UpdateModel();
                Tags.Add(item);
            }

            // Load Master Library templates from ConfigService
            // Tags is [JsonIgnore] so it's never in config.json — re-attach at runtime.
            try
            {
                var config = new ConfigService().LoadConfig();
                if (config.RegisterLibrary != null)
                {
                    foreach (var tmpl in config.RegisterLibrary)
                    {
                        // Re-attach the full preset tag list by matching the known preset names
                        if (tmpl.Name.Contains("MAX", StringComparison.OrdinalIgnoreCase))
                            tmpl.Tags = ConfigService.GetDefaultGrowattTags();
                        else if (tmpl.Name.Contains("MIN", StringComparison.OrdinalIgnoreCase))
                            tmpl.Tags = ConfigService.GetDefaultGrowattMinTags();

                        MasterTemplates.Add(tmpl);
                    }
                }
            }
            catch { }

            if (MasterTemplates.Count > 0)
                SelectedMasterTemplate = MasterTemplates[0];

            // Populate copy-source list: all machines except the one currently being edited
            if (allMachines != null)
            {
                foreach (var m in allMachines)
                    if (m != machine && m.Tags.Count > 0)
                        CopySourceMachines.Add(m);
            }
            if (CopySourceMachines.Count > 0)
                SelectedCopySource = CopySourceMachines[0];

            AddTagCommand                 = new SimpleCommand(AddOrUpdateTag);
            ClearSelectionCommand         = new SimpleCommand(ClearSelection);
            RemoveTagCommand              = new SimpleCommand<TagItem>(RemoveTag);
            PickColorForExistingCommand   = new SimpleCommand<Window>(async w => await PickColorForTag(SelectedTagItem, w));
            PickColorForNewCommand        = new SimpleCommand<Window>(async w => await PickColorForNew(w));
            ImportSelectedTemplateCommand = new SimpleCommand(ImportSelectedTemplate);
            CopyTagsFromMachineCommand    = new SimpleCommand(CopyTagsFromMachine);
            SaveTagsCommand               = new SimpleCommand<Window>(SaveAndClose);
        }

        // ─── Copy Tags From Machine ───────────────────────────────────────────────
        private void CopyTagsFromMachine()
        {
            if (SelectedCopySource == null || SelectedCopySource.Tags.Count == 0) return;

            // Deep-clone all tags from the source machine into this machine
            Tags.Clear();
            foreach (var srcTag in SelectedCopySource.Tags)
            {
                var item = new TagItem
                {
                    Address       = srcTag.Address,
                    Name          = srcTag.Name,
                    FunctionCode  = srcTag.FunctionCode,
                    DataType      = srcTag.DataType,
                    ScalingFactor = srcTag.ScalingFactor,
                    SiUnit        = srcTag.SiUnit,
                    IsPlotted     = srcTag.IsPlotted,
                    Color         = string.IsNullOrWhiteSpace(srcTag.Color) ? "#00FFFF" : srcTag.Color
                };
                item.OnColorChanged += _ => UpdateModel();
                Tags.Add(item);
            }
            UpdateModel();
        }

        private void ImportSelectedTemplate()
        {
            if (SelectedMasterTemplate == null) return;

            // Full-preset: REPLACE all tags with the complete preset tag set
            if (SelectedMasterTemplate.IsFullPreset)
            {
                Tags.Clear();
                foreach (var srcTag in SelectedMasterTemplate.Tags!)
                {
                    var item = new TagItem
                    {
                        Address       = srcTag.Address,
                        Name          = srcTag.Name,
                        FunctionCode  = srcTag.FunctionCode,
                        DataType      = srcTag.DataType,
                        ScalingFactor = srcTag.ScalingFactor,
                        SiUnit        = srcTag.SiUnit,
                        IsPlotted     = srcTag.IsPlotted,
                        Color         = string.IsNullOrWhiteSpace(srcTag.Color) ? "#00FFFF" : srcTag.Color
                    };
                    item.OnColorChanged += _ => UpdateModel();
                    Tags.Add(item);
                }
                UpdateModel();
                return;
            }

            // Legacy single-tag: append (auto-increment address if conflict)
            ushort nextAddr = SelectedMasterTemplate.DefaultAddress;
            if (Tags.Any(t => t.Address == nextAddr))
                nextAddr = Tags.Count > 0 ? (ushort)(Tags.Max(t => t.Address) + 1) : nextAddr;

            var single = new TagItem
            {
                Address       = nextAddr,
                Name          = SelectedMasterTemplate.Name,
                FunctionCode  = SelectedMasterTemplate.FunctionCode,
                DataType      = SelectedMasterTemplate.DataType,
                ScalingFactor = SelectedMasterTemplate.ScalingFactor,
                SiUnit        = SelectedMasterTemplate.SiUnit,
                IsPlotted     = true,
                Color         = string.IsNullOrWhiteSpace(SelectedMasterTemplate.Color) ? "#00FFFF" : SelectedMasterTemplate.Color
            };
            single.OnColorChanged += _ => UpdateModel();
            Tags.Add(single);
            UpdateModel();
        }

        private void AddOrUpdateTag()
        {
            if (!ushort.TryParse(NewAddress, out var addr) || string.IsNullOrWhiteSpace(NewName)) return;

            if (SelectedTagItem != null)
            {
                // Update existing saved tag with modified properties
                SelectedTagItem.Address       = addr;
                SelectedTagItem.Name          = NewName;
                SelectedTagItem.FunctionCode  = NewFunctionCode;
                SelectedTagItem.DataType      = NewDataType;
                SelectedTagItem.ScalingFactor = NewScalingFactor;
                SelectedTagItem.SiUnit        = NewSiUnit;
                SelectedTagItem.Color         = NewColor;
                UpdateModel();
                ClearSelection();
            }
            else
            {
                // Add new tag
                var item = new TagItem
                {
                    Address       = addr,
                    Name          = NewName,
                    FunctionCode  = NewFunctionCode,
                    DataType      = NewDataType,
                    ScalingFactor = NewScalingFactor,
                    SiUnit        = NewSiUnit,
                    IsPlotted     = true,
                    Color         = NewColor
                };
                item.OnColorChanged += _ => UpdateModel();
                Tags.Add(item);
                UpdateModel();

                // Reset fields
                NewAddress = ""; NewName = ""; NewFunctionCode = ModbusFunctionCode.ReadHoldingRegisters; NewDataType = TagDataType.AutoDetect;
                NewScalingFactor = 1.0; NewSiUnit = "";
            }
        }

        private void ClearSelection()
        {
            SelectedTagItem = null;
            NewAddress = ""; NewName = ""; NewFunctionCode = ModbusFunctionCode.ReadHoldingRegisters; NewDataType = TagDataType.AutoDetect;
            NewScalingFactor = 1.0; NewSiUnit = ""; NewColor = "#00FFFF";
        }

        private void RemoveTag(TagItem item)
        {
            if (Tags.Contains(item)) { Tags.Remove(item); UpdateModel(); }
        }

        private async Task PickColorForTag(TagItem? tag, Window? dialogParent)
        {
            if (tag == null) return;
            var vm  = new ColorPickerViewModel(tag.Color);
            var win = new ColorPickerWindow { DataContext = vm };
            var parent = dialogParent ?? _ownerWindow;
            if (parent != null) await win.ShowDialog(parent);
            if (vm.Confirmed) { tag.Color = vm.SelectedColor; UpdateModel(); }
        }

        private async Task PickColorForNew(Window? dialogParent)
        {
            var vm  = new ColorPickerViewModel(NewColor);
            var win = new ColorPickerWindow { DataContext = vm };
            var parent = dialogParent ?? _ownerWindow;
            if (parent != null) await win.ShowDialog(parent);
            if (vm.Confirmed) NewColor = vm.SelectedColor;
        }

        public void UpdateModel()
        {
            _machine.Tags.Clear();
            foreach (var tag in Tags)
            {
                _machine.Tags.Add(new MachineTag
                {
                    Address       = tag.Address,
                    Name          = tag.Name,
                    FunctionCode  = tag.FunctionCode,
                    DataType      = tag.DataType,
                    ScalingFactor = tag.ScalingFactor,
                    SiUnit        = tag.SiUnit,
                    IsPlotted     = tag.IsPlotted,
                    Color         = tag.Color?.Trim() ?? "#00FFFF"
                });
            }
        }

        private void SaveAndClose(Window window)
        {
            UpdateModel();
            window?.Close();
        }
    }
}
