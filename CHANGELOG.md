# Changelog

All notable changes to this project are documented in this file.

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
