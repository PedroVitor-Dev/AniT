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

internal static class OnlinePlayerTrackChecks
{
    sealed class GateStream(Stream inner):Stream
    {
        TaskCompletionSource? gate;
        public TaskCompletionSource Blocked=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Block(){Blocked=new(TaskCreationOptions.RunContinuationsAsynchronously);gate=new(TaskCreationOptions.RunContinuationsAsynchronously);}
        public void Release(){var value=gate;gate=null;value?.TrySetResult();}
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default){if(gate is{} pending){Blocked.TrySetResult();await pending.Task.WaitAsync(token);}return await inner.ReadAsync(buffer,token);}
        public override bool CanRead=>true;public override bool CanSeek=>true;public override bool CanWrite=>false;public override long Length=>inner.Length;
        public override long Position{get=>inner.Position;set=>inner.Position=value;}
        public override long Seek(long offset,SeekOrigin origin)=>inner.Seek(offset,origin);
        public override int Read(byte[] buffer,int offset,int count)=>inner.Read(buffer,offset,count);
        public override void Flush(){}public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
        protected override void Dispose(bool disposing){Release();if(disposing)inner.Dispose();base.Dispose(disposing);}
    }
    public static int Run(Application app)
    {
        int result=1;
        var runner=new Window{Width=100,Height=100,Left=-20000,Top=-20000,ShowActivated=false,ShowInTaskbar=false};
        runner.Loaded+=async(_,_)=>{
            OnlinePlayerWindow? window=null;ContextMenu? menu=null;
            try {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(100));var token=timeout.Token;
                var fixture=Environment.GetEnvironmentVariable("ANIT_PLAYER_TRACK_FIXTURE")??throw new Exception("Set ANIT_PLAYER_TRACK_FIXTURE to a synthetic MKV with two audio and subtitle tracks.");
                var output=Environment.GetEnvironmentVariable("ANIT_PLAYER_TRACK_OUTPUT")??Path.Combine(Path.GetTempPath(),"AniT.Player.Tracks",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
                var store=new OnlinePlayerPreferencesStore(Path.Combine(output,"profile","player-preferences.json"));
                store.Save(new(Volume:44));
                var source=new GateStream(File.OpenRead(fixture));
                await using var server=new OnlineStreamServer(source,Path.GetFileName(fixture));
                bool allowIndex=false;
                using var player=new MpvOnlinePlayer(Path.Combine(AppContext.BaseDirectory,"Player","MPV","mpv.exe"),(_,_)=>Task.FromResult(allowIndex));
                window=new OnlinePlayerWindow(player,"Teste autorizado · Duas faixas e duas legendas",store){ShowActivated=false};window.Show();player.AttachVideoWindow(window.VideoHandle);
                await player.PlayStreamAsync(server.Address,null,new(1,false,"ja","pt-br"),token);
                async Task Until(Func<bool> check,[System.Runtime.CompilerServices.CallerLineNumber]int line=0){var watch=Stopwatch.StartNew();while(!check()){if(watch.Elapsed.TotalSeconds>10)throw new Exception("Track check timed out at line "+line+" state="+JsonSerializer.Serialize(player.Snapshot)+" tracks="+JsonSerializer.Serialize(player.Tracks)+" index="+player.SeekIndexReady);await Task.Delay(40,token);}}
                await Until(()=>player.Snapshot.VideoReady&&player.Tracks.Tracks.Count==4);
                if(Math.Abs(player.Snapshot.Volume-44)>.01)throw new Exception("Saved startup volume did not apply");
                var japanese=player.Tracks.Tracks.Single(t=>t.Kind=="audio"&&t.Language=="jpn");var english=player.Tracks.Tracks.Single(t=>t.Kind=="audio"&&t.Language=="eng");
                var portuguese=player.Tracks.Tracks.Single(t=>t.Kind=="sub"&&t.Language=="por");var signs=player.Tracks.Tracks.Single(t=>t.Kind=="sub"&&t.Language=="eng");
                await Until(()=>player.Tracks.AudioId==japanese.Id&&player.Tracks.SubtitleId==portuguese.Id);
                static void Pick(ContextMenu popup,string kind,int? id){popup.Items.OfType<MenuItem>().Single(i=>i.Tag is OnlinePlayerWindow.TrackChoice choice&&choice.Kind==kind&&choice.Id==id).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));}
                Pick(window.BuildTrackMenu("audio"),"audio",english.Id);await Until(()=>player.Tracks.AudioId==english.Id);
                Pick(window.BuildTrackMenu("sub"),"sub",signs.Id);await Until(()=>player.Tracks.SubtitleId==signs.Id);
                Pick(window.BuildTrackMenu("sub"),"sub",null);await Until(()=>player.Tracks.SubtitleId is null);
                await player.SetPlaybackSpeedAsync(1.25,token);await player.SetSubtitleDelayAsync(-.2,token);
                await Until(()=>Math.Abs(player.Tracks.Speed-1.25)<.001&&Math.Abs(player.Tracks.SubtitleDelay+.2)<.001);
                // Block the reopen itself, then pause while the engine is internally paused for indexing.
                allowIndex=true;source.Block();var reload=player.SeekAsync(TimeSpan.FromSeconds(20),token);
                await source.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(10),token);
                await player.PauseAsync(token);source.Release();await reload;
                await Until(()=>player.SeekIndexReady&&player.Snapshot.Paused&&player.Tracks.AudioId==english.Id&&player.Tracks.SubtitleId is null);
                if(Math.Abs((await player.CommandAsync(["get_property","sub-delay"],token)).GetDouble()+.2)>.001||Math.Abs((await player.CommandAsync(["get_property","speed"],token)).GetDouble()-1.25)>.001)throw new Exception("Seek reload reset delay or speed");
                Pick(window.BuildTrackMenu("sub"),"sub",portuguese.Id);await Until(()=>player.Tracks.SubtitleId==portuguese.Id);
                var options=window.BuildOptionsMenu();var speed=(MenuItem)options.Items[0];
                speed.Items.OfType<MenuItem>().Single(i=>i.Header is TextBlock t&&t.Text=="1,25×" || i.Header is TextBlock u&&u.Text=="1.25×").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await Until(()=>Math.Abs(player.Tracks.Speed-1.25)<.001);
                await player.SetSubtitleDelayAsync(-.2,token);await Until(()=>Math.Abs(player.Tracks.SubtitleDelay+.2)<.001);
                // Native right-click routes to a WPF popup above the embedded video.
                var native=GetWindow(window.VideoHandle,5);PostMessage(native,0x204,new IntPtr(2),new IntPtr(30|(30<<16)));PostMessage(native,0x205,IntPtr.Zero,new IntPtr(30|(30<<16)));
                await Until(()=>window.ActiveMenu?.IsOpen==true);await player.CommandAsync(["keypress","ESC"],token);await Until(()=>window.ActiveMenu is null);
                player.Resume();await Until(()=>!player.Snapshot.Paused);
                var layouts=0;
                foreach(var theme in new[]{AppearanceThemeMode.Dark,AppearanceThemeMode.Light})
                foreach(var scale in new[]{100,140})
                foreach(var kind in new[]{"audio","sub","menu"}) {
                    AppearanceManager.Apply(AniTSystemSettings.Default with{ThemeMode=theme,TextScalePercent=scale});
                    window.Width=640;window.Height=560;
                    window.HandleInput(kind=="sub"?"subtitles":kind);await Until(()=>window.ActiveMenu?.IsOpen==true);menu=window.ActiveMenu!;
                    menu.UpdateLayout();window.UpdateView(player.Snapshot,10);
                    if(!window.ControlsVisible)throw new Exception("Open popup allowed controls to hide");
                    if(menu.ActualHeight>SystemParameters.WorkArea.Height||menu.ActualWidth>SystemParameters.WorkArea.Width)throw new Exception("Track popup exceeds work area");
                    if(scale==100){var bmp=new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth),(int)Math.Ceiling(menu.ActualHeight),96,96,PixelFormats.Pbgra32);bmp.Render(menu);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using var file=File.Create(Path.Combine(output,$"menu-{theme}-{kind}.png"));png.Save(file);}
                    if(kind=="menu") {
                        var submenu=menu.Items.OfType<MenuItem>().Single(i=>i.Header is TextBlock label&&label.Text=="Áudio");
                        submenu.IsSubmenuOpen=true;await Task.Delay(80,token);submenu.ApplyTemplate();
                        var popup=(System.Windows.Controls.Primitives.Popup)submenu.Template.FindName("PART_Popup",submenu);
                        if(!popup.IsOpen||popup.Child is not FrameworkElement child||child.ActualWidth<=0)throw new Exception("Context submenu failed to open");
                        submenu.IsSubmenuOpen=false;
                    }
                    menu.IsOpen=false;menu=null;layouts++;
                }
                await player.SetVolumeAsync(61,token);await Until(()=>player.Snapshot.Volume==61);
                window.Close();
                var saved=store.Load();if(saved.AudioLanguage!="en"||saved.SubtitleLanguage!="pt"||saved.Volume!=61||saved.Speed!=1.25)throw new Exception("Profile language/volume/speed preferences did not persist: "+JsonSerializer.Serialize(saved));
                var nextFixture=Environment.GetEnvironmentVariable("ANIT_PLAYER_NEXT_FIXTURE")??fixture;
                await using var nextServer=new OnlineStreamServer(File.OpenRead(nextFixture),Path.GetFileName(nextFixture));
                window=new OnlinePlayerWindow(player,"Próximo episódio autorizado",store){ShowActivated=false};window.Show();player.AttachVideoWindow(window.VideoHandle);
                await player.PlayStreamAsync(nextServer.Address,null,new(saved.Speed??1,false,saved.AudioLanguage!,saved.SubtitleLanguage!),token);
                await Until(()=>player.Snapshot.VideoReady&&player.Tracks.Tracks.Any(t=>t.Kind=="audio"&&t.Language=="eng"&&t.Id==player.Tracks.AudioId)&&player.Tracks.Tracks.Any(t=>t.Kind=="sub"&&t.Language=="por"&&t.Id==player.Tracks.SubtitleId)&&player.Snapshot.Volume==61&&Math.Abs(player.Tracks.Speed-1.25)<.001);
                var remapped=player.Tracks.AudioId!=english.Id&&player.Tracks.SubtitleId!=portuguese.Id;
                if(nextFixture!=fixture&&!remapped)throw new Exception("Remapped fixture must use different track IDs");
                if(player.Tracks.SubtitleDelay!=0)throw new Exception("Per-episode subtitle delay leaked into next episode");
                player.Dispose();
                await File.WriteAllTextAsync(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{audio_selection=true,subtitle_selection=true,subtitles_off_survive_index_reload=true,user_pause_during_reload=true,context_menu=true,profile_preferences=true,speed=true,subtitle_delay=true,next_episode=true,next_episode_remapped=remapped,layouts},new JsonSerializerOptions{WriteIndented=true}),token);
                Console.WriteLine($"Online tracks: native selection, off/reload, user pause, right-click, preferences, speed/delay, next episode and {layouts} popup layouts passed. {output}");result=0;
            }catch(Exception error){Console.Error.WriteLine(error);}
            finally{if(menu is not null)menu.IsOpen=false;window?.CloseFromPlayer();runner.Close();app.Shutdown();}
        };app.Run(runner);return result;
    }
    [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr parent,uint command);
    [DllImport("user32.dll")]static extern bool PostMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
}
