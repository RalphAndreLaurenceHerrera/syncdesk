using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;

namespace syncdesk
{
    public class InventoryService
    {
        // We reuse one HttpClient for performance
        private readonly HttpClient _client;

        public InventoryService()
        {
            _client = new HttpClient();
        }

        public async Task<List<Product>> GetProductsAsync()
        {
            // Note: 10.0.2.2 is the address Android emulators use to connect to your computer's localhost
            // If testing on Windows Machine directly, use http://localhost/syncdesk/api_inventory.php
            //string url = "http://10.0.2.2/syncdesk/api_inventory.php";
            string url = "http://100.75.86.125/syncdesk/api_inventory.php"; 
            
            string localFilePath = Path.Combine(FileSystem.AppDataDirectory, "inventory_backup.json");

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
            };

            // CHECK 1: Instantly check if we have internet to avoid 15-second lag spikes
            if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
            {
                try
                {
                    HttpResponseMessage response = await _client.GetAsync(url);

                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();

                        // Save the fresh data directly to the phone storage
                        File.WriteAllText(localFilePath, json);

                        return JsonSerializer.Deserialize<List<Product>>(json, options) ?? new List<Product>();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Internet fetch failed: {ex.Message}");
                }
            }

            // FALLBACK: If we are offline or the server is down, load the local file instantly
            if (File.Exists(localFilePath))
            {
                string localJson = File.ReadAllText(localFilePath);
                return JsonSerializer.Deserialize<List<Product>>(localJson, options) ?? new List<Product>();
            }

            return new List<Product>();
        }

        public async Task<bool> UpdateStockAsync(int productId, int newStock)
        {
            // Point directly to your custom API file
            string url = "http://100.75.86.125/syncdesk/api_update_stock.php";

            var payload = new
            {
                id = productId,
                stock = newStock
            };

            string jsonPayload = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");

            try
            {
                HttpResponseMessage response = await _client.PostAsync(url, content);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating stock: {ex.Message}");
                return false;
            }
        }

        public async Task<List<AuditLog>> GetAuditLogsAsync()
        {
            string url = "http://100.75.86.125/syncdesk/api_logs.php";
            string localFilePath = Path.Combine(FileSystem.AppDataDirectory, "logs_backup.json");

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
            };

            // CHECK 2: Check internet so we don't lag while trying to fetch logs
            if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
            {
                try
                {
                    var response = await _client.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();

                        // Save the fresh logs directly to the phone storage
                        File.WriteAllText(localFilePath, json);

                        return JsonSerializer.Deserialize<List<AuditLog>>(json, options) ?? new List<AuditLog>();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error fetching logs: {ex.Message}");
                }
            }

            // FALLBACK: Load the offline logs if the internet is down!
            if (File.Exists(localFilePath))
            {
                string localJson = File.ReadAllText(localFilePath);
                return JsonSerializer.Deserialize<List<AuditLog>>(localJson, options) ?? new List<AuditLog>();
            }

            return new List<AuditLog>();
        }
    }
}
