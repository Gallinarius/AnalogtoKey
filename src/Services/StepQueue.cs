using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AnalogtoKey.Services;

// Per-axis FIFO queue for stacked mode — ensures keypresses are sent sequentially
// and never dropped even when the stick moves faster than holdMs allows.
internal sealed class StepQueue : IDisposable
{
    private readonly record struct Job(ushort Key, int HoldMs, int PauseMs);

    private readonly Channel<Job>            _channel = Channel.CreateUnbounded<Job>();
    private readonly CancellationTokenSource _cts     = new();
    private volatile ushort                  _heldKey;

    public StepQueue() => Task.Run(ConsumeAsync);

    public void Enqueue(ushort key, int holdMs, int pauseMs)
        => _channel.Writer.TryWrite(new Job(key, holdMs, pauseMs));

    public void Clear()
    {
        while (_channel.Reader.TryRead(out _)) { }
    }

    private async Task ConsumeAsync()
    {
        var ct = _cts.Token;
        try
        {
            await foreach (var job in _channel.Reader.ReadAllAsync(ct))
            {
                _heldKey = job.Key;
                KeySender.KeyDown(job.Key);
                try   { await Task.Delay(job.HoldMs, ct); }
                catch { KeySender.KeyUp(job.Key); _heldKey = 0; throw; }
                KeySender.KeyUp(job.Key);
                _heldKey = 0;
                if (_channel.Reader.Count > 0)
                    await Task.Delay(job.PauseMs, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        _channel.Writer.Complete();
        _cts.Cancel();
        var k = _heldKey;
        if (k != 0) KeySender.KeyUp(k);
    }
}
