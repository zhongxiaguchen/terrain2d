# Terrain 2D

Editable 2D ground for Unity. Draw a surface, sculpt it by dragging points, and get a mesh,
a collider, and a **cheap height query** out of it.

Built for platformers that need terrain they can *ask questions about* and *blow holes in*.

---

## Why this, and not Sprite Shape or a tilemap

Sprite Shape and tilemaps describe a **shape**. This describes a **function**: for any X,
what is the ground height?

```csharp
float y = terrain.SampleHeight(x);   // no collider, no raycast, no Play mode
```

That single property is what makes everything else cheap:

| | Height field (this) | Spline / path | Collider raycast |
|---|---|---|---|
| Ground height at X | binary search over an array | solve the curve for X | `Physics2D.Raycast` |
| Usable in the editor, outside Play mode | yes | yes | no |
| Needs a collider to exist first | no | no | yes |
| Scattering 500 props on the ground | 500 array lookups | 500 curve solves | 500 physics queries |
| Saving / syncing / undoing the terrain | it is a `float[]` | control points move | — |

Destruction falls out of the same structure — a crater is just "lower the heights in this
range":

```csharp
terrain.DestroyCircle(hitPoint, 1.5f);   // local space
terrain.RaiseCircle(hitPoint, 1.0f);
```

**What it costs.** The walkable surface is single-valued in X, so the *surface* cannot
overhang. The other three edges are free-form polylines, so you still get caves, rock
formations and irregular bottoms — just not an overhanging floor.

---

## Requirements

| | |
|---|---|
| Unity | **2022.3** or newer (developed and tested on 2022.3.53f1) |
| URP + 2D Renderer | **tested** (URP 14) |
| Built-in Render Pipeline | falls back to `Sprites/Default`; **not tested** |
| HDRP | not supported |

## Install

Package Manager → `+` → *Add package from git URL…*

```
https://github.com/zhongxiaguchen/terrain2d.git
```

> The editor UI is currently in Chinese. An English pass over the UI strings is planned;
> the code, comments and commit history are already in English.

## Quick start

1. Create one from **GameObject ▸ 地形 2D ▸ 创建高度场地形**.
2. Select it — the editing toolbar appears at the bottom of the Scene view.
3. Drag the dots to sculpt.

| Action | |
|---|---|
| Drag a dot | move that point (2D — horizontally as well as vertically) |
| `Shift` + click an edge | insert a point on that edge |
| `Ctrl` / `Cmd` + click a dot | remove that point |
| Collapse the toolbar | the `▸` arrow on the left of it |

The toolbar also carries the terrain's sorting order and colour, plus one-click
`Subdivide ×2`, `Simplify`, `Flatten`, `Rebuild` and `Clear`.

## The runtime API

This is the part that a shape-editing tool cannot give you.

```csharp
using Terrain2D;

public class Spawner : MonoBehaviour
{
    public HeightmapTerrain2D terrain;
    public GameObject tree;

    void Start()
    {
        // 200 trees, snapped to the ground. No raycasts, no colliders involved.
        for (int i = 0; i < 200; i++)
        {
            float x = Random.Range(0f, 40f);
            Vector2 world = terrain.SampleWorldPoint(x);
            Instantiate(tree, world, Quaternion.identity);
        }
    }

    public void Explode(Vector2 worldPoint)
    {
        // Convert to the terrain's local space, then carve.
        Vector2 local = terrain.transform.InverseTransformPoint(worldPoint);
        terrain.DestroyCircle(local, 1.5f);
    }
}
```

| Method | |
|---|---|
| `float SampleHeight(float localX)` | interpolated surface height at a local X |
| `Vector2 SampleWorldPoint(float localX)` | the same point in world space |
| `void DestroyCircle(Vector2 localCenter, float radius)` | carve a crater |
| `void RaiseCircle(Vector2 localCenter, float radius)` | pile terrain up |
| `void Generate()` | rebuild the heights from `seed` and the noise settings |
| `void Flatten()` | reset to `baseHeight` |
| `void Subdivide()` / `void Simplify()` | double / halve the sample count |
| `void Rebuild()` | regenerate the mesh and colliders |

`SampleHeight` uses a binary search over the sample positions, so it stays correct after
points have been dragged sideways — the samples do not have to be evenly spaced.

## Editing the edges

The outline is a closed polyline with four sides, and **each side has its own point list**:

```
surface (left → right) → right chain (top → bottom) → bottom (right → left) → left chain
```

Adding a point to one edge never touches the others. That matters most for the bottom: it
has its own independent polyline, so sculpting the underside of a floating island leaves
the walkable surface exactly as it was.

## Colliders

Two colliders, both generated from the same outline as the mesh:

| Setting | Default | |
|---|---|---|
| `buildColliders` | on | master switch |
| `buildEdgeCollider` | **on** | one open chain along the surface. Cheap, and the right choice for a platformer's one-way ground |
| `buildPolygonCollider` | off | the full closed outline. Turn it on when you need to collide from below or from the side — a floating island, for instance |

Note that turning a collider off disables the component rather than deleting it.

## The other component: PolygonMesh2D

For anything that is not a height field — a cave, an arbitrary outline with holes. It reads
a `PolygonCollider2D` and renders it as a mesh, so you edit the shape with Unity's own
**Edit Collider** and the mesh follows.

Note the direction is reversed compared to `HeightmapTerrain2D`: this component *reads* a
collider, it does not create one.

## Field reference

### Shape

| Field | Default | |
|---|---|---|
| `sampleCount` | 128 | number of samples across the surface |
| `spacing` | 0.25 | distance between samples |
| `originX` | 0 | local X of the first sample |
| `useBottomProfile` | off | per-sample bottom editing |
| `bottomY` | -12 | flat bottom height when the profile is off |

### Procedural generation

| Field | Default | |
|---|---|---|
| `seed` | 12345 | same seed, same terrain |
| `baseHeight` | 0 | |
| `amplitude` | 3 | |
| `frequency` | 0.06 | lower = wider hills |
| `octaves` | 4 | |
| `persistence` | 0.5 | |
| `lacunarity` | 2 | |
| `smoothPasses` | 2 | |

### Appearance

| Field | Default | |
|---|---|---|
| `color` | white | applied per renderer, so duplicating an object does not change its neighbours |
| `sortingLayerName` / `sortingOrder` | `Default` / 0 | |
| `uvScale` | 0.5 | planar UV mapping |
| `flipWinding` | off | turn on if backface culling hides the terrain |

## Known limitations

- The surface cannot overhang (see above).
- Only URP 14 has been tested. Built-in RP should work through the `Sprites/Default`
  fallback, but that path has not been run.
- The editor UI is still in Chinese.

## License

MIT — see [LICENSE.md](LICENSE.md).

中文文档见 [README.zh-CN.md](README.zh-CN.md)。
