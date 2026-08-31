using System.Collections.Generic;
using System.ComponentModel;
using System.IO.Ports;
using System.Runtime.CompilerServices;

namespace Syncr.Core.Models
{
    public enum ConnectionType
    {
        Tcp,
        Rtu
    }

    public enum OperationMode
    {
        Master,
        Slave
    }

    public enum ModbusFunctionCode
    {
        ReadHoldingRegisters = 3, // FC03 (4xxxx) - Default
        ReadInputRegisters   = 4, // FC04 (3xxxx)
        ReadCoils            = 1, // FC01 (0xxxx)
        ReadDiscreteInputs   = 2  // FC02 (1xxxx)
    }

    /// <summary>
    /// How the raw register bytes should be interpreted.
    /// </summary>
    public enum TagDataType
    {
        AutoDetect,
        Bool,
        Int16,      // Signed 16-bit integer (1 register)
        UInt16,     // Unsigned 16-bit integer (1 register) — classic default
        Int32,      // Signed 32-bit integer  (2 registers, big-endian)
        UInt32,     // Unsigned 32-bit integer (2 registers, big-endian)
        Float32,    // IEEE 754 single precision (2 registers)
        Float64,    // IEEE 754 double precision (4 registers)
        String8     // 8 registers → 16-char ASCII string (displayed as number hash for charting)
    }

    public class MachineConfig : INotifyPropertyChanged
    {
        public string Name { get; set; } = "Machine 1";
        public byte SlaveId { get; set; } = 1;
        
        private ConnectionType _type = ConnectionType.Tcp;
        public ConnectionType Type 
        { 
            get => _type;
            set { _type = value; OnPropertyChanged(); }
        }

        private OperationMode _mode = OperationMode.Master;
        public OperationMode Mode
        {
            get => _mode;
            set { _mode = value; OnPropertyChanged(); }
        }

        // TCP Settings
        public string IpAddress { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 502;

        // RTU Settings
        public string SerialPort { get; set; } = "COM1";
        public int BaudRate { get; set; } = 9600;
        public Parity Parity { get; set; } = Parity.None;
        public StopBits StopBits { get; set; } = StopBits.One;
        public int DataBits { get; set; } = 8;

        public bool IsEnabled { get; set; } = true;
        public int PollingIntervalMs { get; set; } = 5000;

        public List<MachineTag> Tags { get; set; } = new List<MachineTag>();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class MachineTag : INotifyPropertyChanged
    {
        public ushort Address { get; set; }
        public string Name { get; set; } = "Tag";

        /// <summary>Modbus Function Code (FC01, FC02, FC03, FC04).</summary>
        public ModbusFunctionCode FunctionCode { get; set; } = ModbusFunctionCode.ReadHoldingRegisters;

        /// <summary>How to decode the raw Modbus registers.</summary>
        public TagDataType DataType { get; set; } = TagDataType.AutoDetect;

        /// <summary>Raw value × ScalingFactor = displayed value. Default 1.0 (no change).</summary>
        public double ScalingFactor { get; set; } = 1.0;

        /// <summary>SI unit label shown in UI and tooltips (e.g. "V", "A", "°C").</summary>
        public string SiUnit { get; set; } = "";

        private bool _isPlotted;
        public bool IsPlotted
        {
            get => _isPlotted;
            set { _isPlotted = value; OnPropertyChanged(); }
        }

        private string _color = "#00FFFF";  // Cyan as hex
        public string Color
        {
            get => _color;
            set { _color = value; OnPropertyChanged(); }
        }

        // Legacy — kept for JSON backwards-compatibility; ignored by new decode logic
        public int BitLength { get; set; } = 16;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class MachineDataPoint
    {
        public string MachineName { get; set; } = "";
        public System.DateTime Timestamp { get; set; }
        public Dictionary<string, double> Values { get; set; } = new Dictionary<string, double>();
        public double LatencyMs { get; set; }
    }
}
