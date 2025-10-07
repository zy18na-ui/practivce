using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace dataAccess.Reports;

public sealed class DateRangeResolver
{
    private readonly TimeZoneInfo _phTz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");

    // Returns: (start, end, label, prevStart, prevEnd, preset)
    public (string start, string end, string label, string? prevStart, string? prevEnd, string? preset)
        Resolve(string userText)
    {
        var nowPH = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _phTz).Date;

        // defaults: this_month
        DateTime start, end; string preset = "this_month";

        var t = (userText ?? string.Empty).ToLowerInvariant().Trim();

        // 0) Honor explicit tags from /api/assistant (Option B)
        //    e.g., "[PERIOD_START=2025-08-01][PERIOD_END=2025-08-31]"
        var tag = Regex.Match(t, @"\[period_start=(\d{4}-\d{2}-\d{2})\]\[period_end=(\d{4}-\d{2}-\d{2})\]");
        if (tag.Success)
        {
            start = DateTime.Parse(tag.Groups[1].Value, CultureInfo.InvariantCulture);
            end = DateTime.Parse(tag.Groups[2].Value, CultureInfo.InvariantCulture);
            preset = "range";
            goto BUILD;
        }

        // 1) Simple presets
        if (Regex.IsMatch(t, @"\byesterday\b|kahapon"))
        {
            start = end = nowPH.AddDays(-1);
            preset = "yesterday";
            goto BUILD;
        }
        if (Regex.IsMatch(t, @"\btoday\b|ngayong\s*araw|ngayon"))
        {
            start = end = nowPH;
            preset = "today";
            goto BUILD;
        }
        if (Regex.IsMatch(t, @"\bthis\s+week\b|ngayong\s+linggo"))
        {
            int diff = ((int)nowPH.DayOfWeek + 6) % 7; // Mon–Sun
            start = nowPH.AddDays(-diff);
            end = start.AddDays(6);
            preset = "this_week";
            goto BUILD;
        }
        if (Regex.IsMatch(t, @"\blast\s+week\b|nakaraang\s+linggo"))
        {
            int diff = ((int)nowPH.DayOfWeek + 6) % 7;
            var thisWeekStart = nowPH.AddDays(-diff);
            var lastWeekStart = thisWeekStart.AddDays(-7);
            start = lastWeekStart;
            end = lastWeekStart.AddDays(6);
            preset = "last_week";
            goto BUILD;
        }
        if (Regex.IsMatch(t, @"\bthis\s+month\b|ngayong\s+buwan"))
        {
            start = new DateTime(nowPH.Year, nowPH.Month, 1);
            end = start.AddMonths(1).AddDays(-1);
            preset = "this_month";
            goto BUILD;
        }
        if (Regex.IsMatch(t, @"\blast\s+month\b|nakaraang\s+buwan"))
        {
            var lm = nowPH.AddMonths(-1);
            start = new DateTime(lm.Year, lm.Month, 1);
            end = start.AddMonths(1).AddDays(-1);
            preset = "last_month";
            goto BUILD;
        }

        // 2) last N months|weeks|days  (full periods for months/weeks)
        var lastN = Regex.Match(t, @"\blast\s+(\d+)\s+(months?|buwan|weeks?|linggo|days?|araw)\b");
        if (lastN.Success)
        {
            var n = int.Parse(lastN.Groups[1].Value, CultureInfo.InvariantCulture);
            var unit = lastN.Groups[2].Value;

            if (unit.StartsWith("month") || unit.Contains("buwan"))
            {
                // N FULL months immediately before the current month
                var endMonthLastDay = new DateTime(nowPH.Year, nowPH.Month, 1).AddDays(-1);
                start = new DateTime(endMonthLastDay.Year, endMonthLastDay.Month, 1).AddMonths(-(n - 1));
                end = new DateTime(endMonthLastDay.Year, endMonthLastDay.Month, 1).AddMonths(1).AddDays(-1);
                preset = "last_n_months";
                goto BUILD;
            }
            if (unit.StartsWith("week") || unit.Contains("linggo"))
            {
                int diff = ((int)nowPH.DayOfWeek + 6) % 7; // Mon–Sun
                var thisWeekStart = nowPH.AddDays(-diff);
                var lastWeekStart = thisWeekStart.AddDays(-7);
                start = lastWeekStart.AddDays(-7 * (n - 1));
                end = lastWeekStart.AddDays(6);
                preset = "last_n_weeks";
                goto BUILD;
            }

            // days → rolling window ending yesterday
            end = nowPH.AddDays(-1);
            start = end.AddDays(-(n - 1));
            preset = "last_n_days";
            goto BUILD;
        }

