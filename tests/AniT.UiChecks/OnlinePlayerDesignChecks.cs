using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AniT.App;
using AniT.Infrastructure;

// Design-only composition: no player process, networking, database or saved preferences.
internal static class OnlinePlayerDesignChecks
{
    static TextBlock Text(string value, bool caption = false) => new() {
        Text = value, Style = (Style)Application.Current.FindResource(caption ? "PlayerCaption" : "PlayerText")
    };
    static Image Art(string name, double size) => new() {
        Source = new BitmapImage(new Uri($"pack://application:,,,/AniT;component/Assets/Player/{name}.png")),
        Width = size, Height = size, Stretch = Stretch.Uniform
    };
    static Button Icon(string name, string label) {
        var button = new Button { Content = Art(name, 38), Style = (Style)Application.Current.FindResource("PlayerIcon"), ToolTip = label };
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        return button;
    }
    static Window Preview(double width, double height) {
        var panel = new DockPanel { Margin = new Thickness(24) };
        var heading = Text("ANIT PLAYER   /   PRÉVIA VISUAL", true);
        heading.Margin = new Thickness(0,0,0,16); DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        var chrome = new StackPanel(); DockPanel.SetDock(chrome, Dock.Bottom); panel.Children.Add(chrome);
        var title = Text("Sua próxima história"); title.FontSize=20; title.FontWeight=FontWeights.SemiBold; title.Margin=new Thickness(0,16,0,4); chrome.Children.Add(title);
        chrome.Children.Add(Text("Episódio 01 · Controles ilustrativos; integração no passo 8", true));
        var slider = new Slider { Minimum=0,Maximum=100,Value=28, Style=(Style)Application.Current.FindResource("PlayerTrack"),Margin=new Thickness(0,8,0,0) };
        System.Windows.Automation.AutomationProperties.SetName(slider,"Posição do episódio"); chrome.Children.Add(slider);
        chrome.Children.Add(Text("06:32 / 23:20   ·   Reserva: 18 segundos", true));
        var controls=new WrapPanel { Margin=new Thickness(0,12,0,0) }; chrome.Children.Add(controls);
        foreach(var item in new[]{("21_voltar_10s","Voltar 10 segundos"),("01_play","Reproduzir"),("22_avancar_10s","Avançar 10 segundos"),("20_volume_alto","Volume")}) controls.Children.Add(Icon(item.Item1,item.Item2));
        foreach(var trackLabel in new[]{"Áudio · Japonês", "Legendas · Português"}) controls.Children.Add(new Button { Content=trackLabel,Style=(Style)Application.Current.FindResource("PlayerAction"),Margin=new Thickness(4),ToolTip=trackLabel });
        controls.Children.Add(Icon("23_tela_cheia","Tela cheia"));
        var stage=new Border { CornerRadius=new CornerRadius(20),BorderThickness=new Thickness(1),Padding=new Thickness(20) };
        stage.SetResourceReference(Border.BackgroundProperty,"AniT.Brush.Surface");stage.SetResourceReference(Border.BorderBrushProperty,"AniT.Brush.Border");
        var center=new StackPanel { VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center };
        center.Children.Add(Art("idle",150)); var label=Text("Tudo pronto para dar play");label.HorizontalAlignment=HorizontalAlignment.Center;label.Margin=new Thickness(0,12,0,0);center.Children.Add(label);
        var note=Text("Área de vídeo · sem moldura sobre o episódio",true);note.HorizontalAlignment=HorizontalAlignment.Center;note.Margin=new Thickness(0,6,0,0);center.Children.Add(note);stage.Child=center;panel.Children.Add(stage);
        return new Window { Content=panel,Width=width,Height=height,Left=-20000,Top=-20000,WindowStartupLocation=WindowStartupLocation.Manual,ShowInTaskbar=false,ShowActivated=false,Title="AniT · Prévia do player" };
    }
    public static void Run() {
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("pack://application:,,,/AniT;component/Styles/OnlinePlayerTheme.xaml") });
        var output=Environment.GetEnvironmentVariable("ANIT_PLAYER_DESIGN_OUTPUT") ?? Path.Combine(Path.GetTempPath(),"AniT.Player.Design");Directory.CreateDirectory(output);
        int count=0;
        foreach(var theme in new[]{AppearanceThemeMode.Dark,AppearanceThemeMode.Light})
        foreach(var scale in new[]{100,140}) {
            AppearanceManager.Apply(AniTSystemSettings.Default with { ThemeMode=theme,TextScalePercent=scale,ReduceMotion=true });
            foreach(var size in new[]{(1366d,720d),(960d,640d)}) {
                var window=Preview(size.Item1,size.Item2);window.Show();Flush();window.UpdateLayout();
                foreach(var button in Descendants(window).OfType<Button>()) CheckBounds(window,button);
                Save(window,Path.Combine(output,$"player-{theme}-{scale}-{size.Item1}.png"));window.Close();Flush();count++;
            }
            foreach(var width in new[]{560d,360d})
            foreach(var state in new[]{"initial","connecting","receiving","error"}) {
                using var cancellation=new CancellationTokenSource();
                var window=new OnlineBufferWindow(cancellation){Width=width,Left=-20000,Top=-20000,ShowInTaskbar=false,ShowActivated=false};window.Show();Flush();
                window.Report(state switch {
                    "connecting"=>new(0,0,0,0,null,Stage:"Procurando fontes para este episódio…"),
                    "receiving"=>new(8*1024*1024,2*1024*1024,12,0.5,null,2*1024*1024,true,Stage:"Lendo o cabeçalho verificado do vídeo…"),
                    "error"=>new(0,0,0,0,"A fonte não respondeu. Tente novamente."),
                    _=>new(0,0,0,0,null,Stage:"Organizando a abertura do seu episódio…",TransferStarted:false)
                });Flush();window.UpdateLayout();
                CheckBounds(window,Descendants(window).OfType<Button>().Single());
                if(Descendants(window).OfType<ProgressBar>().Single().IsIndeterminate)throw new Exception("Reduced motion must disable animated progress");
                var expected=((SolidColorBrush)window.FindResource("AniT.Brush.TextPrimary")).Color;
                foreach(var text in Descendants(window).OfType<TextBlock>().Where(t=>t.Text=="Preparando seu episódio"))
                    if(((SolidColorBrush)text.Foreground).Color!=expected)throw new Exception("Incorrect theme foreground");
                if(scale==100&&width==560)Save(window,Path.Combine(output,$"preparation-{theme}-{state}.png"));
                window.Close();Flush();window.Report(new(0,0,0,0,null));count++;
            }
        }
        Console.WriteLine($"Player design: {count} layouts passed. Screenshots: {output}");
    }
    static void CheckBounds(Window window,FrameworkElement element) {
        var bounds=element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
        if(element.ActualHeight<32||bounds.Left<0||bounds.Right>window.ActualWidth||bounds.Bottom>window.ActualHeight)throw new Exception("Player control clipped");
    }
    static IEnumerable<DependencyObject> Descendants(DependencyObject root) {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var descendant in Descendants(child))yield return descendant;}
    }
    static void Flush()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
    static void Save(Window window,string path) {
        var content=(FrameworkElement)window.Content;
        var image=new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),(int)Math.Ceiling(content.ActualHeight),96,96,PixelFormats.Pbgra32);image.Render(content);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(path);encoder.Save(file);
    }
}
