using Newtonsoft.Json;
using Syncr.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;

namespace Syncr.Core.Services
{
    public class ConfigService
    {
        private readonly string _configPath;

        public ConfigService()
        {
            _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        }

        public AppConfig LoadConfig()
        {
            if (!File.Exists(_configPath))
            {
                return CreateDefaultConfig();
            }

            try
            {
                string raw = File.ReadAllText(_configPath);
                string json = EncryptionService.Decrypt(raw);

                try
                {
                    var config = JsonConvert.DeserializeObject<AppConfig>(json);
                    if (config != null && config.Machines != null)
                    {
                        if (config.RegisterLibrary == null) config.RegisterLibrary = new List<RegisterTemplate>();
                        if (config.RegisterLibrary.Count == 0) config.RegisterLibrary = GetDefaultTemplates();
                        return config;
                    }
                }
                catch { }

                // Fallback: Try to deserialize as List<MachineConfig> (Old format)
                var machines = JsonConvert.DeserializeObject<List<MachineConfig>>(json);
                if (machines != null)
                {
                    return new AppConfig
                    {
                        Machines = machines,
                        Cloud = new CloudConfig(),
                        RegisterLibrary = GetDefaultTemplates()
                    };
                }

                return CreateDefaultConfig();
            }
            catch
            {
                return CreateDefaultConfig();
            }
        }

        public void SaveConfig(AppConfig config)
        {
            string json = JsonConvert.SerializeObject(config, Formatting.Indented);
            string encrypted = EncryptionService.Encrypt(json);
            File.WriteAllText(_configPath, encrypted);
        }

        // ── Color Generator ──────────────────────────────────────────────────────
        // Uses the golden-angle hue distribution (φ²≈137.508°) for maximum
        // visual differentiation across 163 tag color shades.

        private static string[] _colorPalette;

        public static string GetTagColor(int index)
        {
            if (_colorPalette == null)
                _colorPalette = BuildColorPalette(200);
            return _colorPalette[index % _colorPalette.Length];
        }

        private static string[] BuildColorPalette(int count)
        {
            var palette = new string[count];
            for (int i = 0; i < count; i++)
            {
                double h = (i * 137.508) % 360.0;       // golden angle hue spread
                double s = 0.70 + (i % 3) * 0.08;       // 70%, 78%, 86% cycling
                double l = 0.55 + (i % 5) * 0.04;       // 55%-71% cycling
                palette[i] = HslToHex(h, s, l);
            }
            return palette;
        }

