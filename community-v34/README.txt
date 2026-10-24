Codex Dragon — community startup animation and dynamic wallpaper
Portable V34-r13 reliability and shell-layout test candidate for Windows 10/11 x64

STATUS
R13 continues from the sealed R12 public candidate. Cold target discovery now
uses a bounded adaptive response allowance (2 to 4 seconds), within one absolute
30-second horizon; attach is issued once, capped at 12 seconds and remaining time.
Initial hidden-window recovery retains its already verified ownership even if
additional hidden auxiliary windows appear. Ambiguous unowned windows are untouched.
Diagnostics distinguish target loss, transport failure and process exit without
claiming an exit code proves user intent. All earlier identity and media protections remain.

This candidate starts from the freshly downloaded public R10 ZIP on 2026-10-24.
It adds a narrowly checked official page-surface shell when no default main
exists. Empty, conflicting and unknown page structures remain unsupported.
The report proves a missing default main blocked wallpaper preparation, but
does not identify that user's exact page structure. Playback confirmation on
the affected computer is required before publication.
Detailed diagnostics add bounded shell counts, relative window edges, DPI and
numeric environment evidence. No raw DOM, titles, chat or credentials.
R12 also follows verified shell attributes when the official UI changes in place,
records bounded runtime layout evidence after preparation, and discovers optional
official shortcut targets from the registered manifest rather than old EXE names.
A registration change during identity acquisition allows exactly one rediscovery;
signature/path failures still fail closed. No media quality, animation timing,
random appearance policy or resource protection has been reduced.
An explicit repeated launch can restore the same verified target if it was already
minimized when selected. Restoration is bounded and nonactivating, with guarded
position/restore-state compensation on failure. Later user minimization is preserved.
Detailed analysis separates a newer public-entry failure from old playback evidence;
unknown operation modes and conflicting timestamps are reported as uncertain.
Developer regression coverage and remaining real-PC boundaries are documented in
source/tests/reliability-test-cases.md. Users only need Diagnostics, not those tests.

R10 official-identity and hidden/maximized-window handoff fixes remain.
The official Store executable is discovered from registered package metadata,
validated by signature, and held through child creation. The handoff then checks
that actual process and creation time instead of reselecting a newer version.
Generated configuration stores the identity policy, not a fixed official version
or executable hash. Ordinary package version/path updates are resolved on launch.
This candidate has NOT been published; a separate-PC playback test is pending.
The existing public release/download URLs will be retained after approval.

This candidate retains the earlier memory-gate fix in wallpaper preparation. A Windows
low-physical-memory notification or a low RAM reading no longer disables playback
when the current process still has sufficient available commit. Critical commit
below 512 MiB suspends the decoder; recovery requires at least 1 GiB of available
commit for 30 continuous seconds. It resumes the same file/document attachment,
without repeating installation or making the launcher wait for that recovery.
This is a conservative headroom policy, not a guarantee against every allocation
failure. Startup process caps, signed-client checks and file hashes remain intact.
No original media, resolution, frame rate, appearance interval or opacity changed.

This directory is a publication candidate, not a statement that a GitHub release
has already been published. No upstream project-code license was found; this
candidate does not invent a license for upstream code or media. The contributor
has requested submission of the customization and generated media for review;
the maintainer still needs to clarify distribution terms. This is not an official
OpenAI product.

GET THE COMPLETE PACKAGE
Download the complete CodexDragon ZIP from this repository's Releases section.
The complete package includes all videos, high-resolution character assets,
font, source, executables, installation scripts and an integrity manifest.
GitHub's green "Code > Download ZIP" contains source only. It is not a ready-to-run
wallpaper distribution. Large media is kept out of ordinary Git history.
Nothing is re-encoded or downloaded during installation. No API key is required
for the pre-generated videos. Both wallpaper clips are included in the user ZIP.

REQUIREMENTS
- Windows 10/11 x64, .NET Framework 4.8 or later, Windows PowerShell 5.1.
- The current user's official Microsoft Store Codex x64 application.
  The host currently supports the verified Electron application with its private
  CDP pipe. Similar DOM markup does not imply Tauri/Wry/WebView2 transport support.
- Normal Windows media components. Windows N editions need Microsoft's Media
  Feature Pack. GPU drivers must support the video's playback format; no promise
  is made that every driver/hardware combination can sustain 144 fps.
