using AniT.Player;
namespace AniT.Player.Tests;
public sealed class VideoClickTests
{
    [Fact] public void PairUsesCapturedTimeAndPositionAndConsumesThePair()
    {
        var clicks=new VideoClickTracker();
        Assert.False(clicks.Press(1000,300,200,500,4,4));
        Assert.True(clicks.Press(1300,301,201,500,4,4));
        Assert.False(clicks.Press(1400,301,201,500,4,4));
    }
    [Fact] public void SlowOrDistantClicksAreNotDoubleClicks()
    {
        var clicks=new VideoClickTracker();
        Assert.False(clicks.Press(1000,300,200,500,4,4));
        Assert.False(clicks.Press(1600,300,200,500,4,4));
        Assert.False(clicks.Press(1700,350,200,500,4,4));
        Assert.True(clicks.Press(1800,350,200,500,4,4));
    }
    [Fact] public void NativeTickWrapAndExplicitResetAreHandled()
    {
        var clicks=new VideoClickTracker();
        Assert.False(clicks.Press(uint.MaxValue-100,0,0,500,4,4));
        Assert.True(clicks.Press(100,0,0,500,4,4));
        Assert.False(clicks.Press(200,0,0,500,4,4));clicks.Reset();
        Assert.False(clicks.Press(250,0,0,500,4,4));
    }
}
