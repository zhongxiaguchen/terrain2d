using System.Collections.Generic;
using UnityEngine;

namespace Terrain2D
{
    /// <summary>
    /// 高度场地形：两条曲线（地表 + 底部）各是一个 float[] 数组，它们是唯一数据源，
    /// 渲染网格和碰撞体都是从它们派生出来的。
    ///
    /// 形状是一条"带子"：地表曲线和底部曲线之间的部分是实体。
    /// 关闭「底部独立轮廓」时底部退化成一条平直线，就是一块实心板；
    /// 打开之后底部也能逐点编辑，可以做出浮空岛、悬岩、天花板这类不规则地形。
    ///
    /// 因为轮廓是"X 的函数"而不是任意多边形，三角化退化成三角带，
    /// 挖坑也只是改数组里的数字 —— 适合程序化生成、固定种子、可破坏的场景。
    ///
    /// 需要真正的自由多边形（带洞、带隧道）请用 PolygonMesh2D。
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [AddComponentMenu("地形 2D/高度场地形")]
    public class HeightmapTerrain2D : MonoBehaviour
    {
        /// <summary>地表与底部之间必须保留的最小厚度，防止网格翻面。</summary>
        public const float MinThickness = 0.01f;

        [Header("形状")]
        [Tooltip("采样点数量。顶点数是它的 2 倍。")]
        public int sampleCount = 128;

        [Tooltip("采样点之间的世界距离。总宽度 = (sampleCount - 1) × spacing。")]
        public float spacing = 0.25f;

        [Tooltip("第一个采样点的本地 X 坐标。")]
        public float originX = 0f;

        [Tooltip("打开后底部变成可逐点编辑的独立轮廓，能做出浮空岛、悬岩这类不规则地形。")]
        public bool useBottomProfile = false;

        [Tooltip("底部平直时的高度。打开「底部独立轮廓」后，这个值只作为初始高度。")]
        public float bottomY = -12f;

        [Tooltip("底部轮廓左端点相对地表的水平偏移。非 0 时左边界变成斜线。")]
        public float bottomLeftOffsetX = 0f;

        [Tooltip("底部轮廓右端点相对地表的水平偏移。非 0 时右边界变成斜线。")]
        public float bottomRightOffsetX = 0f;

        [Header("程序化生成")]
        [Tooltip("勾上后，组件第一次启用时自动用噪声生成地表。")]
        public bool generateOnEnable = true;

        [Tooltip("随机种子。同一个种子永远生成同一块地形。")]
        public int seed = 12345;

        [Tooltip("基准高度，地形围绕它上下起伏。")]
        public float baseHeight = 0f;

        [Tooltip("相对基准高度的最大起伏幅度。")]
        public float amplitude = 3f;

        [Tooltip("噪声频率（每世界单位）。越小山越宽。")]
        public float frequency = 0.06f;

        [Range(1, 8)]
        [Tooltip("噪声叠加层数。层数越多细节越丰富。")]
        public int octaves = 4;

        [Range(0f, 1f)]
        [Tooltip("每层噪声的幅度衰减。")]
        public float persistence = 0.5f;

        [Tooltip("每层噪声的频率放大倍数。")]
        public float lacunarity = 2f;

        [Range(0, 8)]
        [Tooltip("生成后做几遍平滑。0 = 原始噪声，2 左右比较自然。")]
        public int smoothPasses = 2;

        [Header("外观")]
        [Tooltip("地形颜色。直接写进当前材质，改完立刻生效。")]
        public Color color = Color.white;

        [Tooltip("关掉后不再自动改材质颜色，方便完全手动控制材质。")]
        public bool applyColor = true;

        [Tooltip("2D 排序层。留空表示不改动。")]
        public string sortingLayerName = "Default";

        [Tooltip("2D 排序顺序。数值越大越靠前（越靠镜头）。")]
        public int sortingOrder = 0;

        [Tooltip("UV 按平面映射：x × uvScale、y × uvScale。")]
        public float uvScale = 0.5f;

        [Tooltip("如果材质开了背面剔除导致地形看不见，把这个打开。")]
        public bool flipWinding = false;

        [Header("碰撞")]
        [Tooltip("总开关。关掉后不会创建或更新任何碰撞体。")]
        public bool buildColliders = true;

        [Tooltip("沿地表生成一条开链碰撞体。最省性能，站立/行走用这个就够了。推荐开启。")]
        public bool buildEdgeCollider = true;

        [Tooltip("给边缘一点厚度，防止高速物体穿透。一般 0.02 左右。")]
        public float edgeRadius = 0f;

        [Tooltip("生成整条带子的封闭轮廓。需要从下方或侧面撞到地形，或地形底部不规则时打开。")]
        public bool buildPolygonCollider = false;

        [Tooltip("只作用于碰撞体的垂直偏移。用来把碰撞边缘从视觉边缘推开。")]
        public float colliderOffsetY = 0f;

