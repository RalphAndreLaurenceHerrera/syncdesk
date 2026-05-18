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
        private ObservableCollection<Product> _searchResults = new ObservableCollection<Product>();
        private ObservableCollection<Product> _fullDisplayProducts = new ObservableCollection<Product>();

        private List<AuditLog> _allLogs = new List<AuditLog>();
        private ObservableCollection<AuditLog> _auditLogs = new ObservableCollection<AuditLog>();
        private ObservableCollection<AuditLog> _fullDisplayLogs = new ObservableCollection<AuditLog>();
        private ObservableCollection<AuditLog> _productSpecificLogs = new ObservableCollection<AuditLog>();

        private List<AppNotification> _allNotifications = new List<AppNotification>();
        private ObservableCollection<AppNotification> _displayNotifications = new ObservableCollection<AppNotification>();
        private bool _isFirstLoadComplete = false;

        private IDispatcherTimer _refreshTimer;
        private IDispatcherTimer _clockTimer;
        private DateTime _lastUpdateTime = DateTime.Now;
        private HashSet<int> _alreadyNotifiedIds = new HashSet<int>();

        private Product _currentlySelectedProduct;
        private AuditLog _currentlySelectedLog;
        private bool _logsExpanded = false;

        public MainPage()
        {
            InitializeComponent();

            RecentProductsList.ItemsSource = _recentProducts;
            DashboardAuditLogsList.ItemsSource = _auditLogs;
            SearchDropdownList.ItemsSource = _searchResults;

            // CHANGE THESE TWO LINES:
            ItemLogsList.ItemsSource = _productSpecificLogs;
            FullLogsList.ItemsSource = _fullDisplayLogs;

            _allNotifications = _inventoryService.LoadLocalNotifications();
            NotificationsList.ItemsSource = _displayNotifications;

            FullInventoryList.ItemsSource = _fullDisplayProducts;

            _lastUpdateTime = Preferences.Default.Get("LastSyncTime", DateTime.Now);

            // --- LOAD SAVED SETTINGS ---
            ThemePicker.SelectedIndex = Preferences.Default.Get("AppTheme", 0);
            AutoRefreshSwitch.IsToggled = Preferences.Default.Get("AutoRefreshEnabled", true);
            RefreshFrequencyPicker.SelectedIndex = Preferences.Default.Get("AutoRefreshIntervalIndex", 0);

            // Apply the theme immediately
            ApplyTheme(ThemePicker.SelectedIndex);

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
            if (_refreshTimer == null)
            {
                _refreshTimer = Dispatcher.CreateTimer();
                _refreshTimer.Tick += (s, e) => { LoadInventory(); };
            }

            UpdateTimerInterval();

            // Only start it if the setting is turned on!
            if (Preferences.Default.Get("AutoRefreshEnabled", true))
            {
                _refreshTimer.Start();
            }
        }

        private void UpdateTimerInterval()
        {
            int index = Preferences.Default.Get("AutoRefreshIntervalIndex", 0);
            int seconds = 15; // Default

            if (index == 1) seconds = 30;
            else if (index == 2) seconds = 60;
            else if (index == 3) seconds = 300; // 5 minutes

            _refreshTimer.Interval = TimeSpan.FromSeconds(seconds);
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
                // 1. Update the connection timer
                if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
                {
                    _lastUpdateTime = DateTime.Now;
                    Preferences.Default.Set("LastSyncTime", _lastUpdateTime);
                }

                _allProducts = downloadedProducts;
                _allLogs = downloadedLogs ?? new List<AuditLog>(); // Ensures it never crashes if logs are empty

                // 2. Setup the Dashboard Logs (Take 5)
                _auditLogs.Clear();
                foreach (var log in _allLogs.Take(6))
                {
                    _auditLogs.Add(log);
                }

                // 3. Setup the Complete Logs (Take All)
                _fullDisplayLogs.Clear();
                foreach (var log in _allLogs)
                {
                    _fullDisplayLogs.Add(log);
                }

                // 4. Sort the "Recently Updated" products using the log timeline
                _recentProducts.Clear();

                // Grab the product IDs from the logs (They are already ordered newest to oldest!)
                var recentProductIds = _allLogs.Select(l => l.product_id).Distinct().ToList();

                var sortedRecentProducts = new List<Product>();

                // Add the products in the exact order they appear in the activity logs
                foreach (var id in recentProductIds)
                {
                    var product = _allProducts.FirstOrDefault(p => p.id == id);
                    if (product != null)
                    {
                        sortedRecentProducts.Add(product);
                    }
                }

                // Fallback: If there are brand new products with zero logs, stick them at the bottom
                var untouchedProducts = _allProducts.Where(p => !recentProductIds.Contains(p.id));
                sortedRecentProducts.AddRange(untouchedProducts);

                // Finally, take the top 5 for the dashboard display
                foreach (var item in sortedRecentProducts.Take(6))
                {
                    _recentProducts.Add(item);
                }

                UpdateNotificationBadge();
                SyncNotifications();
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
                filteredList = _allProducts.OrderBy(p => p.product_name).Take(4);
            }
            else
            {
                filteredList = _allProducts.Where(p =>
                    p.product_name.ToLower().Contains(keyword) ||
                    p.sku.ToLower().Contains(keyword))
                    .OrderBy(p => p.product_name)
                    .Take(4);
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

        private void PerformSearch()
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
                    p.sku.ToLower().Contains(keyword) ||
                    p.id.ToString().Contains(keyword))
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

        private void OnSearchButtonClicked(object sender, EventArgs e)
        {
            PerformSearch();
        }

        private void OnSearchButtonPressed(object sender, EventArgs e)
        {
            PerformSearch();
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

            _productSpecificLogs.Clear();
            var matchedLogs = _allLogs.Where(l => l.product_id == product.id).ToList();
            foreach (var log in matchedLogs)
            {
                _productSpecificLogs.Add(log);
            }

            HideAllViews();
            ProductDetailsView.IsVisible = true;
        }
        private void UpdateNotificationBadge()
        {
            int unreadCount = _allNotifications.Count(n => !n.IsRead);
            if (unreadCount > 0)
            {
                NotifBadgeFrame.IsVisible = true;
                NotifCountLabel.Text = unreadCount.ToString();
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

        private void OnRefreshClicked(object sender, EventArgs e)
        {
            if (_refreshTimer != null)
            {
                _refreshTimer.Stop();
                _refreshTimer.Start();
            }
            LoadInventory();
        }

        private async void ShowLocalNotification(string title, string description, int id)
        {
            var request = new NotificationRequest { NotificationId = id, Title = title, Description = description, BadgeNumber = 1 };
            await LocalNotificationCenter.Current.Show(request);
        }

        private void SyncNotifications()
        {
            bool madeChanges = false;

            // 1. Check for Low Stock
            foreach (var product in _allProducts)
            {
                if (product.stock < product.low_stock_threshold)
                {
                    // THE FIX: We removed '&& !n.IsRead'. Now it checks if ANY notification exists so it doesn't spam you!
                    var existing = _allNotifications.FirstOrDefault(n => n.Type == "LowStock" && n.ReferenceId == product.id);

                    if (existing == null)
                    {
                        var notif = new AppNotification { Title = "Low Stock Alert", ProductName = product.product_name, Description = $"Only {product.stock} left in stock.", Type = "LowStock", ReferenceId = product.id, Date = DateTime.Now.ToString("MMM dd, yyyy hh:mm tt"), IsRead = false };
                        _allNotifications.Insert(0, notif);
                        madeChanges = true;
                        if (_isFirstLoadComplete) ShowLocalNotification(notif.Title, notif.Description, product.id);
                    }
                }
                else
                {
                    // SMART CLEANUP: If the stock is healthy again, delete the old alert so it can trigger again in the future!
                    int removedCount = _allNotifications.RemoveAll(n => n.Type == "LowStock" && n.ReferenceId == product.id);
                    if (removedCount > 0)
                    {
                        madeChanges = true;
                    }
                }
            }

            // 2. Check for New Audit Logs
            foreach (var log in _allLogs)
            {
                var existing = _allNotifications.FirstOrDefault(n => n.Type == "AuditLog" && n.ReferenceId == log.id);
                if (existing == null)
                {
                    var notif = new AppNotification { Title = "New Activity", ProductName = log.product_name, Description = log.action, Type = "AuditLog", ReferenceId = log.id, Date = log.date, IsRead = false };
                    _allNotifications.Insert(0, notif);
                    madeChanges = true;
                    if (_isFirstLoadComplete) ShowLocalNotification(notif.Title, $"{notif.ProductName}: {notif.Description}", log.id + 10000);
                }
            }

            _isFirstLoadComplete = true;

            if (madeChanges) _inventoryService.SaveLocalNotifications(_allNotifications);

            UpdateNotificationsList();
            UpdateNotificationBadge();
        }

        private void UpdateNotificationsList()
        {
            _displayNotifications.Clear();
            string filter = NotificationFilterPicker.SelectedItem?.ToString() ?? "All";
            var filteredList = _allNotifications.AsEnumerable();

            if (filter == "Unread") filteredList = filteredList.Where(n => !n.IsRead);
            else if (filter == "Read") filteredList = filteredList.Where(n => n.IsRead);

            foreach (var n in filteredList) _displayNotifications.Add(n);
        }

        private void OnNotificationFilterChanged(object sender, EventArgs e) => UpdateNotificationsList();

        private void OnMarkAllReadClicked(object sender, EventArgs e)
        {
            foreach (var n in _allNotifications) n.IsRead = true;
            _inventoryService.SaveLocalNotifications(_allNotifications);
            UpdateNotificationsList();
            UpdateNotificationBadge();
        }

        private void OnMarkSelectedReadClicked(object sender, EventArgs e)
        {
            foreach (var n in _allNotifications.Where(n => n.IsSelected))
            {
                n.IsRead = true;
                n.IsSelected = false; // Reset checkbox
            }
            _inventoryService.SaveLocalNotifications(_allNotifications);
            UpdateNotificationsList();
            UpdateNotificationBadge();
        }

        private void OnDeleteSelectedClicked(object sender, EventArgs e)
        {
            var itemsToDelete = _allNotifications.Where(n => n.IsSelected).ToList();

            if (itemsToDelete.Count == 0) return;

            foreach (var item in itemsToDelete)
            {
                _allNotifications.Remove(item);
            }

            _inventoryService.SaveLocalNotifications(_allNotifications);
            UpdateNotificationsList();
            UpdateNotificationBadge();
        }

        private void OnNotificationViewClicked(object sender, EventArgs e)
        {
            var button = sender as Button;
            if (button?.CommandParameter is AppNotification notif)
            {
                // Mark as read immediately!
                notif.IsRead = true;
                _inventoryService.SaveLocalNotifications(_allNotifications);
                UpdateNotificationsList();
                UpdateNotificationBadge();

                // Redirect appropriately
                if (notif.Type == "LowStock")
                {
                    var product = _allProducts.FirstOrDefault(p => p.id == notif.ReferenceId);
                    if (product != null) OpenProductDetails(product);
                }
                else if (notif.Type == "AuditLog")
                {
                    var log = _allLogs.FirstOrDefault(l => l.id == notif.ReferenceId);
                    // This reuses the exact same logic your other log buttons use!
                    if (log != null)
                    {
                        _currentlySelectedLog = log;
                        LogDetailAction.Text = log.action;
                        LogDetailDate.Text = log.date;
                        LogDetailUser.Text = $"User: {log.user ?? "admin"}";
                        LogDetailInfo.Text = log.details;
                        LogDetailProduct.Text = log.product_name;
                        LogDetailSku.Text = $"SKU: {log.sku}";
                        HideAllViews();
                        LogDetailsView.IsVisible = true;
                    }
                }
            }
        }

        private void OnNotificationsClicked(object sender, EventArgs e)
        {
            HideAllViews(); 
            TopSearchBarArea.IsVisible = false;
            NotificationsView.IsVisible = true;
            UpdateNotificationsList();
        }

        private void OnSettingsClicked(object sender, EventArgs e)
        {
            HideAllViews(); 
            TopSearchBarArea.IsVisible = false;
            SettingsView.IsVisible = true;

        }

        private void HideAllViews()
        {
            DashboardView.IsVisible = false;
            ProductDetailsView.IsVisible = false;
            AllProductsView.IsVisible = false;
            AllLogsView.IsVisible = false;
            LogDetailsView.IsVisible = false;
            NotificationsView.IsVisible = false;
            SettingsView.IsVisible = false;
            TopSearchBarArea.IsVisible = true;
        }

        private void OnLogViewClicked(object sender, EventArgs e)
        {
            var button = sender as Button;
            if (button?.CommandParameter is AuditLog selectedLog)
            {
                _currentlySelectedLog = selectedLog;

                // Populate the Log Details screen
                LogDetailAction.Text = selectedLog.action;
                LogDetailDate.Text = selectedLog.date;
                LogDetailUser.Text = $"User: {selectedLog.user ?? "admin"}";
                LogDetailInfo.Text = selectedLog.details;
                LogDetailProduct.Text = selectedLog.product_name;
                LogDetailSku.Text = $"SKU: {selectedLog.sku}";

                HideAllViews();
                LogDetailsView.IsVisible = true;
            }
        }

        private void OnViewProductFromLogClicked(object sender, EventArgs e)
        {
            if (_currentlySelectedLog != null)
            {
                // Search our downloaded inventory for the matching product ID
                var product = _allProducts.FirstOrDefault(p => p.id == _currentlySelectedLog.product_id);

                if (product != null)
                {
                    OpenProductDetails(product);
                }
                else
                {
                    // Just in case the product was deleted from the database!
                    DisplayAlertAsync("Not Found", "This product is no longer in your active inventory.", "OK");
                }
            }
        }

        // --- SETTINGS LOGIC ---
        private void OnThemeChanged(object sender, EventArgs e)
        {
            int selectedIndex = ThemePicker.SelectedIndex;
            Preferences.Default.Set("AppTheme", selectedIndex);
            ApplyTheme(selectedIndex);
        }

        private void ApplyTheme(int themeIndex)
        {
            switch (themeIndex)
            {
                case 1:
                    Application.Current.UserAppTheme = AppTheme.Light;
                    break;
                case 2:
                    Application.Current.UserAppTheme = AppTheme.Dark;
                    break;
                default:
                    Application.Current.UserAppTheme = AppTheme.Unspecified; // Follows the Phone's System Setting
                    break;
            }
        }

        private void OnAutoRefreshToggled(object sender, ToggledEventArgs e)
        {
            Preferences.Default.Set("AutoRefreshEnabled", e.Value);
            if (e.Value)
                _refreshTimer?.Start();
            else
                _refreshTimer?.Stop();
        }

        private void OnRefreshFrequencyChanged(object sender, EventArgs e)
        {
            Preferences.Default.Set("AutoRefreshIntervalIndex", RefreshFrequencyPicker.SelectedIndex);
            if (_refreshTimer != null)
            {
                UpdateTimerInterval();

                // If it's currently running, restart it to apply the new speed immediately
                if (AutoRefreshSwitch.IsToggled)
                {
                    _refreshTimer.Stop();
                    _refreshTimer.Start();
                }
            }
        }
    }
}