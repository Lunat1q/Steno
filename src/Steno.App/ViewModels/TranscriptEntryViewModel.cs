using CommunityToolkit.Mvvm.ComponentModel;
using Steno.Core.Session;
using Steno.Core.Transcription;

namespace Steno.App.ViewModels;

/// <summary>One line of dialogue. Mutable because a partial is later replaced by its final text.</summary>
public sealed partial class TranscriptEntryViewModel : ObservableObject
{
    [ObservableProperty] private string _text;
    [ObservableProperty] private string? _translation;
    [ObservableProperty] private bool _isFinal;

    /// <summary>Per-word confidence. The view shades each word with it (whisper.cpp's --print-colors).</summary>
    [ObservableProperty] private IReadOnlyList<TranscriptToken> _tokens;

    /// <summary>Worst word in the line. Drives the "some words are uncertain" marker.</summary>
    public float LowestConfidence => Tokens.Count == 0 ? 1f : Tokens.Min(t => t.Probability);

    public bool HasUncertainWords => LowestConfidence < 0.55f;

    public TranscriptEntryViewModel(TranscriptEntry entry)
    {
        Id = entry.Id;
        Channel = entry.Channel;
        Speaker = entry.Speaker;
        Start = entry.Start;
        _text = entry.Text;
        _translation = entry.Translation;
        _isFinal = entry.IsFinal;
        _tokens = entry.Tokens;
    }

    public Guid Id { get; }

    public SpeakerChannel Channel { get; }

    public string Speaker { get; }

    public TimeSpan Start { get; }

    public bool IsLocal => Channel == SpeakerChannel.Local;

    public string Timestamp => Start.ToString(@"hh\:mm\:ss");

    public void Apply(TranscriptEntry entry)
    {
        Text = entry.Text;
        Translation = entry.Translation;
        IsFinal = entry.IsFinal;
        Tokens = entry.Tokens;

        OnPropertyChanged(nameof(LowestConfidence));
        OnPropertyChanged(nameof(HasUncertainWords));
    }
}
