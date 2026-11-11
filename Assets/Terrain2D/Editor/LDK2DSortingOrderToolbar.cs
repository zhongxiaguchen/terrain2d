using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LDK2D.EditorTools
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
    ///           选中地形时由 LDK2DHeightmapTerrainEditor 画（阶段工具），
    ///           选中别的 2D 物体时由这里画（层级 / Z / 颜色），
    ///           什么都没选时这里只放该阶段的第一个动作。
    ///
    /// 之所以把"层级"从 Inspector 搬出来：它藏在 SpriteRenderer / MeshRenderer 那一大块
    /// 中间，离你正在看的物体很远，而摆关卡时这是最常改的一个数。
    ///
    /// 两块地方都只画在左上/左下，靠 CollectSelection 对地形让位来避免和地形面板重叠。
    /// </summary>
    [InitializeOnLoad]
    internal static class LDK2DSortingOrderToolbar
    {
        static readonly List<Renderer> Selected = new List<Renderer>();

        // 选中物体上的碰撞体，随选择一起重建 —— 工具栏每帧都要读，别每帧 GetComponent。
        static readonly List<Collider2D> Colliders = new List<Collider2D>();

        static bool _dirty = true;

        // Tracked separately from Selected.Count: "nothing selected" and "selected, but the
        // object has no renderer" want different behaviour.
        static bool _selectionEmpty = true;

        // Must match the [MenuItem] paths in LDK2DToolsEditor exactly.
        const string CreateTerrainMenu = "GameObject/LDK2D/创建高度场地形";
        const string CreatePlayerMenu = "GameObject/LDK2D/创建测试角色";

        // 固定栏只装标题 + 三段选择器，所以宽度是算出来的而不是撑满：
        // 撑满会把整条视口底边都占掉，看着像条横幅。
        const float StageBarWidth = 250f;
        const float StageSelectorWidth = 170f;

        static LDK2DSortingOrderToolbar()
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
            GUILayout.BeginArea(LDK2DSceneToolbarLayout.Area(view.position, 60f));
            GUILayout.FlexibleSpace();

            GUILayout.BeginVertical("box", GUILayout.Width(StageBarWidth));
            GUILayout.BeginHorizontal();

            GUILayout.Label("关卡设计", EditorStyles.miniBoldLabel);

            if (LDK2DTerrainStage.DrawSelector(StageSelectorWidth))
                SceneView.RepaintAll();

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.EndArea();
            Handles.EndGUI();
        }

        /// <summary>
        /// 左上角面板：什么都没选的时候，只放当前阶段"最想做的第一件事"。
        ///
        /// 同一个位置在选中地形时由 LDK2DHeightmapTerrainEditor 画完整工具，
        /// 两者二选一 —— 靠 CollectSelection 对地形让位来保证不重叠。
        /// </summary>
        static void DrawStageActionPanel(SceneView view)
        {
            Handles.BeginGUI();
            GUILayout.BeginArea(LDK2DSceneToolbarLayout.Panel(view.position, LDK2DSceneToolbarLayout.PanelWidth));
            GUILayout.BeginVertical("box");

            GUILayout.Label(LDK2DTerrainStage.Labels[(int)LDK2DTerrainStage.Current],
                            EditorStyles.miniBoldLabel);

            switch (LDK2DTerrainStage.Current)
            {
                case LDK2DWorkStage.Test:
                {
                    // 先选镜头怎么跟，再建角色。角色和镜头是一套测试装备 ——
                    // 建完再选的话，按下 Play 的头两秒人是跑出画面的。
                    GUILayout.BeginHorizontal();

                    GUILayout.Label("摄像机", EditorStyles.miniLabel);

                    EditorGUI.BeginChangeCheck();
                    int picked = EditorGUILayout.Popup(
                        (int)LDK2DTestCameraPrefs.Mode, LDK2DTestCameraPrefs.Labels,
                        EditorStyles.popup, GUILayout.Width(78f));

                    if (EditorGUI.EndChangeCheck())
                    {
                        LDK2DTestCameraPrefs.Mode = (LDK2DTestCameraMode)picked;
                        SceneView.RepaintAll();
                    }

                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    // 按钮上带着模式名，免得"我明明选了固定怎么还跟着"这种误会。
                    if (GUILayout.Button("创建测试角色（" + LDK2DTestCameraPrefs.ModeLabel + "）",
                                         EditorStyles.miniButton))
                        EditorApplication.ExecuteMenuItem(CreatePlayerMenu);

                    DrawLiveEditRow();

                    // 占位标记：不管有没有选中地形，测试阶段都能造。
                    // 在搭关卡的时候人经常什么都不选（刚按完 Esc），要是非得先点一块
                    // 地形才能造敌人，这个入口就白放了。
                    DrawActorRow(LDK2DActorKind.Enemy);
                    DrawActorRow(LDK2DActorKind.Npc);
                    DrawActorRow(LDK2DActorKind.Reward);

                    break;
                }

                case LDK2DWorkStage.Asset:
                    // 资产替换阶段没有"凭空造一个东西"的动作 —— 它的起点是选中一块
                    // 已经搭好的地形，所以这里只能给一句提示。
                    GUILayout.Label("选中地形后，这里显示 UV 与材质。",
                                    EditorStyles.wordWrappedMiniLabel);

                    // 场景显示层级放在这里而不是「选中地形」的那块面板里：
                    // 它管的是**整个场景**的显隐，和选中哪块地形无关。
                    // 而且人检查整体效果时通常什么都不选 —— 放在选中地形的面板里，
                    // 等于要先点一块地形才能看整层，多绕一步。
                    //
                    // 走 LDK2DSceneToolbarLayout 上的委托而不是直接调 LDK2DSceneDepthVisibility：
                    // 这个文件比那个老，旧文件引用新类型会在「先编译、后导入」那一轮
                    // 报 does not contain a definition。为 null 时这一段跳过。
                    if (LDK2DSceneToolbarLayout.AssetPanelExtra != null)
                        LDK2DSceneToolbarLayout.AssetPanelExtra();

                    break;

                default:
                    if (GUILayout.Button("创建高度场地形", EditorStyles.miniButton))
                    {
                        // 走菜单路径而不是直接调 LDK2DHeightmapTerrainEditor.CreateTerrain()：
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
            GUILayout.BeginArea(LDK2DSceneToolbarLayout.Panel(view.position, LDK2DSceneToolbarLayout.PanelWidth));
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
                LDK2DDepthPresets.ShowMenu(Selected[0].transform.position.z, ApplyZ);

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
                    LDK2DColorPalette.ShowMenu(GetColor(Selected[0]), ApplyColor);

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            // 碰撞体：和地形层一个逻辑 —— 勾上就生成并启用，取消就禁用（不删）。
            //
            // 永远是同一个开关，不按"当前有没有碰撞体"换成另一种控件：换控件会让这
            // 一行在点下去的一瞬间跳一下，而且人得多记一个按钮的位置。摆关卡时给
            // 背景装饰临时补个碰撞盒很常见，这颗开关顺手就干了，不用回 Inspector。
            GUILayout.BeginHorizontal();

            bool allOn = AllCollidersEnabled();
            bool nowOn = GUILayout.Toggle(
                allOn, "碰撞体", EditorStyles.miniButton, GUILayout.Width(64f));

            // 多选且状态不一致（或有的物体根本没碰撞体）时 allOn 为 false，
            // 点一下就是"全开"。
            if (nowOn != allOn) SetColliders(nowOn);

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
                if (objects[i].GetComponentInParent<LDK2DHeightmapTerrain>() != null) return;
            }

            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] == null) continue;

                // 隐藏的渲染器**也**要收进来。资产替换面板现在能按 Z 整层隐藏，
                // 隐藏之后如果这里把它们过滤掉，就会出现"选中了物体、面板却是空的"，
                // 看着像坏了。层级 / Z / 颜色对这些物体照样有意义。
                var renderer = objects[i].GetComponent<Renderer>();
                if (renderer == null) continue;

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

        /// <summary>
        /// 「全部都有碰撞体、而且都开着」才返回 true。
        ///
        /// 注意 `Colliders` 只收**已经有**碰撞体的物体，所以光看它是不是全部 enabled
        /// 不够：多选时只要有一个物体身上没碰撞体，这颗开关就该是关的 ——
        /// 点一下才补得出来。否则开关显示"开"、实际有个物体没碰撞，摆关卡时
        /// 会以为挡住了其实穿过去了。
        /// </summary>
        static bool AllCollidersEnabled()
        {
            if (Colliders.Count == 0) return false;
            if (Colliders.Count < Selected.Count) return false;

            for (int i = 0; i < Colliders.Count; i++)
                if (!Colliders[i].enabled) return false;

            return true;
        }

        /// <summary>
        /// 碰撞体开关，和地形层的 `SetCollisionEnabled` 一个心智：
        ///
        ///   勾上 → 缺的补一个 `BoxCollider2D`，然后全部 `enabled = true`
        ///   取消 → 只 `enabled = false`，**组件留在物体上**
        ///
        /// 不删是有意的。摆关卡时经常要临时让某个装饰物"能挡 / 不能挡"来回试，
        /// 删了再想加回来就得重新调尺寸和偏移，这一来一回就把节奏打断了。
        /// 而且地形层也是这么做的，两块面板的开关行为一致，人才不用记两套规则。
        /// </summary>
        static void SetColliders(bool on)
        {
            var targets = new List<Collider2D>();

            for (int i = 0; i < Selected.Count; i++)
            {
                Renderer renderer = Selected[i];
                if (renderer == null) continue;

                Collider2D collider = renderer.GetComponent<Collider2D>();

                if (collider == null)
                {
                    // 关闭时没有可关的东西 —— 不该顺手补一个再关掉，
                    // 那等于"取消勾选"反而给物体塞了组件。
                    if (!on) continue;

                    collider = AddBoxCollider(renderer);
                    if (collider == null) continue;
                }

                targets.Add(collider);
            }

            if (targets.Count > 0)
            {
                var objects = new UnityEngine.Object[targets.Count];
                for (int i = 0; i < targets.Count; i++) objects[i] = targets[i];

                Undo.RecordObjects(objects, on ? "启用碰撞体" : "关闭碰撞体");

                for (int i = 0; i < targets.Count; i++)
                {
                    targets[i].enabled = on;
                    EditorUtility.SetDirty(targets[i]);
                }
            }

            // 同一帧就把缓存刷掉，否则这颗开关这一帧还显示旧状态，
            // 得等下一次重绘才变 —— 点一下没反应的样子。
            RebuildSelection();
            _dirty = false;

            SceneView.RepaintAll();
        }

        /// <summary>
        /// 给一个物体补 `BoxCollider2D`，尺寸贴合渲染器的本地包围盒；加不上时返回 null。
        ///
        /// 只加 Box：它最通用也最可预测，Sprite 和 Mesh 都适用。形状不贴的场合
        /// （多边形网格之类）人自己在 Inspector 里换成 `PolygonCollider2D` 就行 ——
        /// 面板上给的是"有个碰撞体"，不是"猜你要哪种"。
        ///
        /// 地形不归这里管：它自己会生成 `PolygonCollider2D`
        /// （`LDK2DHeightmapTerrain.BuildColliders`），而且选中地形时这块面板根本不画。
        /// </summary>
        static Collider2D AddBoxCollider(Renderer renderer)
        {
            // Undo.AddComponent 失败时返回 null，不抛异常 —— 物体上已经有 3D 碰撞体
            // 时就是这样（Unity 不允许 2D / 3D 碰撞体挂在同一个物体上）。
            var box = Undo.AddComponent<BoxCollider2D>(renderer.gameObject);

            if (box == null)
            {
                Debug.LogWarning(
                    "「" + renderer.name + "」上加不了 BoxCollider2D —— 物体上已经有 " +
                    "3D 碰撞体（MeshCollider / BoxCollider 等），Unity 不允许 2D 和 " +
                    "3D 碰撞体共存。", renderer);
                return null;
            }

            // localBounds 是本地空间，和 Collider2D 的 offset / size 是同一套坐标，
            // 不用再乘缩放。
            Bounds bounds = renderer.localBounds;
            Vector2 size = new Vector2(bounds.size.x, bounds.size.y);

            // sprite / 网格是空的时 localBounds 会是 0。给个 1×1 兜底，
            // 否则加出来的碰撞盒尺寸是 0，在视口里点都点不到。
            if (size.x <= 0f || size.y <= 0f) size = Vector2.one;

            box.size = size;
            box.offset = new Vector2(bounds.center.x, bounds.center.y);

            return box;
        }

        // ------------------------------------------------------------------ 颜色
        //
        // 颜色存在哪，取决于物体是什么：
        //   本包自己的网格（LDK2DPolygonMesh / LDK2DHeightmapTerrain）→ 组件上的 color 字段，
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
                || renderer.GetComponent<LDK2DPolygonMesh>() != null
                || renderer.GetComponent<LDK2DHeightmapTerrain>() != null;
        }

        static Color GetColor(Renderer renderer)
        {
            var terrain = renderer.GetComponent<LDK2DHeightmapTerrain>();
            if (terrain != null) return terrain.color;

            var polygon = renderer.GetComponent<LDK2DPolygonMesh>();
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

                var terrain = renderer.GetComponent<LDK2DHeightmapTerrain>();
                if (terrain != null)
                {
                    Undo.RecordObject(terrain, "改颜色");
                    terrain.color = color;
                    terrain.ApplyVisuals();
                    EditorUtility.SetDirty(terrain);
                    continue;
                }

                var polygon = renderer.GetComponent<LDK2DPolygonMesh>();
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

        /// <summary>
        /// 一行「实时编辑」勾选框。
        ///
        /// 它管的只有一件事：**Play 模式下改的东西，退出后要不要留下来**。
        /// 进不进 Play 不归它管 —— 编辑器自带播放键，再放一套只会打架。
        ///
        /// 关着的时候 Play 里连地形手柄都不画（`LDK2DHeightmapTerrainEditor.OnSceneGUI`
        /// 开头那道门禁），所以不会出现「改了半天退出全没了」这种事 —— 根本改不了。
        ///
        /// 放在「创建测试角色」下面：它是测试装备的一部分，和摄像机模式一样，
        /// 都是"这次怎么测"的设置。
        /// </summary>
        static void DrawLiveEditRow()
        {
            bool liveEdit = LDK2DTerrainStage.LiveEdit;

            bool picked = EditorGUILayout.ToggleLeft(
                new GUIContent("实时编辑", "Play 模式下保留地形手柄，退出 Play 时把改动写回场景。"),
                liveEdit, EditorStyles.miniLabel);

            if (picked == liveEdit) return;

            LDK2DTerrainStage.LiveEdit = picked;

            // 视口要立刻跟着变：勾上 → 手柄出现，取消 → 手柄消失。
            SceneView.RepaintAll();
        }

        /// <summary>
        /// 一行「种类 + 类型下拉 + 创建」。
        ///
        /// 只在「什么都没选」这块面板里画 —— 选中地形时那块面板（`DrawTestRows`）
        /// 保持原样，不加这几行。
        ///
        /// 选中的类型存 `EditorPrefs`，重启 Editor 还记得上次选的是哪个。
        /// 创建出来的物体**不进选中状态**：选中新物体会让面板立刻换成物体属性，
        /// 点一下按钮工具就没了 —— 改用 PingObject 闪一下 Hierarchy。
        /// </summary>
        static void DrawActorRow(LDK2DActorKind kind)
        {
            string[] types = LDK2DTestActor.TypesOf(kind);
            string prefKey = "Terrain2D.ActorType." + (int)kind;

            int index = Mathf.Clamp(EditorPrefs.GetInt(prefKey, 0), 0, types.Length - 1);

            GUILayout.BeginHorizontal();

            GUILayout.Label(LDK2DTestActor.LabelOf(kind), EditorStyles.miniLabel, GUILayout.Width(46f));

            EditorGUI.BeginChangeCheck();
            int picked = EditorGUILayout.Popup(index, types, EditorStyles.popup, GUILayout.Width(84f));
            if (EditorGUI.EndChangeCheck()) EditorPrefs.SetInt(prefKey, picked);

            if (GUILayout.Button("创建", EditorStyles.miniButton, GUILayout.Width(48f)))
            {
                Vector3 position = Vector3.zero;

                // 摆在当前 Scene 视图中心。Z 必须手动归零：2D 视图的 pivot.z
                // 实测在 9.8 左右，照抄会把标记甩到相机前面去。
                if (SceneView.lastActiveSceneView != null)
                    position = SceneView.lastActiveSceneView.pivot;

                position.z = 0f;

                var actor = LDK2DTestActor.Create(kind, picked, position);

                // 不能用 return 提前退出 —— 会把下面的 EndHorizontal 跳过。
                if (actor != null)
                {
                    Undo.RegisterCreatedObjectUndo(actor.gameObject, "创建" + actor.DisplayName);
                    EditorGUIUtility.PingObject(actor.gameObject);
                    SceneView.RepaintAll();
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
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
