using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AniT.App;
using AniT.Core;
using AniT.Infrastructure;
using AniT.Player;

internal static class OnlinePlayerExtrasChecks
{
    public static int Run(Application app)
    {
        int result=1;var runner=new Window{Width=100,Height=100,Left=-20000,Top=-20000,ShowInTaskbar=false,ShowActivated=false};
        runner.Loaded+=async(_,_)=>{
            OnlinePlayerWindow? window=null;
            try {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(120));var token=timeout.Token;
                var fixture=Environment.GetEnvironmentVariable("ANIT_PLAYER_TRACK_FIXTURE")??throw new Exception("Set ANIT_PLAYER_TRACK_FIXTURE to a synthetic MKV.");
                var subtitle=Environment.GetEnvironmentVariable("ANIT_PLAYER_EXTERNAL_SUBTITLE")??throw new Exception("Set ANIT_PLAYER_EXTERNAL_SUBTITLE to a synthetic SRT.");
                var output=Environment.GetEnvironmentVariable("ANIT_PLAYER_EXTRAS_OUTPUT")??Path.Combine(Path.GetTempPath(),"AniT.Player.Extras",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
                var store=new PlaybackMomentStore(Path.Combine(output,"profile","moments.json"));var prefs=new OnlinePlayerPreferencesStore(Path.Combine(output,"profile","preferences.json"));
                var episode=Guid.NewGuid();bool allowIndex=false;
                await using var server=new OnlineStreamServer(File.OpenRead(fixture),Path.GetFileName(fixture));
                using var player=new MpvOnlinePlayer(Path.Combine(AppContext.BaseDirectory,"Player","MPV","mpv.exe"),(_,_)=>Task.FromResult(allowIndex));
                window=new OnlinePlayerWindow(player,"Teste autorizado dos recursos extras",prefs,store,episode){ShowActivated=false};window.Show();player.AttachVideoWindow(window.VideoHandle);
                await player.PlayStreamAsync(server.Address,null,MpcHcPlaybackOptions.Default,token);
                async Task Until(Func<bool> condition,[System.Runtime.CompilerServices.CallerLineNumber]int line=0){var watch=Stopwatch.StartNew();while(!condition()){if(watch.Elapsed.TotalSeconds>10)throw new Exception("Extra check timed out at line "+line);await Task.Delay(40,token);}}
                await Until(()=>player.Snapshot.VideoReady&&player.Snapshot.Position?.TotalSeconds>.5);
                // Freeze the UI sampler while exercising sustained waiting advice with a real engine.
                var ticker=(DispatcherTimer)typeof(OnlinePlayerWindow).GetField("refresh",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;ticker.Stop();
                var waiting=player.Snapshot with{Buffering=true,Paused=false,CacheSeconds=0};window.UpdateView(waiting);await Task.Delay(3200,token);window.UpdateView(waiting);
                if(((Image)window.FindName("AssistantFace")).Visibility!=Visibility.Visible)throw new Exception("Baki-Pi did not appear for sustained buffering");
                ((Button)window.FindName("AssistantAction")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Until(()=>player.Snapshot.Paused);
                window.UpdateView(player.Snapshot with{CacheSeconds=12});await Task.Delay(250,token);
                if(!player.Snapshot.Paused)throw new Exception("Reserve assistance must never auto-resume");
                ((Button)window.FindName("AssistantAction")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Until(()=>!player.Snapshot.Paused);
                window.UpdateView(player.Snapshot);if(((Image)window.FindName("AssistantFace")).Visibility!=Visibility.Collapsed)throw new Exception("Baki-Pi did not leave healthy playback quiet");
                ticker.Start();await window.SaveMomentAsync("Minha cena guardada",token);var moment=store.ForEpisode(episode).Single();
                if(!File.Exists(store.ThumbnailPath(moment.Id)))throw new Exception("Moment thumbnail missing");
                var future=new PlaybackMoment(Guid.NewGuid(),episode,70,"SPOILER FUTURO SECRETO");store.Add(future);File.Copy(store.ThumbnailPath(moment.Id),store.ThumbnailPath(future.Id));
                window.HandleInput("moments");await Until(()=>app.Windows.OfType<OnlineMomentsWindow>().Any());var moments=app.Windows.OfType<OnlineMomentsWindow>().Single();Flush();
                if(Descendants(moments).OfType<TextBlock>().Any(t=>t.Text.Contains("SPOILER FUTURO")))throw new Exception("Future note leaked into the visual tree");
                if(Descendants(moments).OfType<Image>().Count(i=>i.Source is not null)!=1)throw new Exception("Protected thumbnail was loaded");
                Descendants(moments).OfType<Button>().Single(b=>Equals(b.Content,"Revelar")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Flush();
                if(!Descendants(moments).OfType<TextBlock>().Any(t=>t.Text.Contains("SPOILER FUTURO")))throw new Exception("Explicit reveal failed");moments.Close();
                int layouts=0;
                foreach(var theme in new[]{AppearanceThemeMode.Dark,AppearanceThemeMode.Light})
                foreach(var scale in new[]{100,140}){
                    AppearanceManager.Apply(AniTSystemSettings.Default with{ThemeMode=theme,TextScalePercent=scale});window.HandleInput("moments");Flush();
                    moments=app.Windows.OfType<OnlineMomentsWindow>().Single();moments.Width=420;moments.Height=520;moments.UpdateLayout();
                    if(Descendants(moments).OfType<TextBlock>().Any(t=>t.Text.Contains("SPOILER FUTURO")))throw new Exception("Reveal leaked into another window session");
                    var label=(TextBlock)Descendants(moments).OfType<CheckBox>().Single().Content;
                    if(((SolidColorBrush)label.Foreground).Color!=((SolidColorBrush)moments.FindResource("AniT.Brush.TextPrimary")).Color)throw new Exception("Moment spoiler label has incorrect theme contrast");
                    CheckButtons(moments);Save(moments,Path.Combine(output,$"moments-{theme}-{scale}.png"));moments.Close();layouts++;
                }
                window.OpenSubtitleCenter();Flush();var center=app.Windows.OfType<OnlineSubtitleWindow>().Single();center.ImportFiles(new[]{subtitle});
                await Until(()=>player.Tracks.Tracks.Any(t=>t.Kind=="sub"&&t.External));var count=player.Tracks.Tracks.Count;
                await player.AddSubtitleAsync(subtitle,token);if(player.Tracks.Tracks.Count!=count)throw new Exception("Repeated import created a duplicate subtitle");
                var ass=Path.Combine(output,"original.ass");
                await File.WriteAllTextAsync(ass,"""
                [Script Info]
                ScriptType: v4.00+
                PlayResX: 320
                PlayResY: 180
                [V4+ Styles]
                Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
                Style: Default,Arial,20,&H000000FF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,1,0,8,10,10,10,1
                [Events]
                Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
                Dialogue: 0,0:00:00.00,0:01:15.00,Default,,0,0,0,,ASS ORIGINAL
                """,token);
                await player.AddSubtitleAsync(ass,token);await Until(()=>player.Tracks.Tracks.Count(t=>t.External)==2);
                Descendants(center).OfType<Slider>().Single(s=>s.Minimum==16).Value=64;Descendants(center).OfType<Slider>().Single(s=>s.Maximum==100).Value=90;Descendants(center).OfType<Slider>().Single(s=>s.Maximum==6).Value=4;
                Descendants(center).OfType<CheckBox>().Single().IsChecked=true;
                Descendants(center).OfType<ComboBox>().Single(c=>c.Items.Contains("#FFF176")).SelectedItem="#FFF176";
                Descendants(center).OfType<Button>().Single(b=>Equals(b.Content,"Aplicar ajustes")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(()=>prefs.Load().SubtitleAppearance is{FontSize:64,Position:90,Outline:4,OverrideAss:true});
                if((await player.CommandAsync(["get_property","sub-ass-override"],token)).GetString()!="force")throw new Exception("ASS override was not applied");
                allowIndex=true;await player.SeekAsync(TimeSpan.FromSeconds(40),token);await Until(()=>player.SeekIndexReady);
                if(!player.Tracks.Tracks.Any(t=>t.External&&t.Id==player.Tracks.SubtitleId))throw new Exception("Imported subtitle was lost during index reload");
                if((await player.CommandAsync(["get_property","sub-font-size"],token)).GetDouble()!=64)throw new Exception("Subtitle style lost during seek");
                foreach(var theme in new[]{AppearanceThemeMode.Dark,AppearanceThemeMode.Light})
                foreach(var scale in new[]{100,140}){
                    AppearanceManager.Apply(AniTSystemSettings.Default with{ThemeMode=theme,TextScalePercent=scale});center.Width=420;center.Height=560;center.UpdateLayout();Flush();
                    Save(center,Path.Combine(output,$"subtitles-{theme}-{scale}.png"));
                    var scroll=Descendants(center).OfType<ScrollViewer>().First();scroll.ScrollToBottom();Flush();
                    var preview=Descendants(center).OfType<TextBlock>().Single(t=>t.Text=="Prévia do texto simples");if(((SolidColorBrush)preview.Foreground).Color!=(Color)ColorConverter.ConvertFromString("#FFF176"))throw new Exception("Subtitle preview lost its chosen color");
                    Save(center,Path.Combine(output,$"appearance-{theme}-{scale}.png"));scroll.ScrollToTop();Flush();layouts++;
                }
                Descendants(center).OfType<Button>().Single(b=>Equals(b.Content,"Restaurar padrão")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(()=>prefs.Load().SubtitleAppearance is{OverrideAss:false,FontSize:48});
                var restored=await player.CommandAsync(["get_property","sub-ass-override"],token);
                if(restored.ValueKind!=JsonValueKind.False && !(restored.ValueKind==JsonValueKind.String && restored.GetString()=="no"))throw new Exception("Original ASS style was not restored");
                await player.CommandAsync(["screenshot-to-file",Path.Combine(output,"ass-original-render.png"),"subtitles"],token);
                foreach(var id in player.Tracks.Tracks.Where(t=>t.External).Select(t=>t.Id).ToArray())await player.RemoveSubtitleAsync(id,token);
                if(player.Tracks.Tracks.Any(t=>t.External)||!File.Exists(subtitle)||!File.Exists(ass))throw new Exception("External removal deleted source or left track behind");
                center.Close();player.Dispose();
                await File.WriteAllTextAsync(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{baki_contextual=true,no_auto_resume=true,moment_capture=true,spoiler_note_hidden=true,spoiler_thumbnail_not_loaded=true,explicit_reveal=true,reveal_session_scoped=true,subtitle_import=true,duplicate_avoided=true,style_applied=true,external_survives_seek=true,original_ass_restored=true,source_preserved=true,layouts},new JsonSerializerOptions{WriteIndented=true}),token);
                Console.WriteLine($"Player extras: Baki-Pi, reserve, capture, spoiler protection, subtitle import/style/seek/restore and {layouts} layouts passed. {output}");result=0;
            }catch(Exception error){Console.Error.WriteLine(error);}
            finally{window?.CloseFromPlayer();runner.Close();app.Shutdown();}
        };app.Run(runner);return result;
    }
    static void Flush()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
    static IEnumerable<DependencyObject> Descendants(DependencyObject root){for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var next in Descendants(child))yield return next;}}
    static void CheckButtons(Window window){foreach(var button in Descendants(window).OfType<Button>().Where(b=>b.IsVisible)){var bounds=button.TransformToAncestor(window).TransformBounds(new Rect(button.RenderSize));bool scroll=false;DependencyObject? parent=button;while(parent is not null&&parent!=window){if(parent is ScrollViewer)scroll=true;parent=VisualTreeHelper.GetParent(parent);}
        if(bounds.Left<0||bounds.Right>window.ActualWidth+1||(!scroll&&bounds.Bottom>window.ActualHeight+1))throw new Exception("Tool window action clipped: "+button.Content+" "+bounds);}}
    static void Save(Window window,string path){var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);png.Save(file);}
}
