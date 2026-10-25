using System.ComponentModel;
using System.Runtime.InteropServices;
using CanvasWm.Win32.Interop;

namespace CanvasWm.Win32.Hosting;

/// <summary>A waitable timer for the tick thread, high resolution when the OS supports it.
/// One waiting thread at a time.</summary>
public sealed unsafe class HighResolutionTimer : IDisposable
{
    private nint _handle;

    /// <summary>Creates the timer.</summary>
    /// <exception cref="Win32Exception">no timer could be created.</exception>
    public HighResolutionTimer()
    {
        nint h = NativeMethods.CreateWaitableTimerExW(0, null,
            NativeConstants.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, NativeConstants.TIMER_ALL_ACCESS);
        if (h != 0)
        {
            IsHighResolution = true;
        }
        else
        {
            h = NativeMethods.CreateWaitableTimerExW(0, null, 0, NativeConstants.TIMER_ALL_ACCESS);
            if (h == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }

        _handle = h;
    }

    /// <summary>True when the OS granted the high-resolution flavour.</summary>
    public bool IsHighResolution { get; }

    /// <summary>Blocks the calling thread for seconds (relative). True on success.</summary>
    /// <param name="seconds">The delay, 0 or more.</param>
    /// <returns>True when the wait completed normally.</returns>
    /// <exception cref="ArgumentOutOfRangeException">seconds negative or not finite.</exception>
    /// <exception cref="ObjectDisposedException">after Dispose.</exception>
    public bool Wait(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "Must be finite and not negative.");
        }

        nint h = Volatile.Read(ref _handle);
        ObjectDisposedException.ThrowIf(h == 0, this);
        if (seconds == 0)
        {
            return true;
        }

        // Relative due time: negative, in units of 100 ns.
        long due = -(long)Math.Min(Math.Ceiling(seconds * 10_000_000.0), long.MaxValue / 2);
        if (!NativeMethods.SetWaitableTimer(h, &due, 0, 0, 0, false))
        {
            return false;
        }

        return NativeMethods.WaitForSingleObject(h, NativeConstants.INFINITE) == NativeConstants.WAIT_OBJECT_0;
    }

    /// <summary>Closes the timer. Idempotent.</summary>
    public void Dispose()
    {
        nint h = Interlocked.Exchange(ref _handle, 0);
        if (h != 0)
        {
            _ = NativeMethods.CloseHandle(h);
        }
    }
}
