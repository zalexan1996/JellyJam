# JellyJam

JellyJam is a radio-style music player for Jellyfin, written in C#, with a Windows CLI and a .NET MAUI Android client. Start playback, choose how music is selected, and let it run without managing playlists or browsing a full music-player interface.

## Project Status

The Windows CLI and Android client are implemented. Automated tests cover selection, playback coordination, cached sessions, SDK requests, secure configuration, and terminal rendering. Generated MP3 playback has been checked through Windows audio and Android's native player. Android sign-in and background media controls have been exercised against a local HTTP fixture; a real Jellyfin server and physical Android device still require the live checks below.

## Windows CLI

Requirements: Windows 10/11, .NET 10 SDK (the repository pins SDK 10.0.113), a default audio output device, and a Jellyfin account with access to music. Windows Media Foundation and an MP3 decoder must be available; Windows N editions may require the Media Feature Pack.

```powershell
dotnet build JellyJam.slnx -c Release
dotnet run --project src/JellyJam.Console -c Release -- configure
dotnet run --project src/JellyJam.Console -c Release -- play
```

Setup prompts for the server URL, username, and a masked password. Enter credentials only in your local terminal. HTTPS is recommended; HTTP requires confirmation. Use the final server URL, including any reverse-proxy path prefix, because HTTP redirects are not followed.

Running without a command also starts playback and offers setup if no login exists. Setup requires interactive input; redirected input fails clearly instead of waiting for credentials.

```powershell
dotnet run --project src/JellyJam.Console -c Release -- play --random Album --repeat Album
dotnet run --project src/JellyJam.Console -c Release -- logout
dotnet run --project src/JellyJam.Console -c Release -- --help
```

Cocona's built-in help deliberately exits with code 129; `dotnet run` may report this as a failed command even though help was displayed. `--version` exits successfully.

## Android Radio

The Android app targets Android 8.0 (API 26) or newer. Its first screen asks for a Jellyfin URL, username, and password. After sign-in, it opens a minimal cyberpunk player with previous/play-stop/next controls, track progress, random/repeat selectors, and sign-out.

- The access token is cached using MAUI `SecureStorage`, backed by Android's secure storage mechanisms and bound to the server/account. The password is never persisted. App backup is disabled to avoid restoring unusable encrypted credentials.
- Subsequent launches restore the radio screen without asking for the password again. Playback starts only when Play is tapped. Sign out removes the cached token even if server revocation fails.
- Settings and mode defaults stay in the app's private storage. Stop ends the current session's audio; Play starts a fresh selection, not pause/resume.
- A foreground media service keeps playback running while the app is backgrounded. Notification/lock-screen controls support previous, stop, and next. Audio focus loss or unplugging headphones stops playback rather than unexpectedly resuming it.
- HTTPS is preferred. Local HTTP servers are supported only after explicit confirmation on the sign-in screen. Certificate validation is not bypassed. Enter the final server URL, including its reverse-proxy prefix.
- The client uses Core's conservative MP3 profile: direct MP3 or server-side MP3 transcoding for other formats. Android handles decoding and buffering, not NAudio.

### Music Spectrum

The radio screen includes an optional 32-band spectrum with cyan levels, magenta peak markers, and amber high-level accents. It maps real Android FFT output onto a fixed logarithmic 60 Hz to 16 kHz axis, with attack/release smoothing and peak hold. Levels are relative visual indicators, not calibrated audio measurements.

- Turn on the Spectrum switch to opt in. Android requires its microphone permission for the `Visualizer` API, even when attached to a playback session. The app explains this before requesting permission; JellyJam reads only its own player's output, not microphone samples or the device-wide audio mix. No samples are stored or transmitted.
- Declining permission leaves music playback available. The preference is remembered, but capture starts only with permission and a visible radio screen.
- Capture and rendering are limited to 20 frames per second in the foreground. Backgrounding the app releases the effect without stopping music; returning reattaches to the current track. Track changes and Stop clear stale levels.
- Silence, stopped playback, and disabled visualization do not produce simulated animation. If a device cannot provide the effect, the widget reports `UNAVAILABLE` and audio continues normally.

Spectrum mapping, signed FFT values, silence, sample-rate handling, and peak decay have automated tests. The emulator smoke test uses generated audio and verifies actual chart movement, permission opt-out, background cleanup, and stop cleanup. Physical-device audio-effect behavior still needs verification.

### Build and Install

Android is kept in [JellyJam.Android.slnx](JellyJam.Android.slnx), separate from the CLI solution. Building or testing the CLI does not require MAUI workloads.

