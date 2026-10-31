using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AniT.Core;
using AniT.Player;
namespace AniT.App;
internal sealed class OnlineSubtitleWindow:Window
{
    readonly MpvOnlinePlayer player;readonly Func<SubtitleAppearanceSettings,double,CancellationToken,Task> apply;
    readonly CancellationTokenSource lifetime=new();readonly DispatcherTimer refresh=new(){Interval=TimeSpan.FromMilliseconds(500)};
    readonly StackPanel tracks=new();readonly TextBlock status=new();readonly Slider size=new(),position=new(),outline=new(),delay=new();
    readonly ComboBox font=new(),color=new();readonly CheckBox overrideAss=new();readonly TextBlock preview=new();
    string trackKey="";bool busy;
    internal OnlineSubtitleWindow(MpvOnlinePlayer player,SubtitleAppearanceSettings? appearance,Func<SubtitleAppearanceSettings,double,CancellationToken,Task> apply)
    {
        this.player=player;this.apply=apply;Title="Central de legendas · AniT";Width=640;Height=700;MinWidth=420;MinHeight=380;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Height=Math.Min(Height,Math.Max(MinHeight,SystemParameters.WorkArea.Height*.9));Width=Math.Min(Width,Math.Max(MinWidth,SystemParameters.WorkArea.Width*.9));
        Resources.MergedDictionaries.Add(new(){Source=new Uri("/AniT;component/Styles/OnlinePlayerTheme.xaml",UriKind.Relative)});SetResourceReference(BackgroundProperty,"AniT.Brush.Background");SetResourceReference(ForegroundProperty,"AniT.Brush.TextPrimary");
        var root=new DockPanel{Margin=new Thickness(20)};var actions=new WrapPanel();DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        actions.Children.Add(Action("Aplicar ajustes",()=>apply(CurrentStyle(),delay.Value,lifetime.Token)));
        actions.Children.Add(Action("Restaurar padrão",async()=>{LoadStyle(new());delay.Value=0;await apply(new(),0,lifetime.Token);}));
        var panel=new StackPanel();root.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});Content=root;
        panel.Children.Add(Text("Central de legendas",22));panel.Children.Add(Text("Importe SRT, ASS, SSA ou VTT. Arraste o arquivo para esta janela ou para os controles do player."));
        panel.Children.Add(Action("Adicionar arquivo…",()=>{var dialog=new Microsoft.Win32.OpenFileDialog{Filter="Legendas|*.srt;*.ass;*.ssa;*.vtt",Multiselect=true};if(dialog.ShowDialog(this)==true)ImportFiles(dialog.FileNames);return Task.CompletedTask;}));
        AllowDrop=true;PreviewDragOver+=(_,e)=>{e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;};Drop+=(_,e)=>{if(e.Data.GetData(DataFormats.FileDrop)is string[] files){ImportFiles(files);e.Handled=true;}};
        panel.Children.Add(tracks);
        panel.Children.Add(Text("Aparência",18));
        font.ItemsSource=Fonts.SystemFontFamilies.Select(f=>f.Source).OrderBy(n=>n).Take(500).ToArray();font.Style=(Style)FindResource("PlayerSelect");font.MinHeight=36;font.IsEditable=false;font.Margin=new Thickness(0,4,0,10);panel.Children.Add(Text("Fonte"));panel.Children.Add(font);
        AddSlider(panel,"Tamanho do texto",size,16,96);AddSlider(panel,"Posição (0 = topo; 100 = base)",position,0,100);AddSlider(panel,"Contorno",outline,0,6);AddSlider(panel,"Atraso em segundos (negativo adianta)",delay,-10,10);
        color.ItemsSource=new[]{"#FFFFFF","#FFF176","#80DEEA","#A5D6A7"};color.Style=(Style)FindResource("PlayerSelect");color.MinHeight=36;color.Margin=new Thickness(0,4,0,10);panel.Children.Add(Text("Cor"));panel.Children.Add(color);
        overrideAss.Content=Text("Aplicar meus ajustes também às legendas ASS/SSA");overrideAss.Margin=new Thickness(0,6,0,8);panel.Children.Add(overrideAss);
        panel.Children.Add(Text("Desmarcado preserva o estilo e a diagramação originais do ASS. Legendas em imagem não permitem mudar a fonte."));
        preview.Tag="ThemeManaged";preview.Text="Prévia do texto simples";preview.TextWrapping=TextWrapping.Wrap;preview.Margin=new Thickness(8);panel.Children.Add(preview);
        status.Style=(Style)FindResource("PlayerCaption");status.Margin=new Thickness(0,10,0,10);panel.Children.Add(status);
        LoadStyle(appearance??new());delay.Value=player.Tracks.SubtitleDelay;
        size.ValueChanged+=(_,_)=>UpdatePreview();color.SelectionChanged+=(_,_)=>UpdatePreview();font.SelectionChanged+=(_,_)=>UpdatePreview();UpdatePreview();
        refresh.Tick+=(_,_)=>RefreshTracks();refresh.Start();RefreshTracks();Closed+=(_,_)=>{refresh.Stop();lifetime.Cancel();lifetime.Dispose();};
    }
    TextBlock Text(string text,double size=14)=>new(){Text=text,FontSize=size,Style=(Style)FindResource("PlayerText"),Margin=new Thickness(0,6,0,4)};
    Button Action(string title,Func<Task> action){var button=new Button{Content=title,Style=(Style)FindResource("PlayerAction"),Margin=new Thickness(0,4,8,4)};button.Click+=async(_,_)=>{try{if(!player.Snapshot.VideoReady){status.Text="Espere o vídeo abrir para aplicar ou importar legendas.";return;}button.IsEnabled=false;await action();status.Text="Ajuste aplicado.";}catch(OperationCanceledException){}catch(Exception e){status.Text=e is ArgumentException or InvalidOperationException?e.Message:"Não foi possível aplicar a alteração.";}finally{button.IsEnabled=true;}};return button;}
    void AddSlider(Panel panel,string title,Slider slider,double min,double max)
    {
        var label=Text(title);panel.Children.Add(label);slider.Minimum=min;slider.Maximum=max;slider.TickFrequency=.1;slider.IsSnapToTickEnabled=true;slider.Style=(Style)FindResource("PlayerTrack");slider.ValueChanged+=(_,_)=>label.Text=title+$" · {slider.Value:0.#}";panel.Children.Add(slider);
    }
    void LoadStyle(SubtitleAppearanceSettings value)
    {
        value=value.Normalize();size.Value=value.FontSize;position.Value=value.Position;outline.Value=value.Outline;color.SelectedItem=value.Color;if(color.SelectedIndex<0)color.SelectedIndex=0;
        font.SelectedItem=value.Font;if(font.SelectedIndex<0)font.SelectedIndex=0;overrideAss.IsChecked=value.OverrideAss;UpdatePreview();
    }
    internal SubtitleAppearanceSettings CurrentStyle()=>new(size.Value,position.Value,outline.Value,color.SelectedItem as string??"#FFFFFF",font.SelectedItem as string??"Segoe UI",overrideAss.IsChecked==true);
    void UpdatePreview(){preview.FontSize=Math.Min(30,size.Value/2);preview.Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(color.SelectedItem as string??"#FFFFFF"));preview.Background=new SolidColorBrush(Color.FromRgb(12,24,45));preview.FontFamily=new FontFamily(font.SelectedItem as string??"Segoe UI");}
    internal async void ImportFiles(IEnumerable<string> paths)
    {
        if(busy)return;busy=true;
        try{foreach(var path in paths.Take(32)){await player.AddSubtitleAsync(path,lifetime.Token);}status.Text="Legenda importada e selecionada.";trackKey="";RefreshTracks();}
        catch(OperationCanceledException){}catch(Exception e){status.Text=e is ArgumentException or InvalidOperationException?e.Message:"Não foi possível importar essa legenda.";}
        finally{busy=false;}
    }
    void RefreshTracks()
    {
        var state=player.Tracks;var key=string.Join("|",state.Tracks.Where(t=>t.Kind=="sub").Select(t=>$"{t.Id}:{t.Label}"))+":"+state.SubtitleId;
        if(key==trackKey)return;trackKey=key;tracks.Children.Clear();tracks.Children.Add(Text("Faixas disponíveis",18));
        tracks.Children.Add(Action(state.SubtitleId is null?"✓ Legendas desligadas":"Desligar legendas",()=>player.SelectSubtitleAsync(null,lifetime.Token)));
        foreach(var track in state.Tracks.Where(t=>t.Kind=="sub")) {
            var row=new StackPanel();var pick=Action((track.Id==state.SubtitleId?"✓ ":"")+track.Label,()=>player.SelectSubtitleAsync(track.Id,lifetime.Token));pick.Content=Text((track.Id==state.SubtitleId?"✓ ":"")+track.Label);row.Children.Add(pick);
            if(track.External)row.Children.Add(Action("Remover esta legenda externa",()=>player.RemoveSubtitleAsync(track.Id,lifetime.Token)));tracks.Children.Add(row);
        }
    }
}
