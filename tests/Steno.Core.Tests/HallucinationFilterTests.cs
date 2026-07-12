using Steno.Core.Transcription;
using Xunit;

namespace Steno.Core.Tests;

/// <summary>
/// whisper.cpp fills silence with the subtitle boilerplate it was trained on. Measured against
/// this model, pure silence yields "Продолжение следует..." at no-speech 0.000 and confidence
/// 0.85 — so the content is the only thing that gives it away (ADR 0019).
/// </summary>
public class HallucinationFilterTests
{
    [Theory]
    [InlineData("Продолжение следует...")]
    [InlineData("продолжение следует")]
    [InlineData("Продолжение следует…")]
    [InlineData("Спасибо за просмотр!")]
    [InlineData("Подписывайтесь на канал!")]
    [InlineData("Субтитры сделал DimaTorzok")]
    [InlineData("Редактор субтитров А.Синецкая")]
    [InlineData("Thanks for watching!")]
    [InlineData("Subtitles by the Amara.org community")]
    [InlineData("[Музыка]")]
    [InlineData("")]
    [InlineData("   ")]
    public void Invented_subtitle_furniture_is_dropped(string text) =>
        Assert.True(HallucinationFilter.IsHallucination(text), $"should have dropped: '{text}'");

    [Theory]
    // Real speech must survive, including speech that happens to contain the words.
    [InlineData("Да, я согласен, давай продолжим завтра.")]
    [InlineData("Продолжение следует за первым этапом, как мы договорились.")]
    [InlineData("Спасибо за просмотр документов, которые я отправил.")]
    [InlineData("Привет, как дела?")]
    [InlineData("Музыкальное образование он получил в Москве.")]
    [InlineData("Thanks for watching the demo, what did you think?")]
    public void Real_speech_is_kept(string text) =>
        Assert.False(HallucinationFilter.IsHallucination(text), $"should have kept: '{text}'");
}
