# Test rig

Everything here lets you try the app without the real game, the real signing key, or GitHub.

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
powershell -ExecutionPolicy Bypass -File test\make-test-rig.ps1 -ProtoDir "<your ProtoInput folder>"
powershell -ExecutionPolicy Bypass -File test\rig\run.ps1
```

`make-test-rig.ps1` creates `test\rig` (ignored by git):

| Path | What it is |
| --- | --- |
| `rig\fakegame\MarvelHeroesOmega.exe` | A stand-in game: an empty window with the game's window class, built from `FakeGame.cs`. It logs the options it was started with to `fakegame.log`. |
| `rig\app\` | A portable copy of the app, set up to start the stand-in game. |
| `rig\feed\` | A stand-in next version, signed with a throwaway key, in the shape of GitHub's release list. |
| `rig\feed-bad\` | The same with one byte of the zip changed. The app must refuse it. |
| `rig\run.ps1` | Starts the portable copy against the feed. `-Bad` uses the tampered feed. `-Reset` puts the current build back after an update test. |

## What to check

- **Launch:** press Start. Two stand-in windows should open, take the two halves of the screen, lose their title bars, and `rig\app\MhoCoop.log` should show both attached. Close both windows and the app exits by itself.
- **Update:** the menu shows a green Update Available button. Update and Restart should leave `rig\app\MhoCoop.exe` at the next version, with no `.old` files after the restart.
- **Tampered update:** with `-Bad`, Update and Restart must say "Not Updated: The download is not signed by the developer" and leave the program untouched.

The two environment variables `run.ps1` sets, `MHOCOOP_UPDATE_FEED` and `MHOCOOP_SIGN_PUBKEY`, exist for these tests only.
