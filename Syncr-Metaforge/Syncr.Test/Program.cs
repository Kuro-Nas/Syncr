using Syncr.Core.Models;
using Syncr.Core.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Syncr.Test
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Starting Syncr Test...");

            var configService = new ConfigService();
            var config = configService.LoadConfig();
            Console.WriteLine($"Loaded Config for {config.Machines.Count} machines.");

            var dataStore = new DataStore();
            var modbusService = new ModbusService(config, useMock: true);

            modbusService.OnDataReceived += async (data) =>
            {
                Console.WriteLine($"Received Data: {data.MachineName} - {data.Timestamp}");
                await dataStore.SaveDataAsync(data);
            };

            modbusService.Start();

            await Task.Delay(5000);

            modbusService.Stop();
            Console.WriteLine("Test Complete.");
        }
    }
}
