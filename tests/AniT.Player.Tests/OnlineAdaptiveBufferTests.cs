using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using AniT.Player;

namespace AniT.Player.Tests;

public sealed class OnlineAdaptiveBufferTests
{
    sealed class Session : IAsyncDisposable
    {
        internal readonly MpvOnlinePlayer Player;
        Session(Func<Uri, CancellationToken, Task<bool>>? prepare = null) { Player = new("not-launched.exe", prepare); }
        internal readonly ConcurrentQueue<JsonElement> Commands = new();
        internal readonly HashSet<string> Unsupported = [];
        internal readonly CancellationTokenSource Lifetime = new(TimeSpan.FromSeconds(10));
        readonly SemaphoreSlim writing = new(1, 1);
        NamedPipeServerStream server = null!;
        NamedPipeClientStream client = null!;
        StreamWriter output = null!;
        Task incoming = Task.CompletedTask, replies = Task.CompletedTask;
        internal Task Control = Task.CompletedTask;
        static readonly BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
        internal void Set(string field, object value) => typeof(MpvOnlinePlayer).GetField(field, Hidden)!.SetValue(Player, value);
        internal static async Task<Session> Open(Func<Uri, CancellationToken, Task<bool>>? prepare = null)
        {
            var value = new Session(prepare);
            var name = "anit-buffer-test-" + Guid.NewGuid().ToString("N");
            value.server = new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            value.client = new(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await Task.WhenAll(value.server.WaitForConnectionAsync(value.Lifetime.Token), value.client.ConnectAsync(value.Lifetime.Token));
            value.output = new StreamWriter(value.server, leaveOpen: true) { AutoFlush = true };
            value.Set("writer", new StreamWriter(value.client, leaveOpen: true) { AutoFlush = true }); value.Set("pipe", value.client);
            value.Set("videoReady", true); value.Set("playbackRestarted", true);
            value.incoming = (Task)typeof(MpvOnlinePlayer).GetMethod("ReadLoopAsync", Hidden)!.Invoke(value.Player, [value.client, value.Lifetime.Token])!;
            value.replies = value.ReplyAsync(); return value;
        }
        async Task WriteAsync(string json)
        {
            await writing.WaitAsync(Lifetime.Token);
            try { await output.WriteLineAsync(json.AsMemory(), Lifetime.Token); }
            finally { writing.Release(); }
        }
        async Task ReplyAsync()
        {
            using var reader = new StreamReader(server, leaveOpen: true);
            try {
                while (await reader.ReadLineAsync(Lifetime.Token) is { } line) {
                    using var json = JsonDocument.Parse(line); var request = json.RootElement;
                    var command = request.GetProperty("command"); Commands.Enqueue(command.Clone());
                    var rejected = command[0].GetString() == "set_property" && Unsupported.Contains(command[1].GetString()!);
                    await WriteAsync(JsonSerializer.Serialize(new { request_id = request.GetProperty("request_id").GetInt32(), error = rejected ? "property unavailable" : "success", data = 10 }));
                }
            } catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
        }
        internal async Task Property(string name, string data)
        {
            await WriteAsync("{\"event\":\"property-change\",\"name\":" + JsonSerializer.Serialize(name) + ",\"data\":" + data + "}");
            await Player.CommandAsync(["get_property", "barrier"], Lifetime.Token);
        }
        internal async Task Event(string name)
        {
            await WriteAsync(JsonSerializer.Serialize(new { @event = name }));
            await Player.CommandAsync(["get_property", "barrier"], Lifetime.Token);
        }
        internal void Start() => Control = Player.ControlBufferAsync(0, Lifetime.Token);
        internal async Task WaitOption(string option, Func<double, bool> matches)
        {
            while (!Commands.Any(command => command[0].GetString() == "set_property" && command[1].GetString() == option && matches(command[2].GetDouble())))
                await Task.Delay(10, Lifetime.Token);
        }
        public async ValueTask DisposeAsync()
        {
            Lifetime.Cancel(); await Control; Player.Dispose();
            await incoming; await replies; await output.DisposeAsync(); await server.DisposeAsync(); await client.DisposeAsync();
            Lifetime.Dispose(); writing.Dispose();
        }
    }
    const string Slow = """{"cache-duration":12,"reader-pts":0,"fw-bytes":1200000,"raw-input-rate":30000,"seekable-ranges":[{"start":0,"end":12}]}""";

    [Fact]
    public async Task ObservedCacheControlsNativeOptionsWithoutDiagnosticsAndNeverChangesManualPause()
    {
        await using var session = await Session.Open();
        await session.Property("demuxer-cache-state", Slow);
        session.Start();
        await session.WaitOption("cache-pause-wait", value => value >= 5);
        await session.WaitOption("cache-secs", value => value >= 20);
        await session.WaitOption("demuxer-readahead-secs", value => value >= 20);
        await session.Property("pause", "true");
        await Task.Delay(1100, session.Lifetime.Token);
        Assert.DoesNotContain(session.Commands, command => command[0].GetString() == "set_property" && command[1].GetString() == "pause");
        Assert.DoesNotContain(session.Commands, command => command[0].GetString() == "set_property" && command[1].GetString() == "speed");
    }

