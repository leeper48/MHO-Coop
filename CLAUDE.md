# MHO-Coop: notes for working on this project

MHO Couch Co-op runs two Marvel Heroes Omega games side by side with one gamepad locked to each, using ProtoInput. One WinForms exe, no installer. User-facing description: `README.md`.

## Build

- `powershell -ExecutionPolicy Bypass -File build.ps1` writes `dist\MhoCoop.exe`.
- It uses the C# compiler that ships with Windows (`Framework64\v4.0.30319\csc.exe`), so the code must stay **C# 5**: no string interpolation, `?.`, `out var`, expression-bodied members, `nameof`, or auto-property initialisers.
- The version is the two assembly attributes at the top of `src/Program.cs`.

## Rules that are decisions, not accidents

- **No account details, ever.** The app never asks for, stores or passes on a login; each game opens at its own login screen. Do not add any code for it, not even switched off.
- **No game artwork in the repository.**
- **Nothing personal in commits.** No real names, local paths or logins; examples use Alex and Sam.
- **Updates are signed with the same release key as MHO Extended Mod Manager.** Only the public half is here (`src/ReleaseSigning.cs`). The private key is never created, stored or committed by this project. Unsigned releases, drafts and pre-releases are ignored by the updater.
- Wording for people: plain sentences.

## How a session works (src/Session.cs)

1. Start game 1, wait for its window (class `LaunchUnrealUWindowsClient`), give it `LoadWait` seconds; same for game 2.
2. Tile: size first, then position, because the game keeps its old position when it resizes. Sizes come from the primary screen's work area and the window frame measured at run time, so any resolution works.
3. Attach ProtoInput to each game. Order matters: set the controller index before installing the Xinput hook, or the game shows "Gamepad disconnected". OpenXinput is on.
4. Borderless: the game sizes its picture as if it still had a title bar, so the borderless window is the work-area height minus the frame height. A taller window makes the game stretch its picture and clicks land in the wrong place.
5. Watch: RB brings both games forward, D-pad up/down moves the cursor to that pad's game, and raw mouse input (the wheel) is routed to the game under the cursor. Ends when both games close.

## Testing without the real game

`test\make-test-rig.ps1` (after `build.ps1`) builds a stand-in game window, a portable copy of the app, and a local signed "next version" feed under `test\rig`. `test\rig\run.ps1` starts the app against it. See `test\README.md`.

The stand-in is not DPI-aware, so its window sizes come out a pixel off on scaled screens; that is the rig, not the app.

Not yet verified with the real game: a full session started by this app (it was developed against the stand-in), and attaching ProtoInput while the game is still at its login screen.

## Releasing

1. Set the version in `src/Program.cs`, run `build.ps1`.
2. On the PC that holds the release key: `tools\make-release.ps1 -Version <v> -KeyPem <release key .pem>`. It packs `MHO-Coop-<v>.zip`, writes `.sha256`, signs with MHO Extended Mod Manager's `--sign-file`, and checks the signature against the key built into the app.
3. Publish the three files on a GitHub release tagged `v<v>`, not as a pre-release. The release notes are shown in the app's update window.
