using System.Diagnostics;
using System.Runtime.InteropServices;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Extensions;
namespace ZePractice;
public static class ZeHudText
{
    private static readonly Lazy<IntPtr> StringVtable = new(ResolveVtable);
    private static readonly Lazy<(long Start, long End)> NativeRange = new(() =>
    {
        using var process = Process.GetCurrentProcess();
        var module = process.Modules.Cast<ProcessModule>().Single(item => string.Equals(item.ModuleName, "counterstrikesharp.dll", StringComparison.OrdinalIgnoreCase));
        return (module.BaseAddress.ToInt64(), module.BaseAddress.ToInt64() + module.ModuleMemorySize);
    });
    private static IntPtr ResolveVtable()
    {
        if (!OperatingSystem.IsWindows()) return IntPtr.Zero;
        using var process = Process.GetCurrentProcess();
        string game = Server.GameDirectory;
        if (Directory.Exists(Path.Combine(game, "csgo"))) game = Path.Combine(game, "csgo");
        string expected = Path.GetFullPath(Path.Combine(game, "bin", "win64", "server.dll"));
        var module = process.Modules.Cast<ProcessModule>().Single(item => string.Equals(item.FileName, expected, StringComparison.OrdinalIgnoreCase));
        int offset = WindowsHudRtti.FindVtable(File.ReadAllBytes(module.FileName), "HUDPanelDialogVariableString_t");
        if (offset <= 0 || offset >= module.ModuleMemorySize - 8) throw new InvalidDataException("HUD vtable lies outside server module.");
        IntPtr address = module.BaseAddress + offset;
        long function = Marshal.ReadIntPtr(address).ToInt64() - module.BaseAddress.ToInt64();
        if (function < 0 || function >= module.ModuleMemorySize) throw new InvalidDataException("HUD vtable target lies outside loaded server module.");
        return address;
    }
    public static void Prepare()
    {
        if (!OperatingSystem.IsWindows()) return;
        _ = StringVtable.Value;
        _ = NativeRange.Value;
    }
    public static void Set(CCSCustomHudLayout layout, string panel, string variable, string value)
    {
        IntPtr vtable = StringVtable.Value;
        layout.SetDialogVariableString(panel, variable, value);
        if (vtable == IntPtr.Zero) return;
        var vector = layout.GlobalLayoutState.DialogVariableStrings;
        int count = vector.Count;
        int stride = Schema.GetClassSize("HUDPanelDialogVariableString_t");
        if (count < 0 || count > 256 || stride < 24 || stride > 128 || stride % 8 != 0) throw new InvalidDataException("Unexpected HUD string vector layout.");
        IntPtr entries = count == 0 ? IntPtr.Zero : NativeAPI.GetNetworkVectorElementAt(vector.Handle, 0);
        if (count > 0 && entries == IntPtr.Zero) throw new InvalidDataException("HUD string vector has no storage.");
        for (int index = 0; index < count; index++)
        {
            IntPtr entry = entries + checked(index * stride);
            IntPtr current = Marshal.ReadIntPtr(entry);
            var native = NativeRange.Value;
            if (current == IntPtr.Zero || (current.ToInt64() >= native.Start && current.ToInt64() < native.End)) Marshal.WriteIntPtr(entry, vtable);
            else if (current != vtable) throw new InvalidDataException($"HUD string vtable mismatch current=0x{current.ToInt64():X} expected=0x{vtable.ToInt64():X} count={count} stride={stride} panelOffset={Schema.GetSchemaOffset("HUDPanelDialogVariableString_t", "m_nPanelIdIndex")}.");
        }
    }
}
