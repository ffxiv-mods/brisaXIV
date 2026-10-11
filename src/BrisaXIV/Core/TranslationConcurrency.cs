namespace BrisaXIV.Core;

// Both language queues share these slots. A priority lane remains available even
// when every background slot is busy. Lowering the limit drains existing work.
public sealed class TranslationConcurrency
{
    private readonly object gate = new();
    private int background, priority, limit;
    private volatile string? billingCode;
    public string? BillingCode => billingCode;
    public string? BillingReason => BillingFailure.Message(billingCode);
    public void PauseBilling(string code) { if (BillingFailure.Message(code) != null) billingCode = code; }
    public void ClearBillingPause() => billingCode = null;
    public TranslationConcurrency(int limit = 1) => SetLimit(limit);
    public void SetLimit(int value) { lock (gate) limit = Math.Clamp(value, 1, 32); }
    public int Limit { get { lock (gate) return limit; } }
    public bool PriorityActive { get { lock (gate) return priority > 0; } }
    public async Task<IDisposable> EnterAsync(bool urgent, CancellationToken token, Func<bool>? shouldYield)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (shouldYield?.Invoke() == true) throw new TranslationYieldException();
            lock (gate)
            {
                if (urgent ? priority < 1 : background < limit && priority == 0)
                {
                    if (urgent) priority++; else background++;
                    return new Lease(this, urgent);
                }
            }
            await Task.Delay(25, token).ConfigureAwait(false);
        }
    }
    private sealed class Lease(TranslationConcurrency owner, bool urgent) : IDisposable
    {
        private int disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            lock (owner.gate) { if (urgent) owner.priority--; else owner.background--; }
        }
    }
}
