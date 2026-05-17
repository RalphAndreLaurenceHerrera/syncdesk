using Microsoft.Maui.Controls;
using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace syncdesk
{
    public partial class MainPage : ContentPage
    {
        private readonly InventoryService _inventoryService = new InventoryService();
        private List<Product> _allProducts = new List<Product>();
        private ObservableCollection<Product> _displayList = new ObservableCollection<Product>();
        private IDispatcherTimer _refreshTimer; 
        private HashSet<int> _alreadyNotifiedIds = new HashSet<int>();

        public MainPage()
        {
            InitializeComponent();
            InventoryList.ItemsSource = _displayList;
            LoadInventory(); 
            StartAutoRefresh();
        }
        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (await LocalNotificationCenter.Current.AreNotificationsEnabled() == false)
            {
                await LocalNotificationCenter.Current.RequestNotificationPermission();
            }
        }
        private void StartAutoRefresh()
        {
            _refreshTimer = Dispatcher.CreateTimer();
            _refreshTimer.Interval = TimeSpan.FromSeconds(15);

            // This tells the app what to do every time a minute passes
            _refreshTimer.Tick += (s, e) =>
            {
                LoadInventory();
            };

            _refreshTimer.Start();
        }

        private async void LoadInventory()
        {
            // Save the downloaded items to the master list
            _allProducts = await _inventoryService.GetProductsAsync();

            string keyword = ProductSearchBar.Text?.ToLower() ?? "";
            IEnumerable<Product> filteredList;

            if (string.IsNullOrWhiteSpace(keyword))
            {
                filteredList = _allProducts;
            }
            else
            {
                filteredList = _allProducts.Where(p =>
                    p.product_name.ToLower().Contains(keyword) ||
                    p.sku.ToLower().Contains(keyword));
            }

            // Gently update the screen
            _displayList.Clear();
            foreach (var item in filteredList)
            {
                _displayList.Add(item);
            }

            foreach (var product in _allProducts)
            {
                if (product.stock < product.low_stock_threshold)
                {
                    // Check if we already sent an alert for this specific product
                    if (!_alreadyNotifiedIds.Contains(product.id))
                    {
                        var request = new NotificationRequest
                        {
                            NotificationId = product.id,
                            Title = "Low Stock Alert",
                            Subtitle = product.product_name,
                            Description = $"Only {product.stock} left in stock",
                            BadgeNumber = 1
                        };

                        await LocalNotificationCenter.Current.Show(request);

                        // Add it to the memory list so it does not trigger again
                        _alreadyNotifiedIds.Add(product.id);
                    }
                }
                else
                {
                    // If the stock is normal again we remove it from the list
                    // This allows it to trigger a new alert if it drops low again later
                    if (_alreadyNotifiedIds.Contains(product.id))
                    {
                        _alreadyNotifiedIds.Remove(product.id);
                    }
                }
            }
        }

        // This runs every time you type or delete a letter
        private async void OnSearchBarTextChanged(object sender, TextChangedEventArgs e)
        {
            string keyword = e.NewTextValue.ToLower();
            await Task.Delay(500);

            if (ProductSearchBar.Text?.ToLower() == keyword)
            {
                IEnumerable<Product> filteredList;

                if (string.IsNullOrWhiteSpace(keyword))
                {
                    filteredList = _allProducts;
                }
                else
                {
                    filteredList = _allProducts.Where(p =>
                        p.product_name.ToLower().Contains(keyword) ||
                        p.sku.ToLower().Contains(keyword));
                }

                _displayList.Clear();
                foreach (var item in filteredList)
                {
                    _displayList.Add(item);
                }
            }
        }

        // This runs when the user clicks the "Refresh Data" button
        private void OnRefreshClicked(object sender, EventArgs e)
        {
            if (_refreshTimer != null)
            {
                _refreshTimer.Stop();
                _refreshTimer.Start();
            }

            LoadInventory();
        }

        private async void OnProductSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is Product selectedProduct)
            {
                string result = await DisplayPromptAsync(
                    "Update Stock",
                    $"Enter new stock for {selectedProduct.product_name}",
                    initialValue: selectedProduct.stock.ToString(),
                    keyboard: Keyboard.Numeric);

                if (!string.IsNullOrWhiteSpace(result) && int.TryParse(result, out int newStock))
                {
                    bool success = await _inventoryService.UpdateStockAsync(selectedProduct.id, newStock);
                    if (success)
                    {
                        // Refresh the list to show the new number
                        LoadInventory();
                    }
                }

                // Remove the highlight from the tapped item
                ((CollectionView)sender).SelectedItem = null;
            }
        }
        private async void OnNotificationsClicked(object sender, EventArgs e)
        {
            await DisplayAlert("Notifications", "Your alerts will appear here", "OK");
        }
    }
}
