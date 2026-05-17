using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace syncdesk
{
    public class Product
    {
        public int id { get; set; }
        public string sku { get; set; }
        public string product_name { get; set; }
        public string category { get; set; }
        public string warehouse_location { get; set; }
        public int stock { get; set; }
        public int low_stock_threshold { get; set; }
        public string supplier { get; set; }
    }
}
