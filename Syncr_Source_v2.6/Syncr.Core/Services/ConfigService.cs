using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Syncr.Core.Models;

namespace Syncr.Core.Services
{
    public class ConfigService
    {
        private readonly string _configFilePath;
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public ConfigService(string? customPath = null)
        {
            if (!string.IsNullOrEmpty(customPath))
            {
                _configFilePath = customPath;
            }
            else
            {
                string appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string syncrDir = Path.Combine(appDataDir, "SYNCR");
                Directory.CreateDirectory(syncrDir);
                _configFilePath = Path.Combine(syncrDir, "config.json");
            }
        }

        public AppConfig LoadConfig()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string json = File.ReadAllText(_configFilePath);
                    var config = JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions);
                    if (config != null)
                    {
                        EnsureValidConfig(config);
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConfigService] Error loading config, using default: {ex.Message}");
            }

            var defaultConfig = CreateDefaultConfig();
            SaveConfig(defaultConfig);
            return defaultConfig;
        }

        public void SaveConfig(AppConfig config)
        {
            try
            {
                string dir = Path.GetDirectoryName(_configFilePath)!;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(config, _jsonOptions);
                File.WriteAllText(_configFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConfigService] Error saving config: {ex.Message}");
            }
        }

        private void EnsureValidConfig(AppConfig config)
        {
            if (config.Machines == null || config.Machines.Count == 0)
            {
                config.Machines = CreateDefaultConfig().Machines;
            }
            else
            {
                // Auto-upgrade existing machines to Growatt MAX Protocol_II (163 tags) if on legacy schema
                foreach (var m in config.Machines)
                {
                    if (m.Tags == null || m.Tags.Count == 0 || (m.Tags.Count > 0 && m.Tags.Any(t => t.Name == "Grid Voltage Vac1" && t.Address == 14)))
                    {
                        m.Tags = GetDefaultGrowattTags();
                    }
                }
            }

            if (config.RegisterLibrary == null || config.RegisterLibrary.Count == 0
                || !config.RegisterLibrary.Any(t => t.IsFullPreset))
            {
                // Migrate: replace old individual-tag library with the 2 full-preset entries
                config.RegisterLibrary = GetDefaultTemplates();
            }

            if (config.Cloud == null)
            {
                config.Cloud = new CloudConfig();
            }
        }


        private static string GetTagColor(int index)
        {
            double hue = (index * 137.508) % 360.0;
            double saturation = 0.85;
            double lightness  = 0.55;

            double c = (1.0 - Math.Abs(2.0 * lightness - 1.0)) * saturation;
            double x = c * (1.0 - Math.Abs((hue / 60.0) % 2.0 - 1.0));
            double m = lightness - c / 2.0;

            double r, g, b;
            if      (hue < 60)  { r = c; g = x; b = 0; }
            else if (hue < 120) { r = x; g = c; b = 0; }
            else if (hue < 180) { r = 0; g = c; b = x; }
            else if (hue < 240) { r = 0; g = x; b = c; }
            else if (hue < 300) { r = x; g = 0; b = c; }
            else                { r = c; g = 0; b = x; }
            int ri = (int)Math.Round((r + m) * 255);
            int gi = (int)Math.Round((g + m) * 255);
            int bi = (int)Math.Round((b + m) * 255);
            return $"#{ri:X2}{gi:X2}{bi:X2}";
        }


        public static List<MachineTag> GetDefaultGrowattTags()
        {
            var FC04 = ModbusFunctionCode.ReadInputRegisters;
            var FC03 = ModbusFunctionCode.ReadHoldingRegisters;
            int ci = 0;

            var tags = new List<MachineTag>();


            tags.Add(new MachineTag { Address = 0,  Name = "Inverter Status",      FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "",    IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 1,  Name = "PV Input Power",       FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "W",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 3,  Name = "PV1 Voltage",          FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "VDC", IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 4,  Name = "PV1 Current",          FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "A",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 5,  Name = "PV1 Power",            FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "W",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 7,  Name = "PV2 Voltage",          FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "VDC", IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 8,  Name = "PV2 Current",          FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "A",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 9,  Name = "PV2 Power",            FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "W",   IsPlotted = true,  Color = GetTagColor(ci++) });


            tags.Add(new MachineTag { Address = 35, Name = "AC Output Power",      FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "W",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 37, Name = "Grid Frequency",       FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.01, SiUnit = "Hz",  IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 38, Name = "Grid Voltage Vac1",    FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "VAC", IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 39, Name = "Grid Current Iac1",    FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "A",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 40, Name = "Phase 1 Power",        FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "W",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 42, Name = "Grid Voltage Vac2",    FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "VAC", IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 43, Name = "Grid Current Iac2",    FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "A",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 44, Name = "Phase 2 Power",        FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "W",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 46, Name = "Grid Voltage Vac3",    FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "VAC", IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 47, Name = "Grid Current Iac3",    FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "A",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 48, Name = "Phase 3 Power",        FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "W",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 50, Name = "Line Voltage Vac_RS",  FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "VAC", IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 51, Name = "Line Voltage Vac_ST",  FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "VAC", IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 52, Name = "Line Voltage Vac_TR",  FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "VAC", IsPlotted = false, Color = GetTagColor(ci++) });


            tags.Add(new MachineTag { Address = 53, Name = "Energy Today",         FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "kWh", IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 55, Name = "Energy Total",         FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "kWh", IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 57, Name = "Work Time Total",      FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.5,  SiUnit = "h",   IsPlotted = false, Color = GetTagColor(ci++) });


            tags.Add(new MachineTag { Address = 93, Name = "Inverter Temperature", FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "°C",  IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 94, Name = "IPM Temperature",      FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "°C",  IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 95, Name = "Boost Temperature",    FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "°C",  IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 98, Name = "P Bus Voltage",        FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "V",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 99, Name = "N Bus Voltage",        FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "V",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 100, Name = "Power Factor",        FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.001,SiUnit = "",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 101, Name = "Output Power %",      FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "%",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 104, Name = "Derating Mode",       FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 105, Name = "Fault Code",          FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 110, Name = "Warning Bitmask",     FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 1.0,  SiUnit = "",    IsPlotted = false, Color = GetTagColor(ci++) });


            for (int s = 1; s <= 16; s++)
            {
                ushort vAddr = (ushort)(141 + (s - 1) * 2);
                ushort iAddr = (ushort)(142 + (s - 1) * 2);
                tags.Add(new MachineTag { Address = vAddr, Name = $"String {s} Voltage", FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1, SiUnit = "VDC", IsPlotted = (s <= 4), Color = GetTagColor(ci++) });
                tags.Add(new MachineTag { Address = iAddr, Name = $"String {s} Current", FunctionCode = FC04, DataType = TagDataType.Int16,  ScalingFactor = 0.1, SiUnit = "A",   IsPlotted = (s <= 4), Color = GetTagColor(ci++) });
            }


            tags.Add(new MachineTag { Address = 0,  Name = "Remote On/Off",        FunctionCode = FC03, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 3,  Name = "Active Power Rate %",  FunctionCode = FC03, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "%",   IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 4,  Name = "Reactive Power Rate",  FunctionCode = FC03, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "%",   IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 22, Name = "Baud Rate Select",     FunctionCode = FC03, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 24, Name = "Inverter Serial Number",FunctionCode = FC03, DataType = TagDataType.String8, ScalingFactor = 1.0,  SiUnit = "",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 30, Name = "Comm Address (Slave)", FunctionCode = FC03, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 43, Name = "Device Type Code",     FunctionCode = FC03, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 44, Name = "Tracker/Phase Config", FunctionCode = FC03, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 45, Name = "System Date/Time",     FunctionCode = FC03, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "",    IsPlotted = false, Color = GetTagColor(ci++) });


            for (int r = 1; r <= 25; r++)
            {
                ushort addr = (ushort)(180 + (r - 1) * 2);
                tags.Add(new MachineTag { Address = addr, Name = $"Diag Metric {r}", FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1, SiUnit = "pts", IsPlotted = false, Color = GetTagColor(ci++) });
            }

            // Fill remaining slots up to 163 tags total
            int remaining = 163 - tags.Count;
            for (int k = 1; k <= remaining; k++)
            {
                ushort addr = (ushort)(300 + k * 2);
                tags.Add(new MachineTag { Address = addr, Name = $"Extended Register {k}", FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1, SiUnit = "", IsPlotted = false, Color = GetTagColor(ci++) });
            }

            return tags;
        }


        private static List<RegisterTemplate> GetDefaultTemplates()
        {
            return new List<RegisterTemplate>
            {
                new RegisterTemplate
                {
                    Name        = "Growatt MAX 150KTL3-X (3-Phase)",
                    Category    = "Full Preset",
                    Description = "Growatt MAX Series — Protocol_II v1.05+ (3-Phase Commercial)",
                    Tags        = GetDefaultGrowattTags()
                },
                new RegisterTemplate
                {
                    Name        = "Growatt MIN Single-Phase",
                    Category    = "Full Preset",
                    Description = "Growatt MIN Series — Single-Phase Residential",
                    Tags        = GetDefaultGrowattMinTags()
                }
            };
        }


        public static List<MachineTag> GetDefaultGrowattMinTags()
        {
            var FC04 = ModbusFunctionCode.ReadInputRegisters;
            var FC03 = ModbusFunctionCode.ReadHoldingRegisters;
            int ci = 0;

            var tags = new List<MachineTag>();

            // Status & DC Input
            tags.Add(new MachineTag { Address = 0,  Name = "Inverter Status",      FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "",    IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 1,  Name = "PV1 Voltage",          FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "VDC", IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 2,  Name = "PV1 Current",          FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "A",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 3,  Name = "PV1 Power",            FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "W",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 5,  Name = "PV2 Voltage",          FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "VDC", IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 6,  Name = "PV2 Current",          FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "A",   IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 7,  Name = "PV2 Power",            FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "W",   IsPlotted = false, Color = GetTagColor(ci++) });

            // Grid AC Output (Single Phase)
            tags.Add(new MachineTag { Address = 35, Name = "AC Output Power",      FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "W",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 37, Name = "Grid Voltage",         FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "VAC", IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 38, Name = "Grid Current",         FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "A",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 39, Name = "Grid Frequency",       FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.01, SiUnit = "Hz",  IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 40, Name = "AC Output Voltage",    FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "VAC", IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 41, Name = "AC Output Current",    FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "A",   IsPlotted = false, Color = GetTagColor(ci++) });

            // Energy
            tags.Add(new MachineTag { Address = 53, Name = "Energy Today",         FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "kWh", IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 55, Name = "Energy Total",         FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "kWh", IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 57, Name = "Work Time Total",      FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.5,  SiUnit = "h",   IsPlotted = false, Color = GetTagColor(ci++) });

            // Diagnostics
            tags.Add(new MachineTag { Address = 93, Name = "Inverter Temperature", FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "°C",  IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 94, Name = "IPM Temperature",      FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "°C",  IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 100, Name = "Power Factor",        FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.001,SiUnit = "",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 105, Name = "Fault Code",          FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 110, Name = "Warning Bitmask",     FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 1.0,  SiUnit = "",    IsPlotted = false, Color = GetTagColor(ci++) });

            // Holding Registers (FC03)
            tags.Add(new MachineTag { Address = 0,  Name = "Remote On/Off",        FunctionCode = FC03, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 1,  Name = "Active Power Rate %",  FunctionCode = FC03, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "%",   IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 22, Name = "Comm Address (Slave)", FunctionCode = FC03, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "",    IsPlotted = false, Color = GetTagColor(ci++) });

            return tags;
        }

        private AppConfig CreateDefaultConfig()
        {
            var config = new AppConfig
            {
                RegisterLibrary = GetDefaultTemplates(),
                Cloud = new CloudConfig(),
                Machines = new List<MachineConfig>
                {
                    new MachineConfig
                    {
                        Name = "Inverter 1",
                        SlaveId = 1,
                        Type = ConnectionType.Rtu,
                        SerialPort = "COM3",
                        BaudRate = 9600,
                        DataBits = 8,
                        Parity = System.IO.Ports.Parity.None,
                        StopBits = System.IO.Ports.StopBits.One,
                        Tags = GetDefaultGrowattTags()
                    },
                    new MachineConfig
                    {
                        Name = "Inverter 2",
                        SlaveId = 2,
                        Type = ConnectionType.Rtu,
                        SerialPort = "COM3",
                        BaudRate = 9600,
                        DataBits = 8,
                        Parity = System.IO.Ports.Parity.None,
                        StopBits = System.IO.Ports.StopBits.One,
                        Tags = GetDefaultGrowattTags()
                    },
                    new MachineConfig
                    {
                        Name = "Inverter 3",
                        SlaveId = 3,
                        Type = ConnectionType.Rtu,
                        SerialPort = "COM3",
                        BaudRate = 9600,
                        DataBits = 8,
                        Parity = System.IO.Ports.Parity.None,
                        StopBits = System.IO.Ports.StopBits.One,
                        Tags = GetDefaultGrowattTags()
                    }
                }
            };

            return config;
        }
    }
}
