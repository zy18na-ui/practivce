using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;
using System;
using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace dataAccess.Reports
{
    // Rich record for unified "reports" table
    public sealed record ReportRecord(
        string Domain,
        string? Scope,
        string? ReportType,
        string? ProductId,
        string PeriodStart,
        string PeriodEnd,
        string PeriodLabel,
        bool CompareToPrior,
        short? TopK,
        string? YamlName,
        string? YamlVersion,
        string? ModelName,
        JsonDocument UiSpec,
        JsonDocument? Meta
    );

    public sealed class ReportRunStore : IReportRunStore
    {
        private readonly string _vecConn;

        // You chose VEC for reports/forecasts storage (good).
        public ReportRunStore(IConfiguration config)
        {
            _vecConn = new VecConnResolver(config).Resolve();
            Console.WriteLine($"[ReportRunStore] Using VEC connection: {VecConnResolver.Mask(_vecConn)}");
        }

        // Legacy method -> forward to rich overload with minimal fields
        public async Task SaveAsync(
        string domain,
        string periodStart,
        string periodEnd,
        string periodLabel,
        JsonDocument uiSpec,
        CancellationToken ct = default)
            {
                var record = new ReportRecord(
                    Domain: domain,
                    Scope: null,
                    ReportType: "standard",
                    ProductId: null,
                    PeriodStart: periodStart,
                    PeriodEnd: periodEnd,
                    PeriodLabel: periodLabel,
                    CompareToPrior: false,
                    TopK: null,
                    YamlName: null,
                    YamlVersion: null,
                    ModelName: null,
                    UiSpec: uiSpec,
                    Meta: null
                );

                _ = await SaveAsync(record, ct); // discard Guid (back-compat)
            }

        // New rich overload -> unified table "public.reports"
        public async Task<Guid> SaveAsync(ReportRecord r, CancellationToken ct = default)
        {
            await using var conn = new NpgsqlConnection(_vecConn);
            await conn.OpenAsync(ct);

            const string sql = @"
                insert into public.reports
                (domain, scope, report_type, product_id, period_start, period_end, period_label,
                 compare_to_prior, top_k, yaml_name, yaml_version, model_name, ui_spec, meta)
                values
                (@domain, @scope, @report_type, @product_id, @period_start::date, @period_end::date, @period_label,
                 @compare_to_prior, @top_k, @yaml_name, @yaml_version, @model_name, @ui_spec::jsonb, @meta::jsonb)
                returning id;";

            await using var cmd = new NpgsqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("domain", r.Domain);
            cmd.Parameters.AddWithValue("scope", (object?)r.Scope ?? DBNull.Value);
            cmd.Parameters.AddWithValue("report_type", (object?)r.ReportType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("product_id", (object?)r.ProductId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("period_start", r.PeriodStart);
            cmd.Parameters.AddWithValue("period_end", r.PeriodEnd);
            cmd.Parameters.AddWithValue("period_label", r.PeriodLabel);
            cmd.Parameters.AddWithValue("compare_to_prior", r.CompareToPrior);
            cmd.Parameters.AddWithValue("top_k", (object?)r.TopK ?? DBNull.Value);
            cmd.Parameters.AddWithValue("yaml_name", (object?)r.YamlName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("yaml_version", (object?)r.YamlVersion ?? DBNull.Value);
            cmd.Parameters.AddWithValue("model_name", (object?)r.ModelName ?? DBNull.Value);

            // JSONB params – set type explicitly
            var uiParam = cmd.Parameters.Add("ui_spec", NpgsqlDbType.Jsonb);
            uiParam.Value = r.UiSpec.RootElement.GetRawText();

            var metaParam = cmd.Parameters.Add("meta", NpgsqlDbType.Jsonb);
            metaParam.Value = (object?)(r.Meta?.RootElement.GetRawText()) ?? DBNull.Value;

            // Only ExecuteScalarAsync (because of RETURNING id)
            var idObj = await cmd.ExecuteScalarAsync(ct);
            return (Guid)idObj!;
        }
    }
}
