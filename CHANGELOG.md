# Changelog

All notable changes to this project are documented in this file.

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
