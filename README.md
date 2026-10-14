# OSRS Check Ping

A small Windows desktop utility for watching your connection latency to Old School RuneScape worlds. It uses native Windows Forms controls with an OSRS-inspired palette and does not bundle a browser or the .NET runtime.

## Run

Requires **Windows x64** and the **.NET 10 Desktop Runtime (x64)**. The standard .NET or ASP.NET runtime alone is insufficient. Install the Desktop Runtime from [Microsoft's .NET 10 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).

Download the `OsrsCheckPing-win-x64` artifact from a successful GitHub Actions **Build and test** run, extract it, and open `OsrsCheckPing.exe`. There is no installer or administrator requirement. You can also build it from source below.

The application downloads Jagex's world catalogue on each launch and sends one ICMP probe to every listed world. Worlds responding at **60 ms or less** are automatically tracked, with another check every **five seconds**. Checks measure network round-trip latency; they do not measure game-server tick processing. Networks that block ICMP may show timeouts even when the game is reachable.

The **Always tracked** section contains up to five saved favorites. Other tracked worlds appear below it. Both sections sort by current latency, then world number, with failed readings last. Colors accompany textual connection statuses.

## Settings

Open **Settings** or press **Ctrl+,**.

**World tracking** lists every discovered world and supports searching by number, region, or activity. Toggle **Track** to enable or disable a world for this session. These choices survive **Recheck all worlds**, but reset after exiting. **Reset session toggles** restores the automatic selection from the most recent scan and clears removal streaks.

Toggle **Always** to save a favorite. Favorites keep being checked through high latency and failures and cannot be manually disabled while pinned. A sixth favorite is rejected until another is unpinned. Unpinning restores the underlying session/automatic selection. Saved favorites missing from the catalogue stay visible as unavailable; the app never guesses a hostname.

**Recheck all worlds** downloads the current catalogue and checks every world once. Live cycles pause during this operation. The new automatic selection replaces the previous scan's selection, preserving session toggles and favorites. Readings labeled **Scan result** are from that scan; inactive worlds are not continuously probed. Unknown location codes are displayed as unknown regions rather than guessed.

**Application** configures the cutoff (1–2,000 ms), refresh interval (1–60 seconds), always-on-top, minimize-to-tray, and close-to-tray. Click **Apply settings** to save. Window and tray options default to off; window size and position are remembered on exit. Use the tray menu's **Exit** command to quit when close-to-tray is enabled. There is no launch-at-login behavior.

For unpinned worlds, slow or failed live checks count toward automatic removal. A successful reading at or below the cutoff resets the streak. Removal requires `ceil(60 / refresh interval)` consecutive bad readings: **12** at the default five seconds. Discovery probes do not count. Changing monitoring settings resets streaks; recheck to add newly eligible automatic worlds. Removed worlds are checked again only after a full recheck, another launch, or manually toggling tracking on.

Probes have a two-second deadline, including DNS, with at most 32 probes in flight and no overlapping probes for a world. Refresh timing is best effort: many simultaneous timeouts or an unusually short interval can lengthen a cycle. Missed cycles are not queued, and suspended time is not counted as failed readings.

## Saved data and failures

Data is stored in `%LOCALAPPDATA%\OsrsCheckPing`:

- `settings.json`: application preferences, up to five favorite world IDs, and window placement.
- `worlds.json`: the last successfully downloaded catalogue and its download time.

Latency samples and manual session toggles are kept in memory. JSON writes use a temporary file and replacement to protect the previous file if writing fails.

When downloading fails, the app uses the last saved catalogue and displays its age. Without a cache, settings remain accessible and **Retry world scan** or **Recheck all worlds** retries the download. Settings write failures are displayed; current choices continue to work in memory.

The app contacts Jagex's public catalogue endpoint and the world hosts supplied by that catalogue. It has no game account login, game-client integration, analytics service, or latency-history database.

## Develop and publish

Install the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). No third-party application runtime dependencies are required.

```sh
dotnet build --configuration Release
dotnet test tests/OsrsCheckPing.Core.Tests --configuration Release
```

On Windows, start the desktop app with:

```sh
dotnet run --project src/OsrsCheckPing.Desktop
```

Publish the framework-dependent single executable:

```sh
dotnet publish src/OsrsCheckPing.Desktop --configuration Release -p:PublishProfile=win-x64 --output artifacts/win-x64
```

The output is `artifacts/win-x64/OsrsCheckPing.exe`. Keep `SelfContained=false`: the installed Desktop Runtime provides the framework. See [Microsoft's single-file publishing documentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview).

The portable core, network adapters, persistence, and core tests target `net10.0`. The desktop and desktop-test projects target `net10.0-windows` and enable Windows targeting so they can be compiled from Linux/macOS. Run the UI smoke tests on Windows with `dotnet test tests/OsrsCheckPing.Desktop.Tests --configuration Release`. Interactive Windows Forms behavior must be verified on Windows.

GitHub Actions runs the portable tests on Linux, builds and tests on Windows, publishes the executable, verifies that the publish directory contains only `OsrsCheckPing.exe`, and smoke-tests starting and closing that executable with the installed runtime.

## Windows acceptance checks

Before distributing a release, test the published executable on Windows with the Desktop Runtime installed:

- Start online and offline, check scan progress and retry/cache behavior, and exit during a scan.
- Confirm favorites, latency sorting, timeout statuses, session toggles across rescans, and the five-favorite limit. Restart to verify that favorites persist and session toggles reset.
- Navigate with Tab/Space, use Ctrl+, to open settings, and check at 100%, 150%, and 200% display scaling.
- Move/resize/maximize the window, exit, and reopen. Disconnect a secondary monitor and verify that the restored window remains visible.
- Enable each tray option, minimize/close, restore through the tray, open settings, and quit through **Exit**. Confirm live tracking continues while hidden.

## License

[Apache License 2.0](LICENSE). This is an independent utility and is not affiliated with Jagex.
