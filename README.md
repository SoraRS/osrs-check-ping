# OSRS World Ping

See your connection latency to Old School RuneScape worlds and keep your favorites close. World Ping is a small Windows app with a fantasy landscape, compact world tiles, and a list that stays in world-number order while readings update.

## Download and open

1. Use **Windows x64** and install Microsoft's **[.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)**. Choose **Desktop Runtime**; the standard or ASP.NET runtime alone is not enough.
2. Download `OsrsWorldPing.exe` from [Releases](https://github.com/SoraRS/osrs-world-ping/releases) when a release is available. Testing builds are available through the steps below.
3. Extract the download to a folder you want to keep, then open `OsrsWorldPing.exe`. No installer or administrator access is normally required.
4. The app starts maximized and checks every world once. Open the **gear**, then **Application**, for connection results or a retry.

**To download a testing build:** sign into GitHub, open [Build and test](https://github.com/SoraRS/osrs-world-ping/actions/workflows/build.yml), and choose a successful run for the branch or version you want. Scroll to **Artifacts**, download **OsrsWorldPing-win-x64**, and extract its ZIP. The interface previews artifact contains pictures, rather than the application. [GitHub's download guide](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/download-workflow-artifacts) explains the sign-in and download steps.

The interface described here is **version 1.4.0 or newer**; check **About**. Older runs contain older executables. Testing builds may be unsigned.

## Updating an older copy

Fully exit the old app first. If it is in the system tray, right-click its icon and choose **Exit**. Replace your old download with `OsrsWorldPing.exe`, open that file, and update any shortcut that points elsewhere. Downloading a new build does not replace other copies automatically.

The application was previously called **OSRS Check Ping**. On first launch, World Ping imports existing favorites, preferences, window placement, and the cached catalogue into `%LOCALAPPDATA%\OsrsWorldPing`. Existing files in that new folder take precedence. The previous folder remains as a backup. A save problem is shown in **Application**; readable old preferences remain usable for that session.

## Read your worlds

The dashboard shows up to **30 eligible worlds** in separate three-column Members and Free to play sections on a large display, plus **five favorites** in a separate row above them. Automatic selection favors the lowest pings at or below **30 ms** by default. Each section displays worlds by number, reading down a column before continuing to the right. A ping change does not shuffle the list. The **BEST** line identifies the tracked world with the lowest current latency. Smaller windows use fewer columns and allow scrolling.

| On the tile | What it means |
| --- | --- |
| Heart at the left | Red means a saved favorite; grey means you can add it. Click to toggle. |
| ★ Members section; gold world numbers | Members worlds. |
| ☆ Free to play section; darker silver world numbers | Free-to-play worlds. |
| Number in ms | Your latest network round-trip time. Lower is better. |
| Flag after the latency | The world's region; a neutral globe is used for unknown regions. |
| Activity icon and name | Genuine OSRS skill and boss sprites identify supported activities. Total-level worlds use the experience bar graph; unfamiliar activities have a generic symbol. Hover for long names. |
| Red world/activity text | Main-character PvP, High Risk, or Bounty Hunter warning. Hover for the reason and read the game's warning before entering. |
| No reply | No ICMP response arrived within two seconds. This can be temporary and does not establish that the game world is offline. |
| Unreachable, DNS error, network error, or permission denied | The diagnostic category explains which part of the check failed. |
| Unavailable | A saved favorite is missing from the catalogue. |

Live worlds update every **five seconds** by default. Other worlds receive no regular live checks. Every **five minutes**, a background review checks the hidden worlds and updates the selection. This review continues even when you choose a live refresh interval longer than five minutes.

Ordinary worlds leave the live selection after one minute's worth of consecutive slow or failed readings—12 at the default interval. A good reading resets the count. They can return at a background review or through **Check connection**. Favorites remain tracked regardless of latency or failures.

Ping measures your network connection, **not server ticks**. A low reading does not guarantee a lag-free game, and an ICMP timeout does not necessarily mean a world is offline.

## Settings and favorites

Click the **gear** or press **Ctrl+,**. Settings slides down inside the same window, keeping the live worlds below every tab. Click the gear again or press **Esc** to return. The gear and tabs animate briefly; motion follows Windows' animation preference.

- **World tracking:** select **Show all** below the live grid to reveal the rest of the catalogue in world-number order. **Hide all** folds it away. Browsing sends no probes. Inactive worlds fade, with clearer colors on hover; hover for the reading time and whether it came from a scan. The scrollbar stays hidden; wheel, touchpad, and keyboard scrolling remain available.
- **Favorites:** use the heart to save up to five worlds, including a world outside the normal cutoff. Favorites use their own slots and never appear twice. Remove a favorite before adding a sixth. Removing its heart restores automatic selection; it may return to the hidden list.
- **Application:** set a latency cutoff from **1–2,000 ms** and refresh interval from **5–600 seconds**, then select **Apply settings**. Both values are required. Always-on-top and the minimize/close-to-tray options start disabled.
- **Check connection:** in Application, check every world again. Its status distinguishes all worlds replying from partial or unavailable results. Use it after raising the cutoff to find newly eligible worlds immediately.
- **Reset to default:** restore the 30 ms cutoff, five-second refresh, and disabled window/tray options. Favorites and saved window placement are kept.
- **About:** find the version, description, SoraRS's GitHub and Discord profiles, project/support links, privacy information, signing status, and license.

The bottom of settings shows when the catalogue was updated. The reserved progress area shows a scan without shifting that timestamp. The app remembers window bounds for restoring; startup is maximized. When close-to-tray is enabled, use the tray menu's **Exit** to quit fully.

## Common problems

**I still see the previous interface.** Fully exit older copies, download the executable artifact from the intended successful build, and launch the extracted file directly. Check **About** for version 1.4.0 or newer and update your shortcut.

**No ordinary worlds appear.** Open **Application** to check progress or a notice. The default 30 ms cutoff may be too low for your location. Raise it, select **Apply settings**, then **Check connection**. You can also favorite a world regardless of ping.

**Some worlds show No reply or Unreachable.** Brief disconnections and missed ICMP replies are expected on some connections. Open **Application → Diagnostics** for the failure breakdown and **Copy diagnostics** if reporting a problem. [Connection result meanings](docs/CONNECTIONS.md) explain the different conditions. A timeout does not prove the world is offline or that ICMP is blocked.

**Everything times out.** Check your connection, VPN, firewall, and network rules. Some networks block ICMP even when the game works. Try another network before assuming every world is offline; administrator access is not normally required.

**The world list will not download.** The app uses its last saved catalogue when possible. Application shows the notice; settings shows the catalogue's saved time. Connect to the internet and select **Check connection** to retry.

**Windows asks for a runtime.** Install the **x64 .NET 10 Desktop Runtime** linked above, then reopen the app.

**Windows says “Don't run” or “unknown publisher.”** Current testing builds are unsigned. Confirm that the file came from this project's release or Actions page. Signed releases can be checked under **Properties → Digital Signatures**, using the signature and checksum information accompanying that release. See [Windows warnings and signing status](docs/SIGNING.md). Signing does not guarantee immediate SmartScreen reputation. Do not disable Windows protection globally.

**The app disappeared.** If a tray option is enabled, look in the system tray. Double-click its icon to restore the app, or right-click for **Settings** and **Exit**.

**My old cutoff changed from 60 to 30 ms.** The earlier default moves to 30 ms once. Other custom cutoffs are kept. An intentional 60 ms selection saved after that migration remains 60 ms through this update. Old refresh intervals below five seconds are raised to the new minimum.

**Preferences do not survive a restart.** Check Application for a save error. The app needs access to `%LOCALAPPDATA%\OsrsWorldPing`. Readable settings can still work for the current session. Keep the old data folder until the import succeeds. To start completely fresh, exit first and rename both the current and legacy data folders so old preferences are not imported again.

## Privacy and help

Preferences, favorites, restored window placement, and the last world catalogue are saved locally. The app downloads Jagex's public catalogue and probes its supplied hosts. There is no game account login, analytics, or automatic saved latency history. **Copy diagnostics** copies the current world-host readings and technical failure details to your clipboard only when you choose it. Read [Privacy](docs/PRIVACY.md) for details.

[Report a problem](https://github.com/SoraRS/osrs-world-ping/issues) with your app version, Windows version, display scaling, and what happened. Build instructions and Windows acceptance checks are in [the developer guide](docs/DEVELOPMENT.md).

Application code and original artwork: [Apache License 2.0](LICENSE). OSRS sprites: © Jagex Ltd.; [sources and credits](src/OsrsWorldPing.Desktop/Assets/Osrs/SOURCES.md). [Profile icon credits](src/OsrsWorldPing.Desktop/Assets/Icons/SOURCES.md). Independent software, not affiliated with or endorsed by Jagex.
