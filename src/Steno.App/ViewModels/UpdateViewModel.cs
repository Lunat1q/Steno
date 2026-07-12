using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Steno.App.Updates;

namespace Steno.App.ViewModels;

/// <summary>
/// Offers an update, and installs it only if the user says yes.
///
/// Deliberately unpushy: the check is silent, a failure is silent (being offline is not news),
/// and the banner can be dismissed. Installing means quitting the app, so it is never done
/// behind the user's back — and never while a call is being transcribed.
/// </summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    private readonly IUpdateChecker _checker;
    private readonly IUpdateInstaller _installer;
    private readonly ILogger<UpdateViewModel> _logger;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOffered), nameof(Headline))]
    private UpdateInfo? _available;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOffered))]
    private bool _isDismissed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOffered))]
    private bool _isInstalling;

    [ObservableProperty] private double _downloadPercent;
    [ObservableProperty] private string? _error;

    public UpdateViewModel(
        IUpdateChecker checker,
        IUpdateInstaller installer,
        ILogger<UpdateViewModel> logger)
    {
        _checker = checker;
        _installer = installer;
        _logger = logger;
    }

    public bool IsOffered => Available is not null && !IsDismissed && !IsInstalling;

    public string Headline => Available is null
        ? string.Empty
        : $"Steno {Available.Version} is available ({Available.SizeText}).";

    /// <summary>Fired in the background at startup. Never blocks anything, never nags on failure.</summary>
    public async Task CheckInBackgroundAsync()
    {
        try
        {
            Available = await _checker.CheckAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Update check failed");
        }
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (Available is not { } update)
            return;

        IsInstalling = true;
        Error = null;

        try
        {
            // The app exits inside here: an MSI cannot replace an exe that is running.
            await _installer.DownloadAndInstallAsync(
                update,
                new Progress<double>(value => DownloadPercent = value * 100));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update failed");
            Error = ex is InvalidOperationException
                ? ex.Message
                : "The update could not be downloaded. Try again later.";

            IsInstalling = false;
        }
    }

    [RelayCommand]
    private void Dismiss() => IsDismissed = true;
}
