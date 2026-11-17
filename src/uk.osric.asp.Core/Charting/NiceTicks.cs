// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Core.Charting;

using System.Globalization;

/// <summary>Picks readable ticks for a chart axis.</summary>
/// <remarks>
/// <para>
/// Both selectors use a fixed table of steps rather than arithmetic on the span, so a time label
/// reads <c>15:30</c> rather than <c>15:27</c> and a value label reads <c>0.5</c> rather than
/// <c>0.4737</c>.
/// </para>
/// <para>
/// Time positions are seconds after <c>from</c>, which is how the charts place their samples. The zone
/// only decides the label text: the ticks themselves are instants.
/// </para>
/// </remarks>
public static class NiceTicks {

    private const int TargetTicks = 6;
    private const double SecondsPerDay = 86400;

    // Room a label keeps from its neighbour, in drawing units.
    private const double LabelGap = 14;

    private static readonly double[] TimeSteps =
        [1, 5, 15, 30, 60, 300, 900, 1800, 3600, 7200, 10800, 21600, 43200, SecondsPerDay];

    /// <summary>Chooses time ticks between two instants, labelled in a zone.</summary>
    /// <param name="from">The left edge of the chart.</param>
    /// <param name="to">The right edge of the chart.</param>
    /// <param name="zone">The zone the labels are written in.</param>
    /// <param name="availableWidth">
    /// The room the labels have, in drawing units. A finite width coarsens the ticks until the labels
    /// fit; an infinite one, or the default, asks for at most <see cref="TargetTicks"/> labels.
    /// </param>
    /// <returns>
    /// The ticks, positioned in seconds after <paramref name="from"/>. A span with no width yields a
    /// single tick rather than none.
    /// </returns>
    public static IReadOnlyList<ChartTick> ForTime(
        DateTimeOffset from,
        DateTimeOffset to,
        TimeZoneInfo zone,
        double availableWidth = double.PositiveInfinity) {

        ArgumentNullException.ThrowIfNull(zone);

        TimeSpan span = to - from;
        if (span <= TimeSpan.Zero) {
            return [new ChartTick(0, LocalLabel(to, zone, "HH:mm"))];
        }

        bool carriesYear = TimeZoneInfo.ConvertTime(from, zone).Year != TimeZoneInfo.ConvertTime(to, zone).Year;
        bool overADay = span >= TimeSpan.FromDays(1);
        (double step, string format) = ScaleFor(span.TotalSeconds, overADay, carriesYear, availableWidth);

        List<ChartTick> ticks = [];
        if (step >= SecondsPerDay) {
            AddDayTicks(ticks, from, to, zone, days: (int)Math.Round(step / SecondsPerDay), format);
            return ticks;
        }

        long stepTicks = (long)(step * TimeSpan.TicksPerSecond);
        long first = (from.UtcTicks + stepTicks - 1) / stepTicks * stepTicks;

        for (long at = first; at <= to.UtcTicks; at += stepTicks) {
            DateTimeOffset instant = new(at, TimeSpan.Zero);
            ticks.Add(new ChartTick((instant - from).TotalSeconds, LocalLabel(instant, zone, format)));
        }

        return ticks;
    }

    /// <summary>Chooses value ticks for a range.</summary>
    /// <param name="min">The lowest value on the chart.</param>
    /// <param name="max">The highest value on the chart.</param>
    /// <returns>
    /// Ticks at 1/2/5 × 10ⁿ intervals. A flat range is widened so the line does not sit on the axis,
    /// and a range that is not a number yields <c>0..1</c>.
    /// </returns>
    public static IReadOnlyList<ChartTick> ForValue(double min, double max) {
        if (!double.IsFinite(min) || !double.IsFinite(max)) {
            (min, max) = (0, 1);
        }

        if (min > max) {
            (min, max) = (max, min);
        }

        if (min == max) {
            (min, max) = ChartSamples.Widen(min, max);
        }

        double step = NiceStep((max - min) / 5);
        double first = Math.Floor(min / step) * step;
        double last = Math.Ceiling(max / step) * step;
        // Every tick on one axis carries the same number of decimal places: trimming trailing zeros
        // puts "0.00025" beside "0.0003" and makes the reader work out whether that is precision or
        // a rounding artefact.
        int decimals = Math.Clamp(-(int)Math.Floor(Math.Log10(step) + 1e-12), 0, 9);
        string format = decimals == 0 ? "0" : "0." + new string('0', decimals);

        List<ChartTick> ticks = [];
        for (double value = first; value <= last + (step * 1e-9); value += step) {
            double position = Math.Abs(value) < step * 1e-9 ? 0 : value;
            ticks.Add(new ChartTick(position, position.ToString(format, CultureInfo.InvariantCulture)));
        }

        return ticks;
    }

