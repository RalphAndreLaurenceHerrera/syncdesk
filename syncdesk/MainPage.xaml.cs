using Microsoft.Maui.Storage;
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

        // The three lists required for the new dashboard and floating search
        private ObservableCollection<Product> _recentProducts = new ObservableCollection<Product>();
        private ObservableCollection<AuditLog> _auditLogs = new ObservableCollection<AuditLog>();
        private ObservableCollection<Product> _searchResults = new ObservableCollection<Product>();
        private ObservableCollection<Product> _fullDisplayProducts = new ObservableCollection<Product>();

        private IDispatcherTimer _refreshTimer;
        private IDispatcherTimer _clockTimer;
        private DateTime _lastUpdateTime = DateTime.Now;
        private HashSet<int> _alreadyNotifiedIds = new HashSet<int>();

        private Product _currentlySelectedProduct;
        private bool _logsExpanded = false;

        public MainPage()
        {
            InitializeComponent();

            // Connect our lists to the screen elements
            RecentProductsList.ItemsSource = _recentProducts;
            DashboardAuditLogsList.ItemsSource = _auditLogs;
            SearchDropdownList.ItemsSource = _searchResults;
            ItemLogsList.ItemsSource = _auditLogs; 
            FullInventoryList.ItemsSource = _fullDisplayProducts;
            FullLogsList.ItemsSource = _auditLogs;
            _lastUpdateTime = Preferences.Default.Get("LastSyncTime", DateTime.Now);
            LoadInventory();
            StartAutoRefresh();
            StartClockTimer();
        }

        // Android 13+ Notification Permissions
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
            // Restored the 15-second timer
            _refreshTimer.Interval = TimeSpan.FromSeconds(15);
            _refreshTimer.Tick += (s, e) => { LoadInventory(); };
            _refreshTimer.Start();
        }

        private void StartClockTimer()
        {
            _clockTimer = Dispatcher.CreateTimer();
            _clockTimer.Interval = TimeSpan.FromSeconds(1);
            _clockTimer.Tick += (s, e) =>
            {
                var timeSpan = DateTime.Now - _lastUpdateTime;

                if (timeSpan.TotalSeconds < 60)
                {
                    LastUpdatedLabel.Text = $"updated {Math.Floor(timeSpan.TotalSeconds)}s ago";
                }
                else if (timeSpan.TotalMinutes < 60)
                {
                    LastUpdatedLabel.Text = $"updated {Math.Floor(timeSpan.TotalMinutes)}m ago";
                }
                else if (timeSpan.TotalHours < 24)
                {
                    LastUpdatedLabel.Text = $"updated {Math.Floor(timeSpan.TotalHours)}h ago";
                }
                else
                {
                    LastUpdatedLabel.Text = $"updated {Math.Floor(timeSpan.TotalDays)}d ago";
                }
            };
            _clockTimer.Start();
        }

        private async void LoadInventory()
        {
            var downloadedProducts = await _inventoryService.GetProductsAsync();
            var downloadedLogs = await _inventoryService.GetAuditLogsAsync();

            if (downloadedProducts != null && downloadedProducts.Count > 0)
            {
                // ONLY reset the clock and save the time if we are actually online
                if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
                {
                    _lastUpdateTime = DateTime.Now;
                    Preferences.Default.Set("LastSyncTime", _lastUpdateTime);
                }

                _allProducts = downloadedProducts;

                // Clear and update the UI lists
                _recentProducts.Clear();
                foreach (var item in _allProducts.Take(5))
                {
                    _recentProducts.Add(item);
                }

                _auditLogs.Clear();
                if (downloadedLogs != null)
                {
                    foreach (var log in downloadedLogs.Take(5))
                    {
                        _auditLogs.Add(log);
                    }
                }

                UpdateNotificationBadge();
            }
        }

        // --- SEARCH BAR FLOATING DROPDOWN LOGIC ---
        private void OnSearchBarFocused(object sender, FocusEventArgs e)
        {
            SearchOverlay.IsVisible = true;
            UpdateSearchDropdown("");
        }

        private void OnSearchBarUnfocused(object sender, FocusEventArgs e)
        {
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(200), () =>
            {
                SearchOverlay.IsVisible = false;
            });
        }

        private async void OnSearchBarTextChanged(object sender, TextChangedEventArgs e)
        {
            await Task.Delay(300);
            if (ProductSearchBar.Text == e.NewTextValue)
            {
                UpdateSearchDropdown(e.NewTextValue.ToLower());
            }
        }

        private void UpdateSearchDropdown(string keyword)
        {
            IEnumerable<Product> filteredList;

            if (string.IsNullOrWhiteSpace(keyword))
            {
                filteredList = _allProducts.OrderBy(p => p.product_name).Take(3);
            }
            else
            {
                filteredList = _allProducts.Where(p =>
                    p.product_name.ToLower().Contains(keyword) ||
                    p.sku.ToLower().Contains(keyword))
                    .OrderBy(p => p.product_name)
                    .Take(3);
            }

            _searchResults.Clear();
            foreach (var item in filteredList)
            {
                _searchResults.Add(item);
            }
        }

        private void OnSearchResultSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is Product selectedProduct)
            {
                OpenProductDetails(selectedProduct);

                SearchOverlay.IsVisible = false;
                ProductSearchBar.Text = string.Empty;
                ProductSearchBar.Unfocus();

                ((CollectionView)sender).SelectedItem = null;
            }
        }

        private void OnCheckAllClicked(object sender, EventArgs e)
        {
            SearchOverlay.IsVisible = false;

            string keyword = ProductSearchBar.Text?.ToLower() ?? "";
            IEnumerable<Product> filteredList;

            if (string.IsNullOrWhiteSpace(keyword))
            {
                filteredList = _allProducts.OrderBy(p => p.product_name);
            }
            else
            {
                filteredList = _allProducts.Where(p =>
                    p.product_name.ToLower().Contains(keyword) ||
                    p.sku.ToLower().Contains(keyword))
                    .OrderBy(p => p.product_name);
            }

            _fullDisplayProducts.Clear();
            foreach (var item in filteredList)
            {
                _fullDisplayProducts.Add(item);
            }

            ProductSearchBar.Unfocus();

            HideAllViews();
            AllProductsView.IsVisible = true;
        }

        private void OnSeeAllRecentClicked(object sender, EventArgs e)
        {
            ProductSearchBar.Text = string.Empty;
            _fullDisplayProducts.Clear();

            foreach (var item in _allProducts.OrderBy(p => p.product_name))
            {
                _fullDisplayProducts.Add(item);
            }

            HideAllViews();
            AllProductsView.IsVisible = true;
        }

        private void OnSeeAllLogsClicked(object sender, EventArgs e)
        {
            HideAllViews();
            AllLogsView.IsVisible = true;
        }

        private void OnProductViewClicked(object sender, EventArgs e)
        {
            var button = sender as Button;
            if (button?.CommandParameter is Product selected)
            {
                OpenProductDetails(selected);
            }
        }

        private void OnBackToDashboardClicked(object sender, EventArgs e)
        {
            HideAllViews();
            DashboardView.IsVisible = true;
        }

        private void OpenProductDetails(Product product)
        {
            _currentlySelectedProduct = product;

            DetailName.Text = product.product_name;
            DetailSku.Text = $"SKU: {product.sku}";
            DetailLocation.Text = $"Location: {product.warehouse_location}";
            DetailStock.Text = $"Qty: {product.stock}";

            HideAllViews();
            ProductDetailsView.IsVisible = true;

            _logsExpanded = false;
            ItemLogsList.IsVisible = false;
            ToggleLogsButton.Text = "^";
        }

        private async void UpdateNotificationBadge()
        {
            foreach (var product in _allProducts)
            {
                if (product.stock < product.low_stock_threshold)
                {
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
                        _alreadyNotifiedIds.Add(product.id);
                    }
                }
                else
                {
                    if (_alreadyNotifiedIds.Contains(product.id)) _alreadyNotifiedIds.Remove(product.id);
                }
            }

            int count = _alreadyNotifiedIds.Count;
            if (count > 0)
            {
                NotifBadgeFrame.IsVisible = true;
                NotifCountLabel.Text = count.ToString();
            }
            else
            {
                NotifBadgeFrame.IsVisible = false;
            }
        }

        private async void OnUpdateStockClicked(object sender, EventArgs e)
        {
            if (_currentlySelectedProduct != null)
            {
                string result = await DisplayPromptAsync(
                    "Update Stock",
                    $"Enter new stock for {_currentlySelectedProduct.product_name}",
                    initialValue: _currentlySelectedProduct.stock.ToString(),
                    keyboard: Keyboard.Numeric);

                if (!string.IsNullOrWhiteSpace(result) && int.TryParse(result, out int newStock))
                {
                    // ONLY PASS 2 PARAMETERS HERE: The ID and the New Stock
                    bool success = await _inventoryService.UpdateStockAsync(_currentlySelectedProduct.id, newStock);

                    if (success)
                    {
                        LoadInventory();
                        DetailStock.Text = $"Qty: {newStock}";
                        _currentlySelectedProduct.stock = newStock;
                    }
                }
            }
        }

        private void OnToggleLogsClicked(object sender, EventArgs e)
        {
            _logsExpanded = !_logsExpanded;
            ItemLogsList.IsVisible = _logsExpanded;
            ToggleLogsButton.Text = _logsExpanded ? "v" : "^";
        }

        private void OnRefreshClicked(object sender, EventArgs e)
        {
            if (_refreshTimer != null)
            {
                _refreshTimer.Stop();
                _refreshTimer.Start();
            }
            LoadInventory();
        }

        private async void OnNotificationsClicked(object sender, EventArgs e)
        {
            await DisplayAlert("Notifications", "Your alerts will appear here", "OK");
        }

        private async void OnSettingsClicked(object sender, EventArgs e)
        {
            await DisplayAlert("Settings", "Settings area coming soon", "OK");
        }
        private void HideAllViews()
        {
            DashboardView.IsVisible = false;
            ProductDetailsView.IsVisible = false;
            AllProductsView.IsVisible = false;
            AllLogsView.IsVisible = false;
        }
    }
}