using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using AniT.Core;
using AniT.Player;

namespace AniT.Player.Tests;

public sealed class OnlineDiagnosticsIpcTests
{
    static Task<OnlinePlaybackMetric[]> ReadEvents(params string[] messages) => ReadEvents(false, messages);
    static async Task<OnlinePlaybackMetric[]> ReadEvents(bool awaitingStart, params string[] messages)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = new ConcurrentBag<OnlinePlaybackMetric>();
        using var player = new MpvOnlinePlayer("not-launched.exe");
        typeof(MpvOnlinePlayer).GetField("telemetry", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, new OnlinePlaybackTrace(events.Add));
        typeof(MpvOnlinePlayer).GetField("awaitingFileStart", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, awaitingStart);
        var name = "anit-diagnostics-test-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await using var client = new NamedPipeClientStream(".", name, PipeDirection.In, PipeOptions.Asynchronous);
        await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), client.ConnectAsync(timeout.Token));
        var read = (Task)typeof(MpvOnlinePlayer).GetMethod("ReadLoopAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(player, [client, timeout.Token])!;
        await using (var writer = new StreamWriter(server, leaveOpen: true)) {
            foreach (var message in messages) await writer.WriteLineAsync(message);
            await writer.FlushAsync(timeout.Token);
        }
        server.Disconnect(); await read.WaitAsync(timeout.Token); player.Dispose();
        return events.OrderBy(item => item.Sequence).ToArray();
    }
    const string Output = """{"event":"property-change","name":"video-out-params","data":{"crop-w":1920}}""";
    const string Frame = """{"event":"property-change","name":"video-frame-info","data":{"picture-type":"I"}}""";
    const string Restart = """{"event":"playback-restart"}""";
    [Fact]
    public async Task ContainerWindowAndPositionAloneDoNotCountAsFirstFrame()
    {
        var events = await ReadEvents("""{"event":"file-loaded"}""", Output,
            """{"event":"property-change","name":"time-pos","data":0}""");
        Assert.Contains(events, item => item.Name == "container_loaded");
        Assert.DoesNotContain(events, item => item.Name == "first_video_frame_signal");
    }
    [Fact]
    public async Task FirstFrameRequiresCurrentFrameAndRestartAndIsReportedOnlyOnce()
    {
        var events = await ReadEvents(Output, Frame, Restart, Restart, Frame);
        Assert.Single(events, item => item.Name == "first_video_frame_signal");
        Assert.Single(events, item => item.Kind == "summary");
    }
    [Fact]
    public async Task EventsBeforeNewStartFileAreIgnored()
    {
        var old = await ReadEvents(true, Output, Frame, Restart);
        Assert.DoesNotContain(old, item => item.Name == "first_video_frame_signal");
        Assert.DoesNotContain(old, item => item.Name == "video_output_configured");
        var fresh = await ReadEvents(true, Output, Frame, Restart, """{"event":"start-file"}""", Output, Frame, Restart);
        Assert.Single(fresh, item => item.Name == "first_video_frame_signal");
    }
}