    [Fact]
    public async Task SeekClearsOldReserveAndControllerContinuesForTheNewDemand()
    {
        await using var session = await Session.Open();
        await session.Property("demuxer-cache-state", Slow); session.Start();
        await session.WaitOption("cache-pause-wait", value => value >= 5);
        await session.Event("seek");
        await session.WaitOption("cache-pause-wait", value => value == 1);
        Assert.False(session.Control.IsCompleted);
        await session.Event("playback-restart");
        await session.Property("demuxer-cache-state", Slow);
        await session.Property("paused-for-cache", "true");
        await session.WaitOption("cache-pause-wait", value => value >= 7);
    }

    [Fact]
    public async Task UnsupportedOptionalSettingIsNotRetriedEveryTickAndCancellationStopsCommands()
    {
        await using var session = await Session.Open();
        session.Unsupported.Add("cache-secs");
        await session.Property("demuxer-cache-state", Slow); session.Start();
        await session.WaitOption("demuxer-readahead-secs", value => value >= 20);
        await session.Property("paused-for-cache", "true");
        await session.WaitOption("demuxer-readahead-secs", value => value >= 29);
        Assert.Single(session.Commands, command => command[0].GetString() == "set_property" && command[1].GetString() == "cache-secs");
        session.Lifetime.Cancel(); await session.Control;
        var count = session.Commands.Count; await Task.Delay(100);
        Assert.Equal(count, session.Commands.Count);
    }

    [Fact]
    public void ContiguousCacheExcludesDistantDownloadedRangesAndMergesOnlyOverlaps()
    {
        using var json = JsonDocument.Parse("""{"cache-duration":200,"seekable-ranges":[{"start":100,"end":200},{"start":0,"end":6},{"start":5,"end":12}]}""");
        var state = MpvCacheState.Parse(json.RootElement);
        Assert.Equal(9, state.ContiguousSeconds(3, 200));
        Assert.Equal(0, state.ContiguousSeconds(50, 200));
        Assert.Equal(50, state.ContiguousSeconds(150, 200));
    }

    [Fact]
    public async Task FallingPlaybackReserveCancelsSpeculativeIndexBeforeReloadingVideo()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await Session.Open(async (_, token) => {
            started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); return true; }
            catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
        });
        session.Set("<CurrentFilePath>k__BackingField", "http://127.0.0.1:12345/test.mkv");
        await session.Property("demuxer-cache-state", """{"cache-duration":12,"reader-pts":0,"fw-bytes":1200000,"raw-input-rate":500000,"idle":true}""");
        session.Start(); await started.Task.WaitAsync(session.Lifetime.Token);
        await session.Property("demuxer-cache-state", """{"cache-duration":1,"reader-pts":0,"fw-bytes":100000,"raw-input-rate":0}""");
        await cancelled.Task.WaitAsync(session.Lifetime.Token);
        Assert.DoesNotContain(session.Commands, command => command[0].GetString() == "loadfile");
        Assert.False(session.Control.IsCompleted);
    }

    [Fact]
    public async Task ShortBufferingPulseBetweenControllerTicksStillIncreasesProtection()
    {
        await using var session = await Session.Open();
        await session.Property("demuxer-cache-state", """{"cache-duration":12,"reader-pts":0,"fw-bytes":1200000,"raw-input-rate":500000}""");
        await session.Property("paused-for-cache", "true");
        await session.Property("paused-for-cache", "false");
        session.Start();
        await session.WaitOption("cache-pause-wait", value => value >= 3);
    }

    [Fact]
    public async Task FreshScalarCacheStillWorksWhenTheDetailedPropertyIsUnavailable()
    {
        await using var session = await Session.Open();
        session.Set("cacheState", MpvCacheState.Empty with { Timestamp = System.Diagnostics.Stopwatch.GetTimestamp() - 10 * System.Diagnostics.Stopwatch.Frequency });
        await session.Property("demuxer-cache-duration", "9");
        var sample = (AniT.Core.OnlineBufferSample)typeof(MpvOnlinePlayer).GetMethod("BufferSample", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(session.Player, null)!;
        Assert.Equal(9, sample.Seconds); Assert.Null(sample.InputBytesPerSecond);
    }

    [Fact]
    public void MissingMalformedAndNonfiniteCacheFieldsAreNotReportedAsMeasuredZeros()
    {
        using var json = JsonDocument.Parse("""{"cache-duration":-1,"fw-bytes":1e999,"raw-input-rate":null,"seekable-ranges":[null,{"start":10,"end":2}]}""");
        var state = MpvCacheState.Parse(json.RootElement);
        Assert.Null(state.Seconds); Assert.Null(state.Bytes); Assert.Null(state.InputRate);
        Assert.Equal(0, state.ContiguousSeconds(1, null));
        using var empty = JsonDocument.Parse("null");
        Assert.Null(MpvCacheState.Parse(empty.RootElement).ContiguousSeconds(null, null));
    }
}
