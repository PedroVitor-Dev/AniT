using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AniT.App;
using AniT.Infrastructure;
using AniT.Player;

internal static class OnlinePlayerHostChecks
{
    public static int Run(Application app)
    {
        int result = 1;
        var runner = new Window { Width=100,Height=100,ShowInTaskbar=false,ShowActivated=false,Left=-20000,Top=-20000 };
        runner.Loaded += async (_,_) => {
            OnlinePlayerWindow? window = null;
            try {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(80)); var token=timeout.Token;
                var output=Environment.GetEnvironmentVariable("ANIT_PLAYER_HOST_OUTPUT") ?? Path.Combine(Path.GetTempPath(),"AniT.Player.Host");Directory.CreateDirectory(output);
                var fixture=Environment.GetEnvironmentVariable("ANIT_PLAYER_HOST_FIXTURE") ?? throw new Exception("Set ANIT_PLAYER_HOST_FIXTURE to an authorized video.");
                await using var server = new OnlineStreamServer(File.OpenRead(fixture),Path.GetFileName(fixture));
                using var player = new MpvOnlinePlayer(Path.Combine(AppContext.BaseDirectory,"Player","MPV","mpv.exe"));
                window = new OnlinePlayerWindow(player,"Vídeo sintético autorizado · Episódio 01") { ShowActivated=false };
                window.Show(); player.AttachVideoWindow(window.VideoHandle);
                player.UserInput+=(_,action)=>Console.WriteLine("Native input: "+action);
                await player.PlayStreamAsync(server.Address,null,MpcHcPlaybackOptions.Default,token);
                async Task Until(Func<bool> predicate, [System.Runtime.CompilerServices.CallerLineNumber] int line=0) {
                    var watch=Stopwatch.StartNew();
                    while(!predicate()){if(watch.Elapsed.TotalSeconds>8)throw new Exception("Native host wait timed out at line "+line);await Task.Delay(50,token);}
                }
                await Until(()=>player.Snapshot.VideoReady && player.Snapshot.Position?.TotalSeconds>0.5);
                if((await player.CommandAsync(["get_property","osc"],token)).GetBoolean())throw new Exception("Native OSC must be disabled");
                var native=GetWindow(window.VideoHandle,5);
                if(native==IntPtr.Zero||GetParent(native)!=window.VideoHandle)throw new Exception("mpv did not attach a native video child");
                async Task Key(string value) { await player.CommandAsync(["keypress",value],token); }
                // Native input goes through mpv's real input section, IPC and AniT actions.
                ((Button)window.FindName("PlayAction")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(()=>player.Snapshot.Paused);
                var pausedAt=player.Snapshot.Position!.Value.TotalSeconds;await Task.Delay(400,token);
                if(Math.Abs(player.Snapshot.Position!.Value.TotalSeconds-pausedAt)>0.15)throw new Exception("Pause did not hold video");
                PostMessage(native,0x201,new IntPtr(1),new IntPtr(40 | (40 << 16)));PostMessage(native,0x202,IntPtr.Zero,new IntPtr(40 | (40 << 16)));
                await Until(()=>!player.Snapshot.Paused);
                PostMessage(native,0x201,new IntPtr(1),new IntPtr(40 | (40 << 16)));PostMessage(native,0x202,IntPtr.Zero,new IntPtr(40 | (40 << 16)));
                PostMessage(native,0x201,new IntPtr(1),new IntPtr(40 | (40 << 16)));PostMessage(native,0x202,IntPtr.Zero,new IntPtr(40 | (40 << 16)));
                await Until(()=>window.IsFullscreen);
                await Task.Delay(System.Windows.Forms.SystemInformation.DoubleClickTime+150,token);
                if(player.Snapshot.Paused)throw new Exception("Double-click also paused playback");
                await Key("ESC");await Until(()=>!window.IsFullscreen);
                await Key("RIGHT");await Until(()=>player.Snapshot.Position?.TotalSeconds>=10);
                await Key("LEFT");await Until(()=>player.Snapshot.Position?.TotalSeconds<8);
                await Key("m");await Until(()=>player.Snapshot.Muted);
                await Key("DOWN");await Until(()=>player.Snapshot.Volume<=95);
                ((Slider)window.FindName("Volume")).Value=55;await Until(()=>Math.Abs(player.Snapshot.Volume-55)<0.01);
                var timeline=(Slider)window.FindName("Timeline");timeline.Value=20;
                timeline.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),Environment.TickCount,System.Windows.Input.Key.Right){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyUpEvent});
                await Until(()=>player.Snapshot.Position?.TotalSeconds>=20);
                window.HandleInput("forward");window.HandleInput("forward");
                await Until(()=>player.Snapshot.Position?.TotalSeconds>=39.8 && player.Snapshot.Position?.TotalSeconds<44);
                await Key("SPACE");await Until(()=>player.Snapshot.Paused);
                await Key("SPACE");await Until(()=>!player.Snapshot.Paused);
                // Check actual window chrome in both themes and larger text at 1366 and compact size.
                foreach(var theme in new[]{AppearanceThemeMode.Dark,AppearanceThemeMode.Light})
                foreach(var size in new[]{(1100d,700d),(640d,540d)})
                foreach(var scale in new[]{100,140}) {
                    AppearanceManager.Apply(AniTSystemSettings.Default with{ThemeMode=theme,TextScalePercent=scale});
                    window.Width=size.Item1;window.Height=size.Item2;window.HandleInput("pointer");window.UpdateView(player.Snapshot);window.UpdateLayout();
                    foreach(var button in Descendants(window).OfType<Button>().Where(b=>b.IsVisible)) {
                        var bounds=button.TransformToAncestor(window).TransformBounds(new Rect(button.RenderSize));
                        if(bounds.Left<0||bounds.Right>window.ActualWidth||bounds.Bottom>window.ActualHeight)throw new Exception("Native host chrome clipped");
                    }
                    if(scale==100) {
                        // RenderTargetBitmap excludes native GPU surfaces; frame checks below use mpv output.
                        var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);
                        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,$"host-{theme}-{size.Item1}.png"));encoder.Save(file);
                    }
                }
                ((Button)window.FindName("PlayAction")).Focus();window.HandleInput("pointer");
                window.UpdateView(player.Snapshot,10);
                if(window.ControlsVisible)throw new Exception("Playing controls did not autohide");
                await Key("MOUSE_MOVE");await Until(()=>window.ControlsVisible);
                await player.PauseAsync(token);await Until(()=>player.Snapshot.Paused);
                window.UpdateView(player.Snapshot,10);
                if(!window.ControlsVisible)throw new Exception("Paused controls were hidden");
                var shot=Path.Combine(output,"native-frame.png");
                await player.CommandAsync(["screenshot-to-file",shot,"video"],token);
                if(!File.Exists(shot)||new FileInfo(shot).Length<100)throw new Exception("Native renderer did not produce a frame");
                var closed=new TaskCompletionSource<global::AniT.Core.PlaybackPositionChangedEventArgs>();player.PlaybackClosed+=(_,e)=>closed.TrySetResult(e);
                window.Close();var checkpoint=await closed.Task.WaitAsync(token);
                if(!checkpoint.IsPositionKnown||player.IsRunning)throw new Exception("Window close must preserve checkpoint and terminate owned mpv");
                // Same instance can reopen after user close, without retaining a dead HWND.
                window=new OnlinePlayerWindow(player,"Reabertura autorizada"){ShowActivated=false};window.Show();player.AttachVideoWindow(window.VideoHandle);
                await player.PlayStreamAsync(server.Address,null,MpcHcPlaybackOptions.Default,token);
                await Until(()=>player.Snapshot.VideoReady&&player.Snapshot.Position?.TotalSeconds>0.5);
                player.Dispose();await Until(()=>!window.IsVisible);
                await File.WriteAllTextAsync(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{embedded=true,first_frame=true,native_input=true,single_click_pause=true,double_click_preserves_pause=true,fullscreen_restore=true,seek=true,rapid_seek=true,mute=true,volume=true,autohide=true,checkpoint=true,reopen=true,layouts=8},new JsonSerializerOptions{WriteIndented=true}),token);
                Console.WriteLine("Online native host: embedding, first frame, native input, pause, double-click, fullscreen, seek, volume, mute, autohide, close/checkpoint, reopen and 8 layouts passed. "+output);
                result=0;
            } catch(Exception error){Console.Error.WriteLine(error);}
            finally{window?.CloseFromPlayer();runner.Close();app.Shutdown();}
        };
        app.Run(runner);return result;
    }
    static IEnumerable<DependencyObject> Descendants(DependencyObject root){for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var item in Descendants(child))yield return item;}}
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr parent,uint command);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr child);
}
