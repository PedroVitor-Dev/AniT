using AniT.Core;
namespace AniT.Tests;
public sealed class PlaybackAssistantTests
{
    sealed class Clock:TimeProvider{long value;public override long TimestampFrequency=>1000;public override long GetTimestamp()=>value;public void Advance(int seconds)=>value+=seconds*1000;}
    [Fact]public void BriefBufferingAndNormalPlaybackStayQuiet()
    {
        var clock=new Clock();var policy=new PlaybackAssistant(clock);var buffering=new PlaybackAssistantInput(true,false,true,false,0,.4);
        Assert.Null(policy.Update(buffering));clock.Advance(2);Assert.Null(policy.Update(buffering));clock.Advance(2);Assert.Equal("slow_supply",policy.Update(buffering)!.Code);
        Assert.Null(policy.Update(buffering with{Buffering=false}));Assert.Null(policy.Update(buffering with{Paused=true}));
    }
    [Fact]public void ManualReserveNeverAutomaticallyResumesAndDismissalIsPerEpisode()
    {
        var policy=new PlaybackAssistant();var sample=new PlaybackAssistantInput(true,true,false,false,12,BuildingReserve:true);
        Assert.Equal("play",policy.Update(sample)!.Action);policy.Dismiss();Assert.Null(policy.Update(sample));policy.Reset();Assert.Equal("ready",policy.Update(sample)!.Code);
    }
    [Fact]public void UnknownSupplyDoesNotInventAConnectionDiagnosis()
    {
        var clock=new Clock();var policy=new PlaybackAssistant(clock);var sample=new PlaybackAssistantInput(true,false,true,false,0);
        policy.Update(sample);clock.Advance(4);Assert.Equal("buffering",policy.Update(sample)!.Code);
    }
}