For a fresh development machine, install the .NET 10 `maui-android` workload, the Android SDK, and the JDK required by that workload. The .NET Android dependency target can install the latter two into user-selected directories after accepting the SDK licenses:

```powershell
dotnet workload install maui-android
dotnet build src/JellyJam.Android -t:InstallAndroidDependencies `
	-p:AndroidSdkDirectory="$env:LOCALAPPDATA/Android/Sdk" `
	-p:JavaSdkDirectory="$env:LOCALAPPDATA/Android/Jdk" `
	-p:AcceptAndroidSDKLicenses=True
dotnet build JellyJam.Android.slnx `
	-p:AndroidSdkDirectory="$env:LOCALAPPDATA/Android/Sdk" `
	-p:JavaSdkDirectory="$env:LOCALAPPDATA/Android/Jdk"
```

This workspace also has an ignored, isolated toolchain under `artifacts/toolchain`, used for development verification without changing the machine-wide SDK:

```powershell
.\artifacts\toolchain\dotnet\dotnet.exe build JellyJam.Android.slnx `
	-p:AndroidSdkDirectory="$PWD/artifacts/toolchain/android-sdk" `
	-p:JavaSdkDirectory="$PWD/artifacts/toolchain/jdk"
```

The build produces `src/JellyJam.Android/bin/Debug/net10.0-android/app.jellyjam.radio-Signed.apk`. Assemblies are embedded so the APK can be sideloaded without IDE Fast Deployment. It uses a development signing key, not a production/store signing identity. With an Android device connected and USB debugging authorized, install using `adb install -r` and that APK path.

### Physical Phones

For ARM64 phones such as the Galaxy S23, an ARM64-only Release profile is available. This produces a non-debuggable APK without emulator libraries. It still uses the development signing key; it is not a store-signed release. Trimming and AOT compilation are disabled in this diagnostic profile to preserve the existing managed application behavior.

```powershell
.\artifacts\toolchain\dotnet\dotnet.exe publish src/JellyJam.Android -c Release -r android-arm64 `
	-p:PublishProfile=AndroidArm64 `
	-p:AndroidSdkDirectory="$PWD/artifacts/toolchain/android-sdk" `
	-p:JavaSdkDirectory="$PWD/artifacts/toolchain/jdk"
```

The signed output is `src/JellyJam.Android/bin/Release/net10.0-android/android-arm64/app.jellyjam.radio-Signed.apk`. A validated copy is available at [artifacts/android/JellyJam-arm64.apk](artifacts/android/JellyJam-arm64.apk) in this workspace. The original universal APK is retained separately.

A generic "not compatible" installer message does not establish the cause. The original APK already includes ARM64, requires Android 8.0 or newer, and passes signature/alignment checks; those requirements fit the S23. The phone-specific package is an alternative to test, not a confirmed fix for that report. Targeting a newer Android SDK does not by itself require that Android version on the phone; the minimum SDK controls that requirement.

If installation is still refused, connect the phone with USB debugging authorized and capture the precise package-manager error:

```powershell
.\artifacts\toolchain\android-sdk\platform-tools\adb.exe -d install --no-incremental -r .\artifacts\android\JellyJam-arm64.apk
```

Keep the `INSTALL_FAILED_*` output and the phone's Android/One UI version for diagnosis. Do not uninstall an existing copy or clear its data just to investigate the error.

Lucide icons and Space Grotesk/Space Mono fonts are bundled with their license files under the Android resources directory.

## Features

- Configure and authenticate a connection to a Jellyfin server.
- Stream music from the authenticated user's library.
- Play music and move to the next or previous track.
- Change random mode: `All`, `Artist`, `Album`, or `None`.
- Change repeat mode: `Song`, `Album`, or `None`.

### Terminal Controls

| Key | Action |
| --- | --- |
| `N` / Right arrow | Next track. |
| `P` / Left arrow | Previous played track; does nothing at the beginning of history. |
| `R` | Cycle random mode: All, Artist, Album, None. |
| `T` | Cycle repeat mode: Song, Album, None. |
| Enter | Start a fresh selection after playback stops. |
| `Q` / Escape / Ctrl+C | End the session and release audio resources. |

The cyberpunk terminal uses cyan signal/progress indicators, magenta metadata and modes, and amber/red status messages. It adapts to narrow terminals and treats server metadata as literal text. Set `NO_COLOR` to disable styling; redirected output uses plain state-change lines. Redirected input disables keyboard controls and exits when playback exhausts or fails.

