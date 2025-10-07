using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace dataAccess.Services
{
    public partial class SqlQueryService
    {
        /// <summary>
        /// Simple allowlist-based dispatcher for structured SQL queries.
        /// Examples:
        ///  - "all products" / "getallproducts"
        ///  - "all suppliers" / "getallsuppliers"
        ///  - "all categories" / "getallcategories"
        ///  - "suppliers: quezon city" (uses SearchSuppliersAsync)
        ///  - "categories: blue"      (uses SearchCategoriesAsync)
        ///  - "order 123"
        /// </summary>
        public async Task<object> DispatchAsync(string input, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(input))
                throw new ArgumentException("Input cannot be empty.", nameof(input));

            var lower = input.ToLowerInvariant().Trim();

            // direct list commands
            if (lower.Contains("all products") || lower.Contains("getallproducts"))
                return await GetAllProductsAsync(100);

            if (lower.Contains("all suppliers") || lower.Contains("getallsuppliers"))
                return await GetSuppliersAsync(100);

            if (lower.Contains("all categories") || lower.Contains("getallcategories"))
                return await GetCategoriesAsync(100);

            // simple searches
            if (lower.StartsWith("suppliers:"))
                return await SearchSuppliersAsync(input.Substring("suppliers:".Length).Trim(), 50);

            if (lower.StartsWith("categories:"))
                return await SearchCategoriesAsync(input.Substring("categories:".Length).Trim(), 50);

            // order details
            var match = Regex.Match(lower, @"order\s+(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var orderId))
                return await GetOrderWithItemsAsync(orderId);

            throw new InvalidOperationException("❌ No matching structured query found in input.");
        }

    }
}
