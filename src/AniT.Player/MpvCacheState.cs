using System.Diagnostics;
using System.Text.Json;

namespace AniT.Player;

internal sealed record MpvCacheState(double? Seconds, double? Bytes, double? InputRate, bool Idle, bool Eof,
    double? Reader, (double Start, double End)[] Ranges, bool HasRanges, long Timestamp)
{
    internal static MpvCacheState Empty => new(null, null, null, false, false, null, [], false, Stopwatch.GetTimestamp());
    static double? Number(JsonElement value, string key, bool nonnegative = true)
        => value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var number)
            && double.IsFinite(number) && (!nonnegative || number >= 0) ? number : null;
    static bool Flag(JsonElement value, string key) => value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.True;
    internal static MpvCacheState Parse(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return Empty;
        var ranges = new List<(double, double)>();
        var hasRanges = value.TryGetProperty("seekable-ranges", out var array) && array.ValueKind == JsonValueKind.Array;
        if (hasRanges) foreach (var range in array.EnumerateArray().Take(128)) {
            if (range.ValueKind != JsonValueKind.Object) continue;
            var start = Number(range, "start", false); var end = Number(range, "end", false);
            if (start is { } from && end is { } to && to >= from) ranges.Add((from, to));
        }
        return new(Number(value, "cache-duration"), Number(value, "fw-bytes"), Number(value, "raw-input-rate"),
            Flag(value, "idle"), Flag(value, "eof"), Number(value, "reader-pts", false), ranges.OrderBy(r => r.Item1).ToArray(), hasRanges, Stopwatch.GetTimestamp());
    }
    internal double? ContiguousSeconds(double? position, double? fallback)
    {
        var at = position ?? Reader;
        if (!HasRanges || at is null || !double.IsFinite(at.Value)) return Seconds ?? fallback;
        double? end = null;
        foreach (var range in Ranges) {
            if (end is null) { if (range.Start <= at + .05 && range.End >= at) end = range.End; }
            else if (range.Start <= end + .05) end = Math.Max(end.Value, range.End);
            else break;
        }
        var contiguous = end is { } until ? Math.Max(0, until - at.Value) : 0;
        var hint = Seconds ?? fallback;
        return hint is >= 0 ? Math.Min(contiguous, hint.Value) : contiguous;
    }
}