    // The shortest step whose labels fit — by count when there is no width to measure, or by their
    // estimated width when there is. The format follows the step: ticks on whole days need no clock.
    private static (double Step, string Format) ScaleFor(
        double seconds,
        bool overADay,
        bool carriesYear,
        double availableWidth) {

        double last = TimeSteps[^1];

        foreach (double candidate in Candidates(seconds)) {
            last = candidate;
            string format = FormatFor(candidate, overADay, carriesYear);

            if (Fits(seconds, candidate, format, availableWidth)) {
                return (candidate, format);
            }
        }

        return (last, FormatFor(last, overADay, carriesYear));
    }

    private static IEnumerable<double> Candidates(double seconds) {
        foreach (double step in TimeSteps) {
            yield return step;
        }

        int days = Math.Max(2, (int)Math.Ceiling(seconds / SecondsPerDay));
        for (int multiple = 2; multiple <= days; multiple++) {
            yield return multiple * SecondsPerDay;
        }
    }

    private static bool Fits(double seconds, double step, string format, double availableWidth) {
        if (double.IsNaN(availableWidth) || availableWidth <= 0 || double.IsPositiveInfinity(availableWidth)) {
            return seconds / step <= TargetTicks;
        }

        double width = (format.Length * SvgText.CharacterWidth) + LabelGap;
        int labels = (int)Math.Ceiling(seconds / step) + 1;

        return labels * width <= availableWidth;
    }

    // A tick on a whole day lands at local midnight; only a span over a day that still ticks by the
    // hour needs both the date and the clock.
    private static string FormatFor(double step, bool overADay, bool carriesYear) {
        if (!overADay) {
            return "HH:mm";
        }

        if (step >= SecondsPerDay) {
            return carriesYear ? "d MMM yyyy" : "d MMM";
        }

        return carriesYear ? "d MMM yyyy HH:mm" : "d MMM HH:mm";
    }

    private static void AddDayTicks(
        List<ChartTick> ticks,
        DateTimeOffset from,
        DateTimeOffset to,
        TimeZoneInfo zone,
        int days,
        string format) {

        DateTime local = TimeZoneInfo.ConvertTime(from, zone).Date;

        for (int guard = 0; guard < 1000; guard++) {
            DateTimeOffset instant = FromLocal(local, zone);
            if (instant > to) {
                return;
            }

            if (instant >= from) {
                ticks.Add(new ChartTick((instant - from).TotalSeconds, LocalLabel(instant, zone, format)));
            }

            local = local.AddDays(days);
        }
    }

    // A local wall time can be invalid or ambiguous at a DST change; the offset still names an
    // instant, and midnight in London never falls in either gap.
    private static DateTimeOffset FromLocal(DateTime local, TimeZoneInfo zone) {
        DateTime unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified));
    }

    private static string LocalLabel(DateTimeOffset instant, TimeZoneInfo zone, string format) =>
        TimeZoneInfo.ConvertTime(instant, zone).ToString(format, CultureInfo.InvariantCulture);

    private static double NiceStep(double raw) {
        if (!(raw > 0) || !double.IsFinite(raw)) {
            return 1;
        }

        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double normalised = raw / magnitude;
        double step = normalised <= 1 ? 1 : normalised <= 2 ? 2 : normalised <= 5 ? 5 : 10;

        return step * magnitude;
    }
}