        [Tooltip("碰撞体每隔 N 个采样点取一个。1 = 全部取。地形特别长时才调大。")]
        [Range(1, 16)] public int colliderStride = 1;

        [Header("实时更新")]
        [Tooltip("在 Inspector 里改参数时自动重建。")]
        public bool rebuildOnValidate = true;

        [Header("Scene 视图")]
        [Tooltip("非编辑模式下是否显示稀疏控制点。编辑模式由 Inspector 上的「进入编辑模式」控制。")]
        public bool showSceneHandles = true;

        [Tooltip("非编辑模式下每隔 N 个采样点画一个控制点。编辑模式会显示全部采样点，不受这个影响。")]
        [Range(1, 32)] public int sceneHandleStride = 8;

        [Tooltip("平滑拖动模式。关掉时每个控制点只影响自己；打开后拖一个点会带动左右邻居。")]
        public bool smoothDrag = false;

        [Tooltip("只在「平滑拖动」打开时生效：拖动时带动左右各 N 个邻居，做出平滑的坡而不是尖刺。")]
        [Range(1, 16)] public int sceneHandleFalloff = 3;

        [Tooltip("是否显示绿色/青色/橙色那几个边界方块。专心拖控制点时可以关掉，免得挡住。")]
        public bool showBoundaryHandles = true;

        // 序列化，这样编辑器里生成好的地形进入 Play 模式后还在。
        [SerializeField, HideInInspector] float[] _heights;
        [SerializeField, HideInInspector] float[] _bottomHeights;
        [SerializeField, HideInInspector] bool _initialized;

        // 左右两条边的中间控制点（本地坐标，不含两个端点 —— 端点是地表/底部曲线的两端）。
        // 空列表 = 一条直线，也就是默认的竖直边。
        [SerializeField, HideInInspector] List<Vector2> _leftEdge = new List<Vector2>();
        [SerializeField, HideInInspector] List<Vector2> _rightEdge = new List<Vector2>();

        // 底边的独立折线（本地坐标，右端 -> 左端，**含两个角点**）。
        // 空列表 = 用采样点那套底部数据（bottomY 平直线，或开了「底部独立轮廓」后的
        // _bottomHeights[]）。
        //
        // 为什么底边需要一套独立数据：地表和底部原来共用同一套采样下标，
        // 往底边加点就必然同时插一个地表点，而且 spacing 重算会把地表所有点横向挪位 ——
        // 也就是"点底边，地表跟着变"。有了这条折线之后，底边的点数和位置跟地表完全脱钩，
        // 加点只动这里。
        [SerializeField, HideInInspector] List<Vector2> _bottomEdge = new List<Vector2>();

        // 地表 / 底部每个采样点的水平偏移。
        // 默认全是 0，也就是均匀分布；拖点横向移动时改这里。
        [SerializeField, HideInInspector] float[] _topOffsetsX;
        [SerializeField, HideInInspector] float[] _bottomOffsetsX;

        Mesh _mesh;
        MeshFilter _filter;
        MeshRenderer _renderer;
        EdgeCollider2D _edge;
        PolygonCollider2D _poly;

        MeshFilter Filter => _filter != null ? _filter : (_filter = GetComponent<MeshFilter>());
        MeshRenderer Renderer => _renderer != null ? _renderer : (_renderer = GetComponent<MeshRenderer>());

        /// <summary>地表高度数组（本地 Y）。改完记得调用 Rebuild()。</summary>
        public float[] Heights
        {
            get { EnsureHeights(); return _heights; }
        }

        /// <summary>底部轮廓数组（本地 Y）。只有「底部独立轮廓」打开时才有意义。</summary>
        public float[] BottomHeights
        {
            get { EnsureBottomHeights(); return _bottomHeights; }
        }

        /// <summary>取某个采样点处的底部高度。没开独立轮廓时就是 bottomY。</summary>
        public float BottomAt(int index)
        {
            if (!useBottomProfile) return bottomY;
            EnsureBottomHeights();
            return _bottomHeights[Mathf.Clamp(index, 0, _bottomHeights.Length - 1)];
        }

        /// <summary>
        /// 取某个采样点处地表的本地 X。
        /// 均匀分布 + 该点的水平偏移 —— 拖点横向移动改的就是这个偏移。
        /// </summary>
        public float TopXAt(int index)
        {
            float x = originX + index * spacing;
            float[] offsets = _topOffsetsX;
            return offsets != null && index >= 0 && index < offsets.Length ? x + offsets[index] : x;
        }

        /// <summary>
        /// 取某个采样点处底部的本地 X。
        /// = 均匀分布 + 两端点斜边偏移（线性过渡）+ 该点自己的水平偏移。
        /// </summary>
        public float BottomXAt(int index)
        {
            float x = originX + index * spacing;
            if (!useBottomProfile) return x;

            float t = sampleCount > 1 ? (float)index / (sampleCount - 1) : 0f;
            x += Mathf.Lerp(bottomLeftOffsetX, bottomRightOffsetX, t);

            float[] offsets = _bottomOffsetsX;
            return offsets != null && index >= 0 && index < offsets.Length ? x + offsets[index] : x;
        }

