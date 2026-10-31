using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using AniT.Player;

namespace AniT.Player.Tests;

public sealed class OnlineIndexPriorityTests
{
    static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    [Fact]
    public async Task ExplicitSeekCancelsBackgroundIndexInsteadOfWaitingBehindItsNetworkBuffering()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var backgroundStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backgroundCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var foregroundStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var player = new MpvOnlinePlayer("not-launched.exe", async (_, token) => {
            if (Interlocked.Increment(ref calls) == 1) {
                backgroundStarted.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) { backgroundCancelled.TrySetResult(); throw; }
            }
            foregroundStarted.TrySetResult(); return false;
        });
        var address = new Uri("http://127.0.0.1:12345/test.mkv");
        typeof(MpvOnlinePlayer).GetField("<CurrentFilePath>k__BackingField", Private)!.SetValue(player, address.AbsoluteUri);
        var name = "anit-seek-priority-" + Guid.NewGuid().ToString("N");
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), client.ConnectAsync(timeout.Token));
        var playerWriter = new StreamWriter(client, leaveOpen: true) { AutoFlush = true };
        typeof(MpvOnlinePlayer).GetField("writer", Private)!.SetValue(player, playerWriter);
        typeof(MpvOnlinePlayer).GetField("pipe", Private)!.SetValue(player, client);
        var readLoop = (Task)typeof(MpvOnlinePlayer).GetMethod("ReadLoopAsync", Private)!.Invoke(player, [client, timeout.Token])!;
        var commands = Task.Run(async () => {
            using var reader = new StreamReader(server, leaveOpen: true);
            using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
            try {
                while (await reader.ReadLineAsync(timeout.Token) is { } line) {
                    using var json = JsonDocument.Parse(line);
                    var id = json.RootElement.GetProperty("request_id").GetInt32();
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new { request_id = id, error = "success", data = 10 }));
                }
            } catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
        }, timeout.Token);
        typeof(MpvOnlinePlayer).GetField("bufferPlan", Private)!.SetValue(player, AniT.Core.OnlineBufferPlan.Initial with { CanPrepareIndex = true });
        var warmup = (Task)typeof(MpvOnlinePlayer).GetMethod("WarmIndexAsync", Private)!.Invoke(player, [address, timeout.Token])!;
        try {
            await backgroundStarted.Task.WaitAsync(timeout.Token);
            await player.SeekAsync(TimeSpan.FromSeconds(42), timeout.Token).WaitAsync(TimeSpan.FromSeconds(2));
            await backgroundCancelled.Task.WaitAsync(timeout.Token);
            await foregroundStarted.Task.WaitAsync(timeout.Token);
            await warmup.WaitAsync(timeout.Token);
            Assert.Equal(2, calls);
        } finally {
            timeout.Cancel(); player.Dispose();
            await readLoop; await commands;
        }
    }
}