- A local writable NTFS folder, not a network share or a linked/junction folder.
  Extract the ZIP completely before opening any EXE. Do not run inside the ZIP.
- Sign in to Codex normally using the official app. This package never needs or
  contains the author's account, API key, cookies, browser profile or proxy data.

INSTALL AND USE
1. Extract the entire release ZIP to a new local folder. Do not mix versions.
2. Open Windows PowerShell (powershell.exe), change to that extracted folder and
   run: powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1
   This installs into %LOCALAPPDATA%\Programs\CodexDragonPublic. It never replaces
   an existing destination. Use -Destination "D:\Apps\CodexDragonPublic" for a new
   folder. No administrator rights are needed for a user-owned location.
   Optional: add -CreateDesktopShortcut to create a NEW community shortcut.
   Alternatively double-click Install-and-Bind.cmd to install and bind the
   unique verified official Codex shortcut on the current user's desktop.
   These two shortcut options are mutually exclusive. Normal launches need no UAC.
3. Save your work and normally exit the official Codex app for first integration.
4. Open Launch.exe in the installed folder. The entry is a windowless WinExe;
   no console is intentionally created. If preparation fails, a concise error
   explains the problem instead of leaving an unresponsive launch request.
5. Open WallpaperControls.exe to pause/resume, inspect, or disable the wallpaper.
   Launch.exe --preview previews the startup without starting or controlling Codex.

You may also use Launch.exe from the fully extracted release without installation.
Keep the complete folder together and writable. Install.ps1 is useful for owning
an isolated copy and providing a narrowly scoped uninstall operation.

OFFICIAL ICON AND WARM STARTS
The public package does not patch the official Codex executable, add a registry
hook, install a service or change startup tasks. Launch.exe is the portable entry.
Optional -BindOfficialDesktopShortcut (Install-and-Bind.cmd) backs up and binds
the unique verified current-user official desktop shortcut to the installed
Launch.exe. The original name and icon are preserved. Public-desktop, ambiguous,
unsupported or modified links are left alone. This is per-user installation,
with no automatic elevation or run-as-administrator flag for everyday use.
Use the installed Uninstall.ps1 to restore the verified original shortcut before
removing the installation. Never delete the installed directory while its shortcut
is bound. If ownership checks fail, preserve the files and resolve the conflict.
If Codex is already running, Launch.exe replays the existing-start flow. A running
Codex that was started without the wallpaper host cannot safely gain the host
after the fact: exit it normally and launch through Launch.exe next time.
ESC skips the startup animation; the original host flow retains the wallpaper.
The r7 replay path requests only read/query and synchronization process rights,
while retaining process-generation, registered-package and executable checks.
It records fixed diagnostic stages when discovery or attestation is rejected.
One tested ACL failure is corrected; the remote machine must still validate the
actual repeated-click behavior. No claim is made that all warm starts are fixed.

RESOURCE PROTECTION
When startup resource protection trips, r8 releases the optional animation and
retains the wallpaper reveal request, including for a late bridge session. This
is not a successful animated handoff and does not override an earlier disable.
The original thresholds and independent wallpaper-host protection remain in
force. Persistent resource pressure can still prevent playback. Diagnostics
distinguish the request to continue from proof of actual video playback.
Launch.exe normally runs without administrator privileges; no UAC prompt is
expected. Elevation is not a substitute for diagnosing an unavailable wallpaper.

WHAT IS PRESERVED
- Original startup artwork, living blinks, tear/impact transition and typography.
- The normal wallpaper appears first on each hosted app start.
- Thereafter each new appearance draws 75% normal / 25% particle video.
- The existing random 10–15 second gaps and transparent readable interface.
- Existing bounded failure handling, visibility pause/recovery and resource guards.
- All video pixels/frame rates remain the delivered originals.

PORTABILITY AND UPDATES
The launcher and host share the same registered-and-signed identity resolver.
On cold start, the host resolves the current registered Store package and its
manifest executable, verifies its OpenAI signature, and validates the child it
actually created. During handoff and warm start, verification binds to the exact
process and its creation time, including a still-registered older running version.
wallpaper-host-config.json is generated locally with media paths and policy version
2. It does not pin the official package version or executable hash. It is not
shipped in the ZIP and must not be copied from another machine or old install.
An update during discovery fails safely instead of trusting an unverified path.
Ordinary signed Store version/path updates do not require manually editing a hash.
This does not prove future Codex layouts are compatible. If a future update
changes the DOM or transport, the wallpaper may decline to attach. Use the official
app normally and report the failure; do not disable signature/path checks.
The original source was developed against Codex 26.1007.9446.0. The r6 audit also
found Electron runtime files in registered Codex 26.1016.6573.0; this inventory
does not prove every version/distribution uses Electron. Fresh builds and
isolated configuration tests do not replace tests on a genuinely separate PC.

FAILURE AND PRIVACY BOUNDARIES
The wallpaper uses Codex's existing renderer and a private local debugging pipe.
It does not listen on a public TCP debugging port, replace TLS settings, read
account files, upload logs or send data to a new service. The official Codex app
continues its own normal network/account behavior.
Wallpaper and Codex still share some renderer/GPU/process failure boundaries.
A hard host crash/forced termination can affect the hosted client; do not kill
WallpaperHost.exe while using Codex. "Perfect isolation" or days-long stability
on every machine is not claimed. Save work before a normal restart or upgrade.
Local diagnostics may contain process/window details and locally generated paths.
Do not upload an entire installed folder, output folder, config, or process dump
as a bug report. Collect-DiagnosticReport.ps1 exports only bounded allowlisted
fields, numeric versions, known file marker presence and six fixed media roles.
It never exports raw paths, titles, messages, account files or process command
lines. Compatibility markers are evidence, not a transport-support guarantee.
The quick report checks media sizes only; use the independent local test helper
or Test-PublicPackage.ps1 for full SHA-256 verification. No tool waits for playback.
The permanent Diagnostics folder provides One-Click-Diagnostics.cmd, with fixed
Reports/latest-report.zip and Reports/latest-analysis.txt outputs. It overwrites
only these latest report outputs instead of accumulating archives. Its default
Detailed mode verifies all packaged files and then collects bounded records and
numeric environment evidence, even when verification reports a damaged file.
Run it soon after reproducing the problem in this package. It performs no GPU
stress test, application launch, install or network operation. Check-All-Files.cmd
can verify files alone; Run-Diagnostics.ps1 -Mode Report skips full hashing.
Records include fixed page-shape counts, relative window edges and DPI, candidate
rejection counts, historical media/first-frame/resource/transport states and
current numeric system/driver evidence. Each log is limited to 256 KiB and 512
allowlisted records; truncation is explicit. Driver registration is not actual
decoder/GPU utilization. One report can narrow causes but cannot prove every
machine or guarantee a single-round resolution.
Only the report ZIP should be shared; generated Reports are excluded from release.

VERIFY / REBUILD
The release-manifest.json inventory covers the full package and all media.
From the source tree, run:
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Test-PublicPackage.ps1 -ReleaseRoot "D:\Downloads\CodexDragon"
Build offline from these sources using the complete media tree:
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1 -MediaRoot "D:\Downloads\CodexDragon" -OutputDirectory "D:\Build\CodexDragon-new"
Use a new output path. No former local installation, old V27/V33 receipt, cached
SDK or author's workspace is used. Windows' installed .NET compiler is required.
The build is reproducible from declared inputs, but binary byte determinism is
not claimed for the legacy framework compiler. Successful compilation verifies
framework metadata and then hashes the complete output closure.
Source-only clones need the corresponding complete release/media bundle; the
build stops on missing/corrupt media instead of silently creating a broken ZIP.
Source-only licensing text is not permission to redistribute every media asset.

UNINSTALL / UPGRADE
Close this installed launcher, wallpaper controls and normally exit the Codex it
hosts. Run powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Uninstall.ps1
inside the installed folder. It removes only manifest-owned files whose bytes
still match, and its own unchanged optional shortcut. Modified files and logs are
retained. -RemoveGeneratedConfiguration also removes the local host configuration.
It will not kill clients, modify the official app or recursively erase unknown
files. For an upgrade, extract/install into a NEW folder first, then retire the
old public install after normally closing it. Your existing custom installation
is never overwritten or automatically migrated.

ATTRIBUTION
Upstream: https://github.com/mytjxs/codex-startup-animation
Customization and release packaging: HanwenZhang-George (GitHub).
See SOURCE-PROVENANCE.txt in source/ and FONT-LICENSE.txt. Project-code and media
licenses are not silently replaced by the public packaging work.
