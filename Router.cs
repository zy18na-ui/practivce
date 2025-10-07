// dataAccess/Planning/Router.cs
using System.Text.RegularExpressions;

namespace dataAccess.Planning;

public enum RouteKind { Chitchat, SqlReport, Vector, Hybrid }

public sealed record RouteDecision(RouteKind Kind, string? ReportType);

public static class Router
{
    // super-light heuristics you can tweak later
    private static readonly Regex Dates = new(@"(\b\d{4}-\d{2}-\d{2}\b|\bQ[1-4]\b|\bMTD|QTD|YTD\b|last\s+(week|month|quarter|year)|this\s+(week|month|quarter|year))",
                                              RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static RouteDecision Decide(string userText)
    {
        var t = userText.ToLowerInvariant().Trim();

        // 1) Chitchat-ish
        if (new[] { "hi", "hello", "thanks", "who are you", "what can you do" }.Any(x => t.Contains(x)))
            return new(RouteKind.Chitchat, null);

        // 2) Explicit report types
        if (t.Contains("expense")) return new(RouteKind.SqlReport, "expense");
        if (t.Contains("sales")) return new(RouteKind.SqlReport, "sales");
        if (t.Contains("inventory")) return new(RouteKind.SqlReport, "inventory");

        // 3) SQL-y phrasing
        var sqlWords = new[] { "report", "kpi", "summary", "top", "trend", "compare", "growth", "decline", "between", "from", "to" };
        var sqlish = sqlWords.Any(t.Contains) || Dates.IsMatch(t);

        // 4) Vector-ish phrasing
        var vectorWords = new[] { "policy", "explain", "definition", "guideline", "where is", "doc", "manual" };
        var vectorish = vectorWords.Any(t.Contains);

        if (sqlish && vectorish) return new(RouteKind.Hybrid, null);
        if (sqlish) return new(RouteKind.SqlReport, null);
        if (vectorish) return new(RouteKind.Vector, null);

        // fallback to chitchat
        return new(RouteKind.Chitchat, null);
    }
}
