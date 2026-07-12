using System.Text.RegularExpressions;

namespace Steno.Core.Transcription;

/// <summary>
/// Drops the phrases whisper.cpp invents out of silence.
///
/// Whisper was trained on YouTube subtitles, so when it is handed noise or near-silence it falls
/// back on what those subtitle tracks contain during quiet passages: "Продолжение следует...",
/// subtitle-author credits, "Спасибо за просмотр!", "[Музыка]". In Russian, "Продолжение
/// следует..." is overwhelmingly the most common.
///
/// This cannot be filtered by confidence. Measured on this model: given *pure silence*, whisper
/// returns "Продолжение следует..." with a no-speech probability of 0.000 and a mean token
/// confidence of 0.85. It is not unsure — it is confidently wrong. The only usable signal is the
/// content itself.
/// </summary>
public static class HallucinationFilter
{
    /// <summary>
    /// Matched against the whole utterance, after normalisation. These are things nobody says on
    /// a phone call, and that whisper says constantly when nobody is speaking at all.
    /// </summary>
    private static readonly string[] Phrases =
    [
        // Russian — the YouTube subtitle furniture.
        "продолжение следует",
        "спасибо за просмотр",
        "спасибо за внимание",
        "подписывайтесь на канал",
        "подписывайтесь",
        "ставьте лайки",
        "до новых встреч",
        "всем пока",
        "музыка",
        "аплодисменты",
        "смех",

        // English equivalents.
        "thanks for watching",
        "thank you for watching",
        "please subscribe",
        "music",
        "applause",
        "silence",
        "blank_audio"
    ];

    /// <summary>Subtitle credits: "Субтитры сделал DimaTorzok", "Редактор субтитров …", "Subtitles by …".</summary>
    private static readonly Regex Credits = new(
        @"^(субтитры|редактор субтитров|корректор|subtitles?|subs)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Noise = new(@"[^\p{L}\p{Nd}\s]", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    /// <summary>True when the line is whisper talking to itself and should never reach the transcript.</summary>
    public static bool IsHallucination(string? text)
    {
        var normalized = Normalize(text);

        if (normalized.Length == 0)
            return true;

        if (Credits.IsMatch(normalized))
            return true;

        // Whole-line match only. A caller who genuinely says "продолжение следует" inside a real
        // sentence keeps it; it is the utterance that consists of nothing else that is fake.
        return Phrases.Contains(normalized, StringComparer.Ordinal);
    }

    /// <summary>Lower-cased, punctuation and ellipses stripped, whitespace collapsed.</summary>
    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var stripped = Noise.Replace(text, " ");
        return Spaces.Replace(stripped, " ").Trim().ToLowerInvariant();
    }
}
