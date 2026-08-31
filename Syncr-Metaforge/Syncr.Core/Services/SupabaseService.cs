using Supabase;
using Supabase.Postgrest.Attributes;
using Syncr.Core.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Net;

namespace Syncr.Core.Services
{
    public class SupabaseService
    {
        private Client? _client;
        private CloudConfig _config;
        private bool _isConnected;
        private Queue<MachineDataPoint> _pendingQueue = new Queue<MachineDataPoint>();
        private readonly string _syncFilePath;
        private bool _isRetrying;
        private DateTime? _lastSyncTime;
        private int _totalSessionSyncs;
        private int _retryCount;
        private readonly SemaphoreSlim _pushLock = new SemaphoreSlim(1, 1);

        public event Action<string>? OnStatusChanged;
        public event Action? OnTelemetryUpdated;

        public int PendingQueueCount { get { lock (_pendingQueue) { return _pendingQueue.Count; } } }
        public bool IsCloudConnected => _isConnected;
        public DateTime? LastSyncTime => _lastSyncTime;
        public int TotalSessionSyncs => _totalSessionSyncs;
        public int RetryCount => _retryCount;

        public SupabaseService(CloudConfig config)
        {
            _config = config;
#pragma warning disable SYSLIB0014
            // Force TLS 1.2 and 1.3 for modern Supabase SSL requirements
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
#pragma warning restore SYSLIB0014
            
            _syncFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pending_sync.json");
            LoadPendingQueue();
            ResetConfig(config);
            Task.Run(RetryLoop);
        }

        public void ResetConfig(CloudConfig config)
        {
            if (_config != null) _config.PropertyChanged -= OnConfigPropertyChanged;
            _config = config;
            _config.PropertyChanged += OnConfigPropertyChanged;
            _ = InitializeClientAsync();
        }

        private void OnConfigPropertyChanged(object? sender, PropertyChangedEventArgs e) => _ = InitializeClientAsync();

        private void LoadPendingQueue()
        {
            try
            {
                if (File.Exists(_syncFilePath))
                {
                    string raw = File.ReadAllText(_syncFilePath);
                    string json = EncryptionService.Decrypt(raw);
                    var list = JsonConvert.DeserializeObject<List<MachineDataPoint>>(json);
                    if (list != null) _pendingQueue = new Queue<MachineDataPoint>(list);
                }
            }
            catch { }
        }

        private void SavePendingQueue()
        {
            try
            {
                List<MachineDataPoint> snapshot;
                lock (_pendingQueue)
                {
                    snapshot = _pendingQueue.ToList();
                }
                string json = JsonConvert.SerializeObject(snapshot);
                string encrypted = EncryptionService.Encrypt(json);
                File.WriteAllText(_syncFilePath, encrypted);
            }
            catch { }
        }

        private async Task RetryLoop()
        {
            while (true)
            {
                if (_isConnected && _pendingQueue.Count > 0 && !_isRetrying)
                {
                    _isRetrying = true;
                    await ProcessQueue();
                    _isRetrying = false;
                }
                await Task.Delay(30000);
            }
        }

        private async Task ProcessQueue()
        {
            int count = _pendingQueue.Count;
            for (int i = 0; i < count; i++)
            {
                if (!_isConnected) break;

                MachineDataPoint data;
                lock (_pendingQueue)
                {
                    if (_pendingQueue.Count == 0) break;
                    data = _pendingQueue.Peek();
                }

                if (await InternalPush(data))
                {
                    lock (_pendingQueue)
                    {
                        _pendingQueue.Dequeue();
                        _lastSyncTime = DateTime.Now;
                        _totalSessionSyncs++;
                        SavePendingQueue();
                        OnTelemetryUpdated?.Invoke();
                    }
                }
                else
                {
                    _retryCount++;
                    OnTelemetryUpdated?.Invoke();
                    break;
                }
            }
        }

