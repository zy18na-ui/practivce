using Microsoft.EntityFrameworkCore;
using Shared.DTOs.Catalog;

namespace dataAccess.Services
{
    public partial class SqlQueryService
    {
        private readonly AppDbContext _db;
        public SqlQueryService(AppDbContext db) => _db = db;

        // PRODUCTS
        public async Task<List<Entities.Product>> GetProductsAsync(int limit = 50)
        {
            return await _db.Products
                .AsNoTracking()
                .OrderByDescending(p => p.UpdatedAt)
                .Take(limit)
                .ToListAsync();
        }

        // SUPPLIERS
        public async Task<List<Entities.Supplier>> GetSuppliersAsync(int limit = 50)
        {
            return await _db.Suppliers
                .AsNoTracking()
                .OrderBy(s => s.SupplierName)
                .Take(limit)
                .ToListAsync();
        }

        // CATEGORIES
        public async Task<List<Entities.ProductCategory>> GetCategoriesAsync(int limit = 50)
        {
            return await _db.ProductCategories
                .AsNoTracking()
                .OrderBy(c => c.ProductCategoryId)
                .Take(limit)
                .ToListAsync();
        }

        // (Optional) simple “q” search versions:

        public async Task<List<Entities.Supplier>> SearchSuppliersAsync(string q, int limit = 50)
        {
            q = q?.Trim() ?? "";
            return await _db.Suppliers
                .AsNoTracking()
                .Where(s =>
                    (s.SupplierName ?? "").Contains(q) ||
                    (s.Address ?? "").Contains(q))
                .OrderBy(s => s.SupplierName)
                .Take(limit)
                .ToListAsync();
        }

        public async Task<List<Entities.ProductCategory>> SearchCategoriesAsync(string q, int limit = 50)
        {
            q = q?.Trim() ?? "";
            return await _db.ProductCategories
                .AsNoTracking()
                .Where(c =>
                    (c.Color ?? "").Contains(q) ||
                    (c.AgeSize ?? "").Contains(q))
                .OrderBy(c => c.ProductCategoryId)
                .Take(limit)
                .ToListAsync();
        }

        // --- ADD: convenience alias that Dispatch expects ---
        public Task<List<Entities.Product>> GetAllProductsAsync(int limit = 100)
            => GetProductsAsync(limit);

        // --- ADD: used by HybridQueryService to materialize entities after ANN ---
        public async Task<List<Entities.Product>> GetProductsByIdsAsync(IReadOnlyList<int> ids)
        {
            if (ids is null || ids.Count == 0) return new();
            return await _db.Products
                .AsNoTracking()
                .Where(p => ids.Contains(p.ProductId))
                .ToListAsync();
        }

        public async Task<List<Entities.Supplier>> GetSuppliersByIdsAsync(IReadOnlyList<int> ids)
        {
            if (ids is null || ids.Count == 0) return new();
            return await _db.Suppliers
                .AsNoTracking()
                .Where(s => ids.Contains(s.SupplierId))
                .ToListAsync();
        }

        public async Task<List<Entities.ProductCategory>> GetCategoriesByIdsAsync(IReadOnlyList<int> ids)
        {
            if (ids is null || ids.Count == 0) return new();
            return await _db.ProductCategories
                .AsNoTracking()
                .Where(c => ids.Contains(c.ProductCategoryId))
                .ToListAsync();
        }

        // --- ADD: used by Dispatch for "order 123" ---
        public async Task<Entities.Order?> GetOrderWithItemsAsync(int orderId)
        {
            return await _db.Orders
                .AsNoTracking()
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.Product)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);
        }

        public async Task<List<ProductCategoryDto>> GetProductCategoriesByProductIdsAsync(
        IEnumerable<int> productIds,
        CancellationToken ct)
        {
            var ids = (productIds ?? Array.Empty<int>()).Distinct().ToList();
            var q = _db.ProductCategories.AsNoTracking().AsQueryable();
            if (ids.Count > 0) q = q.Where(pc => ids.Contains(pc.ProductId));

            // NOTE: ProductCategoryDto does NOT have CreatedAt; map only existing props.
            return await q.Select(pc => new ProductCategoryDto
            {
                ProductCategoryId = pc.ProductCategoryId,
                ProductId = pc.ProductId,
                Price = pc.Price,
                Cost = pc.Cost,
                Color = pc.Color,
                AgeSize = pc.AgeSize,
                CurrentStock = pc.CurrentStock,
                ReorderPoint = pc.ReorderPoint,
                UpdatedStock = pc.UpdatedStock
            }).ToListAsync(ct);
        }

        // DTO-returning overload (distinct from your existing Entities-returning method)
        public async Task<List<ProductDto>> GetProductsByIdsAsync(
            IEnumerable<int> productIds,
            CancellationToken ct)
        {
            var ids = (productIds ?? Array.Empty<int>()).Distinct().ToList();
            var q = _db.Products.AsNoTracking().AsQueryable();
            if (ids.Count > 0) q = q.Where(p => ids.Contains(p.ProductId));

            return await q.Select(p => new ProductDto
            {
                ProductId = p.ProductId,
                ProductName = p.ProductName,
                // IMPORTANT: map to ProductDescription (not Description)
                ProductDescription = p.Description,
                SupplierId = p.SupplierId,
                ImageUrl = p.ImageUrl,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
                // UpdatedByUserId — include if you store it in the entity
            }).ToListAsync(ct);
        }

        public async Task<ProductWithPriceDto?> GetNthCheapestProductDtoAsync(
    int ordinalZeroBased,
    CancellationToken ct = default)
        {
            if (ordinalZeroBased < 0) ordinalZeroBased = 0;

            // Join ProductCategory → Product → Supplier
            var q =
                from pc in _db.ProductCategories.AsNoTracking()
                join p in _db.Products.AsNoTracking() on pc.ProductId equals p.ProductId
                join s in _db.Suppliers.AsNoTracking() on p.SupplierId equals s.SupplierId
                orderby pc.Price ascending,        // cheapest first
                        p.CreatedAt ascending,     // tie-break: oldest product
                        pc.ProductCategoryId ascending
                select new ProductWithPriceDto
                {
                    ProductId = p.ProductId,
                    Name = p.ProductName,
                    ImageUrl = p.ImageUrl,
                    Price = pc.Price,
                    Cost = pc.Cost,
                    SupplierId = s.SupplierId,
                    SupplierName = s.SupplierName
                };

            return await q.Skip(ordinalZeroBased).Take(1).FirstOrDefaultAsync(ct);
        }

        // DTO used above
        public sealed class ProductWithPriceDto
        {
            public int ProductId { get; set; }
            public string? Name { get; set; }
            public string? ImageUrl { get; set; }
            public decimal Price { get; set; }
            public decimal Cost { get; set; }
            public int SupplierId { get; set; }
            public string? SupplierName { get; set; }
        }
    }
}