Initial defaults are random `All` and repeat `None`. Command-line mode options override saved defaults for the session; changing modes with the keyboard saves the active modes for the next session. History is kept only in memory.

## Technical Architecture

Both frontends reuse the same Core playback rules and Jellyfin integration. The CLI is a single long-running process with no background daemon or interprocess controls. Android adds a platform foreground media service within its app to meet background-playback requirements.

### Technology Stack

| Technology | Responsibility |
| --- | --- |
| C# / .NET 10 | Application lifecycle, asynchronous I/O, cancellation, and dependency injection. |
| Cocona 2.2.0 | CLI entry points, argument parsing, and command dispatch. |
| Spectre.Console 0.57.2 | Configuration prompts, playback status, and terminal presentation. |
| Jellyfin.Sdk 2026.10.12 | Authentication, paginated library queries, stream negotiation, and playback-stop reporting. |
| NAudio 3.3.0 | MP3 decoding, bounded PCM buffering, and shared-mode WASAPI output. |
| .NET MAUI 10.0.120 / CommunityToolkit.Mvvm 8.4.2 | Android screens, bindings, secure-storage access, and command state. |
| Android MediaPlayer / MediaSession | Native audio output, audio focus, background service, and system media controls. |

The NAudio backend uses Windows audio APIs. Android provides its own audio and credential adapters; Core remains platform-neutral and does not reference MAUI. Cocona is archived upstream, so its maintenance status is a known dependency risk.

### Component Boundaries

| Component | Responsibility |
| --- | --- |
| CLI and terminal UI | Accept startup options and in-session controls; render state without owning playback logic. |
| Playback coordinator | Own the playback session and serialize play, next, previous, mode changes, and track-completion events. |
| Track selection | Choose the next track using library context, random mode, repeat mode, and playback history. Keep selection independent of network and audio I/O. |
| Jellyfin client | Wrap SDK calls, resolve playable streams, and handle authentication and server errors. |
| Audio player | Read authenticated HTTP streams with cancellation/timeouts, decode MP3 frames, and report start/completion/failure. Hide NAudio behind an interface. |
| Configuration store | Persist server URL, user identity, and default modes; keep authentication secrets out of ordinary configuration. |

Playback continues within the process that started it. Cocona handles entry commands; a terminal input loop sends runtime controls to the playback coordinator. Separate CLI invocations do not control an existing session in the initial design.

The coordinator owns state through a single-reader command/event channel. Stream workers carry generation IDs, so a cancelled load or stale completion cannot advance a newer track. The previous worker releases its resources before another output starts. The UI reads immutable snapshots rather than mutating playback state.

### Playback Flow

1. Load configuration and authenticate with Jellyfin.
2. Query the user's accessible music library and establish the selection context.
3. Select a track and resolve a playable stream through the Jellyfin client.
4. Buffer and decode the stream through the audio player, then output to the audio device.
5. Display track metadata and active modes in the terminal.
6. On completion or a navigation action, let the coordinator select and start the next track.

Direct playback currently supports MP3. Other formats require Jellyfin to provide an HTTP MP3 transcode, up to 320 kbps and two channels; the account must permit audio transcoding. Neither client downloads whole tracks before playback. The Windows adapter bounds PCM buffering to three seconds and network reads to 15 seconds. Android uses its native streaming buffer and a 30-second preparation timeout. Completion advances only after the audio backend reports the end of playback.

## Playback Semantics

Selection follows these rules:

| Mode | Behavior |
| --- | --- |
| Random: `All` | Select from the user's entire accessible music library. |
| Random: `Artist` | Select from the active artist's tracks. |
| Random: `Album` | Select from the active album's tracks. |
| Random: `None` | Follow a stable library order: artist, album, disc number, then track number, with item ID as a tie-breaker. |
| Repeat: `Song` | Replay the current track when it ends naturally. Explicit next/previous actions still navigate. |
| Repeat: `Album` | Restrict selection to the active album and restart its traversal when exhausted. Random mode controls ordering within that album. |
| Repeat: `None` | Do not automatically restart an exhausted selection. Stop when all eligible tracks have played. |

Random selection uses a shuffled traversal rather than repeatedly drawing tracks with replacement. Previous navigates actual playback history; Next then replays forward history before taking another selection. Tracks skipped before output starts are not recorded as played.

The initial track establishes the active primary artist and album. Changing modes anchors selection to the current track, resets upcoming selection and forward history, and preserves past playback history without interrupting the current track. Album repeat takes precedence over broader random scopes. Filters use Jellyfin IDs; missing artist/album IDs restrict that scope to the current track instead of grouping unrelated unknown metadata.

