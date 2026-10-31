using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using AniT.App;
using AniT.Infrastructure;
using AniT.Player;

internal static class OnlinePlayerMouseChecks
{
    public static int Run(Application app)
    {
        int result=1;
        var runner=new Window{Width=20,Height=20,Left=-20000,Top=-20000,ShowInTaskbar=false,ShowActivated=false};
        runner.Loaded+=async(_,_)=>{
            OnlinePlayerWindow? window=null;
            try {
                var fixture=Environment.GetEnvironmentVariable("ANIT_PLAYER_HOST_FIXTURE")??throw new Exception("Authorized fixture required");
                await using var server=new OnlineStreamServer(File.OpenRead(fixture),Path.GetFileName(fixture));
                using var player=new MpvOnlinePlayer(Path.Combine(AppContext.BaseDirectory,"Player","MPV","mpv.exe"));
                window=new OnlinePlayerWindow(player,"Teste do mouse · vídeo sintético autorizado");window.Show();player.AttachVideoWindow(window.VideoHandle);
                var video=(MpvVideoHost)window.FindName("Video");
                video.PointerInput+=(_,p)=>{if(p.Action!="pointer")Console.WriteLine($"NATIVE_POINTER {p.Action} {p.Timestamp} {p.X},{p.Y}");};
                using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));
                await player.PlayStreamAsync(server.Address,null,MpcHcPlaybackOptions.Default,timeout.Token);
                await player.SetPlaybackSpeedAsync(.5,timeout.Token);
                Console.WriteLine("INTERACTIVE_READY");
                bool entered=false,exited=false,hidden=false,restored=false,paused=false; string? lastState=null;
                while(window.IsVisible&&!timeout.IsCancellationRequested){
                    var cursor=new CursorInfo{Size=Marshal.SizeOf<CursorInfo>()};GetCursorInfo(ref cursor);
                    entered |= window.IsFullscreen; exited |= entered&&!window.IsFullscreen;
                    hidden |= window.VideoPointerHidden&&(cursor.Flags&1)==0;
                    restored |= hidden&&!window.VideoPointerHidden&&(cursor.Flags&1)!=0;
                    paused |= player.Snapshot.Paused;
                    var state=$"MOUSE_STATE fullscreen={window.IsFullscreen} chrome={window.ControlsVisible} hidden={window.VideoPointerHidden} cursor_visible={(cursor.Flags&1)!=0} paused={player.Snapshot.Paused} active={window.IsActive} over={video.IsPointerOver}";
                    if(state!=lastState){Console.WriteLine(state);lastState=state;}
                    await Task.Delay(1000,timeout.Token);
                }
                result=entered&&exited&&hidden&&restored&&paused?0:1;
                Console.WriteLine($"INTERACTIVE_RESULT entered={entered} exited={exited} hidden={hidden} restored={restored} single_click_pause={paused}");
            }catch(Exception error){Console.Error.WriteLine(error);}
            finally{window?.CloseFromPlayer();runner.Close();app.Shutdown();}
        };app.Run(runner);return result;
    }
    [StructLayout(LayoutKind.Sequential)]struct CursorInfo{public int Size,Flags;public IntPtr Cursor;public int X,Y;}
    [DllImport("user32.dll")]static extern bool GetCursorInfo(ref CursorInfo info);
}
