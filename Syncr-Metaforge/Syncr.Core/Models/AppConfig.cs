using System.Collections.Generic;

namespace Syncr.Core.Models
{
    /// <summary>
    /// Represents the saved state of a single graph panel, persisted in config.json.
    /// </summary>
    public class GraphLayoutConfig
    {
        public int SerialNumber { get; set; }
        public string Title { get; set; } = "";
        public List<string> AssignedTags { get; set; } = new List<string>();
        public bool IsExpanded { get; set; }
        public bool IsFocused { get; set; }
        public string BadgeText { get; set; } = "";
    }

    public class AppConfig
    {
        public List<MachineConfig> Machines { get; set; } = new List<MachineConfig>();
        public CloudConfig Cloud { get; set; } = new CloudConfig();
        /// <summary>Saved graph panel layout. Empty = use default single-panel view.</summary>
        public List<GraphLayoutConfig> GraphLayout { get; set; } = new List<GraphLayoutConfig>();
        /// <summary>Master library of reusable register definitions.</summary>
        public List<RegisterTemplate> RegisterLibrary { get; set; } = new List<RegisterTemplate>();
        /// <summary>UI Theme choice (true = Dark, false = Light).</summary>
        public bool IsDarkTheme { get; set; } = true;
    }
}
