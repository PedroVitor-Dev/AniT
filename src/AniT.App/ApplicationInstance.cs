using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AniT.App;

/// <summary>One UI per data directory. Restart waits for the old UI to release its lease.</summary>
internal sealed class ApplicationInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly CancellationTokenSource lifetime = new();
    private readonly string pipeName;
    private readonly Action activate;
    private int disposed;
    private ApplicationInstance(Mutex mutex, string name, Action activate)
    {
        this.mutex = mutex; pipeName = name; this.activate = activate;
        _ = Task.Run(ListenAsync);
    }

    public static ApplicationInstance? TryAcquire(string dataRoot, Action activate, bool restarting = false)
    {
        var identity = System.IO.Path.GetFullPath(dataRoot).ToUpperInvariant();
        var name = "AniT.App." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        var mutex = new Mutex(false, name);
        bool acquired;
        try { acquired = mutex.WaitOne(restarting ? TimeSpan.FromSeconds(10) : TimeSpan.Zero); }
        catch (AbandonedMutexException) { acquired = true; }
        if (acquired) return new(mutex, name, activate);
        mutex.Dispose();
        try
        {
            using var pipe = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            pipe.Connect(1500); pipe.WriteByte(1);
        }
        catch (Exception error) when (error is IOException or TimeoutException or UnauthorizedAccessException) { /* First window may still be loading; do not launch a competing UI. */ }
        return null;
    }

    private async Task ListenAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(lifetime.Token).ConfigureAwait(false);
                var signal = new byte[1];
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(1500);
                try { if (await pipe.ReadAsync(signal, timeout.Token).ConfigureAwait(false) == 1 && signal[0] == 1) activate(); }
                catch (OperationCanceledException) when (!lifetime.IsCancellationRequested) { }
                catch (IOException) { }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { AniT.Infrastructure.AniTDiagnostics.Write("INICIALIZAÇÃO", "Não foi possível receber pedidos de ativação da janela.", error); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel(); mutex.ReleaseMutex(); mutex.Dispose();
    }
}
