# VOLK LAUNCHER

A small, fast Minecraft: Java Edition launcher that runs on **stock Windows 8** (and 8.1 / 10 / 11).
One `.exe`, no installer, no .NET download, no Electron, no WebView2 — about **58 KB**.

Made for weak machines: Core 2 Duo / 4 GB RAM / old integrated graphics.

---

## Why this exists

Horizon Launcher and the new official launcher do **not** work on Windows 8:

* they need `icu.dll` / `icuuc.dll`, which Windows only added in **Windows 10 1703**
* they use WebView2, which is not supported on Windows 8
* Horizon's game needs **Vulkan or DirectX 12**, which Core 2 Duo-era GPUs do not have

Volk Launcher sidesteps all of that: it is plain .NET 4.5 WinForms (already part of Windows 8),
and it launches normal **Java** Minecraft.

---

## Quick start (the friend with Windows 8)

1. Copy `VolkLauncher.exe` to any folder (Desktop is fine) and double-click it.
2. Type a nickname (3–16 letters/numbers/underscore).
3. Leave version on **1.8.9** (best for this hardware).
4. Press **PLAY**.
   * If Java is missing it offers to download **Java 8** automatically (~45 MB).
   * It downloads the game files (~60 MB without sounds) and starts Minecraft.

That is all. Next time it starts instantly.

### Recommended settings (already applied)

The **Smooth preset** rewrites `options.txt` on every launch:

| Setting | Value |
|---|---|
| Render distance | 4 chunks |
| Graphics | Fast |
| Smooth lighting / AO | off |
| Particles | minimal |
| Clouds / entity shadows | off |
| Mipmaps | 0 |
| Max FPS | 60 |
| Memory | 1536 MB (auto-capped to half of system RAM) |

---

## What the launcher does

* Resolves any Mojang version from the official manifest (all releases listed in the dropdown).
* Downloads client jar, libraries, Windows natives, and the asset index/objects.
* Verifies/extracts natives into `versions/<id>/natives`.
* Builds the Java command line (`-Xmx`, classpath, natives path, all `${...}` placeholders).
* Picks the right Java per version: Java 8 for ≤1.16.5, Java 17 for 1.17–1.20.4, Java 21 for newer.
* Handles `inheritsFrom` versions (OptiFine/Forge-style).
* Streams the game log into the window and to `logs/volk-latest.log`.
* Detects the GPU and shows whether it can do OpenGL 3.3 / Vulkan.
* Warns about a fake `opengl32.dll` in the game folder (forces ~15 FPS software rendering).
* **Performance boost while playing**: switches Windows to the High performance power plan,
  sets the game process to High priority, then restores your old power plan on exit.
* **Window size** selector: 640x360 / 854x480 / 1280x720.
* **Fast mods (Sodium)**: for 1.17+ versions it creates a Fabric profile automatically and installs
  Sodium, Lithium, FerriteCore, ImmediatelyFast, Krypton and Fabric API from Modrinth, then launches Fabric.
  This is the same optimisation set "fast clients" ship, but on vanilla files.
* **Any mod you paste**: drop `.jar` files into the mods folder. If the folder has mods, that
  version is launched through Fabric automatically (the Sodium box does not need to be ticked).
  Missing performance mods are added, mods you already have are kept (no duplicates), and a
  warning is printed when a mod declares a different Minecraft version.
* **Horizon 26.3 (OpenGL)** button: refuses on OpenGL 2.1 GPUs (impossible), otherwise starts
  `horizon-client.exe` with `HORIZON_GRAPHICS_API=opengl --render-distance 4`.

## Presets

| Preset | Render distance | GUI scale | Max FPS | Use |
|---|---|---|---|---|
| Potato | 2 chunks | 1 | 120 | 4 GB / integrated GPU / "15 FPS" |
| Smooth | 4 chunks | 2 | 60 | default for Core 2 Duo |
| Off | yours | yours | yours | you tuned it yourself |

All presets also disable clouds, entity shadows, smooth lighting/AO and mipmaps, and use fast graphics.

## If FPS is terrible (15 FPS is NOT normal, even on a Core 2 Duo)

1. In game press **F3**. Read the line that starts with `OpenGL:`.
   * `GDI Generic` or `OpenGL 1.1` -> **software rendering**. Fix the driver, or delete a fake `opengl32.dll`.
   * `OpenGL 3.3` / `4.x` -> hardware rendering is fine, the bottleneck is settings/CPU/RAM.
2. Control Panel -> Power Options -> **High performance** (a Core 2 Duo laptop throttled to
   800 MHz gives exactly ~15 FPS).
3. Close the browser before playing. Chrome alone eats the whole 4 GB.
4. Use the **Potato** preset.
5. Install the last GPU driver that supports Windows 8 (Windows Update will not offer it).
6. 64-bit Windows + 64-bit Java 8 only. 32-bit Java cannot use more than ~1 GB.
7. Laptop: clean the fan. Old thermal paste = permanent throttling.

## Can it run 26.3?

Horizon does have an OpenGL renderer (`Options > Graphics`, or `HORIZON_GRAPHICS_API=opengl`),
but:

* OpenGL 3.3 is required. GMA 950/3100/4500 (OpenGL 2.1) cannot do it, at all.
* The Horizon **launcher** needs Windows 10 (`icu.dll`); only the portable
  `horizon-client.exe` works on Windows 8.
