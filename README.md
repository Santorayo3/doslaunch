# Double-Click DOS

Double-Click DOS (formerly DOS Launch) is a small Windows tray application that makes old DOS games start in
[DOSBox](https://www.dosbox.com/) (or [DOSBox-X](https://dosbox-x.com/)) when you double-click them in
Explorer — no shortcut, batch file or manual configuration needed.

While it runs, it temporarily takes over the per-user associations for `.exe`, `.com` and `.bat` files.
Every launch is inspected: Windows programs are simply started as usual, while DOS programs are handed to
DOSBox together with a per-program configuration file that Double-Click DOS generates on the fly. The
configuration is tuned to the game's approximate release year and to what the game folder contains, so
most games run fullscreen, with sensible memory, CPU and sound settings, right away.

Everything is per-user: no administrator rights, no system-wide changes, and the original file
associations are restored as soon as the application exits.

---

## Features

- **Double-click a DOS game and it runs** — 16-bit DOS executables, `COM` programs and batch files that
  start DOS programs are detected automatically.
- **Original associations are preserved** — the previous association for each intercepted file type is
  backed up in the registry and restored on exit, on logoff/shutdown, or via `--unregister`.
- **Generated DOSBox config per program** — fullscreen, 4:3 aspect correction, OpenGL output without a
  software scaler, sensible memory/CPU/Sound Blaster settings, CD-ROM mounting and automatic `exit`.
- **Era-aware presets** — the game's release year is estimated from local evidence (text files,
  descriptions, binary strings) and optionally from archive.org, Wikidata and Wikipedia, then mapped to
  DOSBox settings typical for that era.
- **Editable, self-healing configs** — the generated `.doslaunch.conf` files are plain DOSBox configs you
  can edit. Double-Click DOS keeps them working after the game folder is moved and only adds missing options;
  your own values are never overwritten — except `output`, which is always `opengl`.
- **Works with DOSBox-X and classic DOSBox** (including DOSBox SVN), adjusting the command line for each build.
- **Optional MT-32 music through [MUNT](https://github.com/munt/munt)** — select the MUNT executable once
  and Double-Click DOS starts it before DOSBox and closes it again when DOSBox ends, pointing DOSBox's MIDI
  output at MUNT's MIDI port.
- **Diagnosable** — every decision is written to a log file, and the reasoning behind a generated config is
  documented as comments inside the config itself.

---

## Requirements

| Requirement | Notes |
| --- | --- |
| Windows 10/11 | Uses WinForms, the registry and Win32 APIs, so it is Windows-only. |
| DOSBox-X, DOSBox or DOSBox SVN | You select the executable once; DOSBox-X is recommended because more games run correctly with it. |
| [MUNT](https://github.com/munt/munt) (optional) | Only needed for MT-32 music. Select the executable in the tray menu; MUNT's WinMM MIDI driver must be installed so DOSBox can send MIDI to it. |
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | Only needed to build from source. The self-contained build bundles the runtime, and the framework-dependent build needs the .NET 10 Desktop Runtime on the target machine. |

No NuGet packages are required by the application itself; the test project uses xUnit.

---

## Building and running

```powershell
# Build everything (app + tests)
dotnet build doubleclickdos.slnx

# Run the tray application from source
dotnet run --project src\DoubleClickDOS\DoubleClickDOS.csproj

# Run the tests
dotnet test doubleclickdos.slnx

# Create both distribution builds (see below)
.\build.ps1
```

### Distribution builds

`build.ps1` publishes two variants into `dist\` and zips each of them:

| Build | Output | Requires on the target machine |
| --- | --- | --- |
| `self-contained` | `dist\self-contained\DoubleClickDOS.exe` — a single ~100 MB executable with the .NET runtime bundled (nothing is installed system-wide) | Nothing; just Windows |
| `framework-dependent` | `dist\framework-dependent\` — a small folder with `DoubleClickDOS.exe`, `DoubleClickDOS.dll` and its config | The [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |

Use the self-contained build when you hand the application to someone who should not have to install
anything; use the framework-dependent build when .NET is already present and the download should stay small.
Each build also needs the `assets` folder (tray icon) next to `DoubleClickDOS.exe`, so distribute the folder
or the zip, not the bare executable.

If PowerShell refuses to run the script (`… running scripts is disabled on this system`), start it with:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

The two variants are plain SDK publish profiles, so they can also be invoked directly (they are also usable
from Visual Studio's *Publish* dialog):

```powershell
dotnet publish src\DoubleClickDOS\DoubleClickDOS.csproj -p:PublishProfile=SelfContained-win-x64 `
    -o dist\self-contained
dotnet publish src\DoubleClickDOS\DoubleClickDOS.csproj -p:PublishProfile=FrameworkDependent-win-x64 `
    -o dist\framework-dependent
```

Both profiles target `win-x64`; change `RuntimeIdentifier` in
`src\DoubleClickDOS\Properties\PublishProfiles\*.pubxml` for another architecture.

Copy the resulting folder wherever you like (for example `%LOCALAPPDATA%\Programs\Double-Click DOS`) and start
`DoubleClickDOS.exe`.

---

## Using it

1. **Start Double-Click DOS.** It appears as an icon in the notification area; there is no main window. Starting
   it a second time only shows a message that it is already running.
2. **Select your DOSBox executable.** Right-click the tray icon and choose **Select DOSBox**. The path is
   stored in your user settings, so this is only needed once. On the first start, when no usable DOSBox is
   configured yet, Double-Click DOS shows this prompt automatically; launching a game before that also
   brings it up.
3. **Optional: enable MT-32 music.** Right-click the tray icon and tick **Use MUNT (MT-32) for music**, then
   pick the MUNT executable (`mt32emu-qt.exe`, `munt.exe`, …). It is started before DOSBox and closed when
   DOSBox ends, and DOSBox's MIDI output is pointed at MUNT. Click the entry again to turn MUNT off.
4. **Double-click a DOS game** (or any `.exe`/`.com`/`.bat` file). DOS programs open in DOSBox in
   fullscreen; everything else is started the way Windows would have started it.
5. **Configure a game if necessary.** The first launch writes a `<program name>.doslaunch.conf` file next to
   the game executable. Edit it (drive letters, sound card, cycles, …) — it is reused on every later launch.
   Delete it to let Double-Click DOS generate a fresh one.
6. **Close Double-Click DOS** from the tray menu when you no longer want programs intercepted. Your file
   associations are restored immediately.

The tray menu contains **Select DOSBox**, the checkable **Use MUNT (MT-32) for music** entry, **About**
(logo, short description and a link to the project page) and **Close**.

### Command line

`DoubleClickDOS.exe` also accepts switches, which is how the shell association calls back into the application:

| Switch | Behavior |
| --- | --- |
| *(none)* | Starts the tray application and installs the interception. |
| `--launch <file> [arguments…]` | Launches one program; used by the intercepted associations. |
| `--register` | Installs the interception and exits (no tray icon). |
| `--unregister` | Restores the previous associations and exits. Useful after a crash that left the interception installed. |

---

## How it works

### 1. Intercepting shell launches

At startup (and only if single-instance ownership of the `Local\DoubleClickDOS.Tray` mutex is acquired)
[ProgramFileAssociation](./src/DoubleClickDOS/ProgramFileAssociation.cs) rewrites the per-user command behind the
`exefile`, `comfile` and `batfile` programmatic identifiers:

```
HKCU\Software\Classes\<class>\shell\open\command
    = "<path to DoubleClickDOS.exe>" --launch "%1" %*
```

Before overwriting a key, the previous value is stored **once** under
`HKCU\Software\DosLaunch\Backup\<class>` (a `Present` flag plus the original `Command`; the key keeps the name
the application had before it was called Double-Click DOS, so backups written by those versions still work).
This means an unregister run in a later process — even after a crash — restores what the user really had,
instead of whatever Double-Click DOS wrote. If there was no previous value, the value and the keys created by
Double-Click DOS are removed again.

Because Windows kills the process at logoff/shutdown without running normal cleanup code, a hidden window
listens for `WM_ENDSESSION` and unregisters the interception from there.

### 2. Deciding what was launched

[ProgramLauncher](./src/DoubleClickDOS/ProgramLauncher.cs) receives the file and its arguments and asks
[ExecutableInspector](./src/DoubleClickDOS/ExecutableInspector.cs) whether it is a DOS program:

- `.com` — raw code without a header, therefore treated as DOS unless the file actually starts with an `MZ`
  signature and that image carries a Windows header (e.g. a renamed Windows executable).
- `.exe` — an `MZ` header is required. The offset at `0x3C` (`e_lfanew`) is followed: if it points at a `PE`
  (32/64-bit Windows) signature, the program is started by Windows; every other image — including DOS
  extenders such as `LE` — belongs in DOSBox. An `NE` (16-bit) signature is a Windows program only when the
  image is not a protected-mode DOS program; Borland's 16-bit protected-mode games are linked as `NE` too and
  are recognised by the `RTM` runtime manager they import or their `PM STUB` DOS stub.
- `.bat` — [BatchFileInspector](./src/DoubleClickDOS/BatchFileInspector.cs) reads the commands, follows `cd`, and
  checks whether any referenced `.exe`/`.com` (the extension is appended when omitted, as DOS would) is a DOS
  program. Comments, `echo`/`title` lines, variables, wildcards and missing files are ignored, and the scan is
  bounded (128 KB, 32 programs) so it stays fast.

Programs that are *not* DOS are started with `Process.Start` — batch files through
`%ComSpec% /d /s /c "…"` — because they must never be handed back to the shell, which would immediately
route them into Double-Click DOS again.

### 3. Starting DOSBox

For a DOS program the launcher:

1. Resolves the configured DOSBox path. The setting may be the executable itself or a folder, in which case
   `dosbox-x.exe`, `dosbox-x-sdl2.exe`, `dosbox.exe` and `dosbox64.exe` are probed in that order. If nothing
   usable is configured, the user is asked to select the executable.
2. Detects the build via [DosBoxInstallation](./src/DoubleClickDOS/DosBoxInstallation.cs) (DOSBox-X vs. classic
   DOSBox) to adapt the command line. DOSBox-X gets `-fastlaunch`; classic DOSBox cannot declare itself DPI
   aware, so [HighDpiCompatibility](./src/DoubleClickDOS/HighDpiCompatibility.cs) sets the documented `HIGHDPIAWARE`
   compatibility flag in `HKCU\…\AppCompatFlags\Layers` (otherwise Windows blurs fullscreen output at 125 %+
   display scaling).
3. Appends the user's extra arguments, then either passes the program directly or starts DOSBox with
   `-conf <generated config>`.
4. When MT-32 emulation is enabled, starts the selected MUNT executable before DOSBox and closes the MUNT
   process it started as soon as DOSBox exits. A MUNT that was already running is left alone. The MUNT MIDI
   port is looked up through WinMM and written to the config (see below).

### 4. Generating the DOSBox configuration

[DosBoxConfigFile](./src/DoubleClickDOS/DosBoxConfigFile.cs) creates `<program>.doslaunch.conf` next to the
executable (`WILLY.EXE` → `WILLY.doslaunch.conf`). If two programs share a base name (`WILLY.EXE` and
`WILLY.BAT`), the second one gets `WILLY.BAT.doslaunch.conf` so that the autoexec starts the right program.
The `.doslaunch.conf` suffix is kept from the earlier name of the application, so configs that were already
edited in a game folder are still found and reused.

The generated file contains:

- Header comments naming the program, the estimated game and year, the evidence behind that estimate, and a
  human-readable explanation for every non-obvious setting.
- `[sdl]`, `[render]` and `[dosbox]` sections: fullscreen at desktop resolution, `aspect=true`/`nearest` and
  `scaler=none`, with `output=opengl` in every config — the only renderer Double-Click DOS uses, whichever
  DOSBox build runs it.
- `[cpu]` and `[sblaster]` from the era preset (see below), plus `[dosbox] memsize`.
- `[midi]` while MUNT is enabled: `mpu401=intelligent`, `mididevice=win32` and `midiconfig=<port>` of the
  MUNT ("MT-32 Synth Emulator") MIDI port. Configs that already choose a `mididevice` are left alone, so a
  deliberate MIDI setup — for example DOSBox-X's built-in MT-32 — survives.
- `[autoexec]`: the game folder mounted as `C:`, a CD-ROM drive mounted as `D:` (a `.cue`/`.iso` image via
  `imgmount`, a `cd`/`cdrom`/`disc`/`media`-like subfolder, or the game folder itself), then the program and
  its arguments, followed by `exit` so DOSBox closes when the game ends. Batch files are started with `call`
  so the `exit` is not swallowed.

Configs are never regenerated blindly. On every launch an existing file is kept, but:

- the `mount c "…"` path (and any image path inside it) is rewritten if the game folder was moved or renamed;
- options introduced by later versions (`fullscreen`, `fullresolution`, scaling, `dpi aware`, a
  missing `exit`) are added **only when absent**, so user edits survive;
- `output` is always set to `opengl`: the renderers written by earlier versions (`openglnb`, `ddraw`) and any
  other value are replaced, because it is the only renderer Double-Click DOS uses;
- a program name that older versions quoted is unquoted, because classic DOSBox cannot execute a quoted
  command without arguments.

### 5. Guessing the game and its era

A good DOSBox preset depends on the game's age. [GameInfoLookup](./src/DoubleClickDOS/GameInfoLookup.cs) resolves
the game in two stages:

**Local evidence** — [LocalGameEvidence](./src/DoubleClickDOS/LocalGameEvidence.cs) scans the program's folder
(up to 4000 files in the folder itself plus the contents of up to 20 subfolders) without ever relying on file
timestamps, because copying or unzipping a game rewrites them:

- Text files such as `file_id.diz`, `*.nfo`, `README*`, `*.txt`, `*.doc`, `*.cfg`, `*.ini`, `*.1st`, `*.me`
  (up to 40 files, 256 KB each). [GameTextEvidence](./src/DoubleClickDOS/GameTextEvidence.cs) extracts:
  - Total DOS Collection / GoodTools style metadata (`Name:`, `Year:`, `Publisher:`, `Game (1993)(…) […].zip`);
  - copyright notices — treated as *lower bounds*, since engines and installers are often newer;
  - explicit release notes, build stamps (`Mar 12 1994`), apostrophe years (`'94`) and short dates (`11.08.96`);
  - hardware requirements (minimum MB of RAM, "requires 386/486/Pentium").
- Up to three binaries (`.exe`, `.com`, `.ovl`, `.rtl`, `.dll`).
  [BinaryStrings](./src/DoubleClickDOS/BinaryStrings.cs) extracts printable ASCII runs, which the same parser
  evaluates; toolchain fingerprints (`DOS/4GW`, `DOS32A`, `CWSDPMI`, `WATCOM C/C++`, …) rule out earlier years.

Possible game names come from [GameTitles](./src/DoubleClickDOS/GameTitles.cs), which strips packaging noise
(`(1995)`, `[!]`, `v1.2`, `- CRACKED`), ignores generic names such as `GAMES` or `DOS`, understands 8.3
truncations, and scores `FILE_ID.DIZ` titles above folder names above executable names above readme headings.

**Online lookup** (optional, enabled by default) — the best title candidates are searched in archive.org's
MS-DOS software library (curated years, inherently DOS-only), Wikidata (publication date `P577` plus DOS
platform `P400`) and finally Wikipedia. A hit only counts when the titles match (similarity ≥ 0.8), and a
Wikidata year is only trusted when it confirms the DOS platform or agrees with archive.org, so a console or
Amiga release date cannot leak in. Lookups are cached per executable path in
`%LOCALAPPDATA%\DoubleClickDOS\gameinfo.json`; unresolved results are retried after 30 days or as soon as online
lookup is enabled.

**Weighted voting** — [GameEraEstimator](./src/DoubleClickDOS/GameEraEstimator.cs) deduplicates hints per source,
votes them into era buckets, lets lower bounds rule out clearly older buckets (with a two-year tolerance for
late installers) and reports a confidence (`high` requires several sources). Ties go to the newer era,
because too fast settings still run while too little memory crashes.
[GameProfileBuilder](./src/DoubleClickDOS/GameProfile.cs) then derives the profile:

| Estimated release | `memsize` | `cputype` | `cycles` | `sbtype` |
| --- | --- | --- | --- | --- |
| 1981–1989 | 2 | `386` | `3000` | `sb1` |
| 1990–1992 | 4 | `386` | `auto` | `sbpro2` |
| 1993–1996 | 16 | `auto` | `auto` | `sb16` |
| 1997 or later | 32 | `auto` | `auto` | `sb16` |
| unknown | 16 | `auto` | `auto` | `sb16` |

The profile is corrected upwards when the documentation asks for more memory or protected mode, or when a
DOS extender is found in the folder; a present `setup.exe`/`install.exe` is pointed out in the config. A
`386` is used for the oldest bucket because classic DOSBox has no 286 CPU type, which keeps generated
configs portable between builds.

### 6. Settings, cache and log

| File | Location | Purpose |
| --- | --- | --- |
| `settings.json` | `%APPDATA%\DoubleClickDOS\` | `DosBoxPath` (folder or executable), `MuntPath` (MUNT folder or executable; empty while MT-32 is off), `ExtraArguments` (space-separated, passed to DOSBox), `LookUpGameInfoOnline`. Old `DosBoxX*` keys are migrated automatically. |
| `gameinfo.json` | `%LOCALAPPDATA%\DoubleClickDOS\` | Cached game identities keyed by executable path. |
| `doubleclickdos.log` | `%LOCALAPPDATA%\DoubleClickDOS\` | Append-only log of detection, lookup, config and launch decisions; rotated at 512 KB. |
| `*.doslaunch.conf` | Next to the game executable | Generated DOSBox configuration, editable. |

Settings and the game info cache are also read from the `%APPDATA%\DosLaunch\` and
`%LOCALAPPDATA%\DosLaunch\` folders of earlier versions: they are picked up on first start and then written to
the new locations.

---

## Project layout

```
doubleclickdos.slnx              Solution (app + tests)
build.ps1                        Publishes the framework-dependent and self-contained builds
assets/prog_icon.png             Tray icon
assets/about.png                 Logo in the About window
src/DoubleClickDOS/              The WinForms tray application
  DoubleClickDOS.csproj          Project file (net10.0-windows, WinForms)
  Properties/PublishProfiles/    win-x64 publish profiles for both distribution builds
  Program.cs                     Entry point and command-line switches
  TrayApplication.cs             Tray icon, menu and session-end handling
  AboutForm.cs                   About window with logo, description and project link
  ProgramFileAssociation.cs      Registry interception and restore
  ProgramLauncher.cs             `--launch` handling, DOSBox/Windows dispatch
  ExecutableInspector.cs         MZ/PE/NE checks for .exe and .com files
  BatchFileInspector.cs          "Does this batch file start a DOS program?"
  DosBoxInstallation.cs          DOSBox vs. DOSBox-X detection
  DosBoxSelector.cs              File picker for the DOSBox executable
  MuntSelector.cs                File picker for the MUNT executable
  MuntSession.cs                 Starts MUNT before DOSBox and closes it afterwards
  MidiOutputDevices.cs           Finds the MUNT MIDI port for the DOSBox config
  HighDpiCompatibility.cs        DPI awareness flag for classic DOSBox
  DosBoxConfigFile.cs            Config generation, mounting, config upgrades
  GameProfile.cs                 Folder scan and era preset → DOSBox settings
  GameIdentity.cs                Identity records, eras, evidence, hardware hints
  GameInfoLookup.cs              Local + online resolution, caching
  LocalGameEvidence.cs           Collects hints from the game folder
  GameTextEvidence.cs            Year/hardware parsing of text and strings
  GameTitles.cs                  Title candidates and name similarity
  GameIdentityCache.cs           Persistent identity cache
  GameInfoSources.cs             archive.org, Wikidata, Wikipedia clients
  BinaryStrings.cs               Printable string extraction from binaries
  GameEraEstimator.cs            Weighted voting over year evidence
  AppSettings.cs                 settings.json
  Log.cs                         Log file
tests/DoubleClickDOS.Tests/      xUnit tests for detection, parsing, presets and configs
```

---

## Troubleshooting

- **Nothing happens when double-clicking a game.** Make sure Double-Click DOS is running (tray icon present).
  Without it, the associations are restored and `.exe` files are handled by Windows again.
- **"Double-Click DOS is already running."** Only one instance may run; use the tray icon to change settings or
  close the running instance.
- **A DOS program starts as a Windows program or vice versa.** Check
  `%LOCALAPPDATA%\DoubleClickDOS\doubleclickdos.log`; it names the detected program type and every decision
  taken.
- **A game runs with the wrong hardware settings.** Read the comments at the top of its `.doslaunch.conf`, then
  adjust the values. Delete the file to regenerate it from scratch. If the year estimate is wrong, the log
  lists the evidence it used.
- **No year was detected.** Make sure `LookUpGameInfoOnline` is enabled in `settings.json`, and delete the
  game's entry in `gameinfo.json` to force a new lookup.
- **The game wants its CD on another drive letter.** Edit the `imgmount`/`mount` line and the drive letter in
  `[autoexec]`.
- **No MT-32 music although MUNT is enabled.** MUNT needs its WinMM MIDI driver installed (the MUNT setup
  offers it) so its "MT-32 Synth Emulator" port exists; MUNT must also have its MT-32 ROMs configured, and
  the game must be set up to use MT-32/MPU-401. The log names the detected MIDI port; if none is found,
  the generated config keeps its MIDI settings unchanged.
- **Fullscreen looks blurry with classic DOSBox.** Double-Click DOS sets the `HIGHDPIAWARE` compatibility flag for
  the selected DOSBox executable; if you moved or replaced DOSBox, select it again so the flag is applied.
- **Associations stuck after a crash.** Run `DoubleClickDOS.exe --unregister` once; it restores the backed-up
  commands and removes the state key.

## Limitations

- Windows only; the interception relies on `HKCU\Software\Classes`, so programs started by services or other
  users are not affected.
- While running, *all* `.exe`/`.com`/`.bat` double-clicks take a short detour through Double-Click DOS, including
  Windows programs.
- Detection is heuristic: heavily packed or non-standard images may be misclassified, and batch files that
  depend on variables, `call` chains or installed DOS programs cannot always be resolved.
- Only `.cue` and `.iso` disc images are mounted automatically; multi-disc games need manual `imgmount` edits.
- The online lookup sends the guessed game titles to archive.org, Wikidata and Wikipedia (no personal data,
  no API keys). It can be switched off in `settings.json`.

---

## Acknowledgements

Double-Click DOS was created with the help of DeepSeek V4.1 Flash.
