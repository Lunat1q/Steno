using Microsoft.Extensions.Logging;
using Steno.Core.Abstractions;
using Steno.Core.Transcription;

namespace Steno.Core.Whisper;

/// <summary>
/// Locates the GGML model file whisper.cpp needs, downloading it once into
/// %LOCALAPPDATA%/Steno/models. GGML is the only format whisper.cpp reads (ADR 0001).
///
/// Downloads straight from the ggml-org model repository rather than through Whisper.net's
/// bundled downloader: that one hands back a non-seekable stream with no length, so a
/// progress bar over it can only spin. A 1.5 GB download with no visible progress reads as
/// a hang — see ADR 0009.
/// </summary>
public sealed class WhisperModelProvider : IWhisperModelProvider
{
    private const string ModelRepository = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main";

    private readonly string _cacheDirectory;
    private readonly ILogger<WhisperModelProvider> _logger;
    private readonly HttpClient _http;

    public WhisperModelProvider(ILogger<WhisperModelProvider> logger, string? cacheDirectory = null)
    {
        _logger = logger;
        _cacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Steno",
            "models");

        // Multi-GB files over a slow link: the default 100 s timeout would kill them mid-download.
        _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    public bool IsDownloaded(WhisperModel model) => File.Exists(PathFor(model));

    public string PathFor(WhisperModel model) =>
        Path.Combine(_cacheDirectory, $"ggml-{FileNameFor(model)}.bin");

    public async Task<string> GetModelPathAsync(
        WhisperModel model,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var path = PathFor(model);
        if (File.Exists(path))
            return path;

        Directory.CreateDirectory(_cacheDirectory);

        var url = $"{ModelRepository}/ggml-{FileNameFor(model)}.bin";
        _logger.LogInformation("Downloading whisper.cpp model {Model} from {Url}", model, url);

        // Download to a temp file: a half-written .bin that looks cached is worse than no cache,
        // because whisper.cpp would then fail to load it on every future run.
        var temporaryPath = path + ".part";
        try
        {
            await DownloadAsync(url, temporaryPath, progress, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);

            _logger.LogInformation("Model {Model} ready at {Path}", model, path);
            return path;
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    private async Task DownloadAsync(
        string url,
        string destinationPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        // ResponseHeadersRead: start streaming as soon as the headers land, instead of buffering
        // gigabytes into memory first. It is also what makes Content-Length available up front.
        using var response = await _http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? 0L;

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = File.Create(destinationPath);

        var buffer = new byte[256 * 1024];
        long copied = 0;
        var lastReported = -1d;
        int read;

        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            copied += read;

            if (total <= 0)
                continue;

            // Report at whole percent only: a 1.5 GB file is ~6000 buffers, and firing a UI
            // update for each is pure churn.
            var fraction = Math.Round((double)copied / total, 2);
            if (fraction <= lastReported)
                continue;

            lastReported = fraction;
            progress?.Report(fraction);
        }

        progress?.Report(1d);
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException ex)
        {
            // Next run overwrites it. Not worth failing the download for.
            _logger.LogDebug(ex, "Could not remove partial download {Path}", path);
        }
    }

    /// <summary>File names as published by ggml-org/whisper.cpp. These are the exact model files.</summary>
    private static string FileNameFor(WhisperModel model) => model switch
    {
        WhisperModel.Tiny => "tiny",
        WhisperModel.Base => "base",
        WhisperModel.Small => "small",
        WhisperModel.Medium => "medium",
        WhisperModel.LargeV3 => "large-v3",
        WhisperModel.LargeV3Turbo => "large-v3-turbo",
        _ => throw new ArgumentOutOfRangeException(nameof(model), model, "Unknown whisper.cpp model")
    };
}
