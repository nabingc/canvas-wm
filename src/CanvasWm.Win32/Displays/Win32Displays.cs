using CanvasWm.Core.Displays;
using CanvasWm.Engine.Services;
using CanvasWm.Win32.Coordinates;
using CanvasWm.Win32.Interop;
using CanvasWm.Win32.Windows;

namespace CanvasWm.Win32.Displays;

/// <summary>Current displays as DisplayInfo (B56, B57). Never throws for OS failures.</summary>
public static unsafe class Win32Displays
{
    private const int PathInfoSize = 72;
    private const int ModeInfoSize = 64;
    private const int HeaderSize = 20;
    private const int SourceNameSize = 84;
    private const int TargetNameSize = 420;
    private const int SourceNameOffset = HeaderSize;
    private const int TargetPathOffset = 20 + 4 + 4 + 2 + 2 + 4 + 128;
    private const int NameChars = 32;
    private const int PathChars = 128;
    private const int Attempts = 3;

    /// <summary>Reads the monitors and their display paths now.</summary>
    /// <returns>One entry per readable monitor, in enumeration order; empty when none.</returns>
    public static IReadOnlyList<DisplayInfo> Snapshot()
    {
        var result = new List<DisplayInfo>();
        try
        {
            using var dpi = new DpiScope();
            IReadOnlyList<MonitorInfo> monitors = Monitors.Enumerate();
            var rows = new List<(MonitorInfo Monitor, PixelRect Work, bool Primary, string? Device)>();
            foreach (MonitorInfo m in monitors)
            {
                var mi = new NativeMonitorInfoEx { cbSize = (uint)sizeof(NativeMonitorInfoEx) };
                if (!NativeMethods.GetMonitorInfoExW(m.Handle, ref mi))
                {
                    continue;
                }

                string device = new((char*)mi.szDevice);
                rows.Add((m, mi.rcWork.ToPixelRect(), (mi.dwFlags & NativeConstants.MONITORINFOF_PRIMARY) != 0, device));
            }

            IReadOnlyList<DisplayPathName> paths = ReadPaths();
            IReadOnlyList<string> keys = DisplayKeyChoice.Assign(rows.Select(r => r.Device).ToArray(), paths);
            for (int i = 0; i < rows.Count; i++)
            {
                var (m, work, primary, _) = rows[i];
                try
                {
                    result.Add(new DisplayInfo(
                        new DisplayKey(keys[i]),
                        m.DipFrame,
                        DipSpace.ToDip(work, m.Bounds, m.Dpi),
                        (int)m.Dpi,
                        primary));
                }
                catch (Exception)
                {
                    // A display that cannot be described is left out.
                }
            }
        }
        catch (Exception)
        {
            // Never throws for OS failures: fewer entries.
        }

        return result;
    }

    private static IReadOnlyList<DisplayPathName> ReadPaths()
    {
        var list = new List<DisplayPathName>();
        try
        {
            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                if (NativeMethods.GetDisplayConfigBufferSizes(NativeConstants.QDC_ONLY_ACTIVE_PATHS,
                        out uint pathCount, out uint modeCount) != 0)
                {
                    return list;
                }

                byte[] pathBuf = new byte[Math.Max(1, (int)pathCount) * PathInfoSize];
                byte[] modeBuf = new byte[Math.Max(1, (int)modeCount) * ModeInfoSize];
                int rc;
                fixed (byte* pp = pathBuf)
                fixed (byte* mp = modeBuf)
                {
                    rc = NativeMethods.QueryDisplayConfig(NativeConstants.QDC_ONLY_ACTIVE_PATHS,
                        ref pathCount, pp, ref modeCount, mp, 0);
                }

                if (rc == NativeConstants.ERROR_INSUFFICIENT_BUFFER)
                {
                    continue;
                }

                if (rc != 0)
                {
                    return list;
                }

                for (int i = 0; i < (int)pathCount; i++)
                {
                    int o = i * PathInfoSize;
                    // sourceInfo: adapterId (LUID, 8 bytes), id (4); targetInfo starts at 20: adapterId, id.
                    long srcAdapter = BitConverter.ToInt64(pathBuf, o);
                    uint srcId = BitConverter.ToUInt32(pathBuf, o + 8);
                    long tgtAdapter = BitConverter.ToInt64(pathBuf, o + 20);
                    uint tgtId = BitConverter.ToUInt32(pathBuf, o + 28);
                    list.Add(new DisplayPathName(
                        SourceName(srcAdapter, srcId),
                        TargetPath(tgtAdapter, tgtId)));
                }

                return list;
            }
        }
        catch (Exception)
        {
            // Total failure: keys fall back to the GDI device names.
        }

        return list;
    }

    private static string? SourceName(long adapter, uint id)
    {
        byte* buf = stackalloc byte[SourceNameSize];
        new Span<byte>(buf, SourceNameSize).Clear();
        WriteHeader(buf, NativeConstants.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME, SourceNameSize, adapter, id);
        if (NativeMethods.DisplayConfigGetDeviceInfo(buf) != 0)
        {
            return null;
        }

        return ReadString(buf + SourceNameOffset, NameChars);
    }

    private static string? TargetPath(long adapter, uint id)
    {
        byte* buf = stackalloc byte[TargetNameSize];
        new Span<byte>(buf, TargetNameSize).Clear();
        WriteHeader(buf, NativeConstants.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME, TargetNameSize, adapter, id);
        if (NativeMethods.DisplayConfigGetDeviceInfo(buf) != 0)
        {
            return null;
        }

        return ReadString(buf + TargetPathOffset, PathChars);
    }

    private static void WriteHeader(byte* buf, int type, int size, long adapter, uint id)
    {
        *(int*)buf = type;
        *(uint*)(buf + 4) = (uint)size;
        *(long*)(buf + 8) = adapter;
        *(uint*)(buf + 16) = id;
    }

    private static string? ReadString(byte* p, int maxChars)
    {
        var chars = (char*)p;
        int len = 0;
        while (len < maxChars && chars[len] != '\0')
        {
            len++;
        }

        return len == 0 ? null : new string(chars, 0, len);
    }
}
