using System.IO;
namespace AniT.Player;
public sealed partial class MpvOnlinePlayer
{
    public async Task<TimeSpan> CaptureFrameAsync(string path,CancellationToken token=default)
    {
        if(!videoReady||seeking||buffering)throw new InvalidOperationException("Espere o vídeo ficar pronto antes de marcar este momento.");
        var expected=generation;
        using var operation=CancellationTokenSource.CreateLinkedTokenSource(token,fileLifetime?.Token??CancellationToken.None);token=operation.Token;
        path=Path.GetFullPath(path);if(Path.GetExtension(path)!=".png")throw new ArgumentException("A captura deve ser PNG.");
        var at=await GetPositionAsync(token);
        await CommandAsync(["screenshot-to-file",path,"video"],token);token.ThrowIfCancellationRequested();
        if(expected!=generation)throw new OperationCanceledException(token);
        return at;
    }
}