* 26.3 needs ~3 GB of memory for assets + world. On 4 GB total that leaves Windows almost nothing.

The practical way: prepare 26.3 resources on a Windows 10/11 PC, copy the
`horizon-client.exe` + resource folder to the old PC, then use the
**Horizon 26.3 (OpenGL)** button in Volk.

### What "fast 26.3" actually requires

| Part | Minimum for 30+ FPS | Cost (used) |
|---|---|---|
| GPU | Vulkan or OpenGL 4.x card, e.g. GT 1030 / GTX 750 Ti / RX 550 | $25-40 |
| RAM | 8 GB (26.3 loads ~3 GB of assets) | $10-15 |
| CPU | 4 threads (Core 2 Quad Q6600 / any i3+) | $10-20 |
| OS | Windows 10 64-bit | free |

With those, 26.3 runs 40-60 FPS at 720p low. On the current PC it is 10-20 FPS at best,
and that is with everything turned down.

**The only way to get fast 26.3 without buying parts** is a cloud Windows PC
(e.g. Shadow PC): the game runs on their GPU and your PC only decodes video.
A Core 2 Duo handles that fine.



## Installer

`dist\VolkSetup.exe` (85 MB) is a one-click installer for someone who has nothing:

1. copies the launcher to `%LOCALAPPDATA%\Volk\VolkLauncher.exe`
2. unpacks **Java 8** and **Java 21** to `%APPDATA%\.volk\java` (per-user, no admin rights)
3. writes a ready config: version **1.21.11**, Potato preset, 640x360, 1536 MB, Sodium on
4. creates **Desktop** and **Start Menu** shortcuts
5. optional checkbox: pre-download 1.21.11 + Fabric + Sodium (~250 MB) so the first PLAY is instant
6. starts the launcher

Silent mode: `VolkSetup.exe /quiet [/preload]`

## Mods folder

Drop jars into `%APPDATA%\.minecraft\mods`. Rules:

* **Fast mods ON** = performance pack: launches through Fabric, installs Sodium/Lithium/FerriteCore/
  ImmediatelyFast/Krypton/Fabric API if they are missing, and stashes jars that declare a different
  Minecraft version in `mods.disabled` (they are moved back automatically when you launch their version).
* **Fast mods OFF** = hands off. Nothing is moved, nothing is added, nothing is deleted. The version
  still launches through Fabric so your own mods load. Use this on your own PC.

## Limits (read this)

* **Offline nickname only.** No Microsoft login yet. Servers with `online-mode=true`
  (Hypixel etc.) will reject it. It works on LAN worlds and offline-mode servers.
* **26.3 / 1.17+ cannot render on a Core 2 Duo.** Those versions require a GPU with
  Vulkan/DirectX 12 and a lot more RAM. The launcher warns you before trying.
  Play **1.8.9** or **1.12.2** on that PC — that is the realistic ceiling.
* Sounds are optional ("Download sounds +150 MB"). Without them the game is silent.

---

## Files

```
%APPDATA%\.volk\launcher.log          launcher log
%APPDATA%\.volk\config.txt            saved settings
%APPDATA%\.volk\java\jre8\...         Java 8 downloaded by the launcher
%APPDATA%\.minecraft\                 game folder (worlds, options.txt, logs\volk-latest.log)
```

## Troubleshooting

| Symptom | Fix |
|---|---|
| "Java was not found" | Press PLAY, accept the Java download, or install Temurin JRE 8 and use **Browse** |
| Game closes instantly | Open `%APPDATA%\.minecraft\logs\volk-latest.log` — the last lines say why |
| Black screen / crash on 1.17+ | Expected on old GPUs. Use 1.8.9 |
| "32-bit Windows detected" warning | The OS is 32-bit: Minecraft is capped at ~1 GB. Install 64-bit Windows 10 |
| Slow / stutter | Close the browser first (Chrome eats the whole 4 GB), keep render distance 4 |
| Any error | Send `%APPDATA%\.volk\launcher.log` and `logs\volk-latest.log` |

## Build from source

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Uses the C# compiler that ships with .NET Framework 4 (`csc.exe`), no SDK required.
Produces two files:

| File | Size | Java |
|---|---|---|
| `dist\VolkLauncher.exe` | ~80 KB | downloads Java only if the PC has none |
| `dist\VolkLauncher-offline.exe` | ~90 MB | **Java 8 + Java 21 embedded** — unpacked to `%APPDATA%\.volk\java` on first use |

The offline build needs `assets\jre8.zip` and `assets\jre21.zip` (Temurin JRE zips); it is skipped
if they are missing. Nothing else changes: both exes run the same code, the offline one just
carries the runtimes so the friend never downloads or installs Java.

Note: the game files themselves (~60 MB for 1.8.9, ~250 MB for 1.21.11) still come from Mojang on
first launch. The embedded Java only removes the Java download/install step.


Source layout:

```
src\Program.cs     entry point, crash logging
src\MainForm.cs    WinForms UI (dark theme, custom-drawn controls)
src\Core.cs        settings, system info, HTTP, JSON, Java discovery
src\Game.cs        version resolve, downloads, natives, launch, presets
```
