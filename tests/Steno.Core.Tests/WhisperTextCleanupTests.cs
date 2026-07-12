using Steno.Core.Whisper;
using Xunit;

namespace Steno.Core.Tests;

/// <summary>whisper.cpp annotates non-speech inline. None of it is dialogue.</summary>
public class WhisperTextCleanupTests
{
    [Theory]
    [InlineData(" Привет, как дела?", "Привет, как дела?")]
    [InlineData("[BLANK_AUDIO]", "")]
    [InlineData(" (музыка) Да, я согласен.", "Да, я согласен.")]
    [InlineData("*sighs* Ладно.", "Ладно.")]
    [InlineData("Так   вот,\n я думаю", "Так вот, я думаю")]
    public void Annotations_and_padding_are_stripped(string raw, string expected) =>
        Assert.Equal(expected, WhisperTranscriber.Clean(raw));
}
