using System.Text.Json;
using AniT.Player;
namespace AniT.Player.Tests;
public sealed class OnlineMediaTracksTests
{
    [Fact]
    public void MalformedMetadataIsBoundedAndOnlyPlayableTracksAreExposed()
    {
        using var json=JsonDocument.Parse("""[{"id":1,"type":"video"},{"id":2,"type":"audio","lang":"jpn","title":"Original\nJapanese","codec":"opus","demux-channel-count":6,"selected":true},{"id":2,"type":"audio"},{"id":false,"type":"audio"},{"id":0,"type":"sub"},{"id":1,"type":"sub","lang":"eng","forced":true,"external":true}]""");
        var tracks=OnlineMediaTrack.Parse(json.RootElement);Assert.Equal(2,tracks.Count);
        Assert.Contains("Japonês",tracks[0].Label);Assert.Contains("6 canais",tracks[0].Label);Assert.DoesNotContain("\n",tracks[0].Label);
        Assert.Contains("Forçada",tracks[1].Label);Assert.Contains("Externa",tracks[1].Label);
        using var nullData=JsonDocument.Parse("null");Assert.Empty(OnlineMediaTrack.Parse(nullData.RootElement));
    }
    [Fact]
    public void DuplicateLanguageTracksRemainIndividuallySelectableAndTitlesCannotGrowWithoutLimit()
    {
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(Enumerable.Range(1,300).Select(i=>new{id=i,type="sub",lang="por",title=new string('x',1000)})));
        var tracks=OnlineMediaTrack.Parse(json.RootElement);Assert.Equal(256,tracks.Count);Assert.Equal(160,tracks[0].Title!.Length);
        Assert.NotEqual(tracks[0].Label,tracks[1].Label);
    }
    [Fact]
    public void SelectedIdsAndNegativeSubtitleDelayAreObservedWithoutGuessing()
    {
        using var player=new MpvOnlinePlayer("not-launched.exe");
        using var list=JsonDocument.Parse("""[{"id":2,"type":"audio","selected":true},{"id":3,"type":"sub","selected":true}]""");player.ObserveTracks("track-list",list.RootElement);
        Assert.Equal(2,player.Tracks.AudioId);Assert.Equal(3,player.Tracks.SubtitleId);
        using var disabled=JsonDocument.Parse("false");player.ObserveTracks("sid",disabled.RootElement);Assert.Null(player.Tracks.SubtitleId);
        using var delay=JsonDocument.Parse("-0.3");player.ObserveTracks("sub-delay",delay.RootElement);Assert.Equal(-.3,player.Tracks.SubtitleDelay);
    }
}
