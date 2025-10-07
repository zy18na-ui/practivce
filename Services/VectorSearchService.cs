// dataAccess/Services/VectorSearchService.cs
using System.Globalization;
using Npgsql;
using Microsoft.Extensions.Configuration;

namespace dataAccess.Services
{
    public partial class VectorSearchService
    {
        private readonly string _vec;
        private readonly IEmbeddingProvider _embed;

        public VectorSearchService(IConfiguration cfg, IEmbeddingProvider embed)
        {
            _vec = cfg["APP__VEC__CONNECTIONSTRING"]
                ?? Environment.GetEnvironmentVariable("APP__VEC__CONNECTIONSTRING")
                ?? throw new Exception("APP__VEC__CONNECTIONSTRING missing");
            _embed = embed;
        }

        private static string ToVectorLiteral(float[] v) =>
            "[" + string.Join(",", v.Select(f => f.ToString(CultureInfo.InvariantCulture))) + "]";

        // 🧩 GUARD: throw if qvec is null/empty
        private static float[] EnsureVec(float[]? v)
        {
            if (v is null || v.Length == 0)
                throw new ArgumentException("qvec must be non-empty (embedding required).", nameof(v));
            return v;
        }

        public async Task<List<int>> SearchProductIdsAsync(string text, int topK = 10, float[]? qvec = null, CancellationToken ct = default)
        {
            qvec ??= await _embed.EmbedAsync(text, ct);
            qvec = EnsureVec(qvec);

            const string sql = @"select product_key
                                 from product_embeddings
                                 order by embedding <=> cast(@q as vector)
                                 limit @k;";
            await using var conn = new NpgsqlConnection(_vec);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("q", ToVectorLiteral(qvec));
            cmd.Parameters.AddWithValue("k", topK);

            var ids = new List<int>();
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct)) ids.Add(rd.GetInt32(0));
            return ids;
        }

        public async Task<List<int>> SearchSupplierIdsAsync(string text, int topK = 10, float[]? qvec = null, CancellationToken ct = default)
        {
            qvec ??= await _embed.EmbedAsync(text, ct);
            qvec = EnsureVec(qvec);

            const string sql = @"select supplier_key
                                 from supplier_embeddings
                                 order by embedding <=> cast(@q as vector)
                                 limit @k;";
            await using var conn = new NpgsqlConnection(_vec);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("q", ToVectorLiteral(qvec));
            cmd.Parameters.AddWithValue("k", topK);

            var ids = new List<int>();
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct)) ids.Add(rd.GetInt32(0));
            return ids;
        }

        public async Task<List<int>> SearchCategoryIdsAsync(string text, int topK = 10, float[]? qvec = null, CancellationToken ct = default)
        {
            qvec ??= await _embed.EmbedAsync(text, ct);
            qvec = EnsureVec(qvec);

            const string sql = @"select category_key
                                 from category_embeddings
                                 order by embedding <=> cast(@q as vector)
                                 limit @k;";
            await using var conn = new NpgsqlConnection(_vec);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("q", ToVectorLiteral(qvec));
            cmd.Parameters.AddWithValue("k", topK);

            var ids = new List<int>();
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct)) ids.Add(rd.GetInt32(0));
            return ids;
        }
    }
}