        private async Task<bool> InternalPush(MachineDataPoint data)
        {
            if (!_isConnected || _client == null || !_config.IsEnabled) return false;

            try
            {
                var model = new MachineTelemetry
                {
                    MachineName = data.MachineName,
                    Timestamp   = data.Timestamp,
                    ValuesJson  = JsonConvert.SerializeObject(data.Values)
                };

                await _client.From<MachineTelemetry>().Insert(model);
                return true;
            }
            catch (Exception ex)
            {
                string msg = ex.InnerException != null ? $"{ex.Message} -> {ex.InnerException.Message}" : ex.Message;
                OnStatusChanged?.Invoke($"[SSL/Network] Push Failed: {msg}");
                return false;
            }
        }

        public async Task<(bool ok, string error)> TestConnectionAsync()
        {
            if (_client == null) return (false, "Client not initialized");
            try
            {
                // Lightweight check: select 0 rows
                await _client.From<MachineTelemetry>().Limit(1).Get();
                return (true, "");
            }
            catch (Exception ex)
            {
                string msg = ex.InnerException != null ? $"{ex.Message} -- {ex.InnerException.Message}" : ex.Message;
                return (false, msg);
            }
        }

        private async Task InitializeClientAsync()
        {
            if (!_config.IsEnabled ||
                string.IsNullOrWhiteSpace(_config.SupabaseUrl) ||
                string.IsNullOrWhiteSpace(_config.SupabaseKey))
            {
                _client = null;
                _isConnected = false;
                OnStatusChanged?.Invoke("Disabled");
                return;
            }

            try
            {
                OnStatusChanged?.Invoke("Connecting...");
                
                var options = new SupabaseOptions
                {
                    AutoRefreshToken = true,
                    AutoConnectRealtime = false
                };

                _client = new Client(_config.SupabaseUrl, _config.SupabaseKey, options);
                await _client.InitializeAsync();
                _isConnected = true;
                OnStatusChanged?.Invoke("Connected");

                // Flush any pending data immediately
                if (_pendingQueue.Count > 0)
                    _ = Task.Run(ProcessQueue);
            }
            catch (Exception ex)
            {
                _isConnected = false;
                string msg = ex.InnerException != null ? $"{ex.Message} -> {ex.InnerException.Message}" : ex.Message;
                OnStatusChanged?.Invoke($"Error: {msg}");
            }
        }

        public async Task PushDataAsync(MachineDataPoint data)
        {
            if (!_config.IsEnabled) return;

            // Storage Optimization: Don't push empty data points (e.g. {})
            if (data.Values == null || data.Values.Count == 0)
            {
                OnStatusChanged?.Invoke($"Skipped empty data for {data.MachineName}");
                return;
            }

            // Concurrency Guard: If we are already pushing (waiting for a 100s timeout), 
            // don't start a parallel task. Just queue it immediately.
            if (!_pushLock.Wait(0))
            {
                lock (_pendingQueue)
                {
                    _pendingQueue.Enqueue(data);
                    _retryCount++;
                    SavePendingQueue();
                }
                OnStatusChanged?.Invoke($"Queued ({_pendingQueue.Count} pending)");
                OnTelemetryUpdated?.Invoke();
                return;
            }

            try
            {
                bool success = await InternalPush(data);

                if (success)
                {
                    _lastSyncTime = DateTime.Now;
                    _totalSessionSyncs++;
                    OnStatusChanged?.Invoke("Data Pushed");
                    OnTelemetryUpdated?.Invoke();
                }
                else
                {
                    lock (_pendingQueue)
                    {
                        _pendingQueue.Enqueue(data);
                        _retryCount++;
                        SavePendingQueue();
                    }
                    OnStatusChanged?.Invoke($"Queued ({_pendingQueue.Count} pending)");
                    OnTelemetryUpdated?.Invoke();
                }
            }
            finally
            {
                _pushLock.Release();
            }
        }
    }

    [Table("machine_telemetry")]
    public class MachineTelemetry : Supabase.Postgrest.Models.BaseModel
    {
        [PrimaryKey("id", false)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Column("machine_name")]
        public string MachineName { get; set; } = "";

        [Column("timestamp")]
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Serialized as a JSON string into the Supabase "values" jsonb column.
        /// </summary>
        [Column("values")]
        public string ValuesJson { get; set; } = "{}";
    }
}
