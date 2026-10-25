namespace CanvasWm.Core.Displays;

/// <summary>One active display path as the OS reports it: the GDI source name it drives
/// (e.g. "\\.\DISPLAY1") and the monitor's device path. Either may be null or empty when
/// the OS could not report it.</summary>
/// <param name="SourceName">GDI source name.</param>
/// <param name="DevicePath">Monitor device path.</param>
public readonly record struct DisplayPathName(string? SourceName, string? DevicePath);