        /// <summary>取某个采样点的地表水平偏移。</summary>
        public float TopOffsetX(int index)
        {
            return _topOffsetsX != null && index >= 0 && index < _topOffsetsX.Length
                ? _topOffsetsX[index] : 0f;
        }

        /// <summary>取某个采样点的底部水平偏移。</summary>
        public float BottomOffsetX(int index)
        {
            return _bottomOffsetsX != null && index >= 0 && index < _bottomOffsetsX.Length
                ? _bottomOffsetsX[index] : 0f;
        }

        /// <summary>设置某个采样点的地表水平偏移。</summary>
        public void SetTopOffsetX(int index, float offset)
        {
            EnsureXOffsets();
            if (index < 0 || index >= _topOffsetsX.Length) return;
            _topOffsetsX[index] = offset;
        }

        /// <summary>设置某个采样点的底部水平偏移。</summary>
        public void SetBottomOffsetX(int index, float offset)
        {
            EnsureXOffsets();
            if (index < 0 || index >= _bottomOffsetsX.Length) return;
            _bottomOffsetsX[index] = offset;
        }

        /// <summary>地表横向偏移数组的副本，供编辑器做拖动基准。</summary>
        public float[] TopOffsetXSnapshot()
        {
            EnsureXOffsets();
            return (float[])_topOffsetsX.Clone();
        }

        /// <summary>底部横向偏移数组的副本，供编辑器做拖动基准。</summary>
        public float[] BottomOffsetXSnapshot()
        {
            EnsureXOffsets();
            return (float[])_bottomOffsetsX.Clone();
        }

        void EnsureXOffsets()
        {
            int n = Mathf.Max(2, sampleCount);
            if (_topOffsetsX == null || _topOffsetsX.Length != n) _topOffsetsX = new float[n];
            if (_bottomOffsetsX == null || _bottomOffsetsX.Length != n) _bottomOffsetsX = new float[n];
        }

        static bool HasAnyOffset(float[] offsets)
        {
            if (offsets == null) return false;
            for (int i = 0; i < offsets.Length; i++)
                if (Mathf.Abs(offsets[i]) > 1e-5f) return true;
            return false;
        }

        static void ScaleValues(float[] values, float factor)
        {
            if (values == null) return;
            for (int i = 0; i < values.Length; i++) values[i] *= factor;
        }

        /// <summary>左边界的中间控制点（本地坐标，不含端点）。空 = 一条直线。</summary>
        public List<Vector2> LeftEdge
        {
            get { return _leftEdge ?? (_leftEdge = new List<Vector2>()); }
        }

        /// <summary>右边界的中间控制点（本地坐标，不含端点）。空 = 一条直线。</summary>
        public List<Vector2> RightEdge
        {
            get { return _rightEdge ?? (_rightEdge = new List<Vector2>()); }
        }

        /// <summary>
        /// 底边的独立折线（本地坐标，右端 -> 左端，**含两个角点**）。
        /// 空 = 用采样点那套底部数据。非空时它就是底边的全部，渲染和碰撞都从它走。
        /// </summary>
        public List<Vector2> BottomEdge
        {
            get { return _bottomEdge ?? (_bottomEdge = new List<Vector2>()); }
        }

        /// <summary>底边是否有独立折线（至少两个点才算数）。</summary>
        public bool HasBottomEdge
        {
            get { return _bottomEdge != null && _bottomEdge.Count >= 2; }
        }

        /// <summary>
        /// 把地形轮廓拼成一条闭合折线（顺时针）：
        /// 地表（左→右）→ 右边链（上→下）→ 底部（右→左）→ 左边链（下→上）。
        /// 四条边都是折线，都可以加点。
        /// </summary>
        public void BuildOutline(List<Vector2> outline)
        {
            if (outline == null) return;
            outline.Clear();

            EnsureHeights();

            // 1. 地表：左 -> 右
            for (int i = 0; i < sampleCount; i++)
                outline.Add(new Vector2(TopXAt(i), _heights[i]));

            // 2. 右边链：上 -> 下
            var right = RightEdge;
            for (int i = 0; i < right.Count; i++) outline.Add(right[i]);

            // 3. 底部：右 -> 左
            // 有独立折线就用它（折线自带两个角点）；否则退回采样点那套底部数据。
            if (HasBottomEdge)
            {
                var bottomChain = BottomEdge;
                for (int i = 0; i < bottomChain.Count; i++) outline.Add(bottomChain[i]);
            }
            else
            {
                for (int i = sampleCount - 1; i >= 0; i--)
                    outline.Add(new Vector2(BottomXAt(i), BottomAt(i)));
            }

            // 4. 左边链：下 -> 上
            var left = LeftEdge;
            for (int i = 0; i < left.Count; i++) outline.Add(left[i]);
        }

