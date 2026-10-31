using System.IO;
using System.Text.Json;
using AniT.Core;
namespace AniT.Player;
public sealed partial class MpvOnlinePlayer
{
    readonly HashSet<string> importedSubtitles=new(StringComparer.OrdinalIgnoreCase);
    public SubtitleAppearanceSettings? InitialSubtitleAppearance{get;set;}
    public static bool IsSubtitleFile(string path)=>Path.GetExtension(path).ToLowerInvariant() is ".srt" or ".ass" or ".ssa" or ".vtt";
    async Task RefreshTracksAsync(CancellationToken token)=>ObserveTracks("track-list",await CommandAsync(["get_property","track-list"],token));
    static string? ExternalPath(JsonElement list,int? id)
    {
        if(list.ValueKind!=JsonValueKind.Array||id is null)return null;
        foreach(var track in list.EnumerateArray().Take(256))if(track.ValueKind==JsonValueKind.Object&&track.TryGetProperty("id",out var value)&&value.TryGetInt32Safe(out var number)&&number==id
            &&track.TryGetProperty("external",out var external)&&external.ValueKind==JsonValueKind.True&&track.TryGetProperty("external-filename",out var file)&&file.ValueKind==JsonValueKind.String)return file.GetString();
        return null;
    }
    static int? ExternalId(JsonElement list,string path)
    {
        if(list.ValueKind!=JsonValueKind.Array)return null;
        foreach(var track in list.EnumerateArray().Take(256))if(track.ValueKind==JsonValueKind.Object&&track.TryGetProperty("id",out var value)&&value.TryGetInt32Safe(out var number)
            &&string.Equals(ExternalPath(list,number),path,StringComparison.OrdinalIgnoreCase))return number;
        return null;
    }
    public async Task AddSubtitleAsync(string path,CancellationToken token=default)
    {
        if(!videoReady)throw new InvalidOperationException("Espere o vídeo abrir antes de importar uma legenda.");
        if(!Path.IsPathFullyQualified(path)||!IsSubtitleFile(path)||!File.Exists(path))throw new ArgumentException("Escolha um arquivo SRT, ASS, SSA ou VTT existente.");
        path=Path.GetFullPath(path);if(new FileInfo(path).Length>64*1024*1024)throw new ArgumentException("O arquivo de legenda excede 64 MB.");
        using var request=CancellationTokenSource.CreateLinkedTokenSource(token,fileLifetime?.Token??CancellationToken.None);token=request.Token;
        await indexing.WaitAsync(token);
        try {
            if(importedSubtitles.Count>=32&&!importedSubtitles.Contains(path))throw new InvalidOperationException("O limite de 32 legendas externas por episódio foi atingido.");
            await CommandAsync(["sub-add",path,"cached"],token);importedSubtitles.Add(path);await RefreshTracksAsync(token);
        }finally{indexing.Release();}
    }
    public async Task RemoveSubtitleAsync(int id,CancellationToken token=default)
    {
        using var request=CancellationTokenSource.CreateLinkedTokenSource(token,fileLifetime?.Token??CancellationToken.None);token=request.Token;
        await indexing.WaitAsync(token);
        try {
            if(!Tracks.Tracks.Any(t=>t.Kind=="sub"&&t.Id==id&&t.External))throw new ArgumentException("Apenas legendas externas podem ser removidas.");
            var path=ExternalPath(await CommandAsync(["get_property","track-list"],token),id);
            await CommandAsync(["sub-remove",id],token);if(path is not null)importedSubtitles.Remove(path);await RefreshTracksAsync(token);
        }finally{indexing.Release();}
    }
    public async Task ApplySubtitleAppearanceAsync(SubtitleAppearanceSettings appearance,CancellationToken token=default)
    {
        using var request=CancellationTokenSource.CreateLinkedTokenSource(token,fileLifetime?.Token??CancellationToken.None);token=request.Token;
        await indexing.WaitAsync(token);
        try{appearance=appearance.Normalize();await ApplySubtitleStyleCore(appearance,token);InitialSubtitleAppearance=appearance;}finally{indexing.Release();}
    }
    async Task ApplySubtitleStyleCore(SubtitleAppearanceSettings value,CancellationToken token)
    {
        foreach(var(option,setting)in new(string,object)[]{("sub-font-size",value.FontSize),("sub-pos",value.Position),("sub-outline-size",value.Outline),("sub-color",value.Color),("sub-font",value.Font),("sub-ass-override",value.OverrideAss?"force":"no")})await CommandAsync(["set_property",option,setting],token);
    }
}
