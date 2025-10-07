using System.Globalization;
using System.Text.RegularExpressions;

namespace dataAccess.Planning.Time;

public static class ReportTimeResolver
{
    public enum Grain { Day, Week, Month, Range }

    public sealed record Period(DateOnly Start, DateOnly End, Grain Grain, string Label);

    // Main entry for reports (full, deterministic; PH time; Mon–Sun weeks)
    public static Period Resolve(string userText, DateOnly today, DayOfWeek weekStart = DayOfWeek.Monday)
    {
        var text = (userText ?? "").Trim().ToLowerInvariant();

        // 1) quick presets
        if (ContainsAny(text, "this month", "ngayong buwan")) return FullMonth(today);
        if (ContainsAny(text, "last month", "nakaraang buwan")) return FullMonth(FirstOfMonth(today).AddMonths(-1));
        if (ContainsAny(text, "this week", "ngayong linggo")) return FullWeek(today, weekStart);
        if (ContainsAny(text, "last week", "nakaraang linggo")) return FullWeek(StartOfWeek(today, weekStart).AddDays(-7), weekStart);
        if (ContainsAny(text, "yesterday", "kahapon")) return SingleDay(today.AddDays(-1));
        if (ContainsAny(text, "today", "ngayon")) return SingleDay(today);

        // 2) last N months|weeks|days  (prefer full periods for reports)
        var m = Regex.Match(text, @"last\s+(\d+)\s+(months|month|buwan|weeks|week|linggo|days|day|araw)");
        if (m.Success)
        {
            var n = int.Parse(m.Groups[1].Value);
            var unit = m.Groups[2].Value;
            return unit.StartsWith("month") || unit.Contains("buwan")
                ? LastNFullMonths(today, n)
                : unit.StartsWith("week") || unit.Contains("linggo")
                    ? LastNFullWeeks(today, n, weekStart)
                    : LastNDays(today, n);
        }

        // 3) explicit ISO date range: YYYY-MM-DD to YYYY-MM-DD
        var r = Regex.Match(text, @"(\d{4}-\d{2}-\d{2})\s*(?:to|until|through|-|–|—)\s*(\d{4}-\d{2}-\d{2})");
        if (r.Success)
        {
            var s = DateOnly.ParseExact(r.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var e = DateOnly.ParseExact(r.Groups[2].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            return Range(s, e);
        }

        // 4) month → month (Aug to Sep) [optional year(s)]
        var mr = Regex.Match(text, MonthsRegex(@"from\s+({M})\s+(?:to|until|hanggang)\s+({M})(?:\s+(\d{4}))?"));
        if (mr.Success)
        {
            var m1 = mr.Groups[1].Value; var m2 = mr.Groups[2].Value;
            var yr = mr.Groups[3].Success ? int.Parse(mr.Groups[3].Value) : InferYearForMonthsSpan(today, m1, m2);
            var a = MonthIndex(m1); var b = MonthIndex(m2);
            var start = new DateOnly(yr, a, 1);
            var end = LastDayOfMonth(new DateOnly(yr, b, 1));
            // if span wraps across year (e.g., Nov to Feb), bump end year
            if (b < a) end = LastDayOfMonth(new DateOnly(yr + 1, b, 1));
            return MultiMonth(start, end);
        }

        // 5) single month with optional year: "august" | "aug 2025" | "setyembre 2024"
        var sm = Regex.Match(text, MonthsRegex(@"(^|\s)({M})(?:\s+(\d{4}))?($|\s)"));
        if (sm.Success)
        {
            var mon = sm.Groups[2].Value;
            var yr = sm.Groups[3].Success ? int.Parse(sm.Groups[3].Value) : InferYearForSingleMonth(today, mon);
            var start = new DateOnly(yr, MonthIndex(mon), 1);
            var end = LastDayOfMonth(start);
            return new Period(start, end, Grain.Month, start.ToString("MMMM yyyy", CultureInfo.InvariantCulture));
        }

        // 6) fallback for reports: full current month
        return FullMonth(today);
    }

    // ---------- helpers ----------

    private static bool ContainsAny(string s, params string[] keys) => keys.Any(k => s.Contains(k));

    private static Period SingleDay(DateOnly d)
        => new(d, d, Grain.Day, d.ToString("MMM d, yyyy", CultureInfo.InvariantCulture));

    private static Period FullWeek(DateOnly any, DayOfWeek weekStart)
    {
        var start = StartOfWeek(any, weekStart);
        var end = start.AddDays(6);
        var label = $"{start.ToString("MMM d", CultureInfo.InvariantCulture)}–{end.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}";
        return new(start, end, Grain.Week, label);
    }

    private static DateOnly StartOfWeek(DateOnly d, DayOfWeek weekStart)
    {
        int delta = ((int)d.DayOfWeek - (int)weekStart + 7) % 7;
        return d.AddDays(-delta);
    }

    private static Period FullMonth(DateOnly anyDayInMonth)
    {
        var first = FirstOfMonth(anyDayInMonth);
        var last = LastDayOfMonth(first);
        return new(first, last, Grain.Month, first.ToString("MMMM yyyy", CultureInfo.InvariantCulture));
    }

    private static DateOnly FirstOfMonth(DateOnly d) => new(d.Year, d.Month, 1);
    private static DateOnly LastDayOfMonth(DateOnly firstOfMonth)
        => firstOfMonth.AddMonths(1).AddDays(-1);

    private static Period LastNFullMonths(DateOnly today, int n)
    {
        // last n full months BEFORE the current month
        var endMonth = FirstOfMonth(today).AddDays(-1);             // last day of previous month
        var startMonth = new DateOnly(endMonth.Year, endMonth.Month, 1).AddMonths(-(n - 1));
        var end = LastDayOfMonth(new DateOnly(endMonth.Year, endMonth.Month, 1));
        var label = LabelMonths(startMonth, end);
        return new(startMonth, end, Grain.Month, label);
    }

    private static Period LastNFullWeeks(DateOnly today, int n, DayOfWeek weekStart)
    {
        var lastWeekStart = StartOfWeek(today, weekStart).AddDays(-7);
        var start = lastWeekStart.AddDays(-7 * (n - 1));
        var end = lastWeekStart.AddDays(6);
        var label = $"{start.ToString("MMM d", CultureInfo.InvariantCulture)}–{end.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}";
        return new(start, end, Grain.Week, label);
    }

    private static Period LastNDays(DateOnly today, int n)
    {
        var end = today.AddDays(-1);
        var start = end.AddDays(-(n - 1));
        var label = $"{start.ToString("MMM d", CultureInfo.InvariantCulture)}–{end.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}";
        return new(start, end, Grain.Range, label);
    }

    private static Period Range(DateOnly start, DateOnly end)
    {
        if (end < start) (start, end) = (end, start);
        var label = start.Year == end.Year
            ? $"{start.ToString("MMM d", CultureInfo.InvariantCulture)}–{end.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}"
            : $"{start.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}–{end.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}";
        return new(start, end, Grain.Range, label);
    }

    private static Period MultiMonth(DateOnly start, DateOnly end)
        => new(start, end, Grain.Month, LabelMonths(start, end));

    private static string LabelMonths(DateOnly start, DateOnly end)
    {
        if (start.Year == end.Year)
            return start.Month == end.Month
                ? start.ToString("MMMM yyyy", CultureInfo.InvariantCulture)
                : $"{start.ToString("MMM", CultureInfo.InvariantCulture)}–{end.ToString("MMM yyyy", CultureInfo.InvariantCulture)}";
        return $"{start.ToString("MMM yyyy", CultureInfo.InvariantCulture)}–{end.ToString("MMM yyyy", CultureInfo.InvariantCulture)}";
    }

    // Month lexicon (EN + Tagalog + common abbrev)
    private static readonly string[] Months = new[]
    {
        "january","jan","enero",
        "february","feb","pebrero","febrero",
        "march","mar","marso",
        "april","apr","abril",
        "may","mayo",
        "june","jun","hunyo",
        "july","jul","hulyo",
        "august","aug","agosto",
        "september","sep","sept","setyembre",
        "october","oct","oktubre",
        "november","nov","nobyembre",
        "december","dec","disyembre"
    };

    private static string MonthsRegex(string tmpl)
    {
        // {M} placeholder → alternation of month tokens with word boundaries
        var alt = string.Join("|", Months.Select(Regex.Escape));
        return Regex.Replace(tmpl, @"\{M\}", $"(?:{alt})", RegexOptions.IgnoreCase);
    }

    private static int MonthIndex(string token)
    {
        token = token.ToLowerInvariant();
        return token switch
        {
            "january" or "jan" or "enero" => 1,
            "february" or "feb" or "pebrero" or "febrero" => 2,
            "march" or "mar" or "marso" => 3,
            "april" or "apr" or "abril" => 4,
            "may" or "mayo" => 5,
            "june" or "jun" or "hunyo" => 6,
            "july" or "jul" or "hulyo" => 7,
            "august" or "aug" or "agosto" => 8,
            "september" or "sep" or "sept" or "setyembre" => 9,
            "october" or "oct" or "oktubre" => 10,
            "november" or "nov" or "nobyembre" => 11,
            "december" or "dec" or "disyembre" => 12,
            _ => throw new ArgumentOutOfRangeException(nameof(token), token, "Unknown month token")
        };
    }

    private static int InferYearForSingleMonth(DateOnly today, string monthToken)
    {
        // If month is already past in current year → use current year; if in the future → use last year.
        var m = MonthIndex(monthToken);
        if (m <= today.Month) return today.Year;
        return today.Year - 1;
    }

    private static int InferYearForMonthsSpan(DateOnly today, string m1, string m2)
    {
        var a = MonthIndex(m1); var b = MonthIndex(m2);
        // Choose latest past span that doesn't go into future relative to today.
        if (a <= today.Month && (b <= today.Month || b < a)) return today.Year;
        // If span likely refers to immediately previous cycle
        return today.Year - 1;
    }
}
