# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `HeightmapTerrain2D`: equal-spacing height field terrain with a closed
  four-edge outline (top / right / bottom / left).
  - `SampleHeight(localX)` for ground queries (spawn placement, pathfinding).
  - `DestroyCircle(localX, radius)` for runtime crater-style destruction.
  - Seeded procedural generation (Perlin, octaves / persistence / lacunarity).
  - Automatic `EdgeCollider2D` / `PolygonCollider2D` generation.
  - Per-point horizontal offsets, so surface points can be dragged freely in 2D.
- `PolygonMesh2D`: builds a mesh from any `PolygonCollider2D` outline,
  including holes, via a keyhole-free multi-path triangulation.
- `BottomEdge`: an independent polyline for the bottom edge. Adding a point
  there no longer touches the surface — the bottom's point count and positions
  are fully decoupled from the height field samples.
- Scene view toolbar pinned to the bottom of the viewport (collapsible), with
  live point counts, colour picker, and batch operations.
- `Triangulator`: ear-clipping triangulation that tolerates collinear vertices.
- **Sorting order field in the terrain viewport toolbar**: a new row with an order
  field for the terrain itself, so the draw order can be changed without going to the
  Inspector.
- **Sorting order for any 2D object**: a small toolbar pinned to the bottom of the Scene
  view whenever the selection contains renderers, showing just the sorting order (and
  handling multi-selection, including mixed values). It steps aside while a
  `HeightmapTerrain2D` is selected, because the terrain editor draws its own toolbar in
  the same spot.
- **Level design mode**: an `IMGUIOverlay` docked to the Scene view's top toolbar
  (`DockZone.TopToolbar`) with a toggle that hides every overlay this package draws, so
  the viewport can be cleared for checking art, screenshots, or recording. A 选中地形
  button beside it selects the terrain in the scene and puts it into edit mode. The state
  lives in `EditorPrefs` and defaults to on, so installing the package changes nothing
  until you opt out. The overlay itself is never hidden — otherwise there would be no way
  to switch it back on.

### Notes

- Verified on Unity 2022.3.53f1 with URP 14 (2D Renderer).

[Unreleased]: https://github.com/CHANGE_ME/terrain2d/commits/main
