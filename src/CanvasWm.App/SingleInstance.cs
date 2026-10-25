using System.Security.Principal;

namespace CanvasWm.App;

/// <summary>One CanvasWM per user session: a named mutex (B59, A16).</summary>
internal sealed class SingleInstance : IDisposable
{
    private Mutex? _mutex;
    private bool _owned;

    /// <summary>True when this process now owns the instance. An abandoned mutex (a crashed
    /// earlier instance) counts as acquired.</summary>
    internal bool TryAcquire()
    {
        string sid = WindowsIdentity.GetCurrent().User!.Value;
        _mutex = new Mutex(initiallyOwned: false, "Local\\CanvasWM-" + sid);
        try
        {
            _owned = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            _owned = true;
        }

        return _owned;
    }

    public void Dispose()
    {
        if (_mutex is null)
        {
            return;
        }

        try
        {
            if (_owned)
            {
                _mutex.ReleaseMutex();
            }
        }
        catch (Exception)
        {
            // Released by the OS at process exit.
        }

        _mutex.Dispose();
        _mutex = null;
    }
}
