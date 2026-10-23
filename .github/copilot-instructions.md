# JellyJam – Copilot Instructions

JellyJam is a radio-style music player for Jellyfin written in C# on .NET 10. It has a Windows CLI and a .NET MAUI Android client, and both share one platform-neutral Core. See [README.md](../README.md) for full behavior, playback semantics, and verification steps.

## Architecture

| Project | Target | Role |
| --- | --- | --- |
| `src/JellyJam.Core` | `net10.0` | Models, interfaces (`Abstractions.cs`), track selection/history, `PlaybackCoordinator`, `RadioSession`, Jellyfin SDK wrapper, JSON config store. |
| `src/JellyJam.Infrastructure.Windows` | `net10.0-windows` | NAudio streaming/WASAPI output and DPAPI credential store. |
| `src/JellyJam.Console` | `net10.0-windows` | Cocona commands, Spectre.Console rendering, keyboard loop, and the DI composition root. |
| `src/JellyJam.MAUI` | `net10.0-android` | MAUI screens (MVVM via CommunityToolkit.Mvvm), `SecureStorage`, native MediaPlayer, and foreground media service. Its root namespace is `JellyJam.Mobile`. |
| `tests/JellyJam.Tests` | `net10.0-windows` | xUnit tests with SDK HTTP fakes and CLI process checks. |

Dependency rules must hold:
- **Core references no presentation or platform APIs**, including MAUI, NAudio, Spectre, Cocona, and Windows-only APIs.
- Console → Core + Infrastructure.Windows; Infrastructure.Windows → Core; Android → Core only.
- Platform behavior goes behind Core interfaces (`IJellyfinClient`, `IAudioPlayer`, `IConfigurationStore`, `ICredentialStore`, `IJellyfinSessionClient`). Keep their inputs and outputs platform-generic because an iOS port is planned.
- UI renders immutable snapshots and sends commands. It never owns or mutates playback state.

## Playback Invariants

- `PlaybackCoordinator` serializes every play, next, previous, mode-change, and completion event through a single-reader channel.
- Stream workers carry generation IDs, so stale completions or cancelled loads must never advance a newer track. Dispose the previous stream and output before starting another.
- Treat natural completion and an explicit stop as different events.
- Track selection is pure logic, with no network or audio I/O. Random modes use shuffled traversal, not draws with replacement.
- Only MP3 is played directly. Other formats use a server-side MP3 transcode with at most 320 kbps and 2 channels. Never download whole tracks.

## Security

- Never log or persist passwords. Never log tokens or authenticated stream URLs.
- Tokens live only in OS-protected stores: DPAPI `credentials.dat` on Windows and MAUI `SecureStorage` on Android. Ordinary settings stay separate.
- Prefer HTTPS. HTTP requires explicit user confirmation. Never bypass certificate validation.
- Use bounded retries. Auth and device failures surface as errors and are not retried endlessly.

## Build & Test

- SDK is pinned in `global.json` (10.0.113). `Directory.Build.props` enables nullable reference types, implicit usings, and latest analyzers.
- `JellyJam.slnx` contains the CLI, Core, Windows adapters, and tests, and does not need MAUI workloads. `JellyJam.MAUI.slnx` contains Core and Android.
- Build and test commands:

```powershell
dotnet build JellyJam.slnx -c Release
dotnet test JellyJam.slnx -c Release --filter "Category!=LiveAudio"   # default; no server/audio device needed
dotnet test JellyJam.slnx -c Release --filter "Category=LiveAudio"    # plays a short tone on the default device
```

- Test selection and history with deterministic randomness. Test coordination with fake Jellyfin and audio implementations. Tag tests that need real hardware with `Category=LiveAudio`.

## Coding Guidelines

Follow [.github/skills/clean-code-guidelines/SKILL.md](skills/clean-code-guidelines/SKILL.md). The short version:
- **Think first:** State your assumptions and ask when a request is ambiguous.
- **Simplicity:** Write the minimum code for the request. Don't add speculative features, single-use abstractions, or unrequested configurability.
- **Surgical changes:** Touch only what the task needs and match existing style. Mention unrelated dead code instead of deleting it.
- **Verifiable goals:** Turn tasks into tests or checks and loop until they pass.

Project style:
- Follow SOLID, use constructor dependency injection, and strictly separate UI from services.
- Code should read like prose. Extract meaningful functions but avoid microfunctions. Aim for ≤ 20 statements or ≤ 25 lines per function.
- Use async APIs with `CancellationToken` propagated end to end. Keep network and audio work off UI and input threads.

## Scope

Keep JellyJam small and radio-like. Playlist editing, offline downloads, a full library browser, and remote control of other clients are out of scope. Planned work includes multiple Jellyfin servers as tunable "stations," static between frequencies, per-station lo-fi effects, and an iOS port.
