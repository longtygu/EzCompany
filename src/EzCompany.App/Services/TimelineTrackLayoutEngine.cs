using EzCompany.Core.Data;
using EzCompany.Core.Data.Entities;

namespace EzCompany.App.Services;

public enum TimelineZoomMode
{
    Overview60Days,
    Normal30Days,
    Detailed14Days
}

public sealed record TimelineDateTick(
    DateOnly Date,
    string Label,
    double X,
    bool IsToday,
    bool IsWeekend);

public sealed record TimelineBarItem(
    TimelinePlanEntity Entity,
    int TrackIndex,
    double LeftX,
    double Width,
    string FillHex,
    string StatusBadge,
    string DateRangeLabel,
    string ToolTipText);

public sealed record TimelineTrackRow(
    int TrackIndex,
    string TrackName,
    double CenterY,
    IReadOnlyList<TimelineBarItem> Bars);

public sealed record TimelineLayoutResult(
    DateOnly RangeStart,
    DateOnly RangeEnd,
    int TotalDays,
    double CanvasWidth,
    double CanvasHeight,
    double? TodayX,
    IReadOnlyList<TimelineDateTick> Ticks,
    IReadOnlyList<TimelineTrackRow> Tracks,
    int OverflowPlanCount);

/// <summary>
/// Computes a multi-track horizontal parallel timeline layout with date markers,
/// zoom scaling (14 / 30 / 60 days), and collision-free track packing (3 or 5 tracks).
/// </summary>
public static class TimelineTrackLayoutEngine
{
    public const int DefaultTrackCount = 3;
    public const int ExpandedTrackCount = 5;

    public static TimelineLayoutResult BuildLayout(
        IReadOnlyList<TimelinePlanEntity> plans,
        DateOnly anchorDate,
        TimelineZoomMode zoomMode,
        int maxTracks = DefaultTrackCount,
        double viewportWidth = 820.0)
    {
        ArgumentNullException.ThrowIfNull(plans);

        int trackCount = Math.Clamp(maxTracks, 1, 8);
        int totalDays = zoomMode switch
        {
            TimelineZoomMode.Detailed14Days => 14,
            TimelineZoomMode.Overview60Days => 60,
            _ => 30
        };

        double minDayWidth = zoomMode switch
        {
            TimelineZoomMode.Detailed14Days => 68.0,
            TimelineZoomMode.Overview60Days => 24.0,
            _ => 42.0
        };

        const double leftMargin = 20.0;
        const double rightMargin = 20.0;
        const double headerHeight = 34.0;
        const double trackPitch = 46.0;
        const double bottomMargin = 14.0;

        double safeViewport = Math.Max(420.0, viewportWidth);
        double canvasWidth = Math.Max(safeViewport, totalDays * minDayWidth + leftMargin + rightMargin);
        double dayWidth = (canvasWidth - leftMargin - rightMargin) / totalDays;
        double canvasHeight = headerHeight + trackCount * trackPitch + bottomMargin;

        int leadDays = Math.Max(1, (int)Math.Round(totalDays * 0.20));
        DateOnly rangeStart = anchorDate.AddDays(-leadDays);
        DateOnly rangeEnd = rangeStart.AddDays(totalDays - 1);
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);

        double? todayX = today >= rangeStart && today <= rangeEnd
            ? Math.Round(leftMargin + (today.DayNumber - rangeStart.DayNumber + 0.5) * dayWidth, 1)
            : null;

        int tickStep = zoomMode switch
        {
            TimelineZoomMode.Detailed14Days => 1,
            TimelineZoomMode.Overview60Days => 5,
            _ => 2
        };

        var tickDays = new SortedSet<int>();
        for (int i = 0; i < totalDays; i++)
        {
            if (i % tickStep == 0 || i == totalDays - 1)
            {
                tickDays.Add(i);
            }
        }

        if (today >= rangeStart && today <= rangeEnd)
        {
            tickDays.Add(today.DayNumber - rangeStart.DayNumber);
        }

        var ticks = new List<TimelineDateTick>(tickDays.Count);
        foreach (int offset in tickDays)
        {
            DateOnly d = rangeStart.AddDays(offset);
            double x = Math.Round(leftMargin + (offset + 0.5) * dayWidth, 1);
            bool isWeekend = d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            ticks.Add(new TimelineDateTick(d, d.ToString("dd/MM"), x, d == today, isWeekend));
        }

