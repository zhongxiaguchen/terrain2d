using System.Collections.Generic;
using UnityEngine;

namespace Terrain2D
{
    /// <summary>
    /// 把 PolygonCollider2D 的轮廓渲染成网格。
    ///
    /// 这就是"能像碰撞体一样拖边缘的地形组件"：碰撞体是唯一数据源。
    /// 选中 PolygonCollider2D，点 Inspector 里的 Edit Collider（或直接在 Scene 视图里改点），
    /// 拖动顶点，渲染出来的网格会跟着变。
    ///
    /// 碰撞体里的每条 path 会变成一个独立的填充区域（彼此是"岛"，不是"洞"）。
    /// 要挖洞请用 keyhole 画法：从外轮廓切一条窄缝进去，绕洞一圈再缝回来，
    /// 这样整体仍是一个简单多边形。详见 Triangulator 的注释。
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [AddComponentMenu("地形 2D/多边形网格地形")]
    public class PolygonMesh2D : MonoBehaviour
    {
        public enum OutlineSource
        {
            [InspectorName("多边形碰撞体 2D")]
            PolygonCollider2D,

            [InspectorName("本地顶点列表")]
            LocalPoints
        }

        [Header("轮廓来源")]
        [Tooltip("从 PolygonCollider2D 读轮廓（推荐），或使用下面的本地顶点列表。")]
        public OutlineSource source = OutlineSource.PolygonCollider2D;

        [Tooltip("从这里读轮廓。留空则自动找自己身上的 PolygonCollider2D。")]
        public PolygonCollider2D outline;

        [Tooltip("当来源设为「本地顶点列表」时使用。")]
        public List<Vector2> points = new List<Vector2>();

        [Header("外观")]
        [Tooltip("地形颜色。直接写进当前材质，改完立刻生效。")]
        public Color color = Color.white;

        [Tooltip("关掉后不再自动改材质颜色，方便完全手动控制材质。")]
        public bool applyColor = true;

        [Tooltip("2D 排序层。留空表示不改动。")]
        public string sortingLayerName = "Default";

        [Tooltip("2D 排序顺序。数值越大越靠前（越靠镜头）。")]
        public int sortingOrder = 0;

        [Header("网格")]
        [Tooltip("UV 按平面映射：localX × uvScale、localY × uvScale。")]
        public float uvScale = 1f;

        [Tooltip("网格在本地空间的 Z 偏移，用来给地形分层。")]
        public float zOffset = 0f;

        [Tooltip("如果材质开了背面剔除导致地形看不见，把这个打开。")]
        public bool flipWinding = false;

        [Header("实时更新")]
        [Tooltip("轮廓一变就自动重建。靠比对顶点哈希判断，开销很小。")]
        public bool liveUpdate = true;

        Mesh _mesh;
        MeshFilter _filter;
        MeshRenderer _renderer;
        int _lastHash = int.MinValue;

        MeshFilter Filter => _filter != null ? _filter : (_filter = GetComponent<MeshFilter>());
        MeshRenderer Renderer => _renderer != null ? _renderer : (_renderer = GetComponent<MeshRenderer>());

        void OnEnable()
        {
            Rebuild();
        }

        void Update()
        {
            if (!liveUpdate) return;

            int hash = ComputeOutlineHash();
            if (hash == _lastHash) return;

            _lastHash = hash;
            Rebuild();
        }

        [ContextMenu("重建网格")]
        public void Rebuild()
        {
            var paths = ReadPaths();

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            int baseIndex = 0;

            for (int p = 0; p < paths.Count; p++)
            {
                Vector2[] path = paths[p];
                if (path == null || path.Length < 3) continue;

                List<int> pathTris = Triangulator.Triangulate(path);
                if (pathTris.Count == 0) continue;

                for (int i = 0; i < path.Length; i++)
                {
                    Vector2 v = path[i];
                    verts.Add(new Vector3(v.x, v.y, zOffset));
                    uvs.Add(new Vector2(v.x * uvScale, v.y * uvScale));
                }

                for (int i = 0; i < pathTris.Count; i++)
                    tris.Add(baseIndex + pathTris[i]);

                baseIndex += path.Length;
            }

            if (verts.Count == 0)
            {
                // 没有可画的轮廓，顺手清掉旧网格，避免留残影。
                if (Filter.sharedMesh != null) Filter.sharedMesh.Clear();
                ApplyVisuals();
                return;
            }

            _mesh = MeshUtil.Build("PolygonMesh2D", _mesh, verts, uvs, tris, flipWinding);
            Filter.sharedMesh = _mesh;
            MeshUtil.EnsureMaterial(Renderer);
            ApplyVisuals();
        }

        /// <summary>应用颜色和排序设置。</summary>
        public void ApplyVisuals()
        {
            if (applyColor) MeshUtil.ApplyColor(Renderer, color);
            MeshUtil.ApplySorting(Renderer, sortingLayerName, sortingOrder);
        }

        /// <summary>返回当前轮廓的所有 path，坐标为本地空间。</summary>
        public List<Vector2[]> ReadPaths()
        {
            var result = new List<Vector2[]>();

            if (source == OutlineSource.PolygonCollider2D)
            {
                PolygonCollider2D col = outline != null ? outline : GetComponent<PolygonCollider2D>();
                if (col == null) return result;

                for (int i = 0; i < col.pathCount; i++)
                    result.Add(col.GetPath(i));
            }
            else
            {
                if (points != null && points.Count >= 3)
                    result.Add(points.ToArray());
            }

            return result;
        }

        int ComputeOutlineHash()
        {
            var paths = ReadPaths();

            unchecked
            {
                int hash = paths.Count * 397;
                for (int p = 0; p < paths.Count; p++)
                {
                    Vector2[] path = paths[p];
                    if (path == null) continue;

                    hash = hash * 31 + path.Length;
                    for (int i = 0; i < path.Length; i++)
                    {
                        hash = hash * 31 + path[i].x.GetHashCode();
                        hash = hash * 31 + path[i].y.GetHashCode();
                    }
                }
                return hash;
            }
        }

        void OnValidate()
        {
            // 改 Inspector 上的颜色、UV、Z 偏移等要立刻看到效果。
            // 但 OnValidate 期间不能碰 MeshFilter（Unity 会报
            // "SendMessage cannot be called during Awake, CheckConsistency,
            // or OnValidate"，网格赋值会被丢掉），所以延迟一拍。
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.delayCall -= DeferredRebuild;
                UnityEditor.EditorApplication.delayCall += DeferredRebuild;
                return;
            }
#endif

            if (isActiveAndEnabled) Rebuild();
        }

        void DeferredRebuild()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall -= DeferredRebuild;
#endif

            if (this == null || !isActiveAndEnabled) return;
            Rebuild();
        }
    }
}
