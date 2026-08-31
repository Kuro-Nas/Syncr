using Newtonsoft.Json;
using Syncr.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Syncr.Core.Services
{
    public class DataStore
    {
        private readonly string _dataDir;
        private readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);

        public DataStore()
        {
            _dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
            if (!Directory.Exists(_dataDir))
            {
                Directory.CreateDirectory(_dataDir);
            }
            CleanupOldData();
        }

        public async Task SaveDataAsync(MachineDataPoint data)
        {
            await _fileLock.WaitAsync();
            try
            {
                string fileName = $"data_{DateTime.Now:yyyy-MM-dd}.json";
                string filePath = Path.Combine(_dataDir, fileName);

                string json = JsonConvert.SerializeObject(data, Formatting.None) + Environment.NewLine;
                await File.AppendAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving data: {ex.Message}");
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public List<MachineDataPoint> LoadTodayData()
        {
            var list = new List<MachineDataPoint>();
            string fileName = $"data_{DateTime.Now:yyyy-MM-dd}.json";
            string filePath = Path.Combine(_dataDir, fileName);

            _fileLock.Wait();
            try
            {
                if (File.Exists(filePath))
                {
                    var lines = File.ReadAllLines(filePath);
                    foreach (var line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            try
                            {
                                var point = JsonConvert.DeserializeObject<MachineDataPoint>(line);
                                if (point != null) list.Add(point);
                            }
                            catch { /* Ignore corrupt lines */ }
                        }
                    }
                }
            }
            finally
            {
                _fileLock.Release();
            }
            return list;
        }

        private void CleanupOldData()
        {
            try
            {
                var files = Directory.GetFiles(_dataDir, "data_*.json");
                string todayFile = $"data_{DateTime.Now:yyyy-MM-dd}.json";

                foreach (var file in files)
                {
                    if (Path.GetFileName(file) != todayFile)
                    {
                        File.Delete(file);
                        Console.WriteLine($"Deleted old data file: {file}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error cleaning up data: {ex.Message}");
            }
        }
    }
}
