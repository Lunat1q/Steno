using Steno.Core.Transcription;
using Steno.Core.Whisper;
using Xunit;

namespace Steno.Core.Tests;

/// <summary>
/// whisper.cpp splits a character across tokens whenever it feels like it, and Whisper.net decodes
/// each token alone — so half of "Й" arrives as U+FFFD. The segment text is intact; the tokens are
/// rebuilt from it.
/// </summary>
public class WhisperTokenRestoreTests
{
    [Fact]
    public void A_character_split_across_two_tokens_is_rebuilt_from_the_segment_text()
    {
        var pieces = Tokens((" Мо", 0.9f), ("�", 0.5f), ("�", 0.4f), (" день", 0.8f));

        var restored = WhisperTranscriber.Restore(pieces, " Мой день");

        Assert.Equal([" Мо", "й", " день"], restored.Select(token => token.Text));
        Assert.Equal(0.4f, restored[1].Probability); // the run keeps the least sure of its tokens
    }

    [Fact]
    public void A_token_that_trails_off_mid_character_is_rebuilt_with_it()
    {
        var pieces = Tokens(("Ма�", 0.7f), ("�айский", 0.6f), ("!", 0.9f));

        var restored = WhisperTranscriber.Restore(pieces, "Майский!");

        Assert.Equal(["Майский", "!"], restored.Select(token => token.Text));
    }

    [Fact]
    public void A_broken_run_at_the_end_takes_the_rest_of_the_segment()
    {
        var pieces = Tokens(("Да", 0.9f), ("�", 0.5f), ("�", 0.5f));

        var restored = WhisperTranscriber.Restore(pieces, "Дай");

        Assert.Equal(["Да", "й"], restored.Select(token => token.Text));
    }

    [Fact]
    public void Intact_tokens_are_left_alone()
    {
        var pieces = Tokens((" Hello", 0.9f), (" there", 0.8f));

        Assert.Same(pieces, WhisperTranscriber.Restore(pieces, " Hello there"));
    }

    private static TranscriptToken[] Tokens(params (string Text, float Probability)[] pieces) =>
        [.. pieces.Select(piece => new TranscriptToken(piece.Text, piece.Probability))];
}
