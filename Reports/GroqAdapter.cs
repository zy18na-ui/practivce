// dataAccess/Reports/GroqAdapter.cs
using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace dataAccess.Reports
{
    // Common interface used by YamlReportRunner
    public interface IGroqJsonClient
    {
        Task<JsonDocument> CompleteJsonAsync(string systemPrompt, object input, CancellationToken ct);
    }

    public sealed class ModelSelectingGroqAdapter : IGroqJsonClient
    {
        private readonly object _inner;                  // your GroqJsonClient concrete
        private readonly ReportGenOptions _opt;

        // Cached members we might call
        private readonly MethodInfo? _mi4;   // CompleteJsonAsync(system, user, data, ct)
        private readonly MethodInfo? _mi3;   // CompleteJsonAsync(system, data, ct)

        // Optional model setters / overloads (if present)
        private readonly PropertyInfo? _pModel;       // inner.Model
        private readonly PropertyInfo? _pTemp;        // inner.Temperature
        private readonly PropertyInfo? _pJsonMode;    // inner.JsonMode
        private readonly MethodInfo? _setModel;       // inner.SetModel(string)

        public ModelSelectingGroqAdapter(object inner, ReportGenOptions opt)
        {
            _inner = inner;
            _opt = opt;

            var t = _inner.GetType();
            var methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Public);

            _mi4 = methods.FirstOrDefault(m =>
            {
                if (m.Name != "CompleteJsonAsync") return false;
                var p = m.GetParameters();
                return p.Length == 4
                    && p[0].ParameterType == typeof(string)
                    && p[1].ParameterType == typeof(string)
                    && typeof(object).IsAssignableFrom(p[2].ParameterType)
                    && p[3].ParameterType == typeof(CancellationToken);
            });

            _mi3 = methods.FirstOrDefault(m =>
            {
                if (m.Name != "CompleteJsonAsync") return false;
                var p = m.GetParameters();
                return p.Length == 3
                    && p[0].ParameterType == typeof(string)
                    && typeof(object).IsAssignableFrom(p[1].ParameterType)
                    && p[2].ParameterType == typeof(CancellationToken);
            });

            // Try to find obvious knobs to set model/temp/json
            _pModel = t.GetProperty("Model") ?? t.GetProperty("DefaultModel");
            _pTemp = t.GetProperty("Temperature") ?? null;
            _pJsonMode = t.GetProperty("JsonMode") ?? null;
            _setModel = t.GetMethod("SetModel", new[] { typeof(string) });

            if (_mi4 is null && _mi3 is null)
                throw new MissingMethodException(t.FullName, "CompleteJsonAsync");
        }

        public async Task<JsonDocument> CompleteJsonAsync(string systemPrompt, object input, CancellationToken ct)
        {
            // 1) Try to set model/temperature/json flags on the inner client if available
            try
            {
                if (_setModel is not null) _setModel.Invoke(_inner, new object?[] { _opt.Model });
                else if (_pModel is not null && _pModel.CanWrite) _pModel.SetValue(_inner, _opt.Model);

                if (_pTemp is not null && _pTemp.CanWrite) _pTemp.SetValue(_inner, _opt.Temperature);
                if (_pJsonMode is not null && _pJsonMode.CanWrite) _pJsonMode.SetValue(_inner, _opt.JsonMode);
            }
            catch
            {
                // swallow — not all clients expose setters; we'll still call the method
            }
            // optional: inject ILogger<ModelSelectingGroqAdapter> if you want nicer logs
            Console.WriteLine($"[report-llm] model={_opt.Model} temp={_opt.Temperature} json={_opt.JsonMode}");

            // 2) Invoke the best-matching CompleteJsonAsync
            object? taskObj;
            if (_mi4 is not null)
            {
                // (system, user, data, ct) — user tag is arbitrary
                taskObj = _mi4.Invoke(_inner, new object?[] { systemPrompt, "render", input, ct });
            }
            else
            {
                // (system, data, ct)
                taskObj = _mi3!.Invoke(_inner, new object?[] { systemPrompt, input, ct });
            }

            // 3) Unwrap Task result
            if (taskObj is Task<JsonDocument> tJD)
                return await tJD.ConfigureAwait(false);

            if (taskObj is Task t)
            {
                await t.ConfigureAwait(false);
                var resultProp = t.GetType().GetProperty("Result");
                if (resultProp?.GetValue(t) is JsonDocument jd) return jd;
            }

            throw new InvalidOperationException("Unexpected return type from GroqJsonClient.CompleteJsonAsync.");
        }
    }
}
