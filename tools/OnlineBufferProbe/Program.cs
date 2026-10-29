using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using System.Collections.Concurrent;
using AniT.Core;
using AniT.Player;
using AniT.Infrastructure;

if(args.Length!=2)throw new ArgumentException("Use: <new output folder containing fixture.mp4> <mpv.exe>");
var output=Path.GetFullPath(args[0]);
if(File.Exists(Path.Combine(output,"native-cache.json"))||File.Exists(Path.Combine(output,"native-playback.jsonl")))
    throw new IOException("Use a new output folder; existing results are not overwritten.");
var clock=Stopwatch.StartNew();
await using var source=new VariableSource(await File.ReadAllBytesAsync(Path.Combine(output,"fixture.mp4")),clock);
await using var http=new OnlineStreamServer(source,"fixture.mp4");
using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(75));
var name="anit-native-buffer-"+Guid.NewGuid().ToString("N");
var exe=Path.GetFullPath(args[1]);
var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true};
foreach(var item in new[]{"--no-config","--no-terminal","--idle=yes","--force-window=no","--vo=null","--ao=null","--pause=yes","--cache=yes","--cache-pause-initial=no","--cache-pause-wait=1","--cache-secs=10","--demuxer-readahead-secs=10","--demuxer-max-bytes=33554432","--demuxer-max-back-bytes=8388608","--network-timeout=120","--input-ipc-server=\\\\.\\pipe\\"+name})start.ArgumentList.Add(item);
var owned=Process.Start(start)!;
using var player=new MpvOnlinePlayer(exe);
var hidden=BindingFlags.NonPublic|BindingFlags.Instance;
void Set(string field,object value)=>typeof(MpvOnlinePlayer).GetField(field,hidden)!.SetValue(player,value);
Set("process",owned);Set("lifetime",new CancellationTokenSource());Set("fileLifetime",CancellationTokenSource.CreateLinkedTokenSource(timeout.Token));
await using var pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous);await pipe.ConnectAsync(5000,timeout.Token);
Set("pipe",pipe);Set("writer",new StreamWriter(pipe,leaveOpen:true){AutoFlush=true});
var events=new ConcurrentBag<OnlinePlaybackMetric>();var trace=new OnlinePlaybackTrace(events.Add);Set("telemetry",trace);
var loaded=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);Set("loaded",loaded);Set("awaitingFileStart",true);
var read=(Task)typeof(MpvOnlinePlayer).GetMethod("ReadLoopAsync",hidden)!.Invoke(player,[pipe,timeout.Token])!;
var count=0;foreach(var property in new[]{"time-pos","duration","pause","paused-for-cache","video-out-params","demuxer-cache-state","demuxer-cache-duration","speed"})await player.CommandAsync(["observe_property",++count,property],timeout.Token);
await player.CommandAsync(["loadfile",http.Address.AbsoluteUri,"replace"],timeout.Token);await loaded.Task.WaitAsync(timeout.Token);
await player.CommandAsync(["set_property","pause",false],timeout.Token);
var control=(Task)typeof(MpvOnlinePlayer).GetMethod("ControlBufferAsync",hidden)!.Invoke(player,[0,timeout.Token])!;
var samples=new List<object>();
try {
    for(var second=0;second<50;second++) {
        await Task.Delay(1000,timeout.Token);
        if(second==44)await player.PauseAsync(timeout.Token);
        if(second==47)await player.CommandAsync(["set_property","pause",false],timeout.Token);
        var state=await player.CommandAsync(["get_property","demuxer-cache-state"],timeout.Token);
        var pause=await player.CommandAsync(["get_property","pause"],timeout.Token);
        if(second is >=44 and <47 && !pause.GetBoolean())throw new InvalidOperationException("Adaptive controller overrode manual pause");
        var wait=await player.CommandAsync(["get_property","cache-pause-wait"],timeout.Token);
        samples.Add(new {second,wall_seconds=clock.Elapsed.TotalSeconds,paused=pause.GetBoolean(),resume_seconds=wait.GetDouble(),cache=state.Clone()});
    }
} finally {
    timeout.Cancel();await control;trace.Finish("controlled_native_buffer_complete");player.Dispose();await read;
    await File.WriteAllLinesAsync(Path.Combine(output,"native-playback.jsonl"),events.OrderBy(e=>e.Sequence).Select(e=>JsonSerializer.Serialize(e)));
    await File.WriteAllTextAsync(Path.Combine(output,"native-cache.json"),JsonSerializer.Serialize(samples));
}
var result=new {samples=samples.Count,first_frame_signal=events.Any(e=>e.Name=="first_video_frame_signal"),option_updates=events.Count(e=>e.Name=="buffer_option_applied"),rebuffer_starts=events.Count(e=>e.Name=="rebuffer_start"),manual_pause_preserved=true};
await File.WriteAllTextAsync(Path.Combine(output,"native-result.json"),JsonSerializer.Serialize(result));
Console.WriteLine(JsonSerializer.Serialize(result));
sealed class VariableSource(byte[] data,Stopwatch clock):MemoryStream(data) {
    public override async ValueTask<int> ReadAsync(Memory<byte> bytes,CancellationToken token=default) {
        var count=(int)Math.Min(bytes.Length,Length-Position);double remaining=count;
        while(remaining>0){var at=clock.Elapsed.TotalSeconds;var rate=at<15?400000:at<27?35000:at<40?0:600000;await Task.Delay(50,token);remaining-=rate*.05;}
        return await base.ReadAsync(bytes[..count],token);
    }
}
