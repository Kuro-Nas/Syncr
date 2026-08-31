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
                config.RegisterLibrary = GetDefaultTemplates();
            }
            else if (!config.RegisterLibrary.Any(t => t.Name.Contains("Elmeasure")))
            {
                // Auto-upgrade: ensure the new Elmeasure LG6400N template is added to existing libraries
                var elmeasureTemplate = GetDefaultTemplates().FirstOrDefault(t => t.Name.Contains("Elmeasure"));
                if (elmeasureTemplate != null)
                {
                    config.RegisterLibrary.Add(elmeasureTemplate);
                }
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
                },
                new RegisterTemplate
                {
                    Name        = "Elmeasure LG6400N (3-Phase Energy Meter)",
                    Category    = "Full Preset",
                    Description = "Elmeasure LG6400N Little Genius — Modbus RTU, 9600 baud, Even parity, FC03 Holding Registers. Addresses 40101-40213 (Parameter 1 group). No external scaling needed — meter outputs SI units directly.",
                    Tags        = GetDefaultElmeasureLGTags()
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
                    // ── Growatt solar inverters (RTU, None parity) ────────────────────
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
                    },

                    // ── Elmeasure LG6400N energy meters (RTU, Even parity, ID 5-9) ──
                    new MachineConfig
                    {
                        Name = "Energy Meter 5",
                        SlaveId = 5,
                        Type = ConnectionType.Rtu,
                        SerialPort = "COM3",
                        BaudRate = 9600,
                        DataBits = 8,
                        Parity = System.IO.Ports.Parity.Even,
                        StopBits = System.IO.Ports.StopBits.One,
                        Tags = GetDefaultElmeasureLGTags()
                    },
                    new MachineConfig
                    {
                        Name = "Energy Meter 6",
                        SlaveId = 6,
                        Type = ConnectionType.Rtu,
                        SerialPort = "COM3",
                        BaudRate = 9600,
                        DataBits = 8,
                        Parity = System.IO.Ports.Parity.Even,
                        StopBits = System.IO.Ports.StopBits.One,
                        Tags = GetDefaultElmeasureLGTags()
                    },
                    new MachineConfig
                    {
                        Name = "Energy Meter 7",
                        SlaveId = 7,
                        Type = ConnectionType.Rtu,
                        SerialPort = "COM3",
                        BaudRate = 9600,
                        DataBits = 8,
                        Parity = System.IO.Ports.Parity.Even,
                        StopBits = System.IO.Ports.StopBits.One,
                        Tags = GetDefaultElmeasureLGTags()
                    },
                    new MachineConfig
                    {
                        Name = "Energy Meter 8",
                        SlaveId = 8,
                        Type = ConnectionType.Rtu,
                        SerialPort = "COM3",
                        BaudRate = 9600,
                        DataBits = 8,
                        Parity = System.IO.Ports.Parity.Even,
                        StopBits = System.IO.Ports.StopBits.One,
                        Tags = GetDefaultElmeasureLGTags()
                    },
                    new MachineConfig
                    {
                        Name = "Energy Meter 9",
                        SlaveId = 9,
                        Type = ConnectionType.Rtu,
                        SerialPort = "COM3",
                        BaudRate = 9600,
                        DataBits = 8,
                        Parity = System.IO.Ports.Parity.Even,
                        StopBits = System.IO.Ports.StopBits.One,
                        Tags = GetDefaultElmeasureLGTags()
                    }
                }
            };

            return config;
        }

        /// <summary>
        /// Elmeasure LG6400N Little Genius — 3-Phase Energy Meter register map.
        /// Source: Elmeasure LG64XX Modbus Register Address User Manual.
        /// All registers are FC03 (Holding Registers), Float32 (2 registers each).
        /// Address convention: Modbus PDU address = register number - 40001.
        ///   e.g. Register 40101 → PDU address 100.
        /// The meter outputs SI units directly — no external scaling factor required.
        /// Communication: 9600 baud, 8 data bits, Even parity, 1 stop bit.
        /// </summary>
        public static List<MachineTag> GetDefaultElmeasureLGTags()
        {
            var FC03 = ModbusFunctionCode.ReadHoldingRegisters;
            int ci = 0;
            var tags = new List<MachineTag>();

            // ── Active Power (W) — registers 40101..40107 ─────────────────────────
            tags.Add(new MachineTag { Address = 100, Name = "Watts Total",        FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "W",    IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 102, Name = "Watts R Phase",      FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "W",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 104, Name = "Watts Y Phase",      FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "W",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 106, Name = "Watts B Phase",      FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "W",    IsPlotted = false, Color = GetTagColor(ci++) });

            // ── Reactive Power (VAR) — registers 40109..40115 ─────────────────────
            tags.Add(new MachineTag { Address = 108, Name = "VAR Total",          FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "VAR",  IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 110, Name = "VAR R Phase",        FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "VAR",  IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 112, Name = "VAR Y Phase",        FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "VAR",  IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 114, Name = "VAR B Phase",        FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "VAR",  IsPlotted = false, Color = GetTagColor(ci++) });

            // ── Power Factor — registers 40117..40123 ─────────────────────────────
            tags.Add(new MachineTag { Address = 116, Name = "PF Average",         FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "",     IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 118, Name = "PF R Phase",         FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "",     IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 120, Name = "PF Y Phase",         FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "",     IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 122, Name = "PF B Phase",         FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "",     IsPlotted = false, Color = GetTagColor(ci++) });

            // ── Apparent Power (VA) — registers 40125..40131 ──────────────────────
            tags.Add(new MachineTag { Address = 124, Name = "VA Total",           FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "VA",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 126, Name = "VA R Phase",         FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "VA",   IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 128, Name = "VA Y Phase",         FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "VA",   IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 130, Name = "VA B Phase",         FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "VA",   IsPlotted = false, Color = GetTagColor(ci++) });

            // ── Line-to-Line Voltage (VLL) — registers 40133..40139 ───────────────
            tags.Add(new MachineTag { Address = 132, Name = "VLL Average",        FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "V",    IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 134, Name = "Voltage Vry",        FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "V",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 136, Name = "Voltage Vyb",        FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "V",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 138, Name = "Voltage Vbr",        FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "V",    IsPlotted = false, Color = GetTagColor(ci++) });

            // ── Line-to-Neutral Voltage (VLN) — registers 40141..40147 ───────────
            tags.Add(new MachineTag { Address = 140, Name = "VLN Average",        FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "V",    IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 142, Name = "Voltage R Phase",    FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "V",    IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 144, Name = "Voltage Y Phase",    FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "V",    IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 146, Name = "Voltage B Phase",    FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "V",    IsPlotted = true,  Color = GetTagColor(ci++) });

            // ── Current (A) — registers 40149..40155 ──────────────────────────────
            tags.Add(new MachineTag { Address = 148, Name = "Avg Current",        FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "A",    IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 150, Name = "Current R Phase",    FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "A",    IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 152, Name = "Current Y Phase",    FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "A",    IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 154, Name = "Current B Phase",    FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "A",    IsPlotted = true,  Color = GetTagColor(ci++) });

            // ── Frequency — register 40157 ────────────────────────────────────────
            tags.Add(new MachineTag { Address = 156, Name = "Frequency",          FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "Hz",   IsPlotted = true,  Color = GetTagColor(ci++) });

            // ── Energy Import — registers 40159..40165 ────────────────────────────
            // Note: Manual prints 400159 etc. (6 digits) but correct PDU addr = 40159 - 40001 = 158
            tags.Add(new MachineTag { Address = 158, Name = "kWh Import",         FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "kWh",  IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 160, Name = "kVAh Import",        FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "kVAh", IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 162, Name = "kVARh Ind Import",   FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "kVARh",IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 164, Name = "kVARh Cap Import",   FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "kVARh",IsPlotted = false, Color = GetTagColor(ci++) });

            // ── Energy Export — registers 40167..40173 ────────────────────────────
            tags.Add(new MachineTag { Address = 166, Name = "kWh Export",         FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "kWh",  IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 168, Name = "kVAh Export",        FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "kVAh", IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 170, Name = "kVARh Ind Export",   FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "kVARh",IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 172, Name = "kVARh Cap Export",   FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "kVARh",IsPlotted = false, Color = GetTagColor(ci++) });

            // ── Total Harmonic Distortion — registers 40185..40195 ────────────────
            tags.Add(new MachineTag { Address = 184, Name = "THD Voltage R",      FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "%",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 186, Name = "THD Voltage Y",      FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "%",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 188, Name = "THD Voltage B",      FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "%",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 190, Name = "THD Current R",      FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "%",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 192, Name = "THD Current Y",      FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "%",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 194, Name = "THD Current B",      FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "%",    IsPlotted = false, Color = GetTagColor(ci++) });

            // ── Demand — registers 40197..40213 ──────────────────────────────────
            tags.Add(new MachineTag { Address = 196, Name = "kW Demand",          FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "kW",   IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 198, Name = "kVA Demand",         FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "kVA",  IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 200, Name = "kVAR Demand",        FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "kVAR", IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 208, Name = "kW Max Demand",      FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "kW",   IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 210, Name = "kVA Max Demand",     FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "kVA",  IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 212, Name = "kVAR Max Demand",    FunctionCode = FC03, DataType = TagDataType.Float32, ScalingFactor = 1.0, SiUnit = "kVAR", IsPlotted = false, Color = GetTagColor(ci++) });

            return tags;
        }
    }
}
