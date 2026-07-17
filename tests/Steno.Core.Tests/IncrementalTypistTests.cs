using Steno.Core.Dictation;
using Xunit;

namespace Steno.Core.Tests;

public class IncrementalTypistTests
{
    private readonly IncrementalTypist _typist = new();
    private readonly Guid _utterance = Guid.NewGuid();

    [Fact]
    public void The_first_draft_is_typed_whole()
    {
        var edit = _typist.Next(_utterance, "so I think", isFinal: false);

        Assert.Equal(0, edit.Backspaces);
        Assert.Equal("so I think", edit.Insert);
    }

    [Fact]
    public void A_draft_that_only_grows_types_the_new_words_and_nothing_else()
    {
        _typist.Next(_utterance, "so I think", isFinal: false);

        var edit = _typist.Next(_utterance, "so I think we", isFinal: false);

        // The whole point: no backspacing over text that did not change.
        Assert.Equal(0, edit.Backspaces);
        Assert.Equal(" we", edit.Insert);
    }

    [Fact]
    public void A_correction_rubs_out_only_what_diverged()
    {
        _typist.Next(_utterance, "recognize", isFinal: false);

        var edit = _typist.Next(_utterance, "recognise this", isFinal: false);

        // "recogni" survives; only "ze" goes.
        Assert.Equal(2, edit.Backspaces);
        Assert.Equal("se this", edit.Insert);
    }

    [Fact]
    public void The_final_draft_commits_with_a_trailing_space()
    {
        _typist.Next(_utterance, "hello", isFinal: false);

        var edit = _typist.Next(_utterance, "hello", isFinal: true);

        Assert.Equal(0, edit.Backspaces);
        Assert.Equal(" ", edit.Insert);
    }

    [Fact]
    public void The_next_utterance_is_never_backspaced_into_the_last_one()
    {
        _typist.Next(_utterance, "first sentence", isFinal: true);

        // A new id means the previous text is committed — the user's now, not ours to rub out.
        var edit = _typist.Next(Guid.NewGuid(), "second", isFinal: false);

        Assert.Equal(0, edit.Backspaces);
        Assert.Equal("second", edit.Insert);
    }

    [Fact]
    public void An_empty_draft_does_not_erase_what_is_already_typed()
    {
        _typist.Next(_utterance, "hello", isFinal: false);

        // whisper hedging mid-word must not make the text flicker away and come back.
        var edit = _typist.Next(_utterance, "   ", isFinal: false);

        Assert.True(edit.IsNothing);
    }

    [Fact]
    public void Resetting_abandons_the_correction_instead_of_backspacing_elsewhere()
    {
        _typist.Next(_utterance, "hello", isFinal: false);

        // Focus moved: those characters are in another window now.
        _typist.Reset();
        var edit = _typist.Next(_utterance, "hello there", isFinal: false);

        Assert.Equal(0, edit.Backspaces);
        Assert.Equal("hello there", edit.Insert);
    }

    [Fact]
    public void A_shrinking_draft_rubs_out_the_tail()
    {
        _typist.Next(_utterance, "hello there world", isFinal: false);

        var edit = _typist.Next(_utterance, "hello there", isFinal: false);

        Assert.Equal(6, edit.Backspaces);
        Assert.Equal(string.Empty, edit.Insert);
    }
}
