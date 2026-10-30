using System.Text.Json;

namespace AniT.Infrastructure;

public sealed record OnlinePlayerPreferences(bool RememberLanguages=true,string? AudioLanguage=null,string? SubtitleLanguage=null,
    double Volume=100,bool Muted=false,double? Speed=null)
{
    public static string? NormalizeLanguage(string? value)
    {
        value=value?.Trim().ToLowerInvariant();
        if(string.IsNullOrEmpty(value)||value is "und" or "unknown"||value.Length>32||!value.All(c=>char.IsAsciiLetter(c)||c=='-'))return null;
        return value switch {"jpn"=>"ja","por"=>"pt","eng"=>"en","spa"=>"es","fra" or "fre"=>"fr","deu" or "ger"=>"de","ita"=>"it","kor"=>"ko","zho" or "chi"=>"zh",_=>value};
    }
    public OnlinePlayerPreferences Normalize()=>this with {
        AudioLanguage=NormalizeLanguage(AudioLanguage) is "off" ? null : NormalizeLanguage(AudioLanguage),
        SubtitleLanguage=NormalizeLanguage(SubtitleLanguage),Volume=double.IsFinite(Volume)?Math.Clamp(Volume,0,100):100,
        Speed=Speed is { } speed&&double.IsFinite(speed)&&speed>=0.25&&speed<=3?speed:null
    };
}
/// <summary>Profile-local language preferences, never native track IDs or torrent identifiers.</summary>
public sealed class OnlinePlayerPreferencesStore(string path)
{
    readonly object sync=new();
    static readonly JsonSerializerOptions Options=new(){WriteIndented=true};
    public OnlinePlayerPreferences Load()
    {
        lock(sync)return Read(path)??Read(path+".bak")??new();
    }
    static OnlinePlayerPreferences? Read(string file)
    {
        try { if(!File.Exists(file)||new FileInfo(file).Length>65536)return null;return JsonSerializer.Deserialize<OnlinePlayerPreferences>(File.ReadAllText(file))?.Normalize(); }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or JsonException){return null;}
    }
    public void Save(OnlinePlayerPreferences value)
    {
        lock(sync){
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {
                File.WriteAllText(temporary,JsonSerializer.Serialize(value.Normalize(),Options));
                if(File.Exists(path)) {
                    if(Read(path) is not null)File.Replace(temporary,path,path+".bak");else File.Move(temporary,path,true);
                } else File.Move(temporary,path);
            } finally { if(File.Exists(temporary))File.Delete(temporary); }
        }
    }
}
