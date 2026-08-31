using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Syncr.Core.Models
{
    /// <summary>
    /// Master template for reusable register configurations across machines.
    /// When Tags is non-null and non-empty this is a full-preset template that
    /// replaces ALL tags on the machine when imported. Otherwise it appends a
    /// single tag (legacy behaviour).
    /// </summary>
    public class RegisterTemplate
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "New Register Template";
        public ushort DefaultAddress { get; set; }
        public ModbusFunctionCode FunctionCode { get; set; } = ModbusFunctionCode.ReadHoldingRegisters;
        public TagDataType DataType { get; set; } = TagDataType.AutoDetect;
        public double ScalingFactor { get; set; } = 1.0;
        public string SiUnit { get; set; } = "";
        public string Color { get; set; } = "#00FFFF";
        public string Category { get; set; } = "General";
        public string Description { get; set; } = "";

        /// <summary>
        /// Full preset tag list (runtime only, NOT persisted to config.json).
        /// When non-null this preset REPLACES all machine tags on import.
        /// </summary>
        [JsonIgnore]
        public List<MachineTag>? Tags { get; set; } = null;

        /// <summary>True when this template is a full machine preset (runtime only).</summary>
        [JsonIgnore]
        public bool IsFullPreset => Tags != null && Tags.Count > 0;
    }
}
