using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System;


namespace dataAccess.Services
{
    public partial class VectorSearchService
    {
        /// <summary>
        /// Dispatches a vector search based on input like "similar: shoes topk:5".
        /// Right now it forwards to SearchProductIdsAsync.
        /// </summary>
        public async Task<object> DispatchAsync(string input, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(input))
                throw new ArgumentException("Input cannot be empty.", nameof(input));

            // Default topK = 5 (parse "topk:<n>")
            var topK = 5;
            var match = Regex.Match(input, @"topk:(\d+)", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var k)) topK = k;
            if (topK < 1) topK = 1;
            if (topK > 20) topK = 20; // cap to protect DB/latency


            var lower = input.ToLowerInvariant().Trim();

            // products: <query>
            if (lower.StartsWith("products:"))
            {
                var q = input.Substring("products:".Length).Trim();
                return await SearchProductIdsAsync(q, topK, null, ct);
            }

            // suppliers: <query>
            if (lower.StartsWith("suppliers:"))
            {
                var q = input.Substring("suppliers:".Length).Trim();
                return await SearchSupplierIdsAsync(q, topK, null, ct);
            }

            // categories: <query>
            if (lower.StartsWith("categories:"))
            {
                var q = input.Substring("categories:".Length).Trim();
                return await SearchCategoryIdsAsync(q, topK, null, ct);
            }

            // Default → products
            return await SearchProductIdsAsync(input, topK, null, ct);
        }

    }
}
