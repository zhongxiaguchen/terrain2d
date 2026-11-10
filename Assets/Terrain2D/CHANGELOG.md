# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.5.0] - 2026-11-10

### Added

- **`TestCamera2D` — the test rig now has a camera.** Without one, pressing Play meant
  the character walked off screen in two seconds and you chased it with the Scene view.
  Four modes, chosen when the rig is created:

  | 模式 | 跟什么 | 用来验证 |
  |---|---|---|
  | **跟随**（默认） | 双轴 | 整段关卡 |
  | **仅水平** | 只跟 X，Y 锁死 | 跳跃高度 —— 镜头不上下晃才看得出到底跳多高 |
  | **仅垂直** | 只跟 Y，X 锁死 | 竖井 / 爬塔 |
  | **固定** | 不跟 | 反复试同一屏 |

  锁定的那一轴取的是**切到该模式时的相机位置**，不是世界 0 —— 所以「仅水平」保留你
  当前摆好的构图高度，而不是把镜头拽到 `y = 0`。
- **创建测试角色时一起配好摄像机**（面板按钮和菜单项都是）。优先复用已有的
  `TestCamera2D`，没有就挂在 `Camera.main` 上，连主摄像机都没有才新建一台。
  不另建一台是因为两台启用的摄像机会双份渲染，而主摄像机本来就已经配好了正交尺寸
  和背景色 —— 加一个跟随组件是纯增量，删掉组件场景一点没动。
- **测试阶段标题行显示场景里测试角色有几个** + 当前摄像机模式。
  复制粘贴测试角色之后经常忘掉多出来的那几个，然后纳闷"镜头怎么跟的是另一个"。
- 菜单 `GameObject ▸ 地形 2D ▸ 创建测试摄像机`。

### Changed

- 测试阶段面板新增「摄像机」一行（模式下拉 + 选中）和「跟随快慢」滑块；
  「固定」模式下不显示跟随快慢，因为那个模式没有快慢可言。
- 模式选择存在 `EditorPrefs` 的 `Terrain2D.TestCameraMode`，默认跟随。
  还没建摄像机时也能先选，创建时直接生效。

## [0.4.0] - 2026-11-10

### Changed

- **The viewport is now two fixed corners instead of one bottom bar.**
  - **左下角 · 固定栏** holds nothing but 关卡设计 + the 搭建 / 测试 / 资产替换
    selector. It is always there, because the stage describes how far the level has got,
    not what is selected.
  - **左上角 · 阶段面板** holds that stage's actual tools, and the stage selector is
    deliberately *not* in it — the panel's contents change with the selection, and the
    stage must not. The panel opens with a fold arrow and the stage name, since the
    selector now sits in the opposite corner.
- The panel is laid out for a 280px column (one or two controls per row) instead of four
  full-width rows, so it can sit in the corner without covering the level. Long hints
  moved from the right edge of each row into wrapped notes at the bottom.
- **层级 / Z / 颜色 / 碰撞体 moved from the bottom bar to the top-left panel.** They are
  properties of the selected object, not of the stage, so they show whenever a non-terrain
  2D renderer is selected.
- Panel fold state is global (`SessionState` `Terrain2D.PanelOpen`) rather than per object —
  the panel follows the stage, so per-object folding meant it changed shape when you picked
  a different terrain.
- `SceneToolbarLayout.PanelWidth` is shared by both drawers of the panel, so switching
  selection does not make the panel jump width.

### Fixed

- The panel is no longer clipped by an unknown-height area: it is anchored to the top-left
  corner and given a fixed generous height, instead of being pushed to the bottom with
  `FlexibleSpace`.

## [0.3.1] - 2026-11-10

### Added

- **The stage selector is now always on screen.** It used to appear only while a terrain
  was selected, so deselecting to look at the whole level made the current stage vanish —
  the one piece of state that describes where the level is, not which object you happen to
  be clicking. The empty-selection bar now opens with the same 搭建 / 测试 / 资产替换
  selector, and offers the one action that stage needs first:
  - **搭建** → 创建高度场地形
  - **测试** → 创建测试角色
  - **资产替换** → a hint, since this stage starts from a terrain that already exists
- `TerrainStage` (Editor) owns the stage enum, the persisted value, and the selector's
  drawing. Two mutually exclusive toolbars share it, so the state cannot live on either.

### Fixed

- Selecting a terrain while edit mode was off left the bottom of the viewport completely
  empty — the terrain toolbar was gated on edit mode and the sorting toolbar stands down
  for terrain selections, so both backed off at once. The toolbar now draws regardless;
  edit mode only controls whether the control points are drawn.

## [0.3.0] - 2026-11-10

### Added

- **Three-stage viewport toolbar.** The bottom toolbar now opens with a stage selector —
  搭建 / 测试 / 资产替换 — and only shows that stage's controls. The three phases look at
  completely different numbers, so stacking two dozen controls in one place meant hunting
  through all of them every time. The shell is now a dispatcher
  (`DrawStageBar` → `DrawBlockoutRows` / `DrawTestRows` / `DrawAssetRows`), so a fourth
  stage is one more `case`.
  - **搭建** keeps what was there (colour, edit aids, sorting layer / Z, generate /
    flatten / rebuild / subdivide / simplify / clear) plus the collider toggle.
  - **测试** gets the test character: create or select, drop at the left end of the
    terrain, and live sliders for jump height / move speed / air control that write
    straight to `TestPlayer2D` — adjustable mid-Play, which is the whole point of a test
    phase. Also carries `colliderOffsetY` and `colliderStride`, because "I cleared the gap
    but still fell" is usually a collider/visual mismatch.
  - **资产替换** gets UV mode, `uvScale`, the UV grid toggle, a readout of how many times
    the texture repeats along the surface, the `applyColor` switch, and the material
    handoff state.
- **「材质存为资产」**: turns the auto-generated embedded material into a real project
  asset via `AssetDatabase.CreateAsset` (which converts the object in place rather than
  copying, so the renderer's reference follows). Without this the artist cannot see, edit,
  or version-control the material at all.
- **Stage-aware warnings** in the asset phase: a `Scale ≠ 1` warning that computes the
  spacing needed to preserve the current world width, and an embedded-material warning.
- `TestPlayer2D` can now be built from code (`Terrain2DMenuItems.BuildTestPlayer`), not
  just from the menu item, so the toolbar can reuse the exact same setup.

### Fixed

- Turning off 「应用颜色」 no longer leaves the old tint stuck on the renderer. The colour
  lives in a `MaterialPropertyBlock`, which belongs to the renderer and stays there until
  something clears it — `ApplyVisuals` only stopped *writing* new values, so the previous
  one kept overriding the material and the only way back was reloading the scene. It now
  calls the new `MeshUtil.ClearColor`. This mattered for the asset handoff: the artist
  would set a colour on the material and see nothing change.

### Notes

- The stage is stored in `EditorPrefs` and is **global**, not per-terrain. It describes
  how far the level has got, not a property of one object, so selecting a different
  terrain should not reset it.
- Switching stages does not change which handles are drawn — that is still controlled by
  edit mode. Stage only decides which toolbar is shown.

## [0.2.0] - 2026-11-10

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
[0.5.0]: https://github.com/CHANGE_ME/terrain2d/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/CHANGE_ME/terrain2d/compare/v0.3.1...v0.4.0
[0.3.1]: https://github.com/CHANGE_ME/terrain2d/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/CHANGE_ME/terrain2d/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/CHANGE_ME/terrain2d/compare/v0.1.1...v0.2.0
[0.1.1]: https://github.com/CHANGE_ME/terrain2d/compare/v0.1.0...v0.1.1
