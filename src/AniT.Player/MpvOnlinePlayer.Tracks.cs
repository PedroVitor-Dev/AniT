using System.IO;
using System.Globalization;
using System.Text.Json;

namespace AniT.Player;

public sealed partial class MpvOnlinePlayer
{
    IReadOnlyList<OnlineMediaTrack> mediaTracks = Array.Empty<OnlineMediaTrack>();
    int? audioId, subtitleId;
    double subtitleDelay;
    OnlineTrackState tracks = OnlineTrackState.Empty;
    public OnlineTrackState Tracks => Volatile.Read(ref tracks);
    public double InitialVolume { get; set; } = 100;
    public bool InitialMuted { get; set; }
    public IReadOnlyList<OnlineBufferedRange> BufferedRanges => Array.AsReadOnly(cacheState.Ranges
        .Where(r=>r.End>r.Start).Select(r=>new OnlineBufferedRange(Math.Max(0,r.Start),Math.Max(0,r.End))).ToArray());
    void PublishTracks() => Volatile.Write(ref tracks,new(mediaTracks,audioId,subtitleId,playbackSpeed,subtitleDelay));
    void ResetTracks()
    {
        importedSubtitles.Clear();
        mediaTracks = Array.Empty<OnlineMediaTrack>();audioId=subtitleId=null;subtitleDelay=0;PublishTracks();
    }
    internal void ObserveTracks(string name,JsonElement value)
    {
        switch(name) {
            case "track-list":
                mediaTracks=OnlineMediaTrack.Parse(value);
                audioId=mediaTracks.FirstOrDefault(t=>t.Kind=="audio"&&t.Selected)?.Id;
                subtitleId=mediaTracks.FirstOrDefault(t=>t.Kind=="sub"&&t.Selected)?.Id;
                break;
            case "aid": audioId=TrackId(value);break;
            case "sid": subtitleId=TrackId(value);break;
            case "sub-delay": if(value.ValueKind==JsonValueKind.Number&&value.TryGetDouble(out var delay)&&double.IsFinite(delay))subtitleDelay=delay;break;
        }
        PublishTracks();
    }
    static int? TrackId(JsonElement value) => value.TryGetInt32Safe(out var id)&&id>0?id:null;
    public Task SelectAudioAsync(int id,CancellationToken token=default) => SelectTrackAsync("audio","aid",id,token);
    public Task SelectSubtitleAsync(int? id,CancellationToken token=default) => SelectTrackAsync("sub","sid",id,token);
    async Task SelectTrackAsync(string kind,string property,int? id,CancellationToken token)
    {
        using var operation=CancellationTokenSource.CreateLinkedTokenSource(token,fileLifetime?.Token??CancellationToken.None);
        token=operation.Token;
        await indexing.WaitAsync(token);
        try {
            if (id is not null&&!Tracks.Tracks.Any(t=>t.Kind==kind&&t.Id==id))throw new ArgumentException("Essa faixa não está disponível neste episódio.");
            await CommandAsync(["set_property",property,id is { } selected ? (object)selected : "no"],token);
        } finally { indexing.Release(); }
    }
    public async Task SetPlaybackSpeedAsync(double speed,CancellationToken token=default)
    {
        if(!double.IsFinite(speed)||speed<0.25||speed>3)throw new ArgumentOutOfRangeException(nameof(speed));
        await CommandAsync(["set_property","speed",speed],token);
    }
    public async Task SetSubtitleDelayAsync(double seconds,CancellationToken token=default)
    {
        if(!double.IsFinite(seconds)||seconds < -10||seconds>10)throw new ArgumentOutOfRangeException(nameof(seconds));
        await CommandAsync(["set_property","sub-delay",seconds],token);
    }
    static string SelectionOption(JsonElement value) => value.ValueKind switch {
        JsonValueKind.Number when value.TryGetInt32(out var id)&&id>0 => id.ToString(CultureInfo.InvariantCulture),
        JsonValueKind.String when value.GetString() is "auto" => "auto",
        _ => "no"
    };
    // Both explicit seeks and speculative indexing must preserve manual selections, including sid=no.
    async Task ReloadIndexedAsync(string path,TimeSpan target,CancellationToken token)
    {
        var expected=generation;
        var sessionToken=fileLifetime?.Token??lifetime?.Token??token;
        await BeginPauseReloadAsync(expected,token);
        try {
            var audio=await CommandAsync(["get_property","aid"],token);
            var subtitle=await CommandAsync(["get_property","sid"],token);
            var selectedPath=ExternalPath(await CommandAsync(["get_property","track-list"],token),TrackId(subtitle));
            var externalFiles=importedSubtitles.ToArray();
            ResetBufferObservation();seeking=true;PublishSnapshot();
            loaded=new(TaskCreationOptions.RunContinuationsAsynchronously);awaitingFileStart=true;
            await CommandAsync(["loadfile",path,"replace",-1,new Dictionary<string,string> {
                ["index"]="default",["start"]=target.TotalSeconds.ToString(CultureInfo.InvariantCulture),["pause"]="yes",
                ["aid"]=SelectionOption(audio),["sid"]=selectedPath is not null?"no":SelectionOption(subtitle),
                ["sub-delay"]=Tracks.SubtitleDelay.ToString(CultureInfo.InvariantCulture),
                ["speed"]=playbackSpeed.ToString(CultureInfo.InvariantCulture)
            }],token);
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(120),token);usingIndex=true;
            foreach(var external in externalFiles)if(File.Exists(external))await CommandAsync(["sub-add",external,"auto"],token);
            if(selectedPath is not null){var id=ExternalId(await CommandAsync(["get_property","track-list"],token),selectedPath);await CommandAsync(["set_property","sid",id is {} restored?(object)restored:"no"],token);}
            else await CommandAsync(["set_property","sid",SelectionOption(subtitle)],token);
            if(InitialSubtitleAppearance is {} style)await ApplySubtitleStyleCore(style,token);
            await RefreshTracksAsync(token);
        } finally {await FinishPauseReloadAsync(expected,sessionToken);}

    }
}
