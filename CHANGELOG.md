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
- **2D sorting manager**: a scene-wide window that lists every renderer in draw order
  (front-most at the top), supports drag-to-reorder, and writes the result back as a
  clean `0, 1, 2 …` run of `sortingOrder` values per sorting layer. Also flags the
  renderers whose relative order is undefined because they share layer / order / Z.
  It only touches sorting layer and order — Z is never modified.
  Reachable from `Window ▸ 2D Sorting Manager`, `Tools ▸ 2D Sorting Manager`, and
  `GameObject ▸ 地形 2D ▸ 2D Sorting Manager`.
- **Sorting controls in the terrain viewport toolbar**: a new row with a sorting-layer
  dropdown and an order field for the terrain itself, plus a button that opens the
  scene-wide sorting manager.

### Notes

- Verified on Unity 2022.3.53f1 with URP 14 (2D Renderer).

[Unreleased]: https://github.com/CHANGE_ME/terrain2d/commits/main
