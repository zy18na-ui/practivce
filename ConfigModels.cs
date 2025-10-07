// dataAccess/Planning/ConfigModels.cs
namespace dataAccess.Planning
{
    // Root config object expected by ConfigLoader
    public sealed class BConfig
    {
        public Identity identity { get; set; } = new();
        public Defaults defaults { get; set; } = new();
        public Phases phases { get; set; } = new();
        public Paths paths { get; set; } = new();

        public sealed class Identity
        {
            public string name { get; set; } = "";
            public string role { get; set; } = "";
        }

        public sealed class Defaults
        {
            public Model model { get; set; } = new();
            public Flags flags { get; set; } = new();
            public Rounding rounding { get; set; } = new();
        }

        public sealed class Model
        {
            public string name { get; set; } = "";
            public double temperature { get; set; } = 0.0;
            public int max_output_tokens { get; set; } = 0;
        }

        public sealed class Flags
        {
            public bool enforce_json_mode { get; set; } = true;
            public bool ordinals_bypass_ann { get; set; } = true;
        }

        public sealed class Rounding
        {
            public int decimals { get; set; } = 2;
        }

        public sealed class Phases
        {
            public Phase phase1 { get; set; } = new();
            public Phase phase2 { get; set; } = new();
        }

        public sealed class Phase
        {
            public string label { get; set; } = "";
            public string description { get; set; } = "";
        }

        public sealed class Paths
        {
            public string prompts_dir { get; set; } = "";
            public string registry_file { get; set; } = "";
        }
    }
}
