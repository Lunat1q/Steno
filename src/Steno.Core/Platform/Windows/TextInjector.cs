using System.Runtime.InteropServices;
using Steno.Core.Dictation;

namespace Steno.Core.Platform.Windows;

/// <summary>
/// Types text into whatever window currently has keyboard focus, as if the user had typed it —
/// this is what lets dictation land words in another app's text box (ADR 0025).
///
/// It uses SendInput with Unicode key events, so it does not care what character it is sending or
/// which keyboard layout is active: emoji, accents and CJK all go through unchanged.
/// </summary>
public static class TextInjector
{
    // ponytail: SendInput to the focused control. No UI Automation, no per-app hooks — the OS
    // routes synthetic input to focus exactly like a real keyboard, add UIA only if an app rejects it.

    /// <summary>
    /// Sends <paramref name="text"/> to the foreground window. Returns false without sending if the
    /// current process owns the foreground window, so a stray focus on our own UI never self-types.
    /// </summary>
    public static bool Type(string text) => Apply(new TypingEdit(0, text));

    /// <summary>
    /// Rubs out <see cref="TypingEdit.Backspaces"/> characters and types <see cref="TypingEdit.Insert"/>,
    /// as one SendInput batch — the OS then delivers the whole correction without the user's own
    /// keystrokes interleaving halfway through it.
    /// </summary>
    public static bool Apply(TypingEdit edit)
    {
        if (edit.IsNothing || IsOwnWindowForeground())
            return false;

        // Two INPUTs per keystroke (down then up). Each UTF-16 code unit is one keystroke, so
        // surrogate pairs are just two of them, which SendInput reassembles on the other side.
        var inputs = new INPUT[(edit.Backspaces + edit.Insert.Length) * 2];
        var n = 0;

        for (var i = 0; i < edit.Backspaces; i++)
        {
            inputs[n++] = VirtualKeyInput(VK_BACK, down: true);
            inputs[n++] = VirtualKeyInput(VK_BACK, down: false);
        }

        foreach (var character in edit.Insert)
        {
            inputs[n++] = UnicodeInput(character, down: true);
            inputs[n++] = UnicodeInput(character, down: false);
        }

        // cbSize MUST be the real sizeof(INPUT). The union has to be as big as its largest member
        // (MOUSEINPUT), or the marshalled struct is short, cbSize mismatches, and SendInput sends
        // nothing while returning 0 — which is exactly "dictation types nothing at all".
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
    }

    /// <summary>
    /// Which window would receive the keystrokes right now. Dictation tracks this so that moving
    /// focus mid-sentence abandons the correction instead of backspacing into a different app.
    /// </summary>
    public static IntPtr ForegroundWindow() => GetForegroundWindow();

    private static bool IsOwnWindowForeground()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
            return false;

        GetWindowThreadProcessId(foreground, out var processId);
        return processId == Environment.ProcessId;
    }

    /// <summary>A character, sent by value — no layout, no scan code, no dead keys.</summary>
    private static INPUT UnicodeInput(char character, bool down) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wScan = character,
                dwFlags = KEYEVENTF_UNICODE | (down ? 0u : KEYEVENTF_KEYUP)
            }
        }
    };

    /// <summary>An actual key. Backspace has no character to send, so it goes as a virtual key.</summary>
    private static INPUT VirtualKeyInput(ushort key, bool down) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = key,
                dwFlags = down ? 0u : KEYEVENTF_KEYUP
            }
        }
    };

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const ushort VK_BACK = 0x08;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    // Explicit union of every INPUT variant. MOUSEINPUT is the largest, so it fixes the struct's
    // size even though dictation only ever fills in the keyboard variant.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
