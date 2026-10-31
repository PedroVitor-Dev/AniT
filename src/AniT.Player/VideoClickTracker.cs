namespace AniT.Player;

// Physical screen coordinates and capture timestamps, independent of delayed UI/IPC delivery.
public sealed class VideoClickTracker
{
    bool pending;
    uint previous;
    int x, y;
    public bool Press(uint timestamp, int screenX, int screenY, uint interval, int width, int height)
    {
        var pair = pending && unchecked(timestamp - previous) <= interval
            && Math.Abs((long)screenX - x) <= width / 2 && Math.Abs((long)screenY - y) <= height / 2;
        pending = !pair; previous = timestamp; x = screenX; y = screenY;
        return pair;
    }
    public void Reset() => pending = false;
}