        // 3) explicit ISO range: YYYY-MM-DD to YYYY-MM-DD
        var r = Regex.Match(t, @"(\d{4}-\d{2}-\d{2})\s*(?:to|until|through|-|–|—)\s*(\d{4}-\d{2}-\d{2})");
        if (r.Success)
        {
            start = DateTime.ParseExact(r.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            end = DateTime.ParseExact(r.Groups[2].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            preset = "range";
            goto BUILD;
        }

        // 4) "from <month> to <month> [YYYY?]" (Taglish months supported)
        var mmSpan = Regex.Match(t, MonthsRegex(@"(?:from|mula)\s+({M})\s+(?:to|until|hanggang)\s+({M})(?:\s+(\d{4}))?"));
        if (mmSpan.Success)
        {
            var m1 = mmSpan.Groups[1].Value;
            var m2 = mmSpan.Groups[2].Value;
            var yr = mmSpan.Groups[3].Success ? int.Parse(mmSpan.Groups[3].Value, CultureInfo.InvariantCulture) : InferYearForMonthsSpan(nowPH, m1, m2);
            var a = MonthIndex(m1); var b = MonthIndex(m2);
            start = new DateTime(yr, a, 1);
            end = new DateTime(yr, b, 1).AddMonths(1).AddDays(-1);
            if (b < a) end = new DateTime(yr + 1, b, 1).AddMonths(1).AddDays(-1); // cross-year
            preset = "month_span";
            goto BUILD;
        }

        // 5) single month (EN/Tagalog/abbr) with optional year: "august" | "aug 2025" | "agosto 2025"
        var singleM = Regex.Match(t, MonthsRegex(@"(^|\s)({M})(?:\s+(\d{4}))?($|\s)"));
        if (singleM.Success)
        {
            var mon = singleM.Groups[2].Value;
            var yr = singleM.Groups[3].Success ? int.Parse(singleM.Groups[3].Value, CultureInfo.InvariantCulture) : InferYearForSingleMonth(nowPH, mon);
            start = new DateTime(yr, MonthIndex(mon), 1);
            end = start.AddMonths(1).AddDays(-1);
            preset = "single_month";
            goto BUILD;
        }

        // 6) default (unchanged)
        start = new DateTime(nowPH.Year, nowPH.Month, 1);
        end = start.AddMonths(1).AddDays(-1);
        preset = "this_month";

    BUILD:
        var label = (start.Year == end.Year)
            ? $"{start:MMM d}–{end:MMM d, yyyy}"
            : $"{start:MMM d, yyyy}–{end:MMM d, yyyy}";

        // prior window of equal length
        var days = (end - start).Days + 1;
        var prevEnd = start.AddDays(-1);
        var prevStart = prevEnd.AddDays(-(days - 1));

        return (start.ToString("yyyy-MM-dd"), end.ToString("yyyy-MM-dd"), label,
                prevStart.ToString("yyyy-MM-dd"), prevEnd.ToString("yyyy-MM-dd"), preset);
    }

    public bool WantsCompare(string userText)
        => Regex.IsMatch((userText ?? string.Empty).ToLowerInvariant(),
           @"\b(compare|vs|versus|kumpara|ihambing|wow|mom|yoy|year over year)\b");

    // ---- helpers ----

    private static readonly string[] MonthTokens = new[]
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
        var alt = string.Join("|", MonthTokens.Select(Regex.Escape));
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

    private static int InferYearForSingleMonth(DateTime today, string monthToken)
    {
        var m = MonthIndex(monthToken);
        return (m <= today.Month) ? today.Year : today.Year - 1;
    }

    private static int InferYearForMonthsSpan(DateTime today, string m1, string m2)
    {
        var a = MonthIndex(m1); var b = MonthIndex(m2);
        // choose latest past span that doesn't go into the future
        if (a <= today.Month && (b <= today.Month || b < a)) return today.Year;
        return today.Year - 1;
    }
}
