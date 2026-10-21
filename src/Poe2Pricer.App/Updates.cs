using System.IO;
using Poe2Pricer.Core.Market;
using Velopack;
using Velopack.Sources;

namespace Poe2Pricer.App;

// "Check for updates", in two ways. A copy installed by the setup (Velopack) is told that a
// newer version is out; a click on the message downloads it and installs it, which starts the
// program again. Nothing is downloaded or installed without that click. A copy that was only
// unpacked is told as well, with the page to get it from.
// POE2PRICER_UPDATE_SOURCE: a folder with a packed release in place of github.com, to run an
// update through on one machine before a release is public.
public sealed class Updates
{
    // Installable: an installed copy, a click installs it; otherwise only the page.
    public sealed record Found(string Version, string Page, bool Installable);

    private readonly UpdateManager manager;
    private UpdateInfo? offered;

    public Updates()
    {
        IUpdateSource source =
            Environment.GetEnvironmentVariable("POE2PRICER_UPDATE_SOURCE") is { Length: > 0 } folder
                ? new SimpleFileSource(new DirectoryInfo(folder))
                : new GithubSource("https://github.com/" + UpdateCheck.Repository, null, prerelease: false);
        // Full packages only: putting a release together from a delta starts the updater as a
        // process of its own, which shows a window in the middle of the game.
        // The channel named as the release is packed (tools/build_release.py), whatever the
        // machine: one feed, releases.win.json.
        manager = new UpdateManager(source, new UpdateOptions { MaximumDeltasBeforeFallback = -1, ExplicitChannel = "win" });
    }

    public bool Installed => manager.IsInstalled;

    // Looks for a newer version. null: none. What the request throws (no network, no release
    // yet) is the caller's to log.
    public async Task<Found?> LookAsync()
    {
        if (!Installed)
        {
            using var check = new UpdateCheck();
            var release = await check.NewerAsync();
            return release is null ? null : new(release.Version, release.Page, false);
        }
        var info = await manager.CheckForUpdatesAsync();
        if (info is null)
            return null;
        offered = info;
        return new(info.TargetFullRelease.Version.ToString(), UpdateCheck.ReleasesPage, true);
    }

    // The click: downloads the offered version, then installs it and starts the program again
    // (the process ends in here; beforeRestart runs first). What the download or the install
    // throws is the caller's to show.
    public async Task InstallAsync(Action<string> log, Action beforeRestart)
    {
        if (offered is null)
            return;
        log($"Update {offered.TargetFullRelease.Version}: downloading");
        await manager.DownloadUpdatesAsync(offered);
        log("Update downloaded, installing and restarting");
        beforeRestart();
        manager.ApplyUpdatesAndRestart(offered);
    }
}
