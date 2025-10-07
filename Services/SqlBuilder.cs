// dataAccess/Services/SqlBuilder.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Shared.Allowlists;
using Shared.DTOs;

namespace dataAccess.Services
{
    /// <summary>
    /// Builds safe, parameterized SQL from a structured query plan (V2).
    /// </summary>
    public class SqlBuilder
    {
        private readonly ISqlAllowlist _allowlist;

        public SqlBuilder(ISqlAllowlist allowlist)
        {
            _allowlist = allowlist ?? throw new ArgumentNullException(nameof(allowlist));
        }

        /// <summary>
        /// Build a SQL SELECT and its parameter map from a QueryPlanDtoV2.
        /// </summary>
        public (string Sql, Dictionary<string, object?> Parameters) BuildSql(QueryPlanDtoV2 plan)
        {
            if (plan is null) throw new ArgumentNullException(nameof(plan));
            if (string.IsNullOrWhiteSpace(plan.Table))
                throw new InvalidOperationException("Table is required.");

            // ✅ Allowlist: table
            if (!_allowlist.IsTableAllowed(plan.Table))
                throw new InvalidOperationException($"❌ Table '{plan.Table}' is not allowed.");

            // ---------- SELECT ----------
            // (keeps your original behavior: allow "*" if explicitly requested)
            var requestedCols = (plan.Columns?.Any() == true) ? plan.Columns! : new List<string> { "*" };

            var selectCols = string.Join(", ",
                requestedCols.Where(c => c == "*" || _allowlist.IsColumnAllowed(plan.Table, c))
            );

            if (string.IsNullOrWhiteSpace(selectCols))
                throw new InvalidOperationException("❌ No allowed columns were requested.");

            var sql = new StringBuilder($"SELECT {selectCols} FROM {plan.Table}");

            // ---------- WHERE ----------
            var parameters = new Dictionary<string, object?>();
            if (plan.Filters?.Any() == true)
            {
                var whereClauses = new List<string>();
                int p = 0;

                foreach (var f in plan.Filters!)
                {
                    if (string.IsNullOrWhiteSpace(f.Column))
                        throw new InvalidOperationException("❌ Filter column is required.");

                    if (!_allowlist.IsColumnAllowed(plan.Table, f.Column))
                        throw new InvalidOperationException($"❌ Column '{f.Column}' is not allowed in {plan.Table}.");

                    // ⬇⬇⬇ DROP-IN #1: Normalize operator + default LIKE→ILIKE (Postgres case-insensitive)
                    var op = (f.Operator ?? "").Trim().ToUpperInvariant();
                    if (string.IsNullOrWhiteSpace(op) || !_allowlist.IsOperatorAllowed(op))
                        throw new InvalidOperationException($"❌ Operator '{f.Operator}' is not allowed.");

                    if (op == "LIKE") op = "ILIKE"; // default to case-insensitive on Postgres
                    // ⬆⬆⬆ END DROP-IN #1

                    // ⬇⬇⬇ DROP-IN #2: Safe support for IN (...)
                    if (op == "IN")
                    {
                        // Expect an IEnumerable (but not string)
                        if (f.Value is System.Collections.IEnumerable seq && f.Value is not string)
                        {
                            var elems = new List<string>();
                            foreach (var item in seq)
                            {
                                var pn = $"@p{p++}";
                                elems.Add(pn);
                                parameters[pn] = item ?? DBNull.Value;
                            }
                            if (elems.Count == 0)
                                throw new InvalidOperationException("❌ IN requires at least one value.");

                            whereClauses.Add($"{f.Column} IN ({string.Join(", ", elems)})");
                        }
                        else
                        {
                            throw new InvalidOperationException("❌ IN operator requires an array/list value.");
                        }
                    }
                    else
                    {
                        var paramName = $"@p{p++}";
                        whereClauses.Add($"{f.Column} {op} {paramName}");
                        parameters[paramName] = f.Value ?? DBNull.Value;
                    }
                    // ⬆⬆⬆ END DROP-IN #2
                }

                if (whereClauses.Count > 0)
                {
                    // ⬇⬇⬇ DROP-IN #3: Always AND (removed GroupOp to fix your compile errors)
                    sql.Append(" WHERE " + string.Join(" AND ", whereClauses));
                    // ⬆⬆⬆ END DROP-IN #3
                }
            }

            // ---------- ORDER BY ----------
            if (plan.Sort != null)
            {
                if (string.IsNullOrWhiteSpace(plan.Sort.Column))
                    throw new InvalidOperationException("❌ Sort column is required.");

                if (!_allowlist.IsColumnAllowed(plan.Table, plan.Sort.Column))
                    throw new InvalidOperationException($"❌ Sort column '{plan.Sort.Column}' is not allowed in {plan.Table}.");

                var dir = (plan.Sort.Direction?.Trim().ToUpperInvariant() == "DESC") ? "DESC" : "ASC";
                sql.Append($" ORDER BY {plan.Sort.Column} {dir}");
            }

            // ---------- LIMIT ----------
            var limit = plan.Limit ?? _allowlist.DefaultLimit;
            if (limit <= 0 || limit > _allowlist.MaxLimit)
                limit = _allowlist.DefaultLimit;

            sql.Append($" LIMIT {limit}");

            return (sql.ToString(), parameters);
        }
    }
}
