using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LDK2D.EditorTools
{
    /// <summary>
    /// 视口**右上角**的缩略地图：把整个场景压成一张小图，一眼看到关卡铺到哪、角色在哪。
    ///
    /// 为什么需要它：地形摆到 9 块之后，Scene 视图里永远只能看到一块。
    /// 想确认"整体是不是拉得太长""敌人是不是漏在某个角落"，只能缩到很小或者滚来滚去。
    /// 摆关卡时最常问的两个问题——**我摆到哪了**、**还有哪块是空的**——这张图直接回答。
    ///
    /// 画什么：
    ///   - 地形：用 `BuildOutline()` 的地表折线做**剪影填充**（不是包围盒方框，
    ///     方框会把不规则地形画成一块砖，看不出坡往哪边倒）
    ///   - 测试角色 / 敌人 / NPC / 奖励点：按种类配色的小方块
    ///
    /// **纯展示，不接受任何输入。** 不加按钮、不加开关、不响应点击 ——
    /// 它只是"另一双眼睛"，不参与编辑。
    ///
    /// 位置：右上角，但要**避开 Unity 自己的东西**。2022.3 的右上角被方向指示器
    /// （Orientation gizmo，约 100px）和叠加工具条占着，所以往下让
    /// <see cref="TopInset"/> 而不是贴顶。往左不让，因为那两条的宽度会随窗口变。
    /// </summary>
    [InitializeOnLoad]
    internal static class LDK2DSceneMinimap
    {
        /// <summary>面板宽度。窄一点，别把视口右上角吃掉太多。</summary>
        const float PanelWidth = 190f;

        /// <summary>
        /// 面板高度不写死，按场景长宽比算 —— 但这个关卡是 40 宽 × 4.4 高，
        /// 严格按比例会得到一条 20px 的横线。所以夹在 [MinPanelHeight, MaxPanelHeight]
        /// 之间：太扁了给个下限，免得看着像坏了；太高了封顶，别挡住视口。
        /// </summary>
        const float MinPanelHeight = 72f;
        const float MaxPanelHeight = 150f;

        /// <summary>地图内容和面板边框之间的留白。</summary>
        const float Padding = 5f;

        /// <summary>
        /// 距离视口顶部的距离。**这个值是为了让开方向指示器**（2022.3 里约 100px，
        /// 加边距），所以按行高换算而不是写死像素，跟着 UI Scale 一起变。
        /// 如果哪天它压到 gizmo 上了，改这一个数就行。
        /// </summary>
        static float TopInset { get { return EditorGUIUtility.singleLineHeight * 6.5f; } }

        static readonly Color BackgroundColor = new Color(0.07f, 0.07f, 0.08f, 0.80f);
        static readonly Color BorderColor = new Color(1f, 1f, 1f, 0.20f);
        static readonly Color TerrainColor = new Color(0.30f, 0.44f, 0.30f, 1f);
        static readonly Color TerrainTopColor = new Color(0.58f, 0.80f, 0.50f, 1f);
        static readonly Color PlayerColor = new Color(0.35f, 0.85f, 1f, 1f);

        // 场景对象缓存。只有层级变了才重新 Find —— FindObjectsOfType 每次扫全场景，
        // 而 OnSceneGui 每个鼠标事件都跑。
        static readonly List<LDK2DHeightmapTerrain> _terrains = new List<LDK2DHeightmapTerrain>();
        static readonly List<LDK2DTestActor> _actors = new List<LDK2DTestActor>();
        static LDK2DTestPlayer _player;
        static bool _dirty = true;

        // 复用同一个 List 接轮廓，避免每帧分配。BuildOutline 只往里塞 Vector2（值类型），
        // 所以复用它本身不产生堆分配。
        static readonly List<Vector2> _outline = new List<Vector2>();

        static LDK2DSceneMinimap()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            SceneView.duringSceneGui += OnSceneGui;

            EditorApplication.hierarchyChanged -= MarkDirty;
            EditorApplication.hierarchyChanged += MarkDirty;
        }

        static void MarkDirty()
        {
            _dirty = true;
        }

        static void OnSceneGui(SceneView view)
        {
            if (view == null) return;
            if (Event.current == null) return;

            if (_dirty) ResolveScene();

            // 一块地形都没有的时候不画 —— 空盒子比不画更像坏了。
            if (_terrains.Count == 0 && _actors.Count == 0 && _player == null) return;

            // 地图范围每帧重算：地形雕刻之后 renderer.bounds 会变，但层级没变、
            // 不会触发 hierarchyChanged，缓存住就会画成旧形状。
            Bounds world = ResolveWorldBounds();
            if (world.size.x <= 0f || world.size.y <= 0f) return;

            Rect panel = PanelRect(view, world);
            if (panel.width < 80f || panel.height < 60f) return;

            Rect content = new Rect(
                panel.x + Padding,
                panel.y + Padding,
                panel.width - Padding * 2f,
                panel.height - Padding * 2f);

            Map map = Map.Fit(content, world);

            Handles.BeginGUI();

            EditorGUI.DrawRect(panel, BackgroundColor);

            // 裁剪到内容区：地形和角色都可能落在范围外（被编辑器手动挪出包围盒的物体），
            // 不裁会画到视口上。
            GUI.BeginClip(content);
            DrawTerrain(map);
            DrawActors(map);
            GUI.EndClip();

            DrawBorder(panel);

            Handles.EndGUI();
        }

        static void ResolveScene()
        {
            _dirty = false;

            _terrains.Clear();
            _actors.Clear();
            _player = null;

            var terrains = Object.FindObjectsOfType<LDK2DHeightmapTerrain>(false);
            for (int i = 0; i < terrains.Length; i++)
                if (terrains[i] != null) _terrains.Add(terrains[i]);

            var actors = Object.FindObjectsOfType<LDK2DTestActor>(false);
            for (int i = 0; i < actors.Length; i++)
                if (actors[i] != null) _actors.Add(actors[i]);

            _player = Object.FindObjectOfType<LDK2DTestPlayer>(false);
        }

        /// <summary>
        /// 地图覆盖的世界范围：所有地形包围盒的并集。
        ///
        /// 只用地形、不用"所有 Renderer"：场景里还有一堆当参考图用的 Sprite，
        /// 把它们算进来范围会被撑得很大，地形反而缩成一条线。
        /// </summary>
        static Bounds ResolveWorldBounds()
        {
            bool has = false;
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);

            for (int i = 0; i < _terrains.Count; i++)
            {
                Renderer renderer = _terrains[i] != null ? _terrains[i].GetComponent<Renderer>() : null;
                if (renderer == null) continue;

                if (!has) { bounds = renderer.bounds; has = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            // 没有地形时退回角色位置，至少让人看见角色在哪。
            if (!has)
            {
                for (int i = 0; i < _actors.Count; i++)
                {
                    if (_actors[i] == null) continue;

                    var point = new Bounds(_actors[i].transform.position, Vector3.one);
                    if (!has) { bounds = point; has = true; }
                    else bounds.Encapsulate(point);
                }

                if (_player != null)
                {
                    var point = new Bounds(_player.transform.position, Vector3.one);
                    if (!has) { bounds = point; has = true; }
                    else bounds.Encapsulate(point);
                }
            }

            if (!has) return new Bounds(Vector3.zero, Vector3.zero);

            // 四周留一点，免得贴边的地形正好压在边框上。
            bounds.Expand(bounds.size * 0.06f);
            return bounds;
        }

        /// <summary>
        /// 地形剪影：把地表折线逐段画成竖条。
        ///
        /// 为什么不画包围盒方框：方框会把不规则地形画成一块砖，看不出坡往哪边倒，
        /// 而"这块地形是怎么斜的"恰恰是摆关卡时最想从缩略图里看出来的东西。
        ///
        /// 为什么不画真多边形：`Handles` 的多边形绘制只认世界坐标，在
        /// `Handles.BeginGUI` 之后用不了。逐段竖条是纯 GUI 画法，还顺带免了三角化。
        /// </summary>
        static void DrawTerrain(Map map)
        {
            for (int t = 0; t < _terrains.Count; t++)
            {
                LDK2DHeightmapTerrain terrain = _terrains[t];
                if (terrain == null) continue;

                Renderer renderer = terrain.GetComponent<Renderer>();
                if (renderer == null) continue;

                _outline.Clear();
                terrain.BuildOutline(_outline);

                // 轮廓的前 sampleCount 个点是地表（左 → 右），后面是右链 / 底 / 左链。
                int top = Mathf.Clamp(terrain.sampleCount, 0, _outline.Count);
                if (top < 2) continue;

                float bottomWorld = renderer.bounds.min.y;
                Transform transform = terrain.transform;

                Vector2 previous = transform.TransformPoint(_outline[0]);

                for (int i = 1; i < top; i++)
                {
                    Vector2 current = transform.TransformPoint(_outline[i]);

                    float left = Mathf.Min(previous.x, current.x);
                    float right = Mathf.Max(previous.x, current.x);

                    // 相邻两点的较高者当这条竖条的顶 —— 折线向下走的时候，
                    // 用较低的那个会在拐角留一个缺口。
                    float surface = Mathf.Max(previous.y, current.y);

                    DrawColumn(map, left, right, surface, bottomWorld);

                    previous = current;
                }

                // 顶边压一条亮色，把"地表"和"体块"分开。没有它整块是一个色，
                // 缩略图上看不出地形高低。
                DrawSurface(map, transform, top);
            }
        }

        static void DrawColumn(Map map, float left, float right, float surface, float bottom)
        {
            Vector2 a = map.Point(new Vector2(left, surface));
            Vector2 b = map.Point(new Vector2(right, bottom));

            float x = Mathf.Min(a.x, b.x);
            float y = Mathf.Min(a.y, b.y);
            float w = Mathf.Abs(b.x - a.x);
            float h = Mathf.Abs(b.y - a.y);

            // 至少 1px：地形很窄的时候别让它整个消失。
            EditorGUI.DrawRect(new Rect(x, y, Mathf.Max(1f, w), Mathf.Max(1f, h)), TerrainColor);
        }

        static void DrawSurface(Map map, Transform transform, int top)
        {
            Vector2 previous = map.Point(transform.TransformPoint(_outline[0]));

            for (int i = 1; i < top; i++)
            {
                Vector2 current = map.Point(transform.TransformPoint(_outline[i]));

                float left = Mathf.Min(previous.x, current.x);
                float right = Mathf.Max(previous.x, current.x);
                float y = Mathf.Min(previous.y, current.y);

                // 逐段画一条 1px 的横线。斜的地方会有点阶梯感，但在 190px 的图上
                // 看不出来，而且比引一条 Handles 折线省事得多。
                EditorGUI.DrawRect(
                    new Rect(left, y, Mathf.Max(1f, right - left), 1f), TerrainTopColor);

                previous = current;
            }
        }

        static void DrawActors(Map map)
        {
            for (int i = 0; i < _actors.Count; i++)
            {
                LDK2DTestActor actor = _actors[i];
                if (actor == null) continue;

                Vector2 point = map.Point(actor.transform.position);
                Color color = LDK2DTestActor.ColorOf(actor.kind);

                DrawDot(point, 3f, color);
            }

            if (_player == null) return;

            // 角色画大一点：它只有一个，而且是最该被一眼找到的那个。
            DrawDot(map.Point(_player.transform.position), 4f, PlayerColor);
        }

        static void DrawDot(Vector2 center, float size, Color color)
        {
            EditorGUI.DrawRect(
                new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size),
                color);
        }

        static void DrawBorder(Rect panel)
        {
            EditorGUI.DrawRect(new Rect(panel.x, panel.y, panel.width, 1f), BorderColor);
            EditorGUI.DrawRect(new Rect(panel.x, panel.yMax - 1f, panel.width, 1f), BorderColor);
            EditorGUI.DrawRect(new Rect(panel.x, panel.y, 1f, panel.height), BorderColor);
            EditorGUI.DrawRect(new Rect(panel.xMax - 1f, panel.y, 1f, panel.height), BorderColor);
        }

        /// <summary>
        /// 右上角面板的位置。右边贴 <see cref="LDK2DSceneToolbarLayout.SideInset"/>，
        /// 顶部让开方向指示器。高度按场景长宽比算，夹在上下限之间。
        /// </summary>
        static Rect PanelRect(SceneView view, Bounds world)
        {
            Rect position = view.position;

            float width = Mathf.Min(PanelWidth, position.width - LDK2DSceneToolbarLayout.SideInset * 2f);

            float aspect = world.size.y / Mathf.Max(0.0001f, world.size.x);
            float height = Mathf.Clamp(width * aspect + Padding * 2f, MinPanelHeight, MaxPanelHeight);

            // 视口很矮的时候别让面板顶出画面。
            height = Mathf.Min(height, Mathf.Max(60f, position.height - TopInset - LDK2DSceneToolbarLayout.BottomInset));

            return new Rect(
                position.width - width - LDK2DSceneToolbarLayout.SideInset,
                TopInset,
                width,
                height);
        }

        /// <summary>
        /// 世界坐标 → 内容区局部坐标（原点在内容区左上角，Y 向下）。
        ///
        /// 用局部坐标是因为外面套了 `GUI.BeginClip(content)`，裁剪区内的绘制坐标
        /// 就是从内容区左上角算起的。
        /// </summary>
        struct Map
        {
            public Bounds World;
            public float Scale;
            public float OriginX;
            public float OriginY;

            /// <summary>等比缩放并居中。**必须等比** —— 横竖分开缩会把地形画变形。</summary>
            public static Map Fit(Rect content, Bounds world)
            {
                var map = new Map();
                map.World = world;

                float w = Mathf.Max(0.0001f, world.size.x);
                float h = Mathf.Max(0.0001f, world.size.y);

                map.Scale = Mathf.Min(content.width / w, content.height / h);

                float drawWidth = w * map.Scale;
                float drawHeight = h * map.Scale;

                map.OriginX = (content.width - drawWidth) * 0.5f;
                map.OriginY = (content.height - drawHeight) * 0.5f;

                return map;
            }

            public Vector2 Point(Vector2 world)
            {
                return new Vector2(
                    OriginX + (world.x - World.min.x) * Scale,
                    OriginY + (World.max.y - world.y) * Scale);
            }

            public Vector2 Point(Vector3 world)
            {
                return Point(new Vector2(world.x, world.y));
            }
        }
    }
}
