# Changelog

All notable changes to this project are documented in this file.

## [0.3.6] — 2026-07-28

### Added

* `ClothExclusionLayer` — nuclear exclusion circles on the cloth grid layer from `HQ.GetExclusionZones()`.
* `ClothNotchLayer` — ARH/SARH notch lines at the aircraft with stock bearing (`mapImage.z − yaw`) and seekerMode color; screen-plane 2D billboard.
* `ClothTargetMarkerLayer` — selected-unit target brackets on cloth (3D position, 2D billboard) plus SPD/ALT/HDG/RNG TextMesh from stock `TargetMarker` texts.
* `ClothSpriteUtil` — shared transparent sprite material (clone of Unity default SpriteRenderer mat + SrcAlpha) and builtin font setup for TextMesh.

### Fixed

* Building hangar/ammo icons readable size (stock min floor) and cam-facing rects.
* Notch bearing stability vs nose jitter; true screen-plane 2D display (no cloth foreshortening).

## [0.3.5] — 2026-07-28

### Fixed

* Large building / hangar / ammo map icons missing on the 3D cloth: stock true-size footprints were drawn at real meters (~tens of m) and vanished next to vehicle billboards (100–800 m). Buildings now use a camera-facing rect with stock `width×length` proportions plus the stock min-size floor (`mapInverseScale×10` equivalent) and camera pull.
* Soft-hide stock `iconLayer` via `CanvasGroup` (keeps `UpdateIcon` alive) instead of `SetActive(false)`.

### Changed

* Building icons (`Building && maxRadius > 10`) render as oriented billboard rects rather than flat coplanar footprints (Sprites/Default depth vs opaque cloth).

## [0.3.4] — 2026-07-28

### Added

* `ClothObjectiveLayer` — stock objective markers on the 3D cloth (billboard sprites, terrain lift).
* `ClothRadarLayer` — stock radar ping lines (`radarVisPrefab`) from emitter to own aircraft on cloth.

### Fixed

* Distant unit icons on the tilted minimap no longer shrink to dots: perspective scale compensation (cap ×3.5).

## [0.3.3] — 2026-07-28

### Added

* `MapBrightness` config (default 1.22) to match stock flat map albedo on Unlit cloth.
* RenderTexture quality: 1024px, MSAA 4, mipmapped soft map albedo with configurable `MapMipBias`.

### Fixed

* Residual map float on yaw and large cloth spans: removed vertex UV `Clamp01` (GPU clamp per-fragment), extent hysteresis instead of per-frame lerp, cloth mesh resolution scales with span, grid UV synced 1:1 with cloth.
* Cloth albedo no longer multiplied by `mapBackground` tint (often black).

## [0.3.2] — 2026-07-28

### Changed

* Cloth extends to map borders via heading-independent cardinal edge reach from the look-ahead center (stable on yaw, no diagonal corner stretch).

## [0.3.1] — 2026-07-28

### Added

* Single stock `mapGrid` UV quad on the cloth canvas (map-tiled, line-only bake, 50% opacity overlay).
* Stock look-ahead pivot (`mapCenter = aircraft + forward × 4000 m`) matching `CenterMinimizedMap`.
* Geographic height smoothing cache to stabilize terrain relief during heading changes.

### Fixed

* Floating/wavy cloth on yaw turns (height resampling + framing mismatch).
* Green RT clear replaced with black; `MapBackground` stays cloth tint only.
* Opaque cloth material (`Unlit/Texture`, forced alpha 1).
* Hide full `gridLabels` in minimized 3D to prevent duplicate grids.

## [0.3.0] — 2026-07-28

### Added

* Flat stock `mapGrid` tiles on the 3D cloth (`StockClothGrid`) — same sprites as vanilla, no terrain displacement, configurable opacity (default 0.5).
* Cloth extends to map side/forward borders; camera look-ahead stays on the stock minimap window.

## [0.2.0] — 2026-07-27

### Added

* Full-map `HeightMapCache` bake on mission load via `PathfindingAgent.RaycastTerrain`.
* Cloth mesh displacement from cached heights (bilinear sample); icons and view cone follow relief.
* Asymmetric cloth extents (`HorizonFarScale` / near / side) so map fills past the tilt horizon.

### Fixed

* Invisible relief when mesh Y was incorrectly divided by cloth size.

## [0.1.0] — 2026-07-27

### Added

* Tilted stock `DynamicMap` minimap (cloth MapImage).
* Unit icons and view cone on the same tilted layer as the map substrate.
* Stock zoom framing, heading-aligned UVs, look-ahead 4000 m.
* World-heading billboards for aircraft / `mapOrient` icons.
* Hide stock `mapImage`, `iconLayer`, `viewIndicator`, and `mapGrid_*` while minimized 3D is active; maximize restores vanilla 2D.
