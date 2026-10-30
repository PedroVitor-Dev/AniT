using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using AniT.Infrastructure;
using AniT.Player;

namespace AniT.App;

internal partial class OnlinePlayerWindow
{
    OnlinePlayerPreferencesStore? preferencesStore;
    OnlinePlayerPreferences preferences=new();
    readonly DispatcherTimer preferenceSave=new(){Interval=TimeSpan.FromMilliseconds(600)};
    readonly System.Diagnostics.Stopwatch cachePaint=System.Diagnostics.Stopwatch.StartNew();
    bool preferencesDirty;
    ContextMenu? activeMenu;
    internal ContextMenu? ActiveMenu=>activeMenu;
    internal record TrackChoice(string Kind,int? Id);
    void InitializePreferences(OnlinePlayerPreferencesStore? store)
    {
        preferencesStore=store;preferences=store?.Load()??new();
        if(store is not null){player.InitialVolume=preferences.Volume;player.InitialMuted=preferences.Muted;}
        preferenceSave.Tick+=(_,_)=>SavePreferences();
    }
    void QueueSave()
    {
        if(preferencesStore is null)return;
        preferencesDirty=true;preferenceSave.Stop();preferenceSave.Start();
    }
    void CaptureVolumePreferences(OnlinePlayerSnapshot state)
    {
        if(state.VideoReady&&(preferences.Volume!=state.Volume||preferences.Muted!=state.Muted)) {
            preferences=preferences with{Volume=state.Volume,Muted=state.Muted};QueueSave();
        }
    }
    void SavePreferences()
    {
        preferenceSave.Stop();
        if(preferencesStore is null||!preferencesDirty)return;
        try {preferencesStore.Save(preferences);preferencesDirty=false;}
        catch(Exception error)when(error is System.IO.IOException or UnauthorizedAccessException){
            if(!closing){showError=true;Status.Text="A escolha foi aplicada, mas não foi possível salvar a preferência.";}
        }
    }
    void RememberTrack(OnlineMediaTrack? track,bool subtitle=false,bool off=false)
    {
        if(!preferences.RememberLanguages)return;
        var language=off?"off":OnlinePlayerPreferences.NormalizeLanguage(track?.Language);
        if(language is null)return;
        preferences=subtitle?preferences with{SubtitleLanguage=language}:preferences with{AudioLanguage=language};
        QueueSave();SavePreferences();
    }
    static string ShortLanguage(OnlineMediaTrack? track)=>track is null?"Sem faixa":OnlineMediaTrack.LanguageName(track.Language);
    void RefreshTrackControls(OnlinePlayerSnapshot state)
    {
        var tracks=player.Tracks;
        AudioAction.IsEnabled=state.VideoReady&&tracks.Tracks.Any(t=>t.Kind=="audio");
        SubtitleAction.IsEnabled=state.VideoReady&&tracks.Tracks.Any(t=>t.Kind=="sub");
        OptionsAction.IsEnabled=state.VideoReady;
        AudioLabel.Text="Áudio · "+ShortLanguage(tracks.Tracks.FirstOrDefault(t=>t.Kind=="audio"&&t.Id==tracks.AudioId));
        SubtitleLabel.Text=tracks.SubtitleId is null?"Legendas · Desligadas":"Legendas · "+ShortLanguage(tracks.Tracks.FirstOrDefault(t=>t.Kind=="sub"&&t.Id==tracks.SubtitleId));
        AudioAction.ToolTip=AudioLabel.Text+" (A)";SubtitleAction.ToolTip=SubtitleLabel.Text+" (S)";
        System.Windows.Automation.AutomationProperties.SetName(AudioAction,AudioLabel.Text);
        System.Windows.Automation.AutomationProperties.SetName(SubtitleAction,SubtitleLabel.Text);
        CaptureVolumePreferences(state);
        if(cachePaint.ElapsedMilliseconds>=1000){DrawCacheRail();cachePaint.Restart();}
        var target=dragging||pendingSeekPosition is not null?Timeline.Value:state.Position?.TotalSeconds??0;
        Timeline.ToolTip=Format(TimeSpan.FromSeconds(target))+" · "+(player.BufferedRanges.Any(r=>r.Start<=target&&r.End>=target)?"Em cache no player":"Pode precisar carregar");
    }
    void DrawCacheRail()
    {
        if(CacheRail is null)return;
        CacheRail.Children.Clear();
        var length=player.Snapshot.Duration?.TotalSeconds??0;
        if(length<=0||CacheRail.ActualWidth<=0)return;
        foreach(var range in player.BufferedRanges) {
            var start=Math.Clamp(range.Start,0,length);var end=Math.Clamp(range.End,0,length);
            if(end<=start)continue;
            var rectangle=new Rectangle{Width=Math.Max(.5,(end-start)/length*CacheRail.ActualWidth),Height=4,Opacity=.75};
            rectangle.SetResourceReference(Shape.FillProperty,"AniT.Brush.AccentCyan");
            Canvas.SetLeft(rectangle,start/length*CacheRail.ActualWidth);CacheRail.Children.Add(rectangle);
        }
    }
    void CacheRailSizeChanged(object sender,SizeChangedEventArgs args)=>DrawCacheRail();
    TextBlock MenuText(string text)=>new(){Text=text,Style=(Style)FindResource("PlayerText"),MaxWidth=310,TextWrapping=TextWrapping.Wrap};
    MenuItem Item(string text,Func<Task>? action=null,bool check=false,bool selected=false)
    {
        var item=new MenuItem{Header=MenuText(text),Style=(Style)FindResource("PlayerMenuItem"),IsCheckable=check,IsChecked=selected};
        if(action is not null)item.Click+=(_,args)=>{args.Handled=true;Run(action);};
        return item;
    }
    ContextMenu Menu()=>new(){Style=(Style)FindResource("PlayerPopup"),PlacementTarget=this};
    internal ContextMenu BuildTrackMenu(string kind)
    {
        var menu=Menu();AddTracks(menu,kind);return menu;
    }
    void AddTracks(ItemsControl menu,string kind)
    {
        var state=player.Tracks;
        if(kind=="sub") {
            var off=Item("Desligar legendas",async()=>{await player.SelectSubtitleAsync(null,lifetime.Token);RememberTrack(null,true,true);},true,state.SubtitleId is null);
            off.Tag=new TrackChoice(kind,null);menu.Items.Add(off);
        }
        var available=state.Tracks.Where(t=>t.Kind==kind).ToArray();
        foreach(var track in available) {
            var choice=Item(track.Label,async()=>{
                if(kind=="audio")await player.SelectAudioAsync(track.Id,lifetime.Token);else await player.SelectSubtitleAsync(track.Id,lifetime.Token);
                RememberTrack(track,kind=="sub");
            },true,track.Id==(kind=="audio"?state.AudioId:state.SubtitleId));
            choice.Tag=new TrackChoice(kind,track.Id);menu.Items.Add(choice);
        }
        if(available.Length==0)menu.Items.Add(new MenuItem{Header=MenuText("Nenhuma faixa disponível"),Style=(Style)FindResource("PlayerMenuItem"),IsEnabled=false});
    }
    internal ContextMenu BuildOptionsMenu()
    {
        var menu=Menu();AddOptions(menu);return menu;
    }
    void AddOptions(ItemsControl menu)
    {
        var speed=Item($"Velocidade · {player.Tracks.Speed:0.##}×");
        foreach(var rate in new[]{.5,.75,1,1.25,1.5,1.75,2})speed.Items.Add(Item($"{rate:0.##}×",async()=>{
            await player.SetPlaybackSpeedAsync(rate,lifetime.Token);preferences=preferences with{Speed=rate};QueueSave();SavePreferences();
        },true,Math.Abs(rate-player.Tracks.Speed)<.001));
        menu.Items.Add(speed);
        if(player.Tracks.Tracks.Any(t=>t.Kind=="sub")) {
            var delay=Item($"Sincronizar legenda · {player.Tracks.SubtitleDelay:+0.0;-0.0;0.0} s");
            delay.Items.Add(Item("Adiantar 0,1 s",()=>player.SetSubtitleDelayAsync(Math.Clamp(player.Tracks.SubtitleDelay-.1,-10,10),lifetime.Token)));
            delay.Items.Add(Item("Atrasar 0,1 s",()=>player.SetSubtitleDelayAsync(Math.Clamp(player.Tracks.SubtitleDelay+.1,-10,10),lifetime.Token)));
            delay.Items.Add(Item("Zerar ajuste",()=>player.SetSubtitleDelayAsync(0,lifetime.Token)));menu.Items.Add(delay);
        }
        var remember=Item("Lembrar idiomas neste perfil",check:true,selected:preferences.RememberLanguages);
        remember.StaysOpenOnClick=true;
        remember.Click+=(_,args)=>{args.Handled=true;preferences=preferences with{RememberLanguages=remember.IsChecked};QueueSave();SavePreferences();};
        menu.Items.Add(remember);
    }
    internal ContextMenu BuildContextMenu()
    {
        var menu=Menu();menu.Items.Add(Item(player.Snapshot.Paused?"Reproduzir":"Pausar",()=>player.TogglePauseAsync(lifetime.Token)));
        var audio=Item("Áudio");AddTracks(audio,"audio");menu.Items.Add(audio);
        var subtitles=Item("Legendas");AddTracks(subtitles,"sub");menu.Items.Add(subtitles);
        AddOptions(menu);menu.Items.Add(Item("Tela cheia",()=>{SetFullscreen(!fullscreen);return Task.CompletedTask;}));
        menu.Items.Add(Item("Voltar ao AniT",()=>{Close();return Task.CompletedTask;}));return menu;
    }
    void OpenMenu(ContextMenu menu,FrameworkElement? anchor)
    {
        if(closing)return;
        CloseMenus();ShowControls();keyboardNavigation=true;
        activeMenu=menu;menu.PlacementTarget=anchor??this;menu.Placement=anchor is null?PlacementMode.MousePoint:PlacementMode.Top;
        menu.Closed+=(_,_)=>{if(ReferenceEquals(activeMenu,menu)){activeMenu=null;idle.Restart();}};
        menu.IsOpen=true;
    }
    void CloseMenus(){if(activeMenu is {} menu){activeMenu=null;menu.IsOpen=false;}}
    void AudioClick(object sender,RoutedEventArgs args)=>HandleInput("audio");
    void SubtitleClick(object sender,RoutedEventArgs args)=>HandleInput("subtitles");
    void OptionsClick(object sender,RoutedEventArgs args)=>OpenMenu(BuildOptionsMenu(),OptionsAction);
}