Unavailable tracks are excluded for the session. If no playable candidates remain, playback stops with an error instead of retrying forever, including in repeat modes.

## Configuration

For the Windows CLI, ordinary settings live under `%LOCALAPPDATA%\JellyJam` in `settings.json`. The access token is stored separately in `credentials.dat`, encrypted with Windows DPAPI for the current user and bound to the configured server/account. Passwords are never persisted. Moving credentials to another account or machine requires setup again. Android uses app-private settings and `SecureStorage` instead of these Windows files.

`JELLYJAM_HOME` can override the storage directory, which is useful for isolated profiles and tests. Do not commit either settings or credentials to source control. `logout` removes the local token even if server-side revocation cannot be confirmed.

## Reliability and Security

- Keep network and audio work off the terminal input loop; propagate cancellation from application shutdown.
- Cancel pending stream loads before switching tracks and dispose of the previous stream and audio output.
- Distinguish natural completion from an explicit stop so skipped tracks do not trigger duplicate transitions.
- Bound retries for transient failures; report authentication or device failures without endlessly retrying.
- Prefer HTTPS and an OS-protected credential store for tokens. Never log passwords, tokens, or authenticated stream URLs.
- Test selection and history rules with deterministic randomness; test coordination with fake Jellyfin and audio implementations. Verify real streaming and device output separately with integration tests.

## Scope

JellyJam prioritizes a small, radio-like experience. Playlist editing, offline downloads, a full library-browser interface, and remote control of other Jellyfin clients are outside the scope.

## Project Structure

| Project | Target | Contents |
| --- | --- | --- |
| [JellyJam.Console](src/JellyJam.Console/JellyJam.Console.csproj) | `net10.0-windows` | Commands, prompts, keyboard input, themed rendering, and the DI composition root. |
| [JellyJam.Core](src/JellyJam.Core/JellyJam.Core.csproj) | `net10.0` | Models, interfaces, selection/history, playback coordination, Jellyfin integration, and ordinary configuration storage. |
| [JellyJam.Infrastructure.Windows](src/JellyJam.Infrastructure.Windows/JellyJam.Infrastructure.Windows.csproj) | `net10.0-windows` | NAudio streaming/output and DPAPI credential storage. |
| [JellyJam.Android](src/JellyJam.Android/JellyJam.Android.csproj) | `net10.0-android` | MAUI login/radio screens, Android secure storage, native audio, and foreground media service. |
| [JellyJam.Tests](tests/JellyJam.Tests/JellyJam.Tests.csproj) | `net10.0-windows` | xUnit tests, SDK HTTP fakes, process-level CLI checks, rendering tests, and opt-in real audio verification. |

Console references Core and the Windows adapters; the Windows adapters reference Core. Android references only Core and provides its platform implementations. Core references neither presentation nor platform APIs. Its `RadioSession` manages cached sign-in and lifecycle for mobile, while `PlaybackCoordinator` owns shared playback behavior.

## Verification

Run the normal, silent suite without a server or audio device:

```powershell
dotnet test JellyJam.slnx -c Release --filter "Category!=LiveAudio"
```

The separate Windows audio smoke test generates a temporary MP3, plays a quiet 0.6-second tone through the default device, and deletes the fixture:

```powershell
dotnet test JellyJam.slnx -c Release --filter "Category=LiveAudio"
```

For a live Jellyfin check, configure your account locally, play a direct MP3 and a track requiring transcoding, then exercise natural completion, Next/Previous, every mode, rapid skips, and Ctrl+C. Confirm sound stops on exit and the server does not retain a transcode. Also check incorrect credentials, an empty library, and an unavailable server. Automated SDK fakes do not establish compatibility with a particular live server.

For Android, also verify force-close/relaunch restores sign-in without auto-playing, sign-out clears the cached token, HTTP requires consent, notification/media controls work with the screen off, and audio stops on headphone removal or focus loss. Check portrait/landscape, large text, and small screens on a physical device. Emulator tests with a generated local fixture do not establish real-server transcoding or every manufacturer's background-process behavior.

## Publish

```powershell
dotnet publish src/JellyJam.Console -c Release -r win-x64 --self-contained false -o artifacts/publish
.\artifacts\publish\jellyjam.exe configure
.\artifacts\publish\jellyjam.exe play
```

This produces a framework-dependent Windows x64 application requiring the .NET 10 runtime. Close running copies before rebuilding the same output directory; Windows locks loaded assemblies. Pause/resume, seeking, in-app volume controls, and gapless playback are not implemented; Android volume uses the system media controls.