# OSRS Check Ping

Find a nearby Old School RuneScape world without watching a list jump around. Check Ping is a small Windows app that shows your current network latency, keeps your favorites close, and quietly reviews the other worlds in the background.

## Get started

1. You need **Windows x64** and Microsoft's **[.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)**. Choose **Desktop Runtime**, rather than the ASP.NET or standard runtime.
2. Download `OsrsCheckPing.exe` from [Releases](https://github.com/SoraRS/osrs-check-ping/releases). If a release is not yet available, testing builds are in the **OsrsCheckPing-win-x64** artifact on the [Actions page](https://github.com/SoraRS/osrs-check-ping/actions); extract the ZIP first. Testing builds may be unsigned.
3. Open the executable. No installation or administrator access is needed. The app starts maximized and checks every world once. Open the gear to see progress.

## Read your worlds

The dashboard shows the **20 fastest eligible worlds**, plus **up to five pinned favorites** in their own section. The default cutoff is **30 ms**. Fewer worlds appear when fewer meet your cutoff. Both sections stay in world-number order, so a changing ping won't move a card.

Each card shows a country flag, activity, latency, and signal bars. A **filled gold star** means members; an **outlined star** means free-to-play. A **pin** means always tracked. Red world/activity text warns about main-character PvP, High Risk, or Bounty Hunter rules; hover for the reason and read the game's own warning before entering.

Live worlds update every **five seconds** by default. Hidden worlds receive no live checks; every **five minutes**, the app checks them once and updates the selection. Favorites continue being checked even when slow or unreachable. A timeout can mean ICMP is blocked, rather than that the game world is offline.

These readings measure **network round-trip time**, not server tick processing. Low ping does not guarantee a lag-free game.

## Make it yours

Open the **gear** or press **Ctrl+,**. Settings stays inside the same window. Use **Worlds** or **Esc** to return.

- **World tracking:** search by world number, country, or activity. Enable **Browse hidden worlds** to see the rest; browsing sends no probes. **Track** makes a choice for this session. Manually enabled worlds take priority within the 20 ordinary slots. **Reset session choices** returns to automatic selection.
- **Pin:** save up to five always-tracked worlds. Pinned tracking stays checked and locked. Unpinning restores the underlying tracking choice. A sixth pin is rejected until you remove another.
- **Check connection:** download the catalogue again and check every world. Session choices and pins survive the scan. Inactive readings are labeled **Scan result**.
- **Application:** adjust acceptable latency and update frequency, or enable always-on-top and tray options. Select **Apply settings** to save. If close-to-tray is enabled, choose **Exit** from the tray menu to quit fully.
- **About:** version, project, support, privacy, and license links.

Slow or failed ordinary worlds are removed after one minute's worth of consecutive bad live readings—12 at the default interval. A good reading resets that count. Removed worlds can return at the next background review or **Check connection**, or when manually enabled. Session tracking choices reset after exiting; pins and preferences are remembered.

Existing installations using the old 60 ms default move to 30 ms once. Other custom cutoffs are kept, and you can choose 60 ms again afterward.

## Having trouble?

**Nothing appears:** open settings and check scan progress or any error notice. Raise the cutoff, choose **Apply settings**, then **Check connection**. Thirty milliseconds is intentionally selective and may be too low for your location.

**Every world times out:** check your connection, VPN, firewall, and network rules. Some networks block ICMP. The app may time out even while the game works. Try another network; administrator access is not normally required.

**The catalogue won't download:** the app uses its last saved catalogue when available. Settings shows when it was saved and why a fallback was needed. Without a saved copy, connect to the internet and select **Check connection**.

**Windows asks for a runtime:** install the **x64 .NET 10 Desktop Runtime** from the link above, then reopen the app.

**Windows says “Don't run” or “unknown publisher”:** check the download source and the file's **Properties → Digital Signatures**. Development builds are unsigned. Production signing requires external provider approval, and even a newly signed download can initially show SmartScreen warnings. See [signing status and verification](docs/SIGNING.md). Do not disable Windows protection globally.

**The app disappeared:** look in the system tray if you enabled a tray option. Double-click its icon to restore it, or right-click for Settings and Exit.

**Preferences won't save:** settings shows a notice; your current choices still work for the session. Check access to `%LOCALAPPDATA%\OsrsCheckPing`. To reset saved data, fully exit and rename that folder; the app recreates it next launch.

## Privacy and support

The app contacts Jagex's public catalogue endpoint and probes the supplied world hosts. Preferences and the last catalogue are saved locally. There is no account login, analytics, or saved latency history. See [Privacy](docs/PRIVACY.md).

[Report a problem](https://github.com/SoraRS/osrs-check-ping/issues) with your Windows version, display scaling, and what happened. Development, publishing, and Windows acceptance checks are in [the developer guide](docs/DEVELOPMENT.md).

[Apache License 2.0](LICENSE). Independent software, not affiliated with or endorsed by Jagex.
