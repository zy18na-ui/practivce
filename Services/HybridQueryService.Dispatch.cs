using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace dataAccess.Services
{
    public partial class HybridQueryService
    {
        public async Task<object> DispatchAsync(string input, CancellationToken ct = default)
        {
            input ??= string.Empty;
            var lower = input.ToLowerInvariant();

            // products: keyword (default)
            if (lower.StartsWith("products:"))
                return await SearchProductsHybridAsync(input.Substring("products:".Length).Trim());

            // suppliers: keyword
            if (lower.StartsWith("suppliers:"))
                return await SearchSuppliersHybridAsync(input.Substring("suppliers:".Length).Trim());

            // categories: keyword
            if (lower.StartsWith("categories:"))
                return await SearchCategoriesHybridAsync(input.Substring("categories:".Length).Trim());

            // default -> products
            return await SearchProductsHybridAsync(input);
        }
    }
}