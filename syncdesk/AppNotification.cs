using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;

namespace syncdesk
{
    public class AppNotification : INotifyPropertyChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; }
        public string ProductName { get; set; }
        public string Description { get; set; }
        public string Type { get; set; } // "LowStock" or "AuditLog"
        public int ReferenceId { get; set; }
        public string Date { get; set; }

        public bool HasProduct => !string.IsNullOrEmpty(ProductName);

        private bool _isRead;
        public bool IsRead
        {
            get => _isRead;
            set { _isRead = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReadStatusText)); }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public string ReadStatusText => IsRead ? "Read" : "Unread";

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
