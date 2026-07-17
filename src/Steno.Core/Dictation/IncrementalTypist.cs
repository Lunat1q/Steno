namespace Steno.Core.Dictation;

/// <summary>One keyboard edit: rub out <paramref name="Backspaces"/> characters, then type <paramref name="Insert"/>.</summary>
public readonly record struct TypingEdit(int Backspaces, string Insert)
{
    public static TypingEdit None { get; } = new(0, string.Empty);

    public bool IsNothing => Backspaces == 0 && Insert.Length == 0;
}

/// <summary>
/// Turns a stream of whisper drafts into the smallest keystroke edit that makes the target app's
/// text match the latest draft — so words appear as they are spoken and are corrected in place,
/// instead of arriving in a lump when the sentence ends (ADR 0025).
///
/// Every draft of an utterance re-transcribes the same growing audio buffer and shares the
/// utterance's id, so successive drafts mostly agree on their leading text. The edit is therefore:
/// keep the common prefix, backspace whatever diverged, type the rest.
///
///     "so I think"  →  "so I think we"        0 backspaces, type " we"
///     "recognize"   →  "recognise this"       2 backspaces, type "se this"
///
/// Pure state machine, no Win32 — <see cref="Platform.Windows.TextInjector"/> performs the edit.
/// </summary>
public sealed class IncrementalTypist
{
    /// <summary>What this typist believes is currently on screen for the utterance in progress.</summary>
    private string _typed = string.Empty;
    private Guid _utterance;

    /// <summary>
    /// The edit that brings the target app from the previous draft to <paramref name="text"/>.
    /// A final draft is committed with a trailing space and the next utterance starts fresh.
    /// </summary>
    public TypingEdit Next(Guid utteranceId, string text, bool isFinal)
    {
        // A new utterance means the last one is done and can never be corrected again: whatever it
        // typed is now the user's text, not ours to backspace over.
        if (utteranceId != _utterance)
        {
            _utterance = utteranceId;
            _typed = string.Empty;
        }

        var spoken = text.Trim();

        // An empty draft is whisper hedging mid-word, not the user deleting what they just said.
        // Erasing on it would make the text flicker away and come back.
        if (spoken.Length == 0 && !isFinal)
            return TypingEdit.None;

        var target = isFinal && spoken.Length > 0 ? spoken + " " : spoken;
        var common = CommonPrefixLength(_typed, target);
        var edit = new TypingEdit(_typed.Length - common, target[common..]);

        _typed = target;
        return edit;
    }

    /// <summary>
    /// Forget what we typed, without touching it. Used when focus moves to another window: the
    /// characters we were tracking are in a different app now, and backspacing would eat text
    /// nobody dictated.
    /// </summary>
    public void Reset()
    {
        _typed = string.Empty;
        _utterance = default;
    }

    private static int CommonPrefixLength(string a, string b)
    {
        var max = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < max && a[i] == b[i])
            i++;

        return i;
    }
}
