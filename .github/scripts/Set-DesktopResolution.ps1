# WPF windows are constrained by the host desktop. Establish the acceptance-test
# baseline before the test process reads/caches any Windows display metrics.
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class AcceptanceDisplay
{
    [StructLayout(LayoutKind.Explicit, Size = 220)]
    public struct Mode
    {
        [FieldOffset(68)] public ushort Size;
        [FieldOffset(72)] public uint Fields;
        [FieldOffset(172)] public uint Width;
        [FieldOffset(176)] public uint Height;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettingsW(string device, int index, ref Mode mode);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int ChangeDisplaySettingsW(ref Mode mode, uint flags);
    public static string Set(uint width, uint height)
    {
        var mode = new Mode { Size = (ushort)Marshal.SizeOf<Mode>() };
        if (!EnumDisplaySettingsW(null, -1, ref mode)) throw new Exception("Cannot query the runner display mode.");
        mode.Width = width; mode.Height = height;
        mode.Fields = 0x00080000 | 0x00100000; // DM_PELSWIDTH | DM_PELSHEIGHT
        int result = ChangeDisplaySettingsW(ref mode, 0); // Temporary; do not update the registry.
        if (result != 0) throw new Exception("Cannot establish the 1080p acceptance desktop. Display result: " + result);
        var actual = new Mode { Size = (ushort)Marshal.SizeOf<Mode>() };
        if (!EnumDisplaySettingsW(null, -1, ref actual) || actual.Width != width || actual.Height != height)
            throw new Exception("The runner did not apply the requested display size.");
        return "Acceptance desktop: " + actual.Width + " x " + actual.Height;
    }
}
'@
[AcceptanceDisplay]::Set(1920, 1080)
