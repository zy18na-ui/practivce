using System;
using System.Globalization;

namespace dataAccess.Planning.Nlq
{
    public sealed class TimeResolver
    {
        // PH time with Windows fallback; else UTC
        private readonly TimeZoneInfo _phTz =
            TryFindTz("Asia/Manila") ??
            TryFindTz("Singapore Standard Time") ??
            TimeZoneInfo.Utc;

        private static TimeZoneInfo? TryFindTz(string id)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch { return null; }
        }

        private DateOnly TodayPH()
        {
            var nowUtc = DateTime.UtcNow;
            var ph = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, _phTz);
            return DateOnly.FromDateTime(ph);
        }

        public (DateOnly start, DateOnly end) ResolveRange(TimeSpec t)
        {
            var today = TodayPH();
            switch ((t.Preset ?? "").ToLowerInvariant())
            {
                case "today":
                    {
                        return (today, today);
                    }
                case "yesterday":
                    {
                        var y = today.AddDays(-1);
                        return (y, y);
                    }
                case "this_week":
                    {
                        // Monday–Sunday, include today (PH)
                        var dow = (int)today.DayOfWeek; // Sunday=0
                        var monday = today.AddDays(dow == 0 ? -6 : (1 - dow));
                        var sunday = monday.AddDays(6);
                        return (monday, sunday);
                    }
                case "last_week":
                    {
                        var (s, e) = ResolveRange(new TimeSpec { Preset = "this_week" });
                        return (s.AddDays(-7), e.AddDays(-7));
                    }
                case "this_month":
                    {
                        var first = new DateOnly(today.Year, today.Month, 1);
                        var last = first.AddMonths(1).AddDays(-1);
                        return (first, last);
                    }
                case "last_month":
                    {
                        var first = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
                        var last = first.AddMonths(1).AddDays(-1);
                        return (first, last);
                    }
                case "this_quarter":
                    {
                        var (qs, qe) = QuarterBounds(today.Year, QuarterOf(today));
                        return (qs, qe);
                    }
                case "last_quarter":
                    {
                        var curQ = QuarterOf(today);
                        var year = today.Year;
                        var prevQ = curQ - 1;
                        if (prevQ < 1) { prevQ = 4; year -= 1; }
                        var (qs, qe) = QuarterBounds(year, prevQ);
                        return (qs, qe);
                    }
                case "this_year":
                    {
                        var first = new DateOnly(today.Year, 1, 1);
                        var last = new DateOnly(today.Year, 12, 31);
                        return (first, last);
                    }
                case "last_year":
                    {
                        var first = new DateOnly(today.Year - 1, 1, 1);
                        var last = new DateOnly(today.Year - 1, 12, 31);
                        return (first, last);
                    }
                case "ytd":
                    {
                        var first = new DateOnly(today.Year, 1, 1);
                        return (first, today);
                    }
                case "as_of":
                    {
                        var end = ParseOr(t.End, today);
                        return (end, end);
                    }
                case "range":
                    {
                        var s = ParseOr(t.Start, today);
                        var e = ParseOr(t.End, s);
                        if (e < s) (s, e) = (e, s);
                        return (s, e);
                    }
                default:
                    {
                        // Default policy: expense/sales → this_week; inventory handled by caller/mapper
                        var (ds, de) = ResolveRange(new TimeSpec { Preset = "this_week" });
                        return (ds, de);
                    }
            }
        }

        public (DateOnly ps, DateOnly pe) PriorWindow(DateOnly start, DateOnly end)
        {
            var len = end.DayNumber - start.DayNumber + 1;
            var pe = start.AddDays(-1);
            var ps = pe.AddDays(-(len - 1));
            return (ps, pe);
        }

        private static int QuarterOf(DateOnly d) => ((d.Month - 1) / 3) + 1;

        private static (DateOnly qs, DateOnly qe) QuarterBounds(int year, int q)
        {
            var startMonth = 1 + (q - 1) * 3;
            var qs = new DateOnly(year, startMonth, 1);
            var qe = qs.AddMonths(3).AddDays(-1);
            return (qs, qe);
        }

        private static DateOnly ParseOr(string? s, DateOnly fallback)
        {
            if (!string.IsNullOrWhiteSpace(s) &&
                DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return d;
            return fallback;
        }
    }
}
