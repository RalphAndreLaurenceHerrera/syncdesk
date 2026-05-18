using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;

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

            //string url = "http://192.168.18.6/syncdesk/api_inventory.php";
            string url = "http://100.75.86.125/syncdesk/api_inventory.php";

            try
            {
                HttpResponseMessage response = await _client.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();

                    // Add these options to handle the quotes around numbers
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
                    };

                    List<Product> products = JsonSerializer.Deserialize<List<Product>>(json, options);
                    return products ?? new List<Product>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching data: {ex.Message}");
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
            try
            {
                var response = await _client.GetAsync("http://100.75.86.125y/syncdesk/api_logs.php");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<List<AuditLog>>(json);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching logs: {ex.Message}");
            }
            return new List<AuditLog>();
        }
    }
}
