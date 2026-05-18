using System;
using System.Collections.Generic;
using System.Text;

namespace syncdesk
{
    public class AuditLog
    {
        public int id { get; set; }
        public int product_id { get; set; }
        public string product_name { get; set; }
        public string action { get; set; }
        public DateTime created_at { get; set; }

        // This converts the raw database timestamp into the friendly "May 18, 2026" format for the UI
        public string date => created_at.ToString("MMM dd, yyyy");
    }
}