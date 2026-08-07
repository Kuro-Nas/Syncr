using NModbus;
using NModbus.Serial;
using Syncr.Core.Models;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Syncr.Core.Services
{
    public class ModbusService
    {
        private readonly List<MachineConfig> _machines;
        public List<MachineConfig> Config => _machines;
        private bool _isRunning;
        private readonly HashSet<string> _activeMockMachines = new();
        public bool UseMock { get; set; } 
        public int ActiveMockMachinesCount => _activeMockMachines.Count;
        public bool IsMockMachineActive(string machineName) => _activeMockMachines.Contains(machineName);
        public void SetMockMachineState(string machineName, bool active)
        {
            if (active) { _activeMockMachines.Add(machineName); }
            else { _activeMockMachines.Remove(machineName); }
        }

        public event Action<MachineDataPoint> OnDataReceived;
        public event Action<string> OnConnectionError;
        public event Action OnConfigChanged;

        private readonly IModbusFactory _modbusFactory;

        public ModbusService(AppConfig config, bool useMock = true)
        {
            _machines = config.Machines;
            UseMock = useMock;
            _modbusFactory = new ModbusFactory();
        }

        public void UpdateConfig(AppConfig newConfig)
        {
            Stop();
            // IMPORTANT: _machines is the same List<> reference as newConfig.Machines
            // (set in constructor: _machines = config.Machines).
            // If newConfig IS the same AppConfig, Clear+AddRange is a no-op that
            // would wipe the list and re-add the same items — but if callers pass
            // _appConfig here, _machines.Clear() wipes _appConfig.Machines too.
            // Use a safe replace: only clear and re-add if they differ.
            if (!ReferenceEquals(_machines, newConfig.Machines))
            {
                _machines.Clear();
                _machines.AddRange(newConfig.Machines);
            }
            // If same reference, the list is already correct — just restart polling.
            OnConfigChanged?.Invoke();
            Start();
        }

        private readonly SemaphoreSlim _serialLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource? _pollCts;

        public void Start()
        {
            Stop(); // Cancel any existing polling tasks cleanly
            _pollCts = new CancellationTokenSource();
            var token = _pollCts.Token;
            _isRunning = true;

            foreach (var machine in _machines)
            {
                if (machine.IsEnabled)
                {
                    Task.Run(() => BackgroundPollMachine(machine, token), token);
                }
            }
        }

        public void Stop()
        {
            _isRunning = false;
            try { _pollCts?.Cancel(); } catch { }
            try { _pollCts?.Dispose(); } catch { }
            _pollCts = null;

            try { _cachedPort?.Close(); } catch { }
            _cachedMaster = null;
            _cachedPort = null;
        }

        private async Task BackgroundPollMachine(MachineConfig machine, CancellationToken token)
        {
            // Initial settlement delay on boot/start — gives OS time to bring up network/USB
            await Task.Delay(500, token).ConfigureAwait(false);

            while (_isRunning && machine.IsEnabled && !token.IsCancellationRequested)
            {
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    bool useMock = IsMockMachineActive(machine.Name);
                    // Pass poll token so TCP connect can be cancelled on Stop/ReloadConfig
                    var data = useMock ? await MockRead(machine) : await RealRead(machine, token);
                    sw.Stop();
                    data.LatencyMs = sw.Elapsed.TotalMilliseconds;
                    OnDataReceived?.Invoke(data);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    OnConnectionError?.Invoke($"Connection Error: {machine.Name} - {ex.Message}");
                }

                int delay = machine.PollingIntervalMs > 0 ? machine.PollingIntervalMs : 5000;
                // User requested 3000ms for simulation/mock flow
                if (IsMockMachineActive(machine.Name)) delay = 3000; 
                
                try
                {
                    await Task.Delay(delay, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }


        private readonly object _mockLock = new();
        private readonly Dictionary<string, Dictionary<ushort, double>> _mockOverrides = new();

        public void UpdateMockValue(string machineName, ushort address, double value)
        {
            lock (_mockLock)
            {
                if (!_mockOverrides.ContainsKey(machineName))
                    _mockOverrides[machineName] = new Dictionary<ushort, double>();
                _mockOverrides[machineName][address] = value;
            }
        }


        // Thread-safe random instance for jitter generation
        private static readonly Random _jitterRandom = new Random();

        /// <summary>
        /// Returns a smart baseline value for a tag when no manual override is set.
        /// Uses tag name keywords to produce realistic industrial defaults with ±5% jitter.
        /// </summary>
        private static double GetBaselineValue(MachineTag tag)
        {
            string name = (tag.Name ?? "").ToLowerInvariant();
            double baseline;

            if      (name.Contains("volt"))    baseline = 230.0;
            else if (name.Contains("curr") || name.Contains("amp")) baseline = 15.0;
            else if (name.Contains("power") || name.Contains("watt")) baseline = 3450.0;
            else if (name.Contains("temp"))    baseline = 45.0;
            else if (name.Contains("press"))   baseline = 6.5;
            else if (name.Contains("freq"))    baseline = 50.0;
            else if (name.Contains("speed") || name.Contains("rpm")) baseline = 1440.0;
            else if (name.Contains("humid"))   baseline = 40.0;
            else if (name.Contains("flow"))    baseline = 25.0;
            else if (name.Contains("level"))   baseline = 75.0;
            else if (name.Contains("energy") || name.Contains("kwh")) baseline = 1200.0;
            else                               baseline = 50.0;

            // ±5% random jitter so the graph shows live fluctuations
            double jitter = baseline * 0.05 * (_jitterRandom.NextDouble() * 2.0 - 1.0);
            return baseline + jitter;
        }

        private Task<MachineDataPoint> MockRead(MachineConfig machine)
        {
            var point = new MachineDataPoint { MachineName = machine.Name, Timestamp = DateTime.Now };

            foreach (var tag in machine.Tags)
            {
                string key = $"{tag.Address}:{tag.Name}";
                double raw;

                lock (_mockLock)
                {
                    // Use manual override if the user has set one; otherwise auto-jitter
                    if (_mockOverrides.TryGetValue(machine.Name, out var overrides) && overrides.TryGetValue(tag.Address, out var v))
                        raw = v;
                    else
                        raw = GetBaselineValue(tag);
                }

                point.Values[key] = raw * tag.ScalingFactor;
            }
            return Task.FromResult(point);
        }


        /// <summary>
        /// Number of Modbus holding registers required for a given data type.
        /// </summary>
        public static ushort RegisterCount(TagDataType dt) => dt switch
        {
            TagDataType.Bool    => 1,
            TagDataType.Int16   => 1,
            TagDataType.UInt16  => 1,
            TagDataType.Int32   => 2,
            TagDataType.UInt32  => 2,
            TagDataType.Float32 => 2,
            TagDataType.Float64 => 4,
            TagDataType.String8 => 8,
            _                   => 2  // AutoDetect — read 2 to cover float option
        };

        /// <summary>
        /// Decode raw register word(s) into a double according to the tag's DataType,
        /// then apply the ScalingFactor.
        /// </summary>
        public static double DecodeValue(ushort[] registers, int offset, MachineTag tag)
        {
            double raw;
            int available = registers.Length - offset;

            switch (tag.DataType)
            {
                case TagDataType.Bool:
                    raw = registers[offset] != 0 ? 1.0 : 0.0;
                    break;

                case TagDataType.Int16:
                    raw = (short)registers[offset];
                    break;

                case TagDataType.UInt16:
                    raw = registers[offset];
                    break;

                case TagDataType.Int32 when available >= 2:
                    raw = (int)(((uint)registers[offset] << 16) | registers[offset + 1]);
                    break;

                case TagDataType.UInt32 when available >= 2:
                    raw = ((uint)registers[offset] << 16) | registers[offset + 1];
                    break;

                case TagDataType.Float32 when available >= 2:
                {
                    // Big-endian: high word first
                    byte[] b = new byte[4];
                    b[0] = (byte)(registers[offset + 1] & 0xFF);
                    b[1] = (byte)(registers[offset + 1] >> 8);
                    b[2] = (byte)(registers[offset] & 0xFF);
                    b[3] = (byte)(registers[offset] >> 8);
                    raw = BitConverter.ToSingle(b, 0);
                    break;
                }

                case TagDataType.Float64 when available >= 4:
                {
                    byte[] b = new byte[8];
                    for (int i = 0; i < 4; i++)
                    {
                        b[i * 2]     = (byte)(registers[offset + 3 - i] & 0xFF);
                        b[i * 2 + 1] = (byte)(registers[offset + 3 - i] >> 8);
                    }
                    raw = BitConverter.ToDouble(b, 0);
                    break;
                }

                case TagDataType.String8:
                    // Return a simple hash of the string chars for charting purposes
                    raw = 0;
                    for (int i = 0; i < Math.Min(8, available); i++)
                        raw += registers[offset + i];
                    break;

                case TagDataType.AutoDetect:
                default:
                {
                    // Try float32 if 2 regs available and UInt16 looks maxed-out
                    ushort u16 = registers[offset];
                    if (available >= 2)
                    {
                        byte[] b = new byte[4];
                        b[0] = (byte)(registers[offset + 1] & 0xFF);
                        b[1] = (byte)(registers[offset + 1] >> 8);
                        b[2] = (byte)(registers[offset] & 0xFF);
                        b[3] = (byte)(registers[offset] >> 8);
                        float f = BitConverter.ToSingle(b, 0);
                        if (!float.IsNaN(f) && !float.IsInfinity(f) && u16 == ushort.MaxValue)
                        {
                            raw = f;
                            break;
                        }
                    }
                    raw = u16;
                    break;
                }
            }

            return double.IsNaN(raw) || double.IsInfinity(raw) ? 0.0 : raw * tag.ScalingFactor;
        }


        private static async Task<ushort[]> ExecuteModbusReadAsync(
            IModbusMaster master, 
            byte slaveId, 
            ushort startAddress, 
            ushort count, 
            ModbusFunctionCode fc)
        {
            switch (fc)
            {
                case ModbusFunctionCode.ReadInputRegisters:
                    return await master.ReadInputRegistersAsync(slaveId, startAddress, count);

                case ModbusFunctionCode.ReadCoils:
                    bool[] coils = await master.ReadCoilsAsync(slaveId, startAddress, count);
                    return coils.Select(b => (ushort)(b ? 1 : 0)).ToArray();

                case ModbusFunctionCode.ReadDiscreteInputs:
                    bool[] inputs = await master.ReadInputsAsync(slaveId, startAddress, count);
                    return inputs.Select(b => (ushort)(b ? 1 : 0)).ToArray();

                case ModbusFunctionCode.ReadHoldingRegisters:
                default:
                    return await master.ReadHoldingRegistersAsync(slaveId, startAddress, count);
            }
        }


        private IModbusMaster _cachedMaster;
        private SerialPort _cachedPort;

        private async Task<MachineDataPoint> RealRead(MachineConfig machine, CancellationToken pollToken = default)
        {
            var point = new MachineDataPoint { MachineName = machine.Name, Timestamp = DateTime.Now };

            if (machine.Type == ConnectionType.Rtu)
            {
                await _serialLock.WaitAsync();
                try
                {
                    if (_cachedMaster == null || _cachedPort == null || !_cachedPort.IsOpen)
                    {
                        if (_cachedPort != null && _cachedPort.IsOpen) _cachedPort.Close();

                        _cachedPort = new SerialPort(machine.SerialPort, machine.BaudRate, machine.Parity, machine.DataBits, machine.StopBits);
                        _cachedPort.ReadTimeout  = 1000;
                        _cachedPort.WriteTimeout = 1000;
                        _cachedPort.Open();

                        _cachedMaster = _modbusFactory.CreateRtuMaster(new SerialPortAdapter(_cachedPort));
                    }

                    // Group tags by FunctionCode first
                    var tagsByFunction = machine.Tags.GroupBy(t => t.FunctionCode);

                    foreach (var group in tagsByFunction)
                    {
                        ModbusFunctionCode fc = group.Key;

                        // Growatt RTU Spec (PDF Page 10): Dynamically discover required 45-register memory blocks
                        // (Block 0 = 0..44, Block 1 = 45..89, etc.) based on configured tags to prevent RTU buffer shifting.
                        var tagsByBlock = group.GroupBy(t => t.Address / 45);

                        foreach (var blockGroup in tagsByBlock)
                        {
                            int blockIndex      = blockGroup.Key;
                            ushort blockStart   = (ushort)(blockIndex * 45);
                            ushort blockCount   = 45; // Native Growatt hardware block size

                            var registers = await ExecuteModbusReadAsync(_cachedMaster, machine.SlaveId, blockStart, blockCount, fc);

                            if (registers != null)
                            {
                                foreach (var tag in blockGroup)
                                {
                                    int offset = tag.Address - blockStart;
                                    if (offset >= 0 && offset < registers.Length)
                                    {
                                        string key = $"{tag.Address}:{tag.Name}";
                                        point.Values[key] = DecodeValue(registers, offset, tag);
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                    try { _cachedPort?.Close(); } catch { }
                    _cachedMaster = null;
                    _cachedPort   = null;
                    throw;
                }
                finally
                {
                    _serialLock.Release();
                }
            }
            else if (machine.Type == ConnectionType.Tcp)
            {
                using var client = new TcpClient();

                // Race the TCP connect against a 5-second timeout.
                // IMPORTANT: we do NOT pass the poll token into ConnectAsync directly, because
                // if the 5s timeout fires it throws OperationCanceledException — and the poll
                // loop's `catch (OperationCanceledException) { break; }` would permanently kill
                // the background task. Instead we isolate the connect timeout, catch OCE here,
                // and re-throw as TimeoutException so the poll loop retries after PollingIntervalMs.
                using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
#if NET5_0_OR_GREATER
                    await client.ConnectAsync(machine.IpAddress, machine.Port, connectTimeout.Token);
#else
                    var connectTask = client.ConnectAsync(machine.IpAddress, machine.Port);
                    var timeoutTask = Task.Delay(5000, connectTimeout.Token);
                    if (await Task.WhenAny(connectTask, timeoutTask) == timeoutTask)
                        throw new TimeoutException($"TCP connect to {machine.IpAddress}:{machine.Port} timed out after 5 s");
                    await connectTask;
#endif
                }
                catch (OperationCanceledException) when (!pollToken.IsCancellationRequested)
                {
                    // The 5-second connect timeout fired — NOT a Stop/ReloadConfig cancellation.
                    // Convert to TimeoutException so the poll loop logs it and retries.
                    throw new TimeoutException($"TCP connect to {machine.IpAddress}:{machine.Port} timed out after 5 s");
                }
                // If pollToken itself is cancelled, OCE propagates up naturally → loop breaks.
                var master = _modbusFactory.CreateMaster(client);

                foreach (var tag in machine.Tags)
                {
                    ushort numRegs = RegisterCount(tag.DataType);
                    if (numRegs < 1) numRegs = 1;

                    var registers = await ExecuteModbusReadAsync(master, machine.SlaveId, tag.Address, numRegs, tag.FunctionCode);
                    if (registers != null && registers.Length > 0)
                    {
                        string key = $"{tag.Address}:{tag.Name}";
                        point.Values[key] = DecodeValue(registers, 0, tag);
                    }
                }
            }

            return point;
        }


        private static readonly int[] CommonBaudRates = { 9600, 19200, 38400, 57600, 115200, 2400, 4800, 1200 };

        /// <summary>
        /// Tries each common baud rate in order. Uses the first configured tag's address.
        /// Returns the detected baud rate, or -1 if nothing responded.
        /// </summary>
        public async Task<int> AutoDetectBaudRateAsync(MachineConfig machine, IProgress<string> progress = null)
        {
            if (machine.Tags.Count == 0) return -1;
            ushort testAddress = machine.Tags[0].Address;

            foreach (int baud in CommonBaudRates)
            {
                progress?.Report($"Trying {baud}...");
                SerialPort port = null;
                try
                {
                    port = new SerialPort(machine.SerialPort, baud, machine.Parity, machine.DataBits, machine.StopBits);
                    port.ReadTimeout  = 800;
                    port.WriteTimeout = 800;
                    port.Open();

                    var factory = new ModbusFactory();
                    var master  = factory.CreateRtuMaster(new SerialPortAdapter(port));

                    // Attempt to read 1 register — success means correct baud
                    await master.ReadHoldingRegistersAsync(machine.SlaveId, testAddress, 1);

                    port.Close();
                    machine.BaudRate = baud;
                    progress?.Report($"Detected: {baud}");
                    return baud;
                }
                catch
                {
                    try { port?.Close(); } catch { }
                }
                await Task.Delay(200);
            }

            progress?.Report("Not found");
            return -1;
        }
    }
}
