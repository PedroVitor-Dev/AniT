using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AniT.Infrastructure;

namespace AniT.App;

internal sealed class OnlineImportProgressWindow : Window
{
    readonly TextBlock heading = new() { Text = "Preparando sua biblioteca", FontSize = 24, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock detail = new() { Text = "Lendo a fonte selecionada…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 20) };
    readonly TextBlock phase = new() { Text = "01  Fonte   ·   02  Episódios   ·   03  Capa", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 18, 0, 0) };
    readonly ProgressBar bar = new() { Minimum = 0, Maximum = 100, Height = 8, Value = 4 };
    readonly Button action = new() { Content = "Cancelar", MinHeight = 42, Padding = new Thickness(20, 9, 20, 9), HorizontalAlignment = HorizontalAlignment.Right };
    readonly CancellationTokenSource cancellation;
    bool complete;
    public OnlineImportProgressWindow(CancellationTokenSource cancellation, bool preview = false)
    {
        this.cancellation = cancellation; Title = "Adicionando à biblioteca · AniT"; Width = 520; Height = 360; MinWidth = 400; MinHeight = 310;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        StableWindowRendering.SetEnabled(this, true);
        SetResourceReference(BackgroundProperty, "AniT.Brush.Background"); SetResourceReference(ForegroundProperty, "AniT.Brush.TextPrimary");
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/AniT;component/Styles/TheaterTheme.xaml", UriKind.Relative) });
        foreach (var text in new[] { heading, detail, phase }) text.SetResourceReference(TextBlock.ForegroundProperty, "AniT.Brush.TextPrimary");
        action.Style = (Style)FindResource("CinemaAction"); bar.Style = (Style)FindResource("CinemaProgress");
        var grid = new Grid { Margin = new Thickness(28) }; grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.Children.Add(heading); var body = new StackPanel(); body.Children.Add(detail); body.Children.Add(bar); body.Children.Add(phase);
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(scroll, 1); grid.Children.Add(scroll); Grid.SetRow(action, 2); grid.Children.Add(action);
        var card = new Border { CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Margin = new Thickness(14), Child = grid };
        card.SetResourceReference(Border.BackgroundProperty, "AniT.Brush.Surface"); card.SetResourceReference(Border.BorderBrushProperty, "AniT.Brush.Border"); Content = card;
        action.Click += (_, _) => { if (complete) Close(); else cancellation.Cancel(); };
        Closing += (_, _) => { if (!complete) cancellation.Cancel(); };
        Closed += (_, _) => { bar.BeginAnimation(OpacityProperty, null); BeginAnimation(OpacityProperty, null); };
        if (!preview && !AniTSystemSettingsStore.Load().ReduceMotion && AniTSystemSettingsStore.Load().AnimationIntensity > 0)
        {
            var pulse = new DoubleAnimation(.55, 1, TimeSpan.FromMilliseconds(950)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            bar.BeginAnimation(OpacityProperty, pulse);
            Loaded += (_, _) => BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }
    }
    public void Report(OnlineImportProgress progress)
    {
        if (!IsLoaded || complete) return;
        heading.Text = progress.Title; detail.Text = progress.Detail; bar.Value = Math.Max(bar.Value, progress.Percent);
        if (progress.Percent >= 65) { action.Content = "Concluir agora"; phase.Text = "Episódios já adicionados · identificando nome e capa"; }
    }
    public void Complete(string title, int episodes, bool artworkPending)
    {
        complete = true; bar.BeginAnimation(OpacityProperty, null); bar.Opacity = 1; bar.Value = 100;
        heading.Text = "Carregado!"; detail.Text = $"{title}\n{episodes} episódios adicionados à sua biblioteca.";
        phase.Text = artworkPending ? "Pronto para abrir no Início. As capas poderão ser completadas quando houver conexão." : "Nome, episódios e informações preparados. Abra pelo Início.";
        action.Content = "Ir para o Início";
    }
}