        // ------------------------------------------------------------------ 左右边加点

        /// <summary>在左/右边链的指定位置插入一个控制点（index 是链内下标，0 = 最靠近底端）。</summary>
        public void InsertEdgePoint(bool left, int index, Vector2 localPoint)
        {
            var chain = left ? LeftEdge : RightEdge;
            index = Mathf.Clamp(index, 0, chain.Count);
            chain.Insert(index, localPoint);
            Rebuild();
        }

        /// <summary>删除左/右边链上的一个控制点。</summary>
        public void RemoveEdgePoint(bool left, int index)
        {
            var chain = left ? LeftEdge : RightEdge;
            if (index < 0 || index >= chain.Count) return;
            chain.RemoveAt(index);
            Rebuild();
        }

        /// <summary>移动左/右边链上的一个控制点。</summary>
        public void SetEdgePoint(bool left, int index, Vector2 localPoint)
        {
            var chain = left ? LeftEdge : RightEdge;
            if (index < 0 || index >= chain.Count) return;
            chain[index] = localPoint;
        }

        // ------------------------------------------------------------------ 底边独立折线

        /// <summary>
        /// 底边还没有独立折线时，用当前的底边（`bottomY` 平直线，或开了「底部独立轮廓」
        /// 之后的 `_bottomHeights[]`）把它种下来。
        ///
        /// **形状零变化** —— 折线的点就是现在画出来的那些点，一个不多一个不少。
        /// 种下来之后，往底边加点就只动这条折线，地表完全不参与。
        /// </summary>
        public void SeedBottomEdgeIfEmpty()
        {
            if (HasBottomEdge) return;

            var chain = BottomEdge;
            chain.Clear();

            // 右 -> 左、含两个角点，跟 BuildOutline 遍历底边的方向一致。
            for (int i = sampleCount - 1; i >= 0; i--)
                chain.Add(new Vector2(BottomXAt(i), BottomAt(i)));
        }

        /// <summary>在底边折线的指定位置插入一个控制点（index 是折线内下标）。</summary>
        public void InsertBottomPoint(int index, Vector2 localPoint)
        {
            var chain = BottomEdge;
            index = Mathf.Clamp(index, 0, chain.Count);
            chain.Insert(index, localPoint);
            Rebuild();
        }

        /// <summary>删除底边折线上的一个控制点。至少保留 2 个，否则底边整条没了。</summary>
        public void RemoveBottomPoint(int index)
        {
            var chain = BottomEdge;
            if (chain.Count <= 2) return;
            if (index < 0 || index >= chain.Count) return;
            chain.RemoveAt(index);
            Rebuild();
        }

        /// <summary>移动底边折线上的一个控制点。</summary>
        public void SetBottomPoint(int index, Vector2 localPoint)
        {
            var chain = BottomEdge;
            if (index < 0 || index >= chain.Count) return;
            chain[index] = localPoint;
        }

        /// <summary>清空底边折线，回到"用采样点那套底部数据"的状态。</summary>
        public void ClearBottomEdge()
        {
            BottomEdge.Clear();
            OutlineValid = true;
            Rebuild();
        }

        /// <summary>
        /// 清空左右两条边、以及底边独立折线上的控制点，恢复成直线。
        /// 轮廓拖乱了、或者自交到网格出不来时，用这个一键还原。
        /// </summary>
        public void ClearEdgePoints()
        {
            LeftEdge.Clear();
            RightEdge.Clear();
            BottomEdge.Clear();
            OutlineValid = true;
            Rebuild();
        }

        /// <summary>
        /// 把所有采样点的横向偏移清零，让地表和底部恢复成均匀分布。
        /// 横向拖歪了想重来的时候用这个。
        /// </summary>
        public void ClearXOffsets()
        {
            EnsureXOffsets();

            for (int i = 0; i < _topOffsetsX.Length; i++) _topOffsetsX[i] = 0f;
            for (int i = 0; i < _bottomOffsetsX.Length; i++) _bottomOffsetsX[i] = 0f;

            OutlineValid = true;
            Rebuild();
        }

        void OnEnable()
        {
            EnsureHeights();
            if (useBottomProfile) EnsureBottomHeights();

            if (generateOnEnable && !_initialized)
            {
                Generate();
                _initialized = true;
            }
            else
            {
                Rebuild();
            }
        }

        void OnValidate()
        {
            if (!rebuildOnValidate || !isActiveAndEnabled) return;

            sampleCount = Mathf.Max(2, sampleCount);
            spacing = Mathf.Max(0.0001f, spacing);

            // OnValidate 期间**不能碰 MeshFilter / Collider** ——
            // Unity 会报 "SendMessage cannot be called during Awake,
            // CheckConsistency, or OnValidate"，而且那次网格赋值会被丢掉。
            // 所以这里只排队，真正的重建延迟到编辑器下一次刷新。
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.delayCall -= DeferredRebuild;
                UnityEditor.EditorApplication.delayCall += DeferredRebuild;
                return;
            }
#endif

