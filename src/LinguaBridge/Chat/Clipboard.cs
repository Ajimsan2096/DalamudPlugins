using System;
using System.Runtime.InteropServices;

namespace LinguaBridge.Chat;

/// <summary>
/// Reads the Windows clipboard as text. Works from any thread (commands run outside the ImGui frame),
/// and under Wine. Writing goes through ImGui.SetClipboardText in a Draw handler.
/// </summary>
internal static partial class Clipboard
{
    private const uint UnicodeText = 13; // CF_UNICODETEXT

    public static string? GetText()
    {
        try
        {
            if (!IsClipboardFormatAvailable(UnicodeText))
                return null;
            // Another program may hold the clipboard for a moment.
            for (var attempt = 0; attempt < 5; attempt++)
            {
                if (OpenClipboard(IntPtr.Zero))
                {
                    try
                    {
                        var handle = GetClipboardData(UnicodeText);
                        if (handle == IntPtr.Zero)
                            return null;
                        var pointer = GlobalLock(handle);
                        if (pointer == IntPtr.Zero)
                            return null;
                        try
                        {
                            return Marshal.PtrToStringUni(pointer);
                        }
                        finally
                        {
                            GlobalUnlock(handle);
                        }
                    }
                    finally
                    {
                        CloseClipboard();
                    }
                }
                System.Threading.Thread.Sleep(20);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Not on Windows/Wine: no clipboard to read.
        }
        return null;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(IntPtr owner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr GetClipboardData(uint format);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalLock(IntPtr memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(IntPtr memory);
}
