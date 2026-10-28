namespace AniT.Theater;

/// <summary>Publishes only validated, contiguous bytes while the rest of a bounded block is still in flight.</summary>
internal sealed class StreamReadBlock
{
    private readonly object gate = new();
    private TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int received;
    public byte[] Bytes { get; }
    public int Received => Volatile.Read(ref received);
    public TaskCompletionSource<byte[]> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public StreamReadBlock(int count) => Bytes = new byte[count];
    private StreamReadBlock(byte[] bytes) { Bytes = bytes; received = bytes.Length; Completion.SetResult(bytes); }
    public static StreamReadBlock Completed(byte[] bytes) => new(bytes);
    public void Append(ReadOnlySpan<byte> bytes)
    {
        TaskCompletionSource signal;
        lock (gate)
        {
            if (Completion.Task.IsCompleted || bytes.Length > Bytes.Length - received) throw new InvalidDataException("Bloco de vídeo inválido.");
            bytes.CopyTo(Bytes.AsSpan(received)); Volatile.Write(ref received, received + bytes.Length);
            signal = changed; changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        signal.TrySetResult();
    }
    public async Task<int> AvailableAsync(int offset, CancellationToken token)
    {
        if (offset < 0 || offset >= Bytes.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        while (true)
        {
            Task signal;
            lock (gate)
            {
                if (received > offset) return received;
                signal = Task.WhenAny(changed.Task, Completion.Task);
            }
            if (Completion.Task.IsCompleted) { await Completion.Task.ConfigureAwait(false); return Received; }
            await signal.WaitAsync(token).ConfigureAwait(false);
        }
    }
}
