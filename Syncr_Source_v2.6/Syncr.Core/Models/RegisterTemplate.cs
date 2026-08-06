using System;

namespace Syncr.Core.Models
{
    /// <summary>
    /// Master template for reusable register configurations across machines.
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
    }
}
