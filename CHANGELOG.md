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
- **Colour for any 2D object**: the same toolbar also carries a colour field. Where the
  colour lives depends on the object — `PolygonMesh2D` and `HeightmapTerrain2D` keep it on
  the component (and are told to refresh), a `SpriteRenderer` uses Unity's own `color`
  property so the Inspector stays in sync, and anything else is left alone rather than
  guessed at.
- **Z field next to the sorting order**: sorting order and Z are two halves of the same
  thing — when orders tie, Unity falls back to Z — so both toolbars now show a Z field
  beside the order. Written to world Z, because that is what the camera actually sorts
  by, which is what "put this in front" means. This is the groundwork for
  foreground / background layers.
- **Named Z presets**: the `▾` beside the Z field saves and recalls named depths —
  background / ground / foreground without remembering the numbers. Each entry shows its
  value next to the name, because a Z number means nothing on its own. Stored in
  `EditorPrefs` alongside the colour palette.
- **Collider toggle**: a checkbox in the any-2D-object toolbar that enables or disables the
  selected objects' `Collider2D`, so background decoration stops blocking the player without
  a trip to the Inspector. Greyed out rather than hidden when there is no collider — a
  missing control looks like a missing feature.
- **Empty-selection toolbar**: with nothing selected, a small toolbar offers
  创建高度场地形, so starting a level does not mean hunting through the GameObject menu.
  A selection that has no renderer on it stays quiet instead.
- **Named colour palette**: the `▾` beside any colour field lists saved colours and can
  save the current one under a name. Kept in `EditorPrefs`, so it belongs to the user
  rather than to the project — a deliberate trade, since a swatch list in the project
  folder would show up in version control and merge badly.

### Notes

- Verified on Unity 2022.3.53f1 with URP 14 (2D Renderer).

[Unreleased]: https://github.com/CHANGE_ME/terrain2d/commits/main
