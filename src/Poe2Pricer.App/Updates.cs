using System.IO;
using Poe2Pricer.Core.Market;
using Velopack;
using Velopack.Sources;

namespace Poe2Pricer.App;

// "Check for updates", in two ways. A copy installed by the setup (Velopack) fetches a newer
// version in the background and installs it when the program quits, or at once on a click. A
// copy that was only unpacked can be told that there is one, with the page to get it from.
// POE2PRICER_UPDATE_SOURCE: a folder with a packed release in place of github.com, to run an
// update through on one machine before a release is public.
public sealed class Updates
{
    // Downloaded: an installed copy, the version lies ready; otherwise only the page.
    public sealed record Found(string Version, string Page, bool Downloaded);

    private readonly UpdateManager manager;
    private UpdateInfo? staged;

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

    // Looks for a newer version; an installed copy fetches it right away. null: none. What the
    // request throws (no network, no release yet) is the caller's to log.
    public async Task<Found?> LookAsync(Action<string> log)
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
        string version = info.TargetFullRelease.Version.ToString();
        log($"Update {version} found, downloading");
        await manager.DownloadUpdatesAsync(info);
        staged = info;
        return new(version, UpdateCheck.ReleasesPage, true);
    }

    // Installs the fetched version now and starts the program again (a click).
    public void ApplyNow()
    {
        if (staged is not null)
            manager.ApplyUpdatesAndRestart(staged);
    }

    // At the end of the run: the fetched version is installed once the program is gone, without
    // starting it again.
    public void ApplyOnExit()
    {
        if (staged is not null)
            manager.WaitExitThenApplyUpdates(staged, silent: true, restart: false);
    }
}