            DeferredRebuild();
        }

        void DeferredRebuild()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall -= DeferredRebuild;
#endif

            if (this == null || !isActiveAndEnabled) return;

            if (_heights == null || _heights.Length != sampleCount) Generate();
            else
            {
                if (useBottomProfile) EnsureBottomHeights();
                Rebuild();
            }
        }

        // ------------------------------------------------------------------ 生成

        [ContextMenu("生成地形")]
        public void Generate()
        {
            EnsureHeights();

            // 种子只用来偏移噪声取样位置，所以同一个种子永远得到同一块地形。
            var rng = new System.Random(seed);
            float offsetX = (float)rng.NextDouble() * 10000f;
            float offsetY = (float)rng.NextDouble() * 10000f;

            for (int i = 0; i < sampleCount; i++)
            {
                float x = i * spacing;

                float amp = 1f;
                float freq = frequency;
                float sum = 0f;
                float norm = 0f;

                for (int o = 0; o < octaves; o++)
                {
                    float n = Mathf.PerlinNoise(offsetX + x * freq, offsetY) * 2f - 1f;
                    sum += n * amp;
                    norm += amp;
                    amp *= persistence;
                    freq *= lacunarity;
                }

                _heights[i] = baseHeight + (norm > 0f ? sum / norm : 0f) * amplitude;
            }

            for (int s = 0; s < smoothPasses; s++) SmoothOnce();

            // 生成地表时把底部重置成平直线，避免新旧数据打架。
            if (useBottomProfile)
            {
                EnsureBottomHeights();
                for (int i = 0; i < _bottomHeights.Length; i++) _bottomHeights[i] = bottomY;
            }

            _initialized = true;
            Rebuild();
        }

        [ContextMenu("拍平地形")]
        public void Flatten()
        {
            EnsureHeights();
            for (int i = 0; i < _heights.Length; i++) _heights[i] = baseHeight;

            if (useBottomProfile)
            {
                EnsureBottomHeights();
                for (int i = 0; i < _bottomHeights.Length; i++) _bottomHeights[i] = bottomY;
            }

            Rebuild();
        }

        // ------------------------------------------------------------------ 增删采样点
        // 地形是等距高度场，所以"加点/删点"是细分与合并：点数变了，但地形总宽度保持不变
        // （spacing 会跟着重算）。这样 SampleHeight、挖坑、碰撞体都不用改。

        /// <summary>
        /// 在指定索引处插入一个采样点。新点落在 index-1 与 index 之间，
        /// 高度取两者平均，所以轮廓不会跳。总宽度保持不变。
        /// </summary>
        public void InsertSample(int index)
        {
            EnsureHeights();
            EnsureXOffsets();

            int oldCount = sampleCount;
            if (oldCount < 2) return;

            index = Mathf.Clamp(index, 1, oldCount - 1);
            float totalWidth = (oldCount - 1) * spacing;
            float oldSpacing = spacing;

            _heights = InsertValue(_heights, index);
            _topOffsetsX = InsertValue(_topOffsetsX, index);

            if (useBottomProfile)
            {
                EnsureBottomHeights();
                _bottomHeights = InsertValue(_bottomHeights, index);
                _bottomOffsetsX = InsertValue(_bottomOffsetsX, index);
            }

            sampleCount = oldCount + 1;
            spacing = totalWidth / (sampleCount - 1);

            // 间距变了，横向偏移按同样比例缩放，形状才不会被拉歪。
            float ratio = oldSpacing > 1e-6f ? spacing / oldSpacing : 1f;
            ScaleValues(_topOffsetsX, ratio);
            ScaleValues(_bottomOffsetsX, ratio);

            Rebuild();
        }

        /// <summary>
        /// 删除指定索引处的采样点。两端点不允许删（它们决定地形范围）。
        /// 总宽度保持不变。
        /// </summary>
        public void RemoveSample(int index)
        {
            EnsureHeights();
            EnsureXOffsets();

            int oldCount = sampleCount;
            if (oldCount <= 3) return;

            index = Mathf.Clamp(index, 1, oldCount - 2);
            float totalWidth = (oldCount - 1) * spacing;
            float oldSpacing = spacing;

            _heights = RemoveValue(_heights, index);
            _topOffsetsX = RemoveValue(_topOffsetsX, index);

            if (useBottomProfile)
            {
                EnsureBottomHeights();
                _bottomHeights = RemoveValue(_bottomHeights, index);
                _bottomOffsetsX = RemoveValue(_bottomOffsetsX, index);
            }

            sampleCount = oldCount - 1;
            spacing = totalWidth / (sampleCount - 1);

            float ratio = oldSpacing > 1e-6f ? spacing / oldSpacing : 1f;
            ScaleValues(_topOffsetsX, ratio);
            ScaleValues(_bottomOffsetsX, ratio);

            Rebuild();
        }

        /// <summary>整体细分：每两个采样点之间插一个新点。点数变成 2n-1。</summary>
        public void Subdivide()
        {
            EnsureHeights();
            EnsureXOffsets();

            int oldCount = sampleCount;
            if (oldCount < 2) return;

            float totalWidth = (oldCount - 1) * spacing;
            float oldSpacing = spacing;

            _heights = SubdivideValues(_heights);
            _topOffsetsX = SubdivideValues(_topOffsetsX);

            if (useBottomProfile)
            {
                EnsureBottomHeights();
                _bottomHeights = SubdivideValues(_bottomHeights);
                _bottomOffsetsX = SubdivideValues(_bottomOffsetsX);
            }

            sampleCount = _heights.Length;
            spacing = totalWidth / (sampleCount - 1);

            float ratio = oldSpacing > 1e-6f ? spacing / oldSpacing : 1f;
            ScaleValues(_topOffsetsX, ratio);
            ScaleValues(_bottomOffsetsX, ratio);

            Rebuild();
        }

        /// <summary>整体简化：隔一个取一个。点数约减半。</summary>
        public void Simplify()
        {
            EnsureHeights();
            EnsureXOffsets();

            int oldCount = sampleCount;
            if (oldCount < 5) return;

            float totalWidth = (oldCount - 1) * spacing;
            float oldSpacing = spacing;

            _heights = SimplifyValues(_heights);
            _topOffsetsX = SimplifyValues(_topOffsetsX);

            if (useBottomProfile)
            {
                EnsureBottomHeights();
                _bottomHeights = SimplifyValues(_bottomHeights);
                _bottomOffsetsX = SimplifyValues(_bottomOffsetsX);
            }

            sampleCount = _heights.Length;
            spacing = totalWidth / (sampleCount - 1);

            float ratio = oldSpacing > 1e-6f ? spacing / oldSpacing : 1f;
            ScaleValues(_topOffsetsX, ratio);
            ScaleValues(_bottomOffsetsX, ratio);

            Rebuild();
        }

        static float[] InsertValue(float[] src, int index)
        {
            var result = new float[src.Length + 1];
            for (int i = 0; i < index; i++) result[i] = src[i];
            result[index] = (src[index - 1] + src[index]) * 0.5f;
            for (int i = index; i < src.Length; i++) result[i + 1] = src[i];
            return result;
        }

        static float[] RemoveValue(float[] src, int index)
        {
            var result = new float[src.Length - 1];
            for (int i = 0; i < index; i++) result[i] = src[i];
            for (int i = index + 1; i < src.Length; i++) result[i - 1] = src[i];
            return result;
        }

        static float[] SubdivideValues(float[] src)
        {
            var result = new float[src.Length * 2 - 1];
            for (int i = 0; i < src.Length - 1; i++)
            {
                result[2 * i] = src[i];
                result[2 * i + 1] = (src[i] + src[i + 1]) * 0.5f;
            }
            result[result.Length - 1] = src[src.Length - 1];
            return result;
        }

        static float[] SimplifyValues(float[] src)
        {
            int newCount = (src.Length + 1) / 2;
            var result = new float[newCount];
            for (int i = 0; i < newCount; i++)
            {
                int s = i * 2;
                if (s < src.Length) result[i] = src[s];
            }
            result[newCount - 1] = src[src.Length - 1];
            return result;
        }

        // ------------------------------------------------------------------ 挖坑 / 堆土

        /// <summary>
        /// 挖掉圆形范围内的土，像爆炸弹坑。
        /// <paramref name="localCenter"/> 是本物体本地空间的坐标。
        /// </summary>
        public void DestroyCircle(Vector2 localCenter, float radius)
        {
            ApplyCircle(localCenter, radius, subtract: true);
        }

        /// <summary>在圆形范围内堆土。</summary>
        public void RaiseCircle(Vector2 localCenter, float radius)
        {
            ApplyCircle(localCenter, radius, subtract: false);
        }

        void ApplyCircle(Vector2 center, float radius, bool subtract)
        {
            if (radius <= 0f) return;
            EnsureHeights();

            float r2 = radius * radius;

            for (int i = 0; i < sampleCount; i++)
            {
                float x = TopXAt(i);
                float dx = x - center.x;
                if (dx * dx >= r2) continue;

                float circleY = center.y - Mathf.Sqrt(r2 - dx * dx);

                if (subtract)
                {
                    // 地形是地表线以下的实心区域，所以弹坑底取两者较低的那个。
                    if (circleY < _heights[i])
                        _heights[i] = Mathf.Max(circleY, BottomAt(i) + MinThickness);
                }
                else
                {
                    float top = center.y + Mathf.Sqrt(r2 - dx * dx);
                    if (top > _heights[i]) _heights[i] = top;
                }
            }

            Rebuild();
        }

        /// <summary>
        /// 取某个本地 X 处的插值地表高度。AI 寻路、刷怪点都能用。
        /// 采样点被横向拖过之后 X 不再等距，所以这里用二分查找定位区间。
        /// </summary>
        public float SampleHeight(float localX)
        {
            EnsureHeights();
            if (sampleCount < 2) return baseHeight;

            if (localX <= TopXAt(0)) return _heights[0];
            if (localX >= TopXAt(sampleCount - 1)) return _heights[sampleCount - 1];

            int lo = 0;
            int hi = sampleCount - 1;

            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (TopXAt(mid) <= localX) lo = mid;
                else hi = mid;
            }

            float x0 = TopXAt(lo);
            float x1 = TopXAt(hi);
            float t = x1 - x0 > 1e-6f ? (localX - x0) / (x1 - x0) : 0f;
            return Mathf.Lerp(_heights[lo], _heights[hi], t);
        }

        /// <summary>取某个本地 X 处地表点的世界坐标。</summary>
        public Vector2 SampleWorldPoint(float localX)
        {
            return transform.TransformPoint(new Vector3(localX, SampleHeight(localX), 0f));
        }

        void EnsureHeights()
        {
            if (_heights == null || _heights.Length != sampleCount)
                _heights = new float[Mathf.Max(2, sampleCount)];
        }

        void EnsureBottomHeights()
        {
            int n = Mathf.Max(2, sampleCount);
            if (_bottomHeights != null && _bottomHeights.Length == n) return;

            var old = _bottomHeights;
            _bottomHeights = new float[n];
            for (int i = 0; i < n; i++)
                _bottomHeights[i] = (old != null && i < old.Length) ? old[i] : bottomY;
        }

        /// <summary>把底部轮廓压回地表下方，保证至少 MinThickness 的厚度。</summary>
        void ClampBottomProfile()
        {
            if (!useBottomProfile) return;

            EnsureHeights();
            EnsureBottomHeights();

            for (int i = 0; i < sampleCount; i++)
            {
                float maxBottom = _heights[i] - MinThickness;
                if (_bottomHeights[i] > maxBottom) _bottomHeights[i] = maxBottom;
            }
        }

        void SmoothOnce()
        {
            if (_heights.Length < 3) return;

            var copy = (float[])_heights.Clone();
            for (int i = 1; i < copy.Length - 1; i++)
                _heights[i] = (copy[i - 1] + copy[i] + copy[i + 1]) / 3f;
        }

        // ------------------------------------------------------------------ 构建

        [ContextMenu("重建网格与碰撞体")]
        public void Rebuild()
        {
            EnsureHeights();
            ClampBottomProfile();

            BuildMesh();
            if (buildColliders) BuildColliders();

            ApplyVisuals();
        }

        /// <summary>应用颜色和排序设置。</summary>
        public void ApplyVisuals()
        {
            if (applyColor) MeshUtil.ApplyColor(Renderer, color);
            MeshUtil.ApplySorting(Renderer, sortingLayerName, sortingOrder);
        }

        /// <summary>
        /// 轮廓三角化是否成功。左边/右边加过控制点后会走耳切；如果轮廓自交，
        /// 耳切会失败（网格消失），这时这个值是 false，Inspector 会给出提示。
        /// </summary>
        public bool OutlineValid { get; private set; } = true;

        /// <summary>
        /// 左右两条边是否都是直线、并且没有任何采样点被横向拖过。
        /// 满足时走三角带（快），否则走耳切（支持任意轮廓）。
        /// </summary>
        public bool HasStraightSides
        {
            get
            {
                if (LeftEdge.Count != 0 || RightEdge.Count != 0) return false;

                // 底边有独立折线时也必须走耳切：三角带是按"地表点与底部点逐点对齐"
                // 铺的，而折线的点数和位置跟地表脱钩了，铺不了。
                if (HasBottomEdge) return false;

                return !HasAnyOffset(_topOffsetsX) && !HasAnyOffset(_bottomOffsetsX);
            }
        }

        void BuildMesh()
        {
            // 左右边都是直线时走三角带，又快又稳；加过控制点才走耳切。
            if (HasStraightSides) BuildStripMesh();
            else BuildOutlineMesh();
        }

        void BuildStripMesh()
        {
            int n = sampleCount;

            var verts = new List<Vector3>(n * 2);
            var uvs = new List<Vector2>(n * 2);
            var tris = new List<int>((n - 1) * 6);

            for (int i = 0; i < n; i++)
            {
                float x = TopXAt(i);
                float topY = _heights[i];
                float botX = BottomXAt(i);
                float botY = BottomAt(i);

                verts.Add(new Vector3(x, topY, 0f));       // 2i   地表
                verts.Add(new Vector3(botX, botY, 0f));    // 2i+1 底部

                uvs.Add(new Vector2(x * uvScale, topY * uvScale));
                uvs.Add(new Vector2(botX * uvScale, botY * uvScale));
            }

            for (int i = 0; i < n - 1; i++)
            {
                int topA = 2 * i;
                int botA = 2 * i + 1;
                int topB = 2 * i + 2;
                int botB = 2 * i + 3;

                // 从 +Z 方向看是顺时针，Unity 里算正面。
                tris.Add(topA); tris.Add(topB); tris.Add(botA);
                tris.Add(topB); tris.Add(botB); tris.Add(botA);
            }

            OutlineValid = true;
            _mesh = MeshUtil.Build("HeightmapTerrain2D", _mesh, verts, uvs, tris, flipWinding);
            Filter.sharedMesh = _mesh;
            MeshUtil.EnsureMaterial(Renderer);
        }

        void BuildOutlineMesh()
        {
            var outline = new List<Vector2>();
            BuildOutline(outline);

            var tris = Triangulator.Triangulate(outline);
            OutlineValid = tris.Count > 0;

            if (!OutlineValid)
            {
                // 轮廓自交了，耳切解不出来。
                // 这里**故意不清空网格** —— 清空会让整块地形凭空消失，
                // 而用户正在 Scene 视图里拖点，根本看不到 Inspector 的提示。
                // 改成"保留上一次的形状 + 停止更新 + Scene 视图红色警告"。
                return;
            }

            // 耳切输出逆时针，翻成顺时针以匹配 Unity 的正面朝向。
            for (int i = 0; i + 2 < tris.Count; i += 3)
            {
                int t = tris[i];
                tris[i] = tris[i + 1];
                tris[i + 1] = t;
            }

            var verts = new List<Vector3>(outline.Count);
            var uvs = new List<Vector2>(outline.Count);

            for (int i = 0; i < outline.Count; i++)
            {
                verts.Add(new Vector3(outline[i].x, outline[i].y, 0f));
                uvs.Add(new Vector2(outline[i].x * uvScale, outline[i].y * uvScale));
            }

            _mesh = MeshUtil.Build("HeightmapTerrain2D", _mesh, verts, uvs, tris, flipWinding);
            Filter.sharedMesh = _mesh;
            MeshUtil.EnsureMaterial(Renderer);
        }

        void BuildColliders()
        {
            int stride = Mathf.Max(1, colliderStride);

            // 参与碰撞的采样点，始终包含最后一个点。
            var top = new List<Vector2>();
            var bottom = new List<Vector2>();

            for (int i = 0; i < sampleCount; i += stride)
            {
                top.Add(new Vector2(TopXAt(i), _heights[i] + colliderOffsetY));
                bottom.Add(new Vector2(BottomXAt(i), BottomAt(i) + colliderOffsetY));
            }

            if (top.Count < 2 ||
                top[top.Count - 1].x < TopXAt(sampleCount - 1) - 1e-4f)
            {
                int last = sampleCount - 1;
                top.Add(new Vector2(TopXAt(last), _heights[last] + colliderOffsetY));
                bottom.Add(new Vector2(BottomXAt(last), BottomAt(last) + colliderOffsetY));
            }

            if (buildEdgeCollider)
            {
                if (_edge == null) _edge = GetComponent<EdgeCollider2D>();
                if (_edge == null) _edge = gameObject.AddComponent<EdgeCollider2D>();
                _edge.edgeRadius = edgeRadius;
                _edge.points = top.ToArray();
                _edge.enabled = true;
            }
            else if (_edge != null)
            {
                _edge.enabled = false;
            }

            if (buildPolygonCollider)
            {
                if (_poly == null) _poly = GetComponent<PolygonCollider2D>();
                if (_poly == null) _poly = gameObject.AddComponent<PolygonCollider2D>();

                List<Vector2> outline;

                if (HasStraightSides)
                {
                    // 封闭轮廓：沿地表从左到右，再沿底部从右到左折回来。
                    // 顺时针，正是 PolygonCollider2D 对外轮廓的期望方向。
                    outline = new List<Vector2>(top);
                    for (int i = bottom.Count - 1; i >= 0; i--) outline.Add(bottom[i]);
                }
                else
                {
                    // 左右边加过控制点：直接用完整轮廓，保证碰撞和渲染一致。
                    outline = new List<Vector2>();
                    BuildOutline(outline);

                    if (colliderOffsetY != 0f)
                    {
                        for (int i = 0; i < outline.Count; i++)
                            outline[i] = new Vector2(outline[i].x, outline[i].y + colliderOffsetY);
                    }
                }

                _poly.pathCount = 1;
                _poly.SetPath(0, outline.ToArray());
                _poly.enabled = true;
            }
            else if (_poly != null)
            {
                _poly.enabled = false;
            }
        }
    }
}
