using System;
using AniT.Player;

namespace AniT.App;

internal sealed class MovieTheaterViewerWindow : IDisposable
{
    private readonly MpcReadOnlyView view;
    internal IntPtr WindowHandle => view.WindowHandle;
    public bool IsLockedSurface => view.IsInputBlocked && !view.HasPlayerMenu;
    public event EventHandler? Closed;
    public MovieTheaterViewerWindow(IntPtr playerWindow, IntPtr owner)
    {
        view = new MpcReadOnlyView(playerWindow, owner);
        view.Closed += (_, _) => Closed?.Invoke(this, EventArgs.Empty);
    }
    public void ShowPlayer() => view.Show();
    public void RefreshBounds() => view.RefreshBounds();
    internal void ToggleFullscreen() => view.ToggleFullscreen();
    public void Close() => view.Close();
    public void Dispose() => view.Dispose();
}
