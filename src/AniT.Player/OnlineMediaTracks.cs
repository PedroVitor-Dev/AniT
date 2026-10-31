using System.Text.Json;

namespace AniT.Player;

public sealed record OnlineMediaTrack(int Id, string Kind, string? Language, string? Title, string? Codec,
    int? Channels, bool Forced, bool External, bool Default, bool Selected)
{
    public string Label {
        get {
            var parts = new List<string> { LanguageName(Language) };
            if (!string.IsNullOrWhiteSpace(Title)) parts.Add(Title);
            if (Codec is not null) parts.Add(Codec.ToUpperInvariant());
            if (Channels is > 0) parts.Add(Channels==1?"1 canal":$"{Channels} canais");
            if (Forced) parts.Add("Forçada");
            if (External) parts.Add("Externa");
            if (Default) parts.Add("Padrão");
            return string.Join(" · ", parts) + $" · #{Id}";
        }
    }
    public static string LanguageName(string? language) => language?.ToLowerInvariant() switch {
        "ja" or "jpn" => "Japonês", "pt-br" or "pt-brasil" => "Português (Brasil)", "pt" or "por" => "Português",
        "en" or "eng" => "Inglês", "es" or "spa" => "Espanhol", "fr" or "fra" or "fre" => "Francês",
        "de" or "deu" or "ger" => "Alemão", "it" or "ita" => "Italiano", "ko" or "kor" => "Coreano",
        "zh" or "zho" or "chi" => "Chinês", null or "" or "und" => "Idioma não informado", _ => language
    };
    internal static IReadOnlyList<OnlineMediaTrack> Parse(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Array) return Array.Empty<OnlineMediaTrack>();
        var result = new List<OnlineMediaTrack>(); var seen = new HashSet<(string,int)>();
        foreach (var item in data.EnumerateArray().Take(256)) {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id",out var id) || !id.TryGetInt32Safe(out var number) || number <= 0) continue;
            var kind = Text(item,"type",12);
            if (kind is not ("audio" or "sub") || !seen.Add((kind,number))) continue;
            int? channels = item.TryGetProperty("demux-channel-count",out var count) && count.TryGetInt32Safe(out var c) && c is > 0 and <= 64 ? c : null;
            result.Add(new(number,kind,Text(item,"lang",32),Text(item,"title",160),Text(item,"codec",32),channels,
                Flag(item,"forced"),Flag(item,"external"),Flag(item,"default"),Flag(item,"selected")));
        }
        return result.AsReadOnly();
    }
    static bool Flag(JsonElement item,string name) => item.TryGetProperty(name,out var value) && value.ValueKind == JsonValueKind.True;
    static string? Text(JsonElement item,string name,int limit) => item.TryGetProperty(name,out var value) && value.ValueKind==JsonValueKind.String
        ? new string((value.GetString() ?? "").Where(c=>!char.IsControl(c)).Take(limit).ToArray()).Trim() : null;
}
internal static class TrackJson
{
    internal static bool TryGetInt32Safe(this JsonElement item,out int value) { value=0; return item.ValueKind==JsonValueKind.Number && item.TryGetInt32(out value); }
}
public sealed record OnlineTrackState(IReadOnlyList<OnlineMediaTrack> Tracks, int? AudioId, int? SubtitleId, double Speed, double SubtitleDelay)
{
    public static OnlineTrackState Empty { get; } = new(Array.Empty<OnlineMediaTrack>(),null,null,1,0);
}
public sealed record OnlineBufferedRange(double Start, double End);
