using dataAccess.Entities;
using System.Linq;

namespace dataAccess.Services
{
    public partial class HybridQueryService
    {
        private readonly SqlQueryService _sqlService;
        private readonly VectorSearchService _vectorService;

        public HybridQueryService(SqlQueryService sqlService, VectorSearchService vectorService)
        {
            _sqlService = sqlService;
            _vectorService = vectorService;
        }

        // Hybrid: vector → IDs → EF, preserve ANN order
        public async Task<List<Product>> SearchProductsHybridAsync(string input, int topK = 10, float[]? qvec = null)
        {
            var ids = await _vectorService.SearchProductIdsAsync(input, topK, qvec);
            if (ids.Count == 0) return new List<Product>();
            var list = await _sqlService.GetProductsByIdsAsync(ids);
            return list.OrderBy(p => ids.IndexOf(p.ProductId)).ToList();
        }

        public async Task<List<Supplier>> SearchSuppliersHybridAsync(string input, int topK = 10, float[]? qvec = null)
        {
            var ids = await _vectorService.SearchSupplierIdsAsync(input, topK, qvec);
            if (ids.Count == 0) return new List<Supplier>();
            var list = await _sqlService.GetSuppliersByIdsAsync(ids);
            return list.OrderBy(s => ids.IndexOf(s.SupplierId)).ToList();
        }

        public async Task<List<ProductCategory>> SearchCategoriesHybridAsync(string input, int topK = 10, float[]? qvec = null)
        {
            var ids = await _vectorService.SearchCategoryIdsAsync(input, topK, qvec);
            if (ids.Count == 0) return new List<ProductCategory>();
            var list = await _sqlService.GetCategoriesByIdsAsync(ids);
            return list.OrderBy(c => ids.IndexOf(c.ProductCategoryId)).ToList();
        }
    }
}
