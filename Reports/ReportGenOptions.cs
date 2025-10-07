// dataAccess/Reports/ReportGenOptions.cs
namespace dataAccess.Reports
{
    /// <summary>
    /// Model & generation knobs for YAML Phase-2 rendering.
    /// Kept as a simple public class to avoid cross-assembly accessibility quirks.
    /// </summary>
    public sealed class ReportGenOptions
    {
        public string Model { get; init; } = "gemma2-9b-it";
        public double Temperature { get; init; } = 0.2;
        public bool JsonMode { get; init; } = true;

        public ReportGenOptions() { }

        public ReportGenOptions(string model, double temperature = 0.2, bool jsonMode = true)
        {
            Model = model;
            Temperature = temperature;
            JsonMode = jsonMode;
        }
    }
}
