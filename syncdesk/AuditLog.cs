using System;
using System.Collections.Generic;
using System.Text;

namespace syncdesk
{
    public class AuditLog
    {
        public int id { get; set; }
        public int product_id { get; set; } // Added this so we can link to the product!
        public string product_name { get; set; }
        public string sku { get; set; }

        public string action { get; set; }
        public string details { get; set; }
        public string user { get; set; }

        // This will now automatically hold "May 18, 2026 09:13 PM"
        public string date { get; set; }
    }
}