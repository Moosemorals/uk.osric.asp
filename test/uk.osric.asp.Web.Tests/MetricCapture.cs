// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Web.Tests;

using System.Diagnostics.Metrics;
using uk.osric.asp.Web.Pages;

/// <summary>Collects what one meter records while a test runs.</summary>
internal sealed class MetricCapture : IDisposable {

    private readonly List<Measurement> _measurements = [];
    private readonly Lock _gate = new();
    private readonly MeterListener _listener = new();

    private MetricCapture(string meterName) {
        _listener.InstrumentPublished = (instrument, listener) => {
            if (string.Equals(instrument.Meter.Name, meterName, StringComparison.Ordinal)) {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
            Add(instrument, value, tags.ToArray()));
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
            Add(instrument, value, tags.ToArray()));
        _listener.Start();
    }

    /// <summary>Counts the measurements of one instrument carrying a tag value.</summary>
    public int Count(string instrument, string tagName, string tagValue) {
        lock (_gate) {
            return _measurements.Count(measurement => measurement.Instrument == instrument
                && measurement.HasTag(tagName, tagValue));
        }
    }

    /// <summary>Counts the measurements of one instrument, whatever its tags.</summary>
    public int Count(string instrument) {
        lock (_gate) {
            return _measurements.Count(measurement => measurement.Instrument == instrument);
        }
    }

    /// <summary>Returns the measurements of one instrument for one page.</summary>
    public IReadOnlyList<Measurement> For(string instrument, string path) {
        lock (_gate) {
            return [.. _measurements.Where(measurement => measurement.Instrument == instrument
                && measurement.HasTag("page.path", path))];
        }
    }

    public void Dispose() {
        _listener.Dispose();
    }

    /// <summary>Starts capturing. Create this before the meter so no instrument is missed.</summary>
    public static MetricCapture Start() {
        return Start(PageMetrics.MeterName);
    }

    /// <summary>Starts capturing one named meter. Create this before the meter so no instrument is
    /// missed.</summary>
    /// <param name="meterName">The meter to listen to.</param>
    public static MetricCapture Start(string meterName) {
        return new MetricCapture(meterName);
    }

    private void Add(Instrument instrument, double value, KeyValuePair<string, object?>[] tags) {
        // Another fixture's page meter can record here too, on another thread.
        lock (_gate) {
            _measurements.Add(new Measurement(instrument.Name, value, tags));
        }
    }

    internal sealed record Measurement(string Instrument, double Value, IReadOnlyList<KeyValuePair<string, object?>> Tags) {

        public bool HasTag(string name, string value) {
            return Tags.Any(tag => tag.Key == name
                && string.Equals(tag.Value as string, value, StringComparison.Ordinal));
        }
    }
}
