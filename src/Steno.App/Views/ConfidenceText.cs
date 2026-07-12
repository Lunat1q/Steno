using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Steno.Core.Transcription;

namespace Steno.App.Views;

/// <summary>
/// Renders a transcript line one token at a time, shading each word by how sure whisper.cpp
/// was of it — the same per-token probability its `--print-colors` mode paints with.
///
/// Attached to a TextBlock rather than built into a custom control, because all it needs to do
/// is swap the Inlines collection:
///     &lt;TextBlock views:ConfidenceText.Tokens="{Binding Tokens}" /&gt;
///
/// If the token list is empty (an engine that reported none), the TextBlock's own Text is left
/// alone, so the line still renders.
/// </summary>
public static class ConfidenceText
{
    /// <summary>Above this, whisper heard the word clearly. Rendered at full strength — most words land here.</summary>
    private const float High = 0.85f;

    private const float Medium = 0.70f;

    /// <summary>
    /// Below this the word is closer to a guess than a transcription: it is drawn red *and*
    /// underlined, so the warning does not depend on being able to see the hue.
    /// </summary>
    private const float Low = 0.55f;

    public static readonly AttachedProperty<IReadOnlyList<TranscriptToken>?> TokensProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, IReadOnlyList<TranscriptToken>?>(
            "Tokens", typeof(ConfidenceText));

    static ConfidenceText() => TokensProperty.Changed.AddClassHandler<TextBlock>(OnTokensChanged);

    public static void SetTokens(TextBlock target, IReadOnlyList<TranscriptToken>? value) =>
        target.SetValue(TokensProperty, value);

    public static IReadOnlyList<TranscriptToken>? GetTokens(TextBlock target) =>
        target.GetValue(TokensProperty);

    private static void OnTokensChanged(TextBlock textBlock, AvaloniaPropertyChangedEventArgs args)
    {
        var tokens = args.NewValue as IReadOnlyList<TranscriptToken>;

        if (tokens is null || tokens.Count == 0)
        {
            textBlock.Inlines?.Clear();
            return;
        }

        var inlines = new InlineCollection();

        foreach (var token in tokens)
        {
            inlines.Add(new Run(token.Text)
            {
                Foreground = BrushFor(token.Probability),
                TextDecorations = token.Probability < Low ? Doubt : null
            });
        }

        textBlock.Inlines = inlines;
    }

    private static IBrush BrushFor(float probability)
    {
        var key = probability switch
        {
            >= High => "ConfidenceHigh",
            >= Medium => "ConfidenceMedium",
            >= Low => "ConfidenceLow",
            _ => "ConfidenceVeryLow"
        };

        return Resource(key) ?? Brushes.White;
    }

    private static readonly TextDecorationCollection Doubt =
    [
        new TextDecoration
        {
            Location = TextDecorationLocation.Underline,
            Stroke = Resource("ConfidenceUnderline") ?? Brushes.IndianRed,
            StrokeThickness = 1,
            StrokeDashArray = [2, 2]
        }
    ];

    private static IBrush? Resource(string key) =>
        Application.Current?.TryFindResource(key, out var value) == true ? value as IBrush : null;
}
