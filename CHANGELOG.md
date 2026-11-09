# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.2.0] - 2026-11-09

### Added

- **Surface-following UVs (`uvMode`)**: a second UV mapping mode alongside the original
  planar one. `Planar` keeps the old behaviour — UV is world X/Y times `uvScale`, which is
  trivial but stretches the texture on slopes by `1 / cos(slope)`. `SurfaceTiling` walks
  the surface instead: U is the **arc length** from the start of the surface, V is the
  **perpendicular distance** below it (vertical depth times `cos(slope)`). Both are in
  world units, so `1 / uvScale` means the same thing everywhere and texel density is
  constant across flats, ramps, and cliffs. Defaults to `Planar`, so existing scenes look
  exactly as they did.
- `SurfaceLength`, `ArcLengthAt(index)`, `SlopeCosAt(index)`: the derived metrics behind
  the new mode, exposed publicly. `SurfaceLength` is the total walkable length of the
  surface; the other two read per-sample values. Rebuilt once per `Rebuild()` in O(n) —
  cheap enough to leave unconditional.
- **UV grid overlay (`showUVGrid`)**: draws a tick along the surface every `1 / uvScale`
  world units in the Scene view, so the mapping can be checked **before any texture
  exists**. Even spacing on a flat *and* even spacing measured along a ramp is the proof
  that U is arc-length driven; ticks that thin out on ramps mean it is still planar. Capped
  at 2000 lines so a tiny `uvScale` cannot lock up the viewport.
- **`TestPlayer2D`** — a controllable test character, created from
  `GameObject → 地形 2D → 创建测试角色`. Left/right, jump, coyote time, jump buffer, and an
  extra fall multiplier; jump height is expressed in world units (`v = sqrt(2gh)`) rather
  than raw velocity, so it can be compared directly against the gaps in the level.
  Ground detection uses `Rigidbody2D.IsTouching` with a normal-angle filter, so standing
  against a wall does not count as grounded and slopes up to `maxSlopeAngle` do.
  It generates its own 16×16 placeholder sprite and a frictionless `PhysicsMaterial2D`
  (both `HideAndDontSave`, shared per process), so it is visible and playable with zero art.
  Deliberately **not** a production controller — it exists to answer "can I jump that gap"
  immediately after editing the terrain.

### Notes

- `TestPlayer2D` uses the legacy `Input` API, matching this project's
  `activeInputHandler: 0`. A project on the new Input System needs the four lines in
  `Update()` replaced with `InputAction` reads.
- `TestPlayer2D` only touches the `Rigidbody2D` in Play mode. Configuring it from `Awake`
  in edit mode would dirty the scene on every recompile.

## [0.1.1] - 2026-11-09

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
- **Collider toggle**: a checkbox in both toolbars that enables or disables the selected
  objects' `Collider2D`, so background decoration stops blocking the player without a trip
  to the Inspector. On the terrain it means both halves: unticking sets `buildColliders`
  to false *and* disables the colliders already attached — setting the flag alone only
  makes `Rebuild()` skip `BuildColliders()`, leaving the old colliders live, which looks
  like the checkbox did nothing. Greyed out rather than hidden when there is no collider.
- **Empty-selection toolbar**: with nothing selected, a small toolbar offers
  创建高度场地形, so starting a level does not mean hunting through the GameObject menu.
  A selection that has no renderer on it stays quiet instead.
- **Named colour palette**: the `▾` beside any colour field lists saved colours and can
  save the current one under a name. Kept in `EditorPrefs`, so it belongs to the user
  rather than to the project — a deliberate trade, since a swatch list in the project
  folder would show up in version control and merge badly.

### Fixed

- `DestroyCircle` no longer punches through a sculpted bottom edge. The crater floor used
  `BottomAt(index)`, which only knows about `_bottomHeights` and `bottomY` — it never
  looked at the independent `_bottomEdge` polyline. On a terrain whose bottom had been
  sculpted (a floating island, an overhang) a crater would cut straight through the
  visible bottom and leave a hole. There is now a `BottomHeightAt(localX)` that resolves
  the bottom the same way `BuildOutline` does, and the crater clamps against it.
- Bottom edge points can no longer be dragged above the surface. `ClampBottomProfile`
  only clamped `_bottomHeights`; `_bottomEdge` was left free, so a bottom point pulled
  above the top curve made the outline self-intersect, ear clipping failed, and the mesh
  silently froze. Both sources are clamped now, so the point simply refuses to go above
  the ground.
- `SampleHeight` no longer returns nonsense when surface points are dragged past each
  other. It assumed `TopXAt(i)` was monotonically increasing and binary-searched on that
  assumption; free horizontal dragging can break it. Monotonicity is now checked (and
  cached, invalidated on `Rebuild`), with a linear fallback that takes the highest surface
  crossing the query X.
- `Generate()` now resets the shape overrides it used to leave behind: `LeftEdge`,
  `RightEdge`, `BottomEdge`, and both horizontal offset arrays. Previously it only reset
  `_bottomHeights`, so regenerating on an already-sculpted terrain produced a new surface
  wearing the old outline. Changing `sampleCount` in the Inspector still goes through a
  new `GenerateHeights()` that only re-lays the surface and keeps the edges — that path
  is "same terrain, different density", not "throw my edges away".
- `PolygonMesh2D` no longer allocates every frame in the editor. Its live-update poll ran
  on every instance regardless of selection, and `ReadPaths()` allocates a `Vector2[]`
  per path per call — a level with a dozen terrains meant a dozen array allocations per
  frame. In edit mode the poll now only runs while the object is selected (the outline
  can only change while you are dragging its points); Play mode is unchanged, since
  runtime scripts can edit colliders at any time.
- Meshes created at runtime are now destroyed in `OnDestroy`. They carry
  `HideFlags.DontSave`, so Unity never reclaims them — every scene load and every Play
  mode round-trip leaked one mesh per terrain.
- The three scene-view toolbars no longer share a hard-coded `32f` bottom inset. It is
  now `SceneToolbarLayout.BottomInset`, derived from `EditorGUIUtility.singleLineHeight`
  so it follows UI scale and high-DPI displays instead of fighting them.

### Notes

- The edit-mode live-update gate in `PolygonMesh2D` means an unselected `PolygonMesh2D`
  will not pick up outline changes made by a script. Call `Rebuild()` yourself in that
  case, or leave the object selected.
- Verified on Unity 2022.3.53f1 with URP 14 (2D Renderer).

[Unreleased]: https://github.com/CHANGE_ME/terrain2d/commits/main
[0.2.0]: https://github.com/CHANGE_ME/terrain2d/compare/v0.1.1...v0.2.0
[0.1.1]: https://github.com/CHANGE_ME/terrain2d/compare/v0.1.0...v0.1.1
