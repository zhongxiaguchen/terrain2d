using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Terrain2D.EditorTools
{
    /// <summary>
    /// Draws the two things that live outside the per-object editors: the fixed stage bar in
    /// the bottom-left corner, and the top-left panel whenever a terrain is not selected.
    ///
    /// 布局上只有两块地方，各有各的职责：
    ///
    ///   左下角  固定栏 —— 关卡设计 + 搭建 / 测试 / 资产替换。**永远在**，
    ///           因为它说的是"这块关卡走到哪一步"，不是"当前选中了什么"。
    ///   左上角  面板 —— 当前阶段具体要用的工具。内容随选择变：
    ///           选中地形时由 HeightmapTerrain2DEditor 画（阶段工具），
    ///           选中别的 2D 物体时由这里画（层级 / Z / 颜色），
    ///           什么都没选时这里只放该阶段的第一个动作。
    ///
    /// 之所以把"层级"从 Inspector 搬出来：它藏在 SpriteRenderer / MeshRenderer 那一大块
    /// 中间，离你正在看的物体很远，而摆关卡时这是最常改的一个数。
    ///
    /// 两块地方都只画在左上/左下，靠 CollectSelection 对地形让位来避免和地形面板重叠。
    /// </summary>
    [InitializeOnLoad]
    internal static class SortingOrderToolbar
    {
        static readonly List<Renderer> Selected = new List<Renderer>();

        // 选中物体上的碰撞体，随选择一起重建 —— 工具栏每帧都要读，别每帧 GetComponent。
        static readonly List<Collider2D> Colliders = new List<Collider2D>();

        static bool _dirty = true;

        // Tracked separately from Selected.Count: "nothing selected" and "selected, but the
        // object has no renderer" want different behaviour.
        static bool _selectionEmpty = true;

        // Must match the [MenuItem] paths in Terrain2DToolsEditor exactly.
        const string CreateTerrainMenu = "GameObject/地形 2D/创建高度场地形";
        const string CreatePlayerMenu = "GameObject/地形 2D/创建测试角色";

        // 固定栏只装标题 + 三段选择器，所以宽度是算出来的而不是撑满：
        // 撑满会把整条视口底边都占掉，看着像条横幅。
        const float StageBarWidth = 250f;
        const float StageSelectorWidth = 170f;

        static SortingOrderToolbar()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            SceneView.duringSceneGui += OnSceneGui;
            Selection.selectionChanged -= MarkDirty;
            Selection.selectionChanged += MarkDirty;
        }

        // Selection.gameObjects hands back a fresh array on every call and this runs for every
        // Scene view event, so the list is only rebuilt when the selection actually changes.
        static void MarkDirty()
        {
            _dirty = true;
        }

        static void OnSceneGui(SceneView view)
        {
            if (view == null) return;
            if (Event.current == null) return;

            // 左下角固定栏先画，而且**无条件**画：阶段是"这块关卡走到哪一步"，
            // 和选中什么无关，取消选择不该把它藏起来。
            DrawStageBar(view);

            if (CollectSelection())
            {
                DrawSelectionPanel(view);
                return;
            }

            // A selection that merely has no renderer on it stays quiet — a "create a
            // terrain" prompt there would just be noise.
            if (_selectionEmpty) DrawStageActionPanel(view);
        }

        /// <summary>
        /// 左下角那条固定栏：标题 + 阶段选择器，就这两样。
        ///
        /// 故意做窄：它只回答"我在哪个阶段"，具体工具在左上角那块面板里。
        /// 阶段选择器放这里而不是放面板里，是因为面板的内容会随选择变，
        /// 而阶段不该跟着变。
        /// </summary>
        static void DrawStageBar(SceneView view)
        {
            Handles.BeginGUI();
            GUILayout.BeginArea(SceneToolbarLayout.Area(view.position, 60f));
            GUILayout.FlexibleSpace();

            GUILayout.BeginVertical("box", GUILayout.Width(StageBarWidth));
            GUILayout.BeginHorizontal();

            GUILayout.Label("关卡设计", EditorStyles.miniBoldLabel);

            if (TerrainStage.DrawSelector(StageSelectorWidth))
                SceneView.RepaintAll();

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.EndArea();
            Handles.EndGUI();
        }

        /// <summary>
        /// 左上角面板：什么都没选的时候，只放当前阶段"最想做的第一件事"。
        ///
        /// 同一个位置在选中地形时由 HeightmapTerrain2DEditor 画完整工具，
        /// 两者二选一 —— 靠 CollectSelection 对地形让位来保证不重叠。
        /// </summary>
        static void DrawStageActionPanel(SceneView view)
        {
            Handles.BeginGUI();
            GUILayout.BeginArea(SceneToolbarLayout.Panel(view.position, SceneToolbarLayout.PanelWidth));
            GUILayout.BeginVertical("box");

            GUILayout.Label(TerrainStage.Labels[(int)TerrainStage.Current],
                            EditorStyles.miniBoldLabel);

            switch (TerrainStage.Current)
            {
                case WorkStage.Test:
                {
                    // 先选镜头怎么跟，再建角色。角色和镜头是一套测试装备 ——
                    // 建完再选的话，按下 Play 的头两秒人是跑出画面的。
                    GUILayout.BeginHorizontal();

                    GUILayout.Label("摄像机", EditorStyles.miniLabel);

                    EditorGUI.BeginChangeCheck();
                    int picked = EditorGUILayout.Popup(
                        (int)TestCameraPrefs.Mode, TestCameraPrefs.Labels,
                        EditorStyles.popup, GUILayout.Width(78f));

                    if (EditorGUI.EndChangeCheck())
                    {
                        TestCameraPrefs.Mode = (TestCameraMode)picked;
                        SceneView.RepaintAll();
                    }

                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    // 按钮上带着模式名，免得"我明明选了固定怎么还跟着"这种误会。
                    if (GUILayout.Button("创建测试角色（" + TestCameraPrefs.ModeLabel + "）",
                                         EditorStyles.miniButton))
                        EditorApplication.ExecuteMenuItem(CreatePlayerMenu);

                    break;
                }

                case WorkStage.Asset:
                    // 资产替换阶段没有"凭空造一个东西"的动作 —— 它的起点是选中一块
                    // 已经搭好的地形，所以这里只能给一句提示。
                    GUILayout.Label("选中地形后，这里显示 UV 与材质。",
                                    EditorStyles.wordWrappedMiniLabel);
                    break;

                default:
                    if (GUILayout.Button("创建高度场地形", EditorStyles.miniButton))
                    {
                        // 走菜单路径而不是直接调 HeightmapTerrain2DEditor.CreateTerrain()：
                        // 后者让两个文件产生编译期依赖，而 Unity 的增量编译不止一次出现
                        // "先编译、后导入"，于是报 'does not contain a definition'。
                        // 字符串调用没有这个问题。
                        EditorApplication.ExecuteMenuItem(CreateTerrainMenu);
                    }
                    break;
            }

            GUILayout.EndVertical();
            GUILayout.EndArea();
            Handles.EndGUI();
        }

        /// <summary>
        /// 左上角面板：选中了非地形的 2D 物体。
        /// 内容和阶段无关 —— 层级 / Z / 颜色是"这个物体"的属性，不是关卡进度。
        /// </summary>
        static void DrawSelectionPanel(SceneView view)
        {
            Handles.BeginGUI();
            GUILayout.BeginArea(SceneToolbarLayout.Panel(view.position, SceneToolbarLayout.PanelWidth));
            GUILayout.BeginVertical("box");

            GUILayout.Label(
                Selected.Count == 1 ? Selected[0].gameObject.name : Selected.Count + " 个物体",
                EditorStyles.miniBoldLabel);

            // 层级就是 sortingOrder。同层内数值大的画在上面。
            GUILayout.BeginHorizontal();
            GUILayout.Label("层级", EditorStyles.miniLabel);

            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = HasMixedOrder();
            int order = EditorGUILayout.IntField(Selected[0].sortingOrder, GUILayout.Width(44f));
            EditorGUI.showMixedValue = false;

            if (EditorGUI.EndChangeCheck()) ApplyOrder(order);

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // Z 和 sortingOrder 是一套东西的两半：层级相同时 Unity 就是靠 Z 决定谁在前，
            // 前景 / 背景分层就是靠它。
            GUILayout.BeginHorizontal();
            GUILayout.Label("Z", EditorStyles.miniLabel);

            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = HasMixedZ();
            float depth = EditorGUILayout.FloatField(
                Selected[0].transform.position.z, GUILayout.Width(48f));
            EditorGUI.showMixedValue = false;

            if (EditorGUI.EndChangeCheck()) ApplyZ(depth);

            // 命名保存的 Z（背景 / 地面 / 前景…），省得记数字。
            if (GUILayout.Button("▾", EditorStyles.miniButton, GUILayout.Width(18f)))
                DepthPresets.ShowMenu(Selected[0].transform.position.z, ApplyZ);

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (HasColorControl())
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("颜色", EditorStyles.miniLabel);

                EditorGUI.BeginChangeCheck();
                EditorGUI.showMixedValue = HasMixedColor();
                Color color = EditorGUILayout.ColorField(
                    GUIContent.none, GetColor(Selected[0]), true, true, false, GUILayout.Width(64f));
                EditorGUI.showMixedValue = false;

                if (EditorGUI.EndChangeCheck()) ApplyColor(color);

                // Saved colours. The picker itself cannot hold names, so this is where a
                // palette lives.
                if (GUILayout.Button("▾", EditorStyles.miniButton, GUILayout.Width(18f)))
                    ColorPalette.ShowMenu(GetColor(Selected[0]), ApplyColor);

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            // 碰撞体开关：给背景装饰关掉碰撞，不用去 Inspector 里翻。
            // 没有碰撞体时置灰而不是藏起来 —— 藏起来会让人以为这个功能没做。
            GUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(Colliders.Count == 0))
            {
                bool allOn = AllCollidersEnabled();
                bool nowOn = GUILayout.Toggle(
                    allOn, "碰撞体", EditorStyles.miniButton, GUILayout.Width(64f));

                // 多选且状态不一致时 allOn 为 false，点一下就是"全开"。
                if (Colliders.Count > 0 && nowOn != allOn) ApplyColliders(nowOn);
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUILayout.EndArea();
            Handles.EndGUI();
        }

        /// <summary>
        /// Returns false when the toolbar should not be drawn at all.
        /// </summary>
        static bool CollectSelection()
        {
            if (_dirty)
            {
                _dirty = false;
                RebuildSelection();
            }

            return Selected.Count > 0;
        }

        static void RebuildSelection()
        {
            Selected.Clear();
            Colliders.Clear();

            GameObject[] objects = Selection.gameObjects;
            _selectionEmpty = objects == null || objects.Length == 0;
            if (_selectionEmpty) return;

            // A selected terrain owns the bottom of the viewport; stand down.
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] == null) continue;
                if (objects[i].GetComponentInParent<HeightmapTerrain2D>() != null) return;
            }

            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] == null) continue;

                var renderer = objects[i].GetComponent<Renderer>();
                if (renderer == null || !renderer.enabled) continue;

                Selected.Add(renderer);

                var collider = objects[i].GetComponent<Collider2D>();
                if (collider != null) Colliders.Add(collider);
            }
        }

        static bool HasMixedOrder()
        {
            if (Selected.Count < 2) return false;

            int first = Selected[0].sortingOrder;
            for (int i = 1; i < Selected.Count; i++)
                if (Selected[i].sortingOrder != first) return true;

            return false;
        }

        static void ApplyOrder(int order)
        {
            var objects = new UnityEngine.Object[Selected.Count];
            for (int i = 0; i < Selected.Count; i++) objects[i] = Selected[i];

            Undo.RecordObjects(objects, "改层级");

            for (int i = 0; i < Selected.Count; i++)
            {
                Selected[i].sortingOrder = order;
                EditorUtility.SetDirty(Selected[i]);
            }

            SceneView.RepaintAll();
        }

        static bool HasMixedZ()
        {
            if (Selected.Count < 2) return false;

            float first = Selected[0].transform.position.z;
            for (int i = 1; i < Selected.Count; i++)
                if (!Mathf.Approximately(Selected[i].transform.position.z, first)) return true;

            return false;
        }

        static void ApplyZ(float depth)
        {
            // World Z, not local: world Z is what the camera actually sorts by, so it is the
            // number a level designer means when they say "put this in front".
            var transforms = new UnityEngine.Object[Selected.Count];
            for (int i = 0; i < Selected.Count; i++) transforms[i] = Selected[i].transform;

            Undo.RecordObjects(transforms, "改 Z");

            for (int i = 0; i < Selected.Count; i++)
            {
                Transform transform = Selected[i].transform;
                Vector3 position = transform.position;
                position.z = depth;
                transform.position = position;
                EditorUtility.SetDirty(transform);
            }

            SceneView.RepaintAll();
        }

        static bool AllCollidersEnabled()
        {
            if (Colliders.Count == 0) return false;

            for (int i = 0; i < Colliders.Count; i++)
                if (!Colliders[i].enabled) return false;

            return true;
        }

        static void ApplyColliders(bool enabled)
        {
            var objects = new UnityEngine.Object[Colliders.Count];
            for (int i = 0; i < Colliders.Count; i++) objects[i] = Colliders[i];

            Undo.RecordObjects(objects, enabled ? "启用碰撞体" : "关闭碰撞体");

            for (int i = 0; i < Colliders.Count; i++)
            {
                Colliders[i].enabled = enabled;
                EditorUtility.SetDirty(Colliders[i]);
            }

            SceneView.RepaintAll();
        }

        // ------------------------------------------------------------------ 颜色
        //
        // 颜色存在哪，取决于物体是什么：
        //   本包自己的网格（PolygonMesh2D / HeightmapTerrain2D）→ 组件上的 color 字段，
        //       改完调 ApplyVisuals() 刷到渲染器上；
        //   SpriteRenderer → Unity 自己的 color 属性，直接改，Inspector 里会同步显示；
        //   其他渲染器 → 不动。没有可靠的"每物体一份"的写法，与其乱猜不如不显示。

        static bool HasColorControl()
        {
            for (int i = 0; i < Selected.Count; i++)
                if (SupportsColor(Selected[i])) return true;
            return false;
        }

        static bool SupportsColor(Renderer renderer)
        {
            return renderer is SpriteRenderer
                || renderer.GetComponent<PolygonMesh2D>() != null
                || renderer.GetComponent<HeightmapTerrain2D>() != null;
        }

        static Color GetColor(Renderer renderer)
        {
            var terrain = renderer.GetComponent<HeightmapTerrain2D>();
            if (terrain != null) return terrain.color;

            var polygon = renderer.GetComponent<PolygonMesh2D>();
            if (polygon != null) return polygon.color;

            var sprite = renderer as SpriteRenderer;
            if (sprite != null) return sprite.color;

            return Color.white;
        }

        static void ApplyColor(Color color)
        {
            for (int i = 0; i < Selected.Count; i++)
            {
                var renderer = Selected[i];

                var terrain = renderer.GetComponent<HeightmapTerrain2D>();
                if (terrain != null)
                {
                    Undo.RecordObject(terrain, "改颜色");
                    terrain.color = color;
                    terrain.ApplyVisuals();
                    EditorUtility.SetDirty(terrain);
                    continue;
                }

                var polygon = renderer.GetComponent<PolygonMesh2D>();
                if (polygon != null)
                {
                    Undo.RecordObject(polygon, "改颜色");
                    polygon.color = color;
                    polygon.ApplyVisuals();
                    EditorUtility.SetDirty(polygon);
                    continue;
                }

                var sprite = renderer as SpriteRenderer;
                if (sprite != null)
                {
                    Undo.RecordObject(sprite, "改颜色");
                    sprite.color = color;
                    EditorUtility.SetDirty(sprite);
                }
            }

            SceneView.RepaintAll();
        }

        static bool HasMixedColor()
        {
            if (Selected.Count < 2) return false;

            Color first = GetColor(Selected[0]);
            for (int i = 1; i < Selected.Count; i++)
                if (GetColor(Selected[i]) != first) return true;

            return false;
        }
    }
}
