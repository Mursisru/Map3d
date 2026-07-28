# Map3d

[![Nuclear Option](https://img.shields.io/badge/Game-Nuclear%20Option-blue)](https://store.steampowered.com/app/2168680/Nuclear_Option/)
[![BepInEx 5](https://img.shields.io/badge/Loader-BepInEx%205-orange)](https://docs.bepinex.dev/)
[![Version](https://img.shields.io/badge/Version-0.3.2-green)](https://github.com/Mursisru/Map3d)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow)](LICENSE)

BepInEx 5 plugin for **Nuclear Option** that tilts the stock minimized `DynamicMap` into a 3D cloth view with terrain height relief. Unit icons and the view cone render on the same tilted layer as the map substrate.

---

> [!IMPORTANT]
> **BepInEx 5 (x64) required** — install [BepInEx](https://docs.bepinex.dev/articles/user_guide/installation/index.html) before this mod.

> [!NOTE]
> **Maximize stays vanilla 2D.** Only the minimized cockpit map is replaced with the tilted RenderTexture view.

> [!WARNING]
> After rebuilding or updating, delete `BepInEx\config\com.at747.map3d.cfg` if old camera/icon/height defaults stick around.

> [!TIP]
> Use [Configuration Manager](https://github.com/BepInEx/BepInEx.ConfigurationManager) to tweak tilt, zoom, height relief, grid opacity, icon size, and cone scale in-game.

---

## Features

* **Tilted stock MapImage** — heading-up UV window (look-ahead 4000 m like `CenterMinimizedMap`), default tilt **55°**, black clear.
* **Terrain height relief** — full-map height cache baked on mission load; cloth and icons displaced from cache.
* **Horizon fill** — cloth extends ahead past the visual horizon (`HorizonFarScale`).
* **Same-layer icons** — unit sprites on the cloth pivot; stock flat `iconLayer` / `viewIndicator` hidden while 3D is active.
* **World heading on billboards** — aircraft icons face real `unit.forward` (not locked to your nose); still billboard toward the map camera.
* **Stock-sized view cone** — length from stock `viewIndicator` rect / `mapDisplayFactor`; tip pivot matched to vanilla `(0.5, 0.05)`.
* **Stock zoom framing** — visible radius derived from `mapScaleMinimized` × display factor × map lossy scale.
* **Stock 3D grid** — flat vanilla `mapGrid` sprites on the cloth (no relief); default 50% opacity; HUD tiles stay hidden in minimized 3D.

---

## Requirements

* **Nuclear Option** ([Steam](https://store.steampowered.com/app/2168680/Nuclear_Option/)).
* **BepInEx 5** (x64) in the game root.

---

## Player installation

1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx) for Nuclear Option.
2. Copy `Map3d.dll` into:

   ```text
   Nuclear Option\BepInEx\plugins\
   ```

3. Launch the game. Minimized map shows the tilted view; maximize is stock 2D.

---

## Configuration

Config file: `BepInEx\config\com.at747.map3d.cfg`

| Key | Default | Notes |
| --- | --- | --- |
| `TiltDegrees` | 55 | Cloth pitch toward the player |
| `UseStockZoom` | true | Radius from stock minimap scale |
| `LookAheadMeters` | 4000 | Same as stock `CenterMinimizedMap` |
| `HorizonFarScale` | 4.5 | Cloth extent ahead / radius |
| `Height.Enabled` | true | Displace cloth from height cache |
| `Height.CacheResolution` | 256 | Full-map bake resolution |
| `Height.VisualFraction` | 0.28 | Relief vertical span / radius |
| `IconSizeFraction` | 0.05 | Icon world size / radius |
| `ConeLengthScale` | 1 | Multiplier on stock view-cone meters |

---

## Build

```text
dotnet build Map3d.csproj -c Release
```

Release build auto-deploys `Map3d.dll` to the game `BepInEx\plugins` folder when `NuclearOptionRoot` is set (see local `Directory.Build.props`, not published).

---

## Changelog

See [CHANGELOG.md](CHANGELOG.md).

---

## Licence

MIT — see [LICENSE](LICENSE).

---

Keywords: `nuclear-option`, `bepinex`, `minimap`, `dynamicmap`, `harmony`, `unity`, `mod`
