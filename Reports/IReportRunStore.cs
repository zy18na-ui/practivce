// dataAccess/Reports/IReportRunStore.cs
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace dataAccess.Reports
{
    public interface IReportRunStore
    {
        // legacy (keep)
        Task SaveAsync(string domain, string periodStart, string periodEnd, string periodLabel, JsonDocument uiSpec, CancellationToken ct = default);

        // new rich overload
        Task<Guid> SaveAsync(ReportRecord record, CancellationToken ct = default);
    }
}
