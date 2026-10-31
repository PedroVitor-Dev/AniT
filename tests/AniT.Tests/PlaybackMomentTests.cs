using AniT.Core;
using AniT.Infrastructure;
using System.Text.Json;
namespace AniT.Tests;
public sealed class PlaybackMomentTests
{
    [Theory][InlineData(60,10,false)][InlineData(60,60,true)][InlineData(60,0,false)]
    public void FutureMomentsAreHidden(double moment,double current,bool expected)=>Assert.Equal(expected,MomentSpoilerProtection.CanReveal(moment,current));
    [Fact]public void RevealRequiresAConfirmedPositionOrExplicitChoice(){Assert.False(MomentSpoilerProtection.CanReveal(10,null));Assert.False(MomentSpoilerProtection.CanReveal(10,double.NaN));Assert.True(MomentSpoilerProtection.CanReveal(10,null,explicitlyRevealed:true));}
    [Fact]public void StoreIsolatesEpisodesAndProfilesAndDoesNotAcceptInvalidPositions()
    {
        var root=Path.Combine(Path.GetTempPath(),"AniT.Moments.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var path=Path.Combine(root,"moments.json");var store=new PlaybackMomentStore(path);var episode=Guid.NewGuid();var moment=new PlaybackMoment(Guid.NewGuid(),episode,42,new string('a',600));
        store.Add(moment);Assert.Equal(500,store.ForEpisode(episode).Single().Note.Length);Assert.Empty(store.ForEpisode(Guid.NewGuid()));Assert.Empty(new PlaybackMomentStore(Path.Combine(root,"other","moments.json")).ForEpisode(episode));
        Assert.Throws<ArgumentException>(()=>store.Add(moment with{Id=Guid.NewGuid(),Seconds=double.NaN}));
        store.Edit(episode,moment.Id,"updated");Assert.Equal("updated",store.ForEpisode(episode).Single().Note);
        File.WriteAllText(path,"broken");Assert.Equal(500,store.ForEpisode(episode).Single().Note.Length);
        store.Remove(Guid.NewGuid(),moment.Id);Assert.Single(store.ForEpisode(episode));store.Remove(episode,moment.Id);Assert.Empty(store.ForEpisode(episode));
    }
    [Fact]public void EpisodeLimitPreventsUnboundedMetadataGrowth(){var root=Path.Combine(Path.GetTempPath(),"AniT.Moments.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var path=Path.Combine(root,"moments.json");var episode=Guid.NewGuid();File.WriteAllText(path,JsonSerializer.Serialize(Enumerable.Range(0,200).Select(i=>new PlaybackMoment(Guid.NewGuid(),episode,i,"note"))));Assert.Throws<InvalidOperationException>(()=>new PlaybackMomentStore(path).Add(new(Guid.NewGuid(),episode,201,"next")));}    [Fact]public void FullUnicodeNotesRemainReadableAcrossManyEpisodes()
    {
        var root=Path.Combine(Path.GetTempPath(),"AniT.Moments.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var path=Path.Combine(root,"moments.json");
        var episodes=Enumerable.Range(0,8).Select(_=>Guid.NewGuid()).ToArray();
        File.WriteAllText(path,JsonSerializer.Serialize(episodes.SelectMany(episode=>Enumerable.Range(0,100).Select(i=>new PlaybackMoment(Guid.NewGuid(),episode,i,new string('語',500))))));
        var store=new PlaybackMomentStore(path);Assert.Equal(100,store.ForEpisode(episodes[0]).Count);
        store.Add(new(Guid.NewGuid(),episodes[0],101,"one more"));Assert.Equal(101,store.ForEpisode(episodes[0]).Count);
    }

}
