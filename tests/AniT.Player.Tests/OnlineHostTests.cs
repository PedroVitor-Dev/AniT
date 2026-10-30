using System.Reflection;
using System.Text.Json;
using AniT.Player;

namespace AniT.Player.Tests;

public sealed class OnlineHostTests
{
    [Fact]
    public void HostInputAcceptsOnlyKnownActionsWithTheExpectedNamespace()
    {
        using var player = new MpvOnlinePlayer("not-launched.exe");
        player.AttachVideoWindow(new IntPtr(1));
        var actions = new List<string>(); player.UserInput += (_, action) => actions.Add(action);
        foreach(var json in new[]{"""{"args":["anit-ui","pause"]}""", """{"args":["other","pause"]}""", """{"args":["anit-ui","quit"]}""", """{"args":["anit-ui",3]}""", """{"args":null}""", """{"args":["anit-ui","pause","extra"]}"""}) {
            using var document = JsonDocument.Parse(json); player.ReceiveHostInput(document.RootElement);
        }
        Assert.Equal(new[]{"pause"},actions);
        Assert.Equal("4294967295",MpvOnlinePlayer.WindowId(new IntPtr(-1)));
        Assert.Throws<ArgumentException>(()=>player.AttachVideoWindow(IntPtr.Zero));
    }
    [Fact]
    public void ClosingBeforeFirstFrameDoesNotInventProgressAndNotifiesOnlyOnce()
    {
        using var player = new MpvOnlinePlayer("not-launched.exe");
        int closes=0,disposals=0;
        player.PlaybackClosed += (_, progress) => { closes++; Assert.False(progress.IsPositionKnown); };
        player.Disposed += (_,_)=>disposals++;
        player.ClosePlayback();player.ClosePlayback();
        Assert.Equal(1,closes);Assert.Equal(1,disposals);Assert.True(player.WasClosedByUser);
    }
    [Fact]
    public void UserClosePreservesTheLastConfirmedPositionBeforeDestroyingTheHost()
    {
        using var player = new MpvOnlinePlayer("not-launched.exe");
        var type=typeof(MpvOnlinePlayer);var hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        type.GetField("position",hidden)!.SetValue(player,TimeSpan.FromSeconds(42));
        type.GetField("duration",hidden)!.SetValue(player,TimeSpan.FromSeconds(300));
        type.GetField("videoReady",hidden)!.SetValue(player,true);
        global::AniT.Core.PlaybackPositionChangedEventArgs? checkpoint=null;
        player.PlaybackClosed+=(_,progress)=>checkpoint=progress;
        player.ClosePlayback();
        Assert.NotNull(checkpoint);Assert.True(checkpoint.IsPositionKnown);Assert.Equal(42,checkpoint.Position.TotalSeconds);Assert.Equal(300,checkpoint.Duration!.Value.TotalSeconds);
    }
}
