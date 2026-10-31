using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AniT.Player;

namespace AniT.App;

internal partial class OnlinePlayerWindow : Window
{
    readonly MpvOnlinePlayer player;
    readonly CancellationTokenSource lifetime = new();
    readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly DispatcherTimer clickDelay = new() { Interval = TimeSpan.FromMilliseconds(System.Windows.Forms.SystemInformation.DoubleClickTime + 30) };
    readonly Stopwatch idle = Stopwatch.StartNew();
    CancellationTokenSource? seek;
    bool stopped, closing, syncing, dragging, fullscreen, lastPaused = true, lastMuted, volumePending;
    bool showError, keyboardNavigation;
    double pendingVolume = 100;
    double? pendingSeekPosition;
    readonly VideoClickTracker videoClicks = new();
    WindowStyle savedStyle;
    ResizeMode savedResize;
    WindowState savedState;
    Rect savedBounds;
    public IntPtr VideoHandle => Video.Handle;

    public OnlinePlayerWindow(MpvOnlinePlayer player, string title, global::AniT.Infrastructure.OnlinePlayerPreferencesStore? preferencesStore = null, global::AniT.Infrastructure.PlaybackMomentStore? moments = null, Guid? episode = null)
    {
        this.player = player;
        InitializePreferences(preferencesStore);
        InitializeComponent();
        momentStore=moments;SetEpisodeContext(episode);
        Width = Math.Min(Width, Math.Max(MinWidth, SystemParameters.WorkArea.Width * 0.9));
        Height = Math.Min(Height, Math.Max(MinHeight, SystemParameters.WorkArea.Height * 0.9));
        SetEpisodeTitle(title);
        player.UserInput += OnInput;
        Video.PointerInput += OnVideoPointer;
        Deactivated += (_, _) => Video.SetPointerHidden(false);
        player.PlaybackClosed += OnPlayerClosed;
        player.Disposed += OnPlayerDisposed;
        refresh.Tick += (_, _) => UpdateView(player.Snapshot);
        clickDelay.Tick += (_, _) => { clickDelay.Stop(); videoClicks.Reset(); buildingReserve=false; Run(() => player.TogglePauseAsync(lifetime.Token)); };
        SizeChanged += (_, _) => UpdateChromeDensity();
        Loaded += (_, _) => { UpdateChromeDensity(); refresh.Start(); UpdateView(player.Snapshot); };
        Closing += (_, _) => {
            if (closing) return;
            closing = true;
            Stop();
            player.ClosePlayback();
        };
        Closed += (_, _) => Stop();
    }
    void UpdateChromeDensity()
    {
        var expanded = ActualWidth >= 1000;
        RegisterMomentLabel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        SubtitleToolLabel.Visibility = MomentsToolLabel.Visibility = OptionsToolLabel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        BrandPlate.Width = ActualWidth < 800 ? 198 : 218;
        BrandPlate.Height = 70;
        BrandWordmark.Width = ActualWidth < 800 ? 172 : 190;
    }
    internal void SetEpisodeTitle(string title)
    {
        CloseSubtitleCenter();
        CloseMoments();
        assistant.Reset(); buildingReserve=false;
        CloseMenus();
        EpisodeTitle.Text = title; EpisodeTitle.ToolTip = title; Title = title + " · AniT Player";
        showError = false; ShowControls();
    }
    void Stop()
    {
        if (stopped) return;
        stopped = true;
        CloseSubtitleCenter();
        CloseMoments();
        CloseMenus(); CaptureVolumePreferences(player.Snapshot); preferenceSave.Stop(); SavePreferences();
        refresh.Stop(); clickDelay.Stop(); lifetime.Cancel(); seek?.Cancel(); seek?.Dispose(); lifetime.Dispose();
        Video.SetPointerHidden(false); Video.PointerInput -= OnVideoPointer;
        player.UserInput -= OnInput; player.PlaybackClosed -= OnPlayerClosed; player.Disposed -= OnPlayerDisposed;
    }
    void OnPlayerClosed(object? sender, global::AniT.Core.PlaybackPositionChangedEventArgs args) => CloseFromPlayer();
    void OnPlayerDisposed(object? sender, EventArgs args) => CloseFromPlayer();
    internal void CloseFromPlayer()
    {
        if (!Dispatcher.CheckAccess()) { if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(CloseFromPlayer); return; }
        if (closing) return;
        closing = true; Stop(); Close();
    }
    internal bool VideoPointerHidden => Video.PointerHidden;
    void OnVideoPointer(object? sender, VideoPointerEventArgs input)
    {
        if (closing) return;
        keyboardNavigation=false; ShowControls();
        if (input.Action != "press") { if(input.Action=="menu") HandleInput("menu"); return; }
        var size = System.Windows.Forms.SystemInformation.DoubleClickSize;
        if (videoClicks.Press(input.Timestamp,input.X,input.Y,(uint)System.Windows.Forms.SystemInformation.DoubleClickTime,size.Width,size.Height)) {
            clickDelay.Stop(); SetFullscreen(!fullscreen);
        } else { clickDelay.Stop(); clickDelay.Start(); }
    }
    void OnInput(object? sender, string action) => HandleInput(action);
    internal void HandleInput(string action)
    {
        if (closing) return;
        if (action is "pointer") keyboardNavigation = false;
        ShowControls();
        if (action != "pointer") { clickDelay.Stop(); videoClicks.Reset(); }
        if (!player.Snapshot.VideoReady && action is "pause" or "mute" or "volume-up" or "volume-down") return;
        switch (action) {
            case "pause": buildingReserve=false; Run(() => player.TogglePauseAsync(lifetime.Token)); break;
            case "fullscreen": SetFullscreen(!fullscreen); break;
            case "escape": if(activeMenu?.IsOpen==true)CloseMenus();else if (fullscreen) SetFullscreen(false); break;
            case "back": SeekRelative(-10); break;
            case "forward": SeekRelative(10); break;
            case "volume-up": Run(() => player.SetVolumeAsync(player.Snapshot.Volume + 5, lifetime.Token)); break;
            case "volume-down": Run(() => player.SetVolumeAsync(player.Snapshot.Volume - 5, lifetime.Token)); break;
            case "mute": Run(() => player.ToggleMuteAsync(lifetime.Token)); break;
            case "close": Close(); break;
            case "moments": OpenMoments(); break;
            case "menu": OpenMenu(BuildContextMenu(), null); break;
            case "audio": OpenMenu(BuildTrackMenu("audio"), AudioAction); break;
            case "subtitles": OpenMenu(BuildTrackMenu("sub"), SubtitleAction); break;
        }
    }
    internal void SetFullscreen(bool enabled)
    {
        if (fullscreen == enabled || closing) return;
        CloseMenus();
        fullscreen = enabled;
        if (enabled) {
            savedStyle = WindowStyle; savedResize = ResizeMode; savedState = WindowState;
            savedBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            WindowState = WindowState.Normal; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized;
        } else {
            WindowState = WindowState.Normal; WindowStyle = savedStyle; ResizeMode = savedResize;
            Left = savedBounds.Left; Top = savedBounds.Top; Width = savedBounds.Width; Height = savedBounds.Height;
            WindowState = savedState;
        }
        ShowControls();
    }
    internal bool IsFullscreen => fullscreen;
    internal bool ControlsVisible => Chrome.Visibility == Visibility.Visible;
    void ShowControls() { Video.SetPointerHidden(false); idle.Restart(); Header.Visibility = Chrome.Visibility = Visibility.Visible; }
    internal void UpdateView(OnlinePlayerSnapshot state, double? idleSeconds = null)
    {
        if (closing) return;
        syncing = true;
        try {
            RegisterMomentAction.IsEnabled = !registeringMoment && momentStore is not null && momentEpisode is not null && state.VideoReady && !state.Buffering && !state.Seeking;
            PlayAction.IsEnabled = MuteAction.IsEnabled = Volume.IsEnabled = state.VideoReady;
            Timeline.IsEnabled = state.VideoReady && state.Duration is { TotalSeconds: > 0 };
            Timeline.Maximum = Math.Max(1, state.Duration?.TotalSeconds ?? 1);
            if (!dragging) Timeline.Value = Math.Clamp(pendingSeekPosition ?? state.Position?.TotalSeconds ?? 0, 0, Timeline.Maximum);
            TimeLabel.Text = (dragging || pendingSeekPosition is not null ? "Ir para " + Format(TimeSpan.FromSeconds(Timeline.Value)) + " · " : "")
                + Format(state.Position ?? TimeSpan.Zero) + " / " + (state.Duration is { } duration ? Format(duration) : "--:--");
            RefreshTrackControls(state);
            if (!Volume.IsMouseCaptureWithin && !Volume.IsKeyboardFocusWithin) Volume.Value = state.Volume;
            if (state.Paused != lastPaused) { lastPaused = state.Paused; PlayIcon.Source = Image(state.Paused ? "01_play" : "02_pause"); }
            if (state.Muted != lastMuted) { lastMuted = state.Muted; VolumeIcon.Source = Image(state.Muted ? "17_mudo" : "20_volume_alto"); }
            if (!showError) Status.Text = state.Seeking || pendingSeekPosition is not null ? "Buscando o trecho escolhido…" : state.Buffering ? "Repondo a reserva de vídeo…"
                : !state.VideoReady ? "Abrindo seu episódio…" : state.Paused ? "Pausado" : $"Reproduzindo · {state.CacheSeconds:0} s de reserva";
            UpdateAssistant(state);
            bool hide = state.VideoReady && !state.Paused && !state.Buffering && !state.Seeking && !showError
                && activeMenu?.IsOpen != true && pendingSeekPosition is null && !dragging && !Chrome.IsMouseOver && !Chrome.IsMouseCaptureWithin && !(keyboardNavigation && (Chrome.IsKeyboardFocusWithin || Header.IsKeyboardFocusWithin)) && (idleSeconds ?? idle.Elapsed.TotalSeconds) >= 3;
            Video.SetPointerHidden(hide && IsActive && Video.IsPointerOver);
            if (hide) Header.Visibility = Chrome.Visibility = Visibility.Collapsed;
            else Header.Visibility = Chrome.Visibility = Visibility.Visible;
        } finally { syncing = false; }
    }
    static string Format(TimeSpan time) => time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"mm\:ss");
    static BitmapImage Image(string name) => new(new Uri($"pack://application:,,,/AniT;component/Assets/Player/{name}.png"));
    async void Run(Func<Task> action)
    {
        if (closing) return;
        try { await action(); showError = false; }
        catch (OperationCanceledException) { }
        catch (Exception error) {
            if (closing) return;
            showError = true; ShowControls(); Status.Text = "Não foi possível concluir o comando. Tente novamente.";
            global::AniT.Infrastructure.AniTDiagnostics.Write("ONLINE PLAYER", error.Message);
        }
    }
    void SeekRelative(double seconds) => SeekTo(Math.Clamp((pendingSeekPosition ?? player.Snapshot.Position?.TotalSeconds ?? 0) + seconds, 0, player.Snapshot.Duration?.TotalSeconds ?? double.MaxValue));
    void SeekTo(double seconds)
    {
        if (!player.Snapshot.VideoReady) return;
        seek?.Cancel(); seek?.Dispose(); seek = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var request = seek; var token = request.Token; showError = false;
        pendingSeekPosition = seconds;
        Run(async () => {
            try { await player.SeekAsync(TimeSpan.FromSeconds(seconds), token); }
            finally { if (ReferenceEquals(seek, request)) pendingSeekPosition = null; }
        }); ShowControls();
    }
    void TimelineDown(object sender, MouseButtonEventArgs args) { dragging = true; ShowControls(); }
    void TimelineUp(object sender, MouseButtonEventArgs args) { if (!dragging) return; dragging = false; SeekTo(Timeline.Value); }
    void TimelineLostCapture(object sender, MouseEventArgs args) { if (dragging && Mouse.LeftButton == MouseButtonState.Released) { dragging = false; SeekTo(Timeline.Value); } }
    void TimelineKeyUp(object sender, KeyEventArgs args) { if (args.Key is Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown) SeekTo(Timeline.Value); }
    void PointerMoved(object sender, MouseEventArgs args) { keyboardNavigation = false; ShowControls(); }
    void PointerPressed(object sender, MouseButtonEventArgs args) { keyboardNavigation = false; ShowControls(); }
    void KeyPressed(object sender, KeyEventArgs args)
    {
        keyboardNavigation = true; ShowControls();
        // Sliders keep native keyboard editing; buttons keep Space/Enter activation.
        if (args.OriginalSource is System.Windows.Controls.Slider || Timeline.IsKeyboardFocusWithin || Volume.IsKeyboardFocusWithin) {
            if (args.Key != Key.Escape && args.Key != Key.F11) return;
        }
        if (args.Key == Key.Space && Chrome.IsKeyboardFocusWithin) return;
        var action = args.Key switch { Key.B => "moments", Key.A => "audio", Key.S => "subtitles", Key.Apps => "menu", Key.F10 when Keyboard.Modifiers == ModifierKeys.Shift => "menu", Key.Space or Key.P => "pause", Key.F or Key.F11 => "fullscreen", Key.Escape => "escape", Key.Left => "back", Key.Right => "forward", Key.Up => "volume-up", Key.Down => "volume-down", Key.M => "mute", Key.W when Keyboard.Modifiers == ModifierKeys.Control => "close", _ => null };
        if (action is null) return;
        args.Handled = true; HandleInput(action);
    }
    void BackClick(object sender, RoutedEventArgs args) => HandleInput("back");
    void ForwardClick(object sender, RoutedEventArgs args) => HandleInput("forward");
    void PlayClick(object sender, RoutedEventArgs args) => HandleInput("pause");
    void MuteClick(object sender, RoutedEventArgs args) => HandleInput("mute");
    void FullscreenClick(object sender, RoutedEventArgs args) => HandleInput("fullscreen");
    void ReturnClick(object sender, RoutedEventArgs args) => Close();
    async void VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> args)
    {
        if (syncing || closing || !IsLoaded) return;
        pendingVolume = args.NewValue;
        if (volumePending) return;
        volumePending = true;
        try {
            // Coalesce dragging: never queue one IPC request per pointer pixel.
            while (!closing) {
                var value = pendingVolume;
                await player.SetVolumeAsync(value, lifetime.Token);
                if (value == pendingVolume) break;
            }
        } catch (OperationCanceledException) { }
        catch (Exception) { if (!closing) { showError = true; Status.Text = "Não foi possível ajustar o volume. Tente novamente."; } }
        finally { volumePending = false; }
    }
}