        private static string HslToHex(double h, double s, double l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
            double m = l - c / 2;
            double r, g, b;
            if      (h < 60)  { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else               { r = c; g = 0; b = x; }
            int ri = (int)Math.Round((r + m) * 255);
            int gi = (int)Math.Round((g + m) * 255);
            int bi = (int)Math.Round((b + m) * 255);
            return $"#{ri:X2}{gi:X2}{bi:X2}";
        }

        // ── Default Growatt Tag Set (163 tags) ──────────────────────────────────
        // This is the DEFAULT set only written on first boot (when config.json
        // does not exist). Every setting is editable via Settings → Edit Tags.
        // Changes are saved to config.json and survive restarts/reboots.

        public static List<MachineTag> GetDefaultGrowattTags()
        {
            var FC04 = ModbusFunctionCode.ReadInputRegisters;
            var FC03 = ModbusFunctionCode.ReadHoldingRegisters;
            int ci = 0;  // rolling color index

            var tags = new List<MachineTag>();

            // ── CATEGORY 1: Inverter Status ──────────────────────────────────────
            tags.Add(new MachineTag { Address = 0,  Name = "Inverter Status",      FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "",    IsPlotted = true,  Color = GetTagColor(ci++) });

            // ── CATEGORY 2: PV DC Input — Tracker 1 & 2 ────────────────────────
            tags.Add(new MachineTag { Address = 1,  Name = "PV Input Power",       FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "W",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 3,  Name = "PV1 Voltage",          FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "VDC", IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 4,  Name = "PV1 Current",          FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "A",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 5,  Name = "PV1 Power",            FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "W",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 7,  Name = "PV2 Voltage",          FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "VDC", IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 8,  Name = "PV2 Current",          FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "A",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 9,  Name = "PV2 Power",            FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "W",   IsPlotted = true,  Color = GetTagColor(ci++) });

            // ── CATEGORY 3: Grid AC Output ───────────────────────────────────────
            tags.Add(new MachineTag { Address = 11, Name = "AC Output Power",      FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "W",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 13, Name = "Grid Frequency",       FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.01, SiUnit = "Hz",  IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 14, Name = "Grid Voltage Vac1",    FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.01, SiUnit = "VAC", IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 15, Name = "Grid Current Iac1",    FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "A",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 16, Name = "Phase 1 Power",        FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "W",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 18, Name = "Grid Voltage Vac2",    FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.01, SiUnit = "VAC", IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 19, Name = "Grid Current Iac2",    FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "A",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 20, Name = "Phase 2 Power",        FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "W",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 22, Name = "Grid Voltage Vac3",    FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.01, SiUnit = "VAC", IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 23, Name = "Grid Current Iac3",    FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "A",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 24, Name = "Phase 3 Power",        FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "W",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 45, Name = "Output Power Factor",  FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 58, Name = "Reactive Power",       FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "VAR", IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 66, Name = "Output Percent",       FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "%",   IsPlotted = true,  Color = GetTagColor(ci++) });

            // ── CATEGORY 4: Energy Production & Accumulation ─────────────────────
            tags.Add(new MachineTag { Address = 26, Name = "Energy Today",         FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "kWh", IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 28, Name = "Energy Total",         FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "kWh", IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 48, Name = "Epv1 Today",           FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "kWh", IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 50, Name = "Epv1 Total",           FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "kWh", IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 52, Name = "Epv2 Today",           FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "kWh", IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 54, Name = "Epv2 Total",           FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "kWh", IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 56, Name = "Epv Total",            FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "kWh", IsPlotted = false, Color = GetTagColor(ci++) });

            // ── CATEGORY 5: Health, Temperature & Diagnostics ────────────────────
            tags.Add(new MachineTag { Address = 30, Name = "Work Time Total",      FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.5,  SiUnit = "h",   IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 32, Name = "Inverter Temperature", FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "°C",  IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 40, Name = "Fault Code",           FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 41, Name = "IPM Temperature",      FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "°C",  IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 42, Name = "P Bus Voltage",        FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "V",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 43, Name = "N Bus Voltage",        FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "V",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 47, Name = "Derating Mode",        FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 64, Name = "Warning Code",         FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 65, Name = "Warning Value 1",      FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 69, Name = "Warning Value 2",      FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "",    IsPlotted = false, Color = GetTagColor(ci++) });

            // ── CATEGORY 6: PV String Monitoring (Strings 1–8) ──────────────────
            tags.Add(new MachineTag { Address = 70, Name = "String 1 Voltage",     FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "VDC", IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 71, Name = "String 1 Current",     FunctionCode = FC04, DataType = TagDataType.Int16,  ScalingFactor = 0.1,  SiUnit = "A",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 72, Name = "String 2 Voltage",     FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "VDC", IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 73, Name = "String 2 Current",     FunctionCode = FC04, DataType = TagDataType.Int16,  ScalingFactor = 0.1,  SiUnit = "A",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 74, Name = "String 3 Voltage",     FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "VDC", IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 75, Name = "String 3 Current",     FunctionCode = FC04, DataType = TagDataType.Int16,  ScalingFactor = 0.1,  SiUnit = "A",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 76, Name = "String 4 Voltage",     FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "VDC", IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 77, Name = "String 4 Current",     FunctionCode = FC04, DataType = TagDataType.Int16,  ScalingFactor = 0.1,  SiUnit = "A",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 78, Name = "String 5 Voltage",     FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "VDC", IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 79, Name = "String 5 Current",     FunctionCode = FC04, DataType = TagDataType.Int16,  ScalingFactor = 0.1,  SiUnit = "A",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 80, Name = "String 6 Voltage",     FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "VDC", IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 81, Name = "String 6 Current",     FunctionCode = FC04, DataType = TagDataType.Int16,  ScalingFactor = 0.1,  SiUnit = "A",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 82, Name = "String 7 Voltage",     FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "VDC", IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 83, Name = "String 7 Current",     FunctionCode = FC04, DataType = TagDataType.Int16,  ScalingFactor = 0.1,  SiUnit = "A",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 84, Name = "String 8 Voltage",     FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "VDC", IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 85, Name = "String 8 Current",     FunctionCode = FC04, DataType = TagDataType.Int16,  ScalingFactor = 0.1,  SiUnit = "A",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 86, Name = "String Fault",         FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 87, Name = "String Warning",       FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 88, Name = "String Disconnect",    FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 89, Name = "PID Fault Code",       FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "",    IsPlotted = false, Color = GetTagColor(ci++) });

            // ── CATEGORY 7: Grid Fault Records (5 records × 5 regs) ─────────────
            for (int f = 0; f < 5; f++)
            {
                int baseAddr = 90 + f * 5;
                string fn = $"Grid Fault {f + 1}";
                tags.Add(new MachineTag { Address = (ushort)(baseAddr),     Name = $"{fn} Code",      FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 1.0, SiUnit = "", IsPlotted = false, Color = GetTagColor(ci++) });
                tags.Add(new MachineTag { Address = (ushort)(baseAddr + 1), Name = $"{fn} Year/Month",FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 1.0, SiUnit = "", IsPlotted = false, Color = GetTagColor(ci++) });
                tags.Add(new MachineTag { Address = (ushort)(baseAddr + 2), Name = $"{fn} Day/Hour",  FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 1.0, SiUnit = "", IsPlotted = false, Color = GetTagColor(ci++) });
                tags.Add(new MachineTag { Address = (ushort)(baseAddr + 3), Name = $"{fn} Min/Sec",   FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 1.0, SiUnit = "", IsPlotted = false, Color = GetTagColor(ci++) });
                tags.Add(new MachineTag { Address = (ushort)(baseAddr + 4), Name = $"{fn} Value",     FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 1.0, SiUnit = "", IsPlotted = false, Color = GetTagColor(ci++) });
            }

            // ── CATEGORY 8: PV3 Tracker + System Fault ───────────────────────────
            tags.Add(new MachineTag { Address = 120, Name = "PV3 Voltage",         FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "VDC", IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 121, Name = "PV3 Current",         FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "A",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 122, Name = "PV3 Power",           FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "W",   IsPlotted = true,  Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 124, Name = "Epv3 Today",          FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "kWh", IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 126, Name = "Epv3 Total",          FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "kWh", IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 128, Name = "System Fault Code",   FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 1.0,  SiUnit = "",    IsPlotted = false, Color = GetTagColor(ci++) });

            // ── CATEGORY 9: FC03 Protection & Control Settings ───────────────────
            tags.Add(new MachineTag { Address = 0,  Name = "On/Off Control",       FunctionCode = FC03, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 3,  Name = "Active Power Rate",    FunctionCode = FC03, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "%",   IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 4,  Name = "Reactive Power Rate",  FunctionCode = FC03, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "%",   IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 5,  Name = "Power Factor Setting", FunctionCode = FC03, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "",    IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 17, Name = "PV Start Voltage",     FunctionCode = FC03, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "V",   IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 18, Name = "Startup Delay",        FunctionCode = FC03, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "s",   IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 19, Name = "Vac Low Protect",      FunctionCode = FC03, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "V",   IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 20, Name = "Vac High Protect",     FunctionCode = FC03, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "V",   IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 21, Name = "Fac Low Protect",      FunctionCode = FC03, DataType = TagDataType.UInt16, ScalingFactor = 0.01, SiUnit = "Hz",  IsPlotted = false, Color = GetTagColor(ci++) });
            tags.Add(new MachineTag { Address = 22, Name = "Fac High Protect",     FunctionCode = FC03, DataType = TagDataType.UInt16, ScalingFactor = 0.01, SiUnit = "Hz",  IsPlotted = false, Color = GetTagColor(ci++) });

            // ── CATEGORY 10: Historical Energy — Hourly (24 h) ──────────────────
            for (int h = 0; h < 24; h++)
            {
                ushort addr = (ushort)(450 + h * 2);
                tags.Add(new MachineTag { Address = addr, Name = $"Energy Hour {h:D2}:00", FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1, SiUnit = "kWh", IsPlotted = false, Color = GetTagColor(ci++) });
            }

            // ── CATEGORY 11: Historical Energy — Daily (last 7 days) ────────────
            for (int d = 0; d < 7; d++)
            {
                ushort addr = (ushort)(498 + d * 2);
                tags.Add(new MachineTag { Address = addr, Name = $"Energy Day-{d}", FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1, SiUnit = "kWh", IsPlotted = false, Color = GetTagColor(ci++) });
            }

            // ── CATEGORY 12: Historical Energy — Monthly (last 12 months) ───────
            for (int m = 0; m < 12; m++)
            {
                ushort addr = (ushort)(512 + m * 2);
                tags.Add(new MachineTag { Address = addr, Name = $"Energy Month-{m}", FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1, SiUnit = "kWh", IsPlotted = false, Color = GetTagColor(ci++) });
            }

            // ── CATEGORY 13: Historical Energy — Yearly (last 20 years) ─────────
            for (int y = 0; y < 20; y++)
            {
                ushort addr = (ushort)(536 + y * 2);
                tags.Add(new MachineTag { Address = addr, Name = $"Energy Year-{y}", FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1, SiUnit = "kWh", IsPlotted = false, Color = GetTagColor(ci++) });
            }

            return tags;
            // Total: 163 tags — 1 status + 8 PV DC + 22 Grid AC + 7 Energy accum + 
            //        11 Health + 20 Strings + 25 FaultRecords + 6 PV3 + 10 FC03 + 24+7+12+20 History
        }

        // ── Growatt Template Library ─────────────────────────────────────────────
        // Full list shown in Import Template dropdown inside Tag Editor.
        // Selecting any entry auto-fills Address, FC, DataType, Scale, Unit.

        private static List<RegisterTemplate> GetDefaultTemplates()
        {
            var FC04 = ModbusFunctionCode.ReadInputRegisters;
            var FC03 = ModbusFunctionCode.ReadHoldingRegisters;
            int ci = 0;

            return new List<RegisterTemplate>
            {
                // Grid AC
                new RegisterTemplate { Name = "Inverter Status",      DefaultAddress = 0,   FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "",    Color = GetTagColor(ci++), Category = "Status" },
                new RegisterTemplate { Name = "Grid Frequency",       DefaultAddress = 13,  FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.01, SiUnit = "Hz",  Color = GetTagColor(ci++), Category = "Grid AC" },
                new RegisterTemplate { Name = "Grid Voltage Vac1",    DefaultAddress = 14,  FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.01, SiUnit = "VAC", Color = GetTagColor(ci++), Category = "Grid AC" },
                new RegisterTemplate { Name = "Grid Current Iac1",    DefaultAddress = 15,  FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "A",   Color = GetTagColor(ci++), Category = "Grid AC" },
                new RegisterTemplate { Name = "Grid Voltage Vac2",    DefaultAddress = 18,  FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.01, SiUnit = "VAC", Color = GetTagColor(ci++), Category = "Grid AC" },
                new RegisterTemplate { Name = "Grid Current Iac2",    DefaultAddress = 19,  FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "A",   Color = GetTagColor(ci++), Category = "Grid AC" },
                new RegisterTemplate { Name = "Grid Voltage Vac3",    DefaultAddress = 22,  FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.01, SiUnit = "VAC", Color = GetTagColor(ci++), Category = "Grid AC" },
                new RegisterTemplate { Name = "Grid Current Iac3",    DefaultAddress = 23,  FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "A",   Color = GetTagColor(ci++), Category = "Grid AC" },
                new RegisterTemplate { Name = "AC Output Power",      DefaultAddress = 11,  FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "W",   Color = GetTagColor(ci++), Category = "Grid AC" },
                new RegisterTemplate { Name = "Phase 1 Power",        DefaultAddress = 16,  FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "W",   Color = GetTagColor(ci++), Category = "Grid AC" },
                new RegisterTemplate { Name = "Phase 2 Power",        DefaultAddress = 20,  FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "W",   Color = GetTagColor(ci++), Category = "Grid AC" },
                new RegisterTemplate { Name = "Phase 3 Power",        DefaultAddress = 24,  FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "W",   Color = GetTagColor(ci++), Category = "Grid AC" },
                new RegisterTemplate { Name = "Reactive Power",       DefaultAddress = 58,  FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "VAR", Color = GetTagColor(ci++), Category = "Grid AC" },
                // PV DC
                new RegisterTemplate { Name = "PV Input Power",       DefaultAddress = 1,   FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "W",   Color = GetTagColor(ci++), Category = "PV DC" },
                new RegisterTemplate { Name = "PV1 Voltage",          DefaultAddress = 3,   FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "VDC", Color = GetTagColor(ci++), Category = "PV DC" },
                new RegisterTemplate { Name = "PV1 Current",          DefaultAddress = 4,   FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "A",   Color = GetTagColor(ci++), Category = "PV DC" },
                new RegisterTemplate { Name = "PV2 Voltage",          DefaultAddress = 7,   FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "VDC", Color = GetTagColor(ci++), Category = "PV DC" },
                new RegisterTemplate { Name = "PV2 Current",          DefaultAddress = 8,   FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "A",   Color = GetTagColor(ci++), Category = "PV DC" },
                new RegisterTemplate { Name = "PV3 Voltage",          DefaultAddress = 120, FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "VDC", Color = GetTagColor(ci++), Category = "PV DC" },
                new RegisterTemplate { Name = "PV3 Current",          DefaultAddress = 121, FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "A",   Color = GetTagColor(ci++), Category = "PV DC" },
                // Strings
                new RegisterTemplate { Name = "String 1 Voltage",     DefaultAddress = 70,  FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "VDC", Color = GetTagColor(ci++), Category = "Strings" },
                new RegisterTemplate { Name = "String 1 Current",     DefaultAddress = 71,  FunctionCode = FC04, DataType = TagDataType.Int16,  ScalingFactor = 0.1,  SiUnit = "A",   Color = GetTagColor(ci++), Category = "Strings" },
                new RegisterTemplate { Name = "String 2 Voltage",     DefaultAddress = 72,  FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "VDC", Color = GetTagColor(ci++), Category = "Strings" },
                new RegisterTemplate { Name = "String 2 Current",     DefaultAddress = 73,  FunctionCode = FC04, DataType = TagDataType.Int16,  ScalingFactor = 0.1,  SiUnit = "A",   Color = GetTagColor(ci++), Category = "Strings" },
                new RegisterTemplate { Name = "String 3 Voltage",     DefaultAddress = 74,  FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "VDC", Color = GetTagColor(ci++), Category = "Strings" },
                new RegisterTemplate { Name = "String 3 Current",     DefaultAddress = 75,  FunctionCode = FC04, DataType = TagDataType.Int16,  ScalingFactor = 0.1,  SiUnit = "A",   Color = GetTagColor(ci++), Category = "Strings" },
                new RegisterTemplate { Name = "String 4 Voltage",     DefaultAddress = 76,  FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "VDC", Color = GetTagColor(ci++), Category = "Strings" },
                new RegisterTemplate { Name = "String 4 Current",     DefaultAddress = 77,  FunctionCode = FC04, DataType = TagDataType.Int16,  ScalingFactor = 0.1,  SiUnit = "A",   Color = GetTagColor(ci++), Category = "Strings" },
                new RegisterTemplate { Name = "P Bus Voltage",        DefaultAddress = 42,  FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "V",   Color = GetTagColor(ci++), Category = "Diagnostics" },
                new RegisterTemplate { Name = "N Bus Voltage",        DefaultAddress = 43,  FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "V",   Color = GetTagColor(ci++), Category = "Diagnostics" },
                new RegisterTemplate { Name = "Inverter Temperature", DefaultAddress = 32,  FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "°C",  Color = GetTagColor(ci++), Category = "Diagnostics" },
                new RegisterTemplate { Name = "IPM Temperature",      DefaultAddress = 41,  FunctionCode = FC04, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "°C",  Color = GetTagColor(ci++), Category = "Diagnostics" },
                new RegisterTemplate { Name = "Energy Today",         DefaultAddress = 26,  FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "kWh", Color = GetTagColor(ci++), Category = "Energy" },
                new RegisterTemplate { Name = "Energy Total",         DefaultAddress = 28,  FunctionCode = FC04, DataType = TagDataType.UInt32, ScalingFactor = 0.1,  SiUnit = "kWh", Color = GetTagColor(ci++), Category = "Energy" },
                // FC03 Protection
                new RegisterTemplate { Name = "On/Off Control",       DefaultAddress = 0,   FunctionCode = FC03, DataType = TagDataType.UInt16, ScalingFactor = 1.0,  SiUnit = "",    Color = GetTagColor(ci++), Category = "Control" },
                new RegisterTemplate { Name = "Vac Low Protect",      DefaultAddress = 19,  FunctionCode = FC03, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "V",   Color = GetTagColor(ci++), Category = "Control" },
                new RegisterTemplate { Name = "Vac High Protect",     DefaultAddress = 20,  FunctionCode = FC03, DataType = TagDataType.UInt16, ScalingFactor = 0.1,  SiUnit = "V",   Color = GetTagColor(ci++), Category = "Control" },
                new RegisterTemplate { Name = "Fac Low Protect",      DefaultAddress = 21,  FunctionCode = FC03, DataType = TagDataType.UInt16, ScalingFactor = 0.01, SiUnit = "Hz",  Color = GetTagColor(ci++), Category = "Control" },
                new RegisterTemplate { Name = "Fac High Protect",     DefaultAddress = 22,  FunctionCode = FC03, DataType = TagDataType.UInt16, ScalingFactor = 0.01, SiUnit = "Hz",  Color = GetTagColor(ci++), Category = "Control" },
            };
        }

        // ── Default Config (first boot only) ────────────────────────────────────
        // Creates 3 Growatt inverters each pre-loaded with 163 tags.
        // Only runs when config.json does not exist.
        // All settings are editable via Settings UI and saved persistently.

        private AppConfig CreateDefaultConfig()
        {
            var defaultTags = GetDefaultGrowattTags();

            var config = new AppConfig
            {
                Machines = new List<MachineConfig>
                {
                    new MachineConfig
                    {
                        Name            = "Inverter 1",
                        Mode            = OperationMode.Master,
                        Type            = ConnectionType.Rtu,
                        SerialPort      = "/dev/ttyUSB0",
                        BaudRate        = 9600,
                        Parity          = System.IO.Ports.Parity.None,
                        StopBits        = System.IO.Ports.StopBits.One,
                        DataBits        = 8,
                        SlaveId         = 1,
                        IsEnabled       = true,
                        PollingIntervalMs = 1000,
                        Tags            = new List<MachineTag>(defaultTags)
                    },
                    new MachineConfig
                    {
                        Name            = "Inverter 2",
                        Mode            = OperationMode.Master,
                        Type            = ConnectionType.Rtu,
                        SerialPort      = "/dev/ttyUSB0",
                        BaudRate        = 9600,
                        Parity          = System.IO.Ports.Parity.None,
                        StopBits        = System.IO.Ports.StopBits.One,
                        DataBits        = 8,
                        SlaveId         = 2,
                        IsEnabled       = true,
                        PollingIntervalMs = 1000,
                        Tags            = new List<MachineTag>(defaultTags)
                    },
                    new MachineConfig
                    {
                        Name            = "Inverter 3",
                        Mode            = OperationMode.Master,
                        Type            = ConnectionType.Rtu,
                        SerialPort      = "/dev/ttyUSB0",
                        BaudRate        = 9600,
                        Parity          = System.IO.Ports.Parity.None,
                        StopBits        = System.IO.Ports.StopBits.One,
                        DataBits        = 8,
                        SlaveId         = 3,
                        IsEnabled       = true,
                        PollingIntervalMs = 1000,
                        Tags            = new List<MachineTag>(defaultTags)
                    }
                },
                Cloud = new CloudConfig(),
                RegisterLibrary = GetDefaultTemplates()
            };

            SaveConfig(config);
            return config;
        }
    }
}
