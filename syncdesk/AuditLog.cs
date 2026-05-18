using System;
using System.Collections.Generic;
using System.Text;

namespace syncdesk
{
    public class AuditLog
    {
        public int id { get; set; }
        public string product_name { get; set; }
        public string sku { get; set; }
        public string action { get; set; }
        public string details { get; set; }
        public string user { get; set; }
        public string date { get; set; }
    }
}