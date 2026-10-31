using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AniT.Core;
using AniT.Infrastructure;
using AniT.Player;
namespace AniT.App;
internal sealed class OnlineMomentsWindow:Window
{
    readonly PlaybackMomentStore store;readonly Guid episode;readonly Func<OnlinePlayerSnapshot> state;
    readonly Func<string,CancellationToken,Task> save;readonly Action<double> seek;readonly Func<bool> protect;
    readonly StackPanel list=new();readonly TextBox note=new(){MaxLength=500,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=112,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    readonly TextBlock status=new();readonly TextBlock captureState=new();readonly TextBlock noteCount=new();readonly Button add;readonly CancellationTokenSource lifetime=new();
    readonly DispatcherTimer refresh=new(){Interval=TimeSpan.FromMilliseconds(500)};
    readonly HashSet<Guid> revealed=[];readonly Dictionary<Guid,(PlaybackMoment Moment,Border Row,bool Locked)> rows=[];
    bool saving;
    internal OnlineMomentsWindow(PlaybackMomentStore store,Guid episode,Func<OnlinePlayerSnapshot> state,Func<string,CancellationToken,Task> save,Action<double> seek,Func<bool> protect,Action<bool> setProtection)
    {
        this.store=store;this.episode=episode;this.state=state;this.save=save;this.seek=seek;this.protect=protect;
        Title="Momentos · AniT";Width=640;Height=620;MinWidth=420;MinHeight=360;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Height=Math.Min(Height,Math.Max(MinHeight,SystemParameters.WorkArea.Height*.9));Width=Math.Min(Width,Math.Max(MinWidth,SystemParameters.WorkArea.Width*.9));
        Resources.MergedDictionaries.Add(new(){Source=new Uri("/AniT;component/Styles/OnlinePlayerTheme.xaml",UriKind.Relative)});
        SetResourceReference(BackgroundProperty,"AniT.Brush.Background");SetResourceReference(ForegroundProperty,"AniT.Brush.TextPrimary");
        var root=new DockPanel{Margin=new Thickness(20)};
        var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var title=Text("Seus momentos",24);title.FontWeight=FontWeights.SemiBold;header.Children.Add(title);
        var subtitle=Text("Cenas e anotações deste episódio.",13);subtitle.SetResourceReference(TextBlock.ForegroundProperty,"AniT.Brush.TextSecondary");header.Children.Add(subtitle);
        var check=new CheckBox{Content=Text("Ocultar momentos à frente da reprodução"),IsChecked=protect(),Margin=new Thickness(0,8,0,12)};
        check.Click+=(_,_)=>{setProtection(check.IsChecked==true);UpdateVisibility();};header.Children.Add(check);
        var bottom=new StackPanel{Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        add=Action("Registrar momento",async()=>{
            if(saving)return;saving=true;UpdateCaptureState();
            try{await save(note.Text,lifetime.Token);note.Clear();Reload();status.Text="Momento registrado com miniatura.";}
            finally{saving=false;UpdateCaptureState();}
        });
        add.Margin=new Thickness(0);add.FontWeight=FontWeights.SemiBold;add.MinHeight=46;
        System.Windows.Automation.AutomationProperties.SetName(add,"Registrar momento atual");bottom.Children.Add(add);
        status.Style=(Style)FindResource("PlayerCaption");status.Margin=new Thickness(0,6,0,0);bottom.Children.Add(status);
        var content=new StackPanel();var capture=new StackPanel();
        captureState.Style=(Style)FindResource("PlayerText");captureState.FontWeight=FontWeights.SemiBold;captureState.Margin=new Thickness(0,0,0,10);capture.Children.Add(captureState);
        capture.Children.Add(Text("Anotação (opcional)",13));note.Style=(Style)FindResource("PlayerNoteInput");note.ToolTip="Escreva uma anotação para a cena atual. Até 500 caracteres.";
        System.Windows.Automation.AutomationProperties.SetName(note,"Anotação do momento");capture.Children.Add(note);
        noteCount.Style=(Style)FindResource("PlayerCaption");noteCount.HorizontalAlignment=HorizontalAlignment.Right;noteCount.Margin=new Thickness(0,5,0,0);
        noteCount.Text="0 / 500";note.TextChanged+=(_,_)=>noteCount.Text=$"{note.Text.Length} / 500";capture.Children.Add(noteCount);
        content.Children.Add(Card(capture));
        var listTitle=Text("Momentos registrados",16);listTitle.FontWeight=FontWeights.SemiBold;listTitle.Margin=new Thickness(0,18,0,10);content.Children.Add(listTitle);content.Children.Add(list);
        root.Children.Add(new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});Content=root;
        Reload();UpdateCaptureState();refresh.Tick+=(_,_)=>{UpdateCaptureState();UpdateVisibility();};refresh.Start();
        Closed+=(_,_)=>{refresh.Stop();lifetime.Cancel();lifetime.Dispose();};
    }
    Border Card(UIElement content)
    {
        var card=new Border{Child=content,Padding=new Thickness(14),CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1)};
        card.SetResourceReference(Border.BackgroundProperty,"AniT.Brush.Surface");card.SetResourceReference(Border.BorderBrushProperty,"AniT.Brush.Border");return card;
    }
    internal void ConfirmRegistered()=>status.Text="Momento registrado. Use Editar anotação na lista para adicionar uma nota.";
    void UpdateCaptureState()
    {
        var current=state();add.IsEnabled=!saving&&current.VideoReady&&!current.Seeking&&!current.Buffering;
        add.Content=saving?"Registrando momento…":"Registrar momento";
        captureState.Text=!current.VideoReady?"Aguarde o vídeo abrir":current.Seeking?"Aguarde o avanço terminar":current.Buffering?"Aguarde o vídeo carregar":
            "Cena atual · "+(current.Position??TimeSpan.Zero).ToString(@"hh\:mm\:ss");
    }
    TextBlock Text(string text,double size=14)=>new(){Text=text,FontSize=size,Style=(Style)FindResource("PlayerText"),Margin=new Thickness(0,0,0,6)};
    Button Action(string text,Func<Task> action)
    {
        var button=new Button{Content=text,Style=(Style)FindResource("PlayerAction"),Margin=new Thickness(0,4,6,0)};
        button.Click+=async(_,_)=>{try{await action();}catch(OperationCanceledException){}catch(Exception e){status.Text=e is InvalidOperationException?e.Message:"Não foi possível salvar a alteração. Tente novamente.";}};return button;
    }
    bool Locked(PlaybackMoment moment)=>!MomentSpoilerProtection.CanReveal(moment.Seconds,state().Position?.TotalSeconds,protect(),revealed.Contains(moment.Id));
    internal void Reload()
    {
        rows.Clear();list.Children.Clear();var moments=store.ForEpisode(episode);
        if(moments.Count==0){
            var empty=new StackPanel{Margin=new Thickness(4,8,4,8)};
            var heading=Text("Nenhum momento registrado",16);heading.FontWeight=FontWeights.SemiBold;empty.Children.Add(heading);
            var hint=Text("Use Registrar momento para guardar a cena atual. A anotação é opcional.",13);hint.SetResourceReference(TextBlock.ForegroundProperty,"AniT.Brush.TextSecondary");empty.Children.Add(hint);list.Children.Add(Card(empty));
        }
        foreach(var moment in moments){var row=new Border{Padding=new Thickness(12),Margin=new Thickness(0,0,0,10),CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1)};
            row.SetResourceReference(Border.BackgroundProperty,"AniT.Brush.Surface");row.SetResourceReference(Border.BorderBrushProperty,"AniT.Brush.Border");
            var locked=Locked(moment);row.Child=BuildRow(moment,locked);rows[moment.Id]=(moment,row,locked);list.Children.Add(row);}
    }
    internal void UpdateVisibility()
    {
        foreach(var id in rows.Keys.ToArray()){var item=rows[id];var locked=Locked(item.Moment);if(locked==item.Locked)continue;item.Row.Child=BuildRow(item.Moment,locked);rows[id]=(item.Moment,item.Row,locked);}
    }
    FrameworkElement BuildRow(PlaybackMoment moment,bool locked)
    {
        var stack=new StackPanel();var body=new DockPanel();stack.Children.Add(body);
        var preview=new Border{Width=112,Height=68,Margin=new Thickness(0,0,12,0)};DockPanel.SetDock(preview,Dock.Left);body.Children.Add(preview);
        // Protected content is never assigned to a visual or loaded into an ImageSource.
        if(locked)preview.Child=Text("Protegido");else {
            var path=store.ThumbnailPath(moment.Id);
            if(File.Exists(path)){try{var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=160;image.UriSource=new Uri(path);image.EndInit();image.Freeze();preview.Child=new Image{Source=image,Stretch=Stretch.Uniform,Tag="PlayerFrame"};}catch(Exception e)when(e is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException){preview.Child=Text("Sem miniatura");}}
            else preview.Child=Text("Sem miniatura");
        }
        var info=new StackPanel();body.Children.Add(info);info.Children.Add(Text(TimeSpan.FromSeconds(moment.Seconds).ToString(@"hh\:mm\:ss")+(locked?" · Momento protegido":"")));
        if(!locked)info.Children.Add(Text(string.IsNullOrWhiteSpace(moment.Note)?"Sem anotação":moment.Note));
        var actions=new WrapPanel();stack.Children.Add(actions);
        if(locked)actions.Children.Add(Action("Revelar",()=>{revealed.Add(moment.Id);UpdateVisibility();return Task.CompletedTask;}));
        actions.Children.Add(Action("Ir para este ponto",()=>{seek(moment.Seconds);return Task.CompletedTask;}));
        bool editing=false;
        if(!locked)actions.Children.Add(Action("Editar anotação",()=>{
            if(editing)return Task.CompletedTask;editing=true;
            revealed.Add(moment.Id);var edit=new TextBox{Text=moment.Note,MaxLength=500,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Style=(Style)FindResource("PlayerNoteInput"),MinHeight=112,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
            stack.Children.Add(edit);stack.Children.Add(Action("Salvar anotação",()=>{store.Edit(episode,moment.Id,edit.Text);Reload();return Task.CompletedTask;}));return Task.CompletedTask;}));
        actions.Children.Add(Action("Excluir",()=>{store.Remove(episode,moment.Id);Reload();return Task.CompletedTask;}));return stack;
    }
}