        var trackIntervals = new List<(double Left, double Right)>[trackCount];
        var trackBars = new List<TimelineBarItem>[trackCount];
        for (int t = 0; t < trackCount; t++)
        {
            trackIntervals[t] = [];
            trackBars[t] = [];
        }

        var sortedPlans = plans
            .Where(p => !p.IsDeleted)
            .Select(p =>
            {
                DateOnly s = DateOnly.FromDateTime(p.StartDateUtc.ToLocalTime().Date);
                DateOnly e = DateOnly.FromDateTime(p.EndDateUtc.ToLocalTime().Date);
                if (e < s) e = s;
                return (Plan: p, Start: s, End: e);
            })
            .Where(x => x.End >= rangeStart && x.Start <= rangeEnd)
            .OrderBy(x => x.Start)
            .ThenByDescending(x => x.End.DayNumber - x.Start.DayNumber)
            .ToArray();

        int overflowCount = 0;
        foreach ((TimelinePlanEntity plan, DateOnly pStart, DateOnly pEnd) in sortedPlans)
        {
            DateOnly clippedStart = pStart < rangeStart ? rangeStart : pStart;
            DateOnly clippedEnd = pEnd > rangeEnd ? rangeEnd : pEnd;

            int startIdx = clippedStart.DayNumber - rangeStart.DayNumber;
            int spanDays = Math.Max(1, clippedEnd.DayNumber - clippedStart.DayNumber + 1);

            double rawLeft = leftMargin + startIdx * dayWidth + 3.0;
            double rawWidth = spanDays * dayWidth - 6.0;
            double width = Math.Max(46.0, rawWidth);
            double left = Math.Min(rawLeft, canvasWidth - rightMargin - width);
            double right = left + width;

            int assignedTrack = -1;
            for (int t = 0; t < trackCount; t++)
            {
                bool collides = trackIntervals[t].Any(iv => left < iv.Right + 4.0 && right + 4.0 > iv.Left);
                if (!collides)
                {
                    assignedTrack = t;
                    break;
                }
            }

            if (assignedTrack < 0)
            {
                overflowCount++;
                continue;
            }

            trackIntervals[assignedTrack].Add((left, right));
            string fillHex = ResolvePlanColorHex(plan.Category, plan.Status);
            string badge = plan.Status switch
            {
                "Done" => "✓",
                "InProgress" => "●",
                _ => "○"
            };
            string rangeText = pStart == pEnd
                ? $"{pStart:dd/MM}"
                : $"{pStart:dd/MM}→{pEnd:dd/MM}";
            string tooltip = $"{plan.Title}\nDanh mục: {plan.Category} • Trạng thái: {plan.Status}\nThời gian: {pStart:dd/MM/yyyy} → {pEnd:dd/MM/yyyy}\nNgười tạo: {plan.CreatedByFullName}{(string.IsNullOrWhiteSpace(plan.Description) ? string.Empty : $"\nMô tả: {plan.Description}")}";

            trackBars[assignedTrack].Add(new TimelineBarItem(
                plan,
                assignedTrack,
                Math.Round(left, 1),
                Math.Round(width, 1),
                fillHex,
                badge,
                rangeText,
                tooltip));
        }

        var trackRows = new List<TimelineTrackRow>(trackCount);
        for (int t = 0; t < trackCount; t++)
        {
            double centerY = Math.Round(headerHeight + t * trackPitch + (trackPitch / 2.0), 1);
            trackRows.Add(new TimelineTrackRow(t, $"Đường {t + 1}", centerY, trackBars[t].ToArray()));
        }

        return new TimelineLayoutResult(
            rangeStart,
            rangeEnd,
            totalDays,
            Math.Round(canvasWidth, 1),
            Math.Round(canvasHeight, 1),
            todayX,
            ticks,
            trackRows,
            overflowCount);
    }

    public static string ResolvePlanColorHex(string? category, string? status)
    {
        if (string.Equals(status, "Done", StringComparison.OrdinalIgnoreCase))
        {
            return "#107C10";
        }

        if (string.Equals(category, TimelineCategories.Milestone, StringComparison.OrdinalIgnoreCase))
        {
            return "#744DA9";
        }

        if (string.Equals(category, TimelineCategories.Meeting, StringComparison.OrdinalIgnoreCase))
        {
            return "#008272";
        }

        if (string.Equals(category, TimelineCategories.Deadline, StringComparison.OrdinalIgnoreCase))
        {
            return "#C50F1F";
        }

        return "#0078D4";
    }
}
