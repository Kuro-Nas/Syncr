using NModbus;
using NModbus.Data;
using NModbus.Serial;
using Syncr.Core.Models;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Syncr.Core.Services
{
    public class ModbusSlaveService : IDisposable
    {
        private readonly List<MachineConfig> _machines;
        private readonly ModbusService _modbusService;
        private bool _isRunning;
        private readonly IModbusFactory _modbusFactory;
        private readonly List<IModbusSlaveNetwork> _slaveNetworks = new();
        private readonly Dictionary<string, ISlaveDataStore> _machineStores = new();
        private CancellationTokenSource? _simCancellation;
        [ThreadStatic]
        private static bool _isInternalUpdate;
        private readonly object _machinesLock = new();

        public event Action<MachineDataPoint>? OnDataWritten;
        public event Action<string>? OnLog;

        public ModbusSlaveService(AppConfig config, ModbusService modbusService)
        {
            _machines = new List<MachineConfig>();
            _machines.AddRange(config.Machines);
            _modbusService = modbusService;
            _modbusFactory = new ModbusFactory();
        }

        public void ResetConfig(AppConfig config)
        {
            lock (_machinesLock)
            {
                _machines.Clear();
                _machines.AddRange(config.Machines);
            }
        }

        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;

            foreach (var machine in _machines)
            {
                if (!machine.IsEnabled || machine.Mode != OperationMode.Slave) continue;

                Task.Run(() => SetupSlaveWithRetry(machine));
            }
            
            _simCancellation = new CancellationTokenSource();
            _ = Task.Run(() => SimulationLoop(_simCancellation.Token));
        }

        private async Task SetupSlaveWithRetry(MachineConfig machine)
        {
            int retryDelay = 5000;
            int attempt = 0;
            while (_isRunning && attempt < 3)
            {
                attempt++;
                try
                {
                    SetupSlave(machine);
                    break; // Successfully initialized
                }
                catch (UnauthorizedAccessException uex)
                {
                    OnLog?.Invoke($"[Port Access Denied] {machine.Name} cannot open {machine.SerialPort}: {uex.Message}. Fix: sudo usermod -a -G dialout $USER");
                    break; // Don't loop spam on permission error
                }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"[Boot Retry] Slave start failed for {machine.Name} ({machine.SerialPort}): {ex.Message}");
                    if (attempt >= 3) break;
                    try { await Task.Delay(retryDelay); } catch { break; }
                }
            }
        }

        private void SetupSlave(MachineConfig machine)
        {
            // Create a default data store and wrap it
            var defaultStore = new SlaveDataStore();
            var customStore = new ObservableSlaveDataStore(defaultStore);
            
            // Hook into our custom store's holding registers written event
            customStore.HoldingRegistersWritten += (s, e) => 
            {
                HandleDataWritten(machine, customStore);
            };

            if (machine.Type == ConnectionType.Rtu)
            {
                var port = new SerialPort(machine.SerialPort, machine.BaudRate, machine.Parity, machine.DataBits, machine.StopBits);
                port.Open();
                var adapter = new SerialPortAdapter(port);
                var transport = _modbusFactory.CreateRtuTransport(adapter);
                var network = _modbusFactory.CreateSlaveNetwork(transport);
                
                var slave = _modbusFactory.CreateSlave(machine.SlaveId, customStore);
                network.AddSlave(slave);
                
                lock (_machinesLock)
                {
                    _slaveNetworks.Add(network);
                }
                _ = Task.Run(() => network.ListenAsync());
                OnLog?.Invoke($"RTU Slave started for {machine.Name} on {machine.SerialPort} ID:{machine.SlaveId}");
            }
            else if (machine.Type == ConnectionType.Tcp)
            {
                var listener = new TcpListener(IPAddress.Parse(machine.IpAddress), machine.Port);
                var network = _modbusFactory.CreateSlaveNetwork(listener);
                
                var slave = _modbusFactory.CreateSlave(machine.SlaveId, customStore);
                network.AddSlave(slave);
                
                lock (_machinesLock)
                {
                    _slaveNetworks.Add(network);
                    _machineStores[machine.Name] = customStore;
                }
                _ = Task.Run(() => network.ListenAsync());
                OnLog?.Invoke($"TCP Slave started for {machine.Name} on {machine.IpAddress}:{machine.Port} ID:{machine.SlaveId}");
            }
        }

        private readonly Random _random = new Random();

        private void HandleDataWritten(MachineConfig machine, ISlaveDataStore dataStore)
        {
            if (_isInternalUpdate) return; // Prevent event flood from internal sim loop
            
            var point = new MachineDataPoint
            {
                MachineName = machine.Name,
                Timestamp = DateTime.Now,
                LatencyMs = _random.Next(5, 15) // Realistic jitter for slave processing
            };

            foreach (var tag in machine.Tags)
            {
                var registers = dataStore.HoldingRegisters.ReadPoints(tag.Address, 1);
                ushort value = registers.Length > 0 ? registers[0] : (ushort)0;
                
                string key = $"{tag.Address}:{tag.Name}";
                point.Values[key] = value;
            }

            OnDataWritten?.Invoke(point);
        }

        public void Stop()
        {
            _simCancellation?.Cancel();
            lock (_machinesLock)
            {
                foreach (var network in _slaveNetworks)
                {
                    try { network.Dispose(); } catch {}
                }
                _slaveNetworks.Clear();
                _machineStores.Clear();
            }
        }

    private DateTime _startTime = DateTime.Now;

        private async Task SimulationLoop(CancellationToken token)
        {
            var random = new Random();
            while (!token.IsCancellationRequested)
            {
                try 
                {
                    List<MachineConfig> machinesCopy;
                    lock (_machinesLock)
                    {
                        machinesCopy = _machines.ToList();
                    }

                    foreach (var machine in machinesCopy)
                    {
                        if (!machine.IsEnabled) continue;

                        _isInternalUpdate = true;
                        try 
                        {
                            if (_machineStores.TryGetValue(machine.Name, out var store)) 
                            {
                                foreach (var tag in machine.Tags)
                                {
                                    double val = GenerateRandomValue(tag.Name, random);
                                    ushort regVal = (ushort)Math.Max(0, Math.Min(ushort.MaxValue, val));
                                    store.HoldingRegisters.WritePoints(tag.Address, new[] { regVal });
                                    _modbusService.UpdateMockValue(machine.Name, tag.Address, val);
                                }
                            }
                            else
                            {
                                foreach (var tag in machine.Tags)
                                {
                                    double val = GenerateRandomValue(tag.Name, random);
                                    _modbusService.UpdateMockValue(machine.Name, tag.Address, val);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            OnLog?.Invoke($"Sim Error (Machine {machine.Name}): {ex.Message}");
                        }
                        finally
                        {
                            _isInternalUpdate = false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"Global Simulation Error: {ex.Message}");
                }

                await Task.Delay(3000, token); // User requested 3000ms (3s)
            }
        }

        private double GenerateRandomValue(string tagName, Random random)
        {
            string lowerName = tagName.ToLower();
            double seconds = (DateTime.Now - _startTime).TotalSeconds;
            
            // Industrial Baseline Logic (aligned with ModbusService)
            double baseline;
            if      (lowerName.Contains("volt"))    baseline = 230.0;
            else if (lowerName.Contains("curr") || lowerName.Contains("amp")) baseline = 15.0;
            else if (lowerName.Contains("power") || lowerName.Contains("watt")) baseline = 3450.0;
            else if (lowerName.Contains("temp"))    baseline = 45.0;
            else if (lowerName.Contains("press"))   baseline = 6.5;
            else if (lowerName.Contains("freq"))    baseline = 50.0;
            else if (lowerName.Contains("speed") || lowerName.Contains("rpm")) baseline = 1440.0;
            else if (lowerName.Contains("humid"))   baseline = 40.0;
            else if (lowerName.Contains("flow"))    baseline = 25.0;
            else if (lowerName.Contains("level"))   baseline = 75.0;
            else                                    baseline = 50.0;

            // Composite Wave + Noise for realism
            double drift = Math.Sin(seconds * 0.1) * (baseline * 0.02) + Math.Sin(seconds * 0.03) * (baseline * 0.015);
            double noise = (random.NextDouble() - 0.5) * (baseline * 0.01); // 1% fast jitter
            
            return baseline + drift + noise;
        }

        public void Dispose()
        {
            Stop();
        }
    }

    public class ObservableSlaveDataStore : ISlaveDataStore
    {
        public IPointSource<bool> CoilDiscretes { get; }
        public IPointSource<bool> CoilInputs { get; }
        public IPointSource<ushort> InputRegisters { get; }
        public IPointSource<ushort> HoldingRegisters => _observableHoldingRegisters;

        private readonly ObservablePointSource<ushort> _observableHoldingRegisters;

        public event EventHandler? HoldingRegistersWritten;

        public ObservableSlaveDataStore(ISlaveDataStore inner)
        {
            CoilDiscretes = inner.CoilDiscretes;
            CoilInputs = inner.CoilInputs;
            InputRegisters = inner.InputRegisters;
            _observableHoldingRegisters = new ObservablePointSource<ushort>(inner.HoldingRegisters);
            _observableHoldingRegisters.Written += (s, e) => HoldingRegistersWritten?.Invoke(this, EventArgs.Empty);
        }
    }

    public class ObservablePointSource<T> : IPointSource<T>
    {
        private readonly IPointSource<T> _inner;
        public event EventHandler? Written;

        public ObservablePointSource(IPointSource<T> inner)
        {
            _inner = inner;
        }

        public T[] ReadPoints(ushort startAddress, ushort numberOfPoints)
        {
            return _inner.ReadPoints(startAddress, numberOfPoints);
        }

        public void WritePoints(ushort startAddress, T[] points)
        {
            _inner.WritePoints(startAddress, points);
            Written?.Invoke(this, EventArgs.Empty);
        }
    }
}
