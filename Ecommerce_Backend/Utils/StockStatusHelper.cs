namespace Ecommerce_Backend.Utils
{
    public class StockStatusHelper
    {
        public static string GetStockStatus(int quantity)
        {
            if (quantity == 0) return "OUT_OF_STOCK";
            if (quantity < 5) return "LOW_STOCK";
            return "IN_STOCK";
        }
    }
}
