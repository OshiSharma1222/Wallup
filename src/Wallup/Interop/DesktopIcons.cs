using System.Runtime.InteropServices;
using static Wallup.Interop.NativeMethods;

namespace Wallup.Interop;

/// <summary>
/// Tells a desktop icon from the bare desktop around it. Both are the same window - the
/// icon list view covers the whole screen - so the only way to know is to ask the list
/// view itself with LVM_HITTEST.
///
/// The catch is that LVM_HITTEST takes a pointer, and the list view lives in Explorer.
/// A pointer into our own memory means nothing there, so the hit-test struct is written
/// into a scratch page allocated inside Explorer, the message is sent with that page's
/// address, and the answer is read back out. Both are 64-bit, so the layout matches.
/// </summary>
internal static class DesktopIcons
{
    private const uint LVM_FIRST = 0x1000;
    private const uint LVM_GETITEMCOUNT = LVM_FIRST + 4;
    private const uint LVM_GETITEMRECT = LVM_FIRST + 14;
    private const uint LVM_HITTEST = LVM_FIRST + 18;
    private const int LVIR_ICON = 1;

    /// <summary>
    /// Explorer answering slower than this is treated as "no icon", which is how every
    /// click was treated before. It is called from inside the mouse hook, so it must be
    /// short: a hook that stalls stalls the whole pointer.
    /// </summary>
    private const uint TimeoutMs = 50;

    [StructLayout(LayoutKind.Sequential)]
    private struct LVHITTESTINFO
    {
        public POINT pt;
        public uint flags;
        public int iItem;
        public int iSubItem;
        public int iGroup;
    }

    /// <summary>True when the screen point is over an icon or its label.</summary>
    internal static bool IsIconAt(IntPtr listView, POINT screen)
    {
        var client = screen;
        if (!ScreenToClient(listView, ref client))
        {
            return false;
        }

        using var page = ExplorerPage.Open(listView, Marshal.SizeOf<LVHITTESTINFO>());
        if (page is null || !page.Write(new LVHITTESTINFO { pt = client }))
        {
            return false;
        }

        return Send(listView, LVM_HITTEST, IntPtr.Zero, page.Address) is { } item && item.ToInt64() >= 0;
    }

    /// <summary>The centre of every icon on the desktop, in screen pixels. For --diagnose.</summary>
    internal static IReadOnlyList<POINT> IconCentres(IntPtr listView)
    {
        var count = Send(listView, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero)?.ToInt32() ?? 0;
        var centres = new List<POINT>(count);

        using var page = ExplorerPage.Open(listView, Marshal.SizeOf<RECT>());
        if (page is null)
        {
            return centres;
        }

        for (var i = 0; i < count; i++)
        {
            // LVM_GETITEMRECT reads which part of the item to measure from rect.left.
            if (!page.Write(new RECT { Left = LVIR_ICON })
                || Send(listView, LVM_GETITEMRECT, i, page.Address) is not { } ok || ok == IntPtr.Zero
                || page.Read<RECT>() is not { } rect)
            {
                continue;
            }

            var centre = new POINT { X = rect.Left + rect.Width / 2, Y = rect.Top + rect.Height / 2 };
            ClientToScreen(listView, ref centre);
            centres.Add(centre);
        }

        return centres;
    }

    private static IntPtr? Send(IntPtr listView, uint msg, IntPtr wParam, IntPtr lParam) =>
        SendMessageTimeout(listView, msg, wParam, lParam, SMTO_ABORTIFHUNG, TimeoutMs, out var result) == IntPtr.Zero
            ? null
            : result;

    /// <summary>A scratch page inside the process that owns a window.</summary>
    private sealed class ExplorerPage : IDisposable
    {
        private readonly IntPtr _process;
        private readonly int _size;

        private ExplorerPage(IntPtr process, IntPtr address, int size)
        {
            _process = process;
            Address = address;
            _size = size;
        }

        internal IntPtr Address { get; }

        internal static ExplorerPage? Open(IntPtr window, int size)
        {
            GetWindowThreadProcessId(window, out var pid);
            var process = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, pid);
            if (process == IntPtr.Zero)
            {
                return null;
            }

            var address = VirtualAllocEx(process, IntPtr.Zero, (nuint)size, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
            if (address == IntPtr.Zero)
            {
                CloseHandle(process);
                return null;
            }

            return new ExplorerPage(process, address, size);
        }

        internal bool Write<T>(T value) where T : unmanaged
        {
            var bytes = MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in value)).ToArray();
            return WriteProcessMemory(_process, Address, bytes, (nuint)bytes.Length, out _);
        }

        internal T? Read<T>() where T : unmanaged
        {
            var bytes = new byte[_size];
            return ReadProcessMemory(_process, Address, bytes, (nuint)bytes.Length, out _)
                ? MemoryMarshal.Read<T>(bytes)
                : null;
        }

        public void Dispose()
        {
            VirtualFreeEx(_process, Address, 0, MEM_RELEASE);
            CloseHandle(_process);
        }
    }
}
