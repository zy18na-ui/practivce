using Microsoft.Extensions.Configuration;

namespace dataAccess.Reports
{
    public sealed class VecConnResolver
    {
        private readonly string _vecConn;
        public VecConnResolver(IConfiguration cfg)
        {
            _vecConn =
                System.Environment.GetEnvironmentVariable("APP__VEC__CONNECTIONSTRING")
                ?? cfg["APP__VEC__CONNECTIONSTRING"]
                ?? cfg["APP:VEC:CONNECTIONSTRING"]
                ?? cfg.GetConnectionString("VEC")
                ?? cfg.GetConnectionString("Vector")
                ?? cfg.GetConnectionString("APP__VEC__CONNECTIONSTRING")
                ?? throw new System.InvalidOperationException(
                    "Vector connection string not found (APP__VEC__CONNECTIONSTRING / ConnectionStrings:VEC/Vector).");
        }

        public string Resolve() => _vecConn;
        public static string Mask(string s) =>
            System.Text.RegularExpressions.Regex.Replace(s ?? "", @"Password=[^;]*", "Password=***",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}