using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Terrain2D.EditorTools
{
    [CustomEditor(typeof(PolygonMesh2D))]
    public class PolygonMesh2DEditor : UnityEditor.Editor
    {
        static readonly System.Type ComponentType = typeof(PolygonMesh2D);

        public override void OnInspectorGUI()
        {
            var component = (PolygonMesh2D)target;

            serializedObject.Update();

            InspectorL10n.ScriptField(serializedObject);

            InspectorL10n.Section("轮廓来源");
            Field("source", "轮廓来源");
            Field("outline", "轮廓");
            Field("points", "本地顶点列表");

            InspectorL10n.Section("外观");
            Field("color", "颜色");
            Field("applyColor", "应用颜色");
            Field("sortingLayerName", "排序层");
            Field("sortingOrder", "排序顺序");

            InspectorL10n.Section("网格");
            Field("uvScale", "UV 缩放");
            Field("zOffset", "Z 偏移");
            Field("flipWinding", "翻转绕序");

            InspectorL10n.Section("实时更新");
            Field("liveUpdate", "实时更新");

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(10);

            if (GUILayout.Button("按轮廓重建网格", GUILayout.Height(26)))
            {
                Undo.RecordObject(component, "重建多边形网格地形");
                component.Rebuild();
                SceneView.RepaintAll();
            }

            EditorGUILayout.HelpBox(
                "轮廓来自 Polygon Collider 2D —— 它就是你的编辑入口。\n\n" +
                "1. 选中本物体上的 Polygon Collider 2D 组件。\n" +
                "2. 点它 Inspector 里的「Edit Collider」按钮进入编辑状态。\n" +
                "3. 在 Scene 视图里操作：\n" +
                "     拖动锚点 = 移动顶点\n" +
                "     Shift + 点边线 = 在该处插入一个顶点\n" +
                "     Ctrl / Cmd + 点锚点 = 删除该顶点（至少保留 3 个）\n" +
                "4. 四条边都是同一种锚点，没有「哪条边不能编辑」的区别。\n\n" +
                "「实时更新」开着时，改完锚点网格会自动跟随。\n\n" +
                "注意：如果锚点加不上，检查本物体的 Scale 是不是太大 —— " +
                "这是 Unity 碰撞体编辑器的已知问题，把 Scale 设为 1 即可。",
                MessageType.Info);
        }

        void Field(string propertyName, string label)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null) return;

            EditorGUILayout.PropertyField(
                property,
                InspectorL10n.Label(ComponentType, property, label),
                true);
        }
    }

    [CustomEditor(typeof(HeightmapTerrain2D))]
    public class HeightmapTerrain2DEditor : UnityEditor.Editor
    {
        static readonly System.Type ComponentType = typeof(HeightmapTerrain2D);

        // 编辑模式下鼠标到控制点/线段的命中范围（屏幕像素）。
        const float HandlePickRadius = 14f;

        // 左右边界拖动时采样间距的下限，防止把地形压成一条线。
        const float MinSpacing = 0.01f;

        static readonly Color TopColor = new Color(0.20f, 0.55f, 0.95f, 0.90f);
        static readonly Color BottomColor = new Color(0.60f, 0.42f, 0.92f, 0.90f);
        static readonly Color DeleteColor = new Color(0.95f, 0.35f, 0.30f, 0.95f);
        static readonly Color EdgeColor = new Color(0.30f, 0.80f, 0.45f, 0.95f);
        static readonly Color BottomEdgeColor = new Color(0.95f, 0.70f, 0.20f, 0.95f);
        static readonly Color SlantColor = new Color(0.25f, 0.80f, 0.85f, 0.95f);
        static readonly Color EdgePointColor = new Color(0.95f, 0.55f, 0.15f, 0.95f);

        static readonly int DragControlId = "Terrain2D.DragPoint".GetHashCode();

        // ---- 拖动状态 ----
        // 不用 Handles.Slider：它既负责渲染又负责算偏移，在"每帧重建网格"的场景下
        // 反馈不稳定。这里自己管 hotControl，用 HandleUtility.CalcLineTranslation
        // 直接把鼠标位移投影到拖动轴上，得到相对抓取点的绝对偏移 —— 不累积、不自激。
        int _dragKind;              // 0=无 1=地表点 2=底部点 3=左边界 4=右边界 5=底边整体
                                    // 6=左斜边 7=右斜边 8=左边链点 9=右边链点 10=底边折线点
        int _dragIndex = -1;
        Vector2 _dragStartMouse;
        Vector3 _dragStartWorld;
        Vector3 _dragAxis;
        float[] _dragBaseTop;
        float[] _dragBaseBottom;
        float _dragOriginX;
        float _dragSpacing;
        float _dragBottomY;
        float _dragLeftOffsetX;
        float _dragRightOffsetX;

        // 左右边控制点是自由 2D 拖动，用"抓取时的本地偏移"来定位。
        Vector2 _dragGrabLocal;
        Vector2 _dragStartMouseLocal;

        // 拖动地表 / 底部点时，顺便记下横向偏移的基准。
        float[] _dragBaseTopOffsetX;
        float[] _dragBaseBottomOffsetX;

        static string EditKey(int instanceId) => "Terrain2D.EditMode." + instanceId;

        /// <summary>
        /// 编辑模式开关。默认 **开**。
        ///
        /// 注意：这里用的是 SessionState —— 它只在本次 Editor 会话内有效，Unity 一重启
        /// （包括崩溃后重启、切版本、关掉再开）就会全部清空，而 key 又绑在 instanceID 上，
        /// 重启后 instanceID 也变了。如果默认值是 false，重启后编辑模式静默关闭，
        /// Scene 视图里的控制点就全不见了，看起来像"地形坏了"，实际只是这个开关。
        ///
        /// 所以默认给 true。它只控制**画不画控制点** —— 左上角那块阶段面板
        /// 和编辑模式无关，任何时候都在。
        /// </summary>
        static bool IsEditing(HeightmapTerrain2D terrain)
        {
            return SessionState.GetBool(EditKey(terrain.GetInstanceID()), true);
        }

        public override void OnInspectorGUI()
        {
            var terrain = (HeightmapTerrain2D)target;

            serializedObject.Update();

            InspectorL10n.ScriptField(serializedObject);

            InspectorL10n.Section("形状");
            Field("sampleCount", "采样点数量");
            Field("spacing", "采样间距");
            Field("originX", "起始 X");
            Field("useBottomProfile", "底部独立轮廓");
            Field("bottomY", "底部 Y");

            // 这两个只有开了底部轮廓才有意义，平时藏起来免得误导。
            var useBottom = serializedObject.FindProperty("useBottomProfile");
            if (useBottom != null && useBottom.boolValue)
            {
                Field("bottomLeftOffsetX", "底部左端点水平偏移");
                Field("bottomRightOffsetX", "底部右端点水平偏移");
            }

            InspectorL10n.Section("程序化生成");
            Field("generateOnEnable", "启用时自动生成");
            Field("seed", "随机种子");
            Field("baseHeight", "基准高度");
            Field("amplitude", "起伏幅度");
            Field("frequency", "噪声频率");
            Field("octaves", "叠加层数");
            Field("persistence", "幅度衰减");
            Field("lacunarity", "频率放大");
            Field("smoothPasses", "平滑遍数");

            InspectorL10n.Section("外观");
            Field("color", "颜色");
            Field("applyColor", "应用颜色");
            Field("sortingLayerName", "排序层");
            Field("sortingOrder", "排序顺序");
            Field("uvScale", "UV 缩放");
            Field("uvMode", "UV 模式");
            Field("showUVGrid", "显示 UV 网格");
            Field("flipWinding", "翻转绕序");

            InspectorL10n.Section("碰撞");
            Field("buildColliders", "生成碰撞体");
            Field("buildEdgeCollider", "生成边缘碰撞体");
            Field("edgeRadius", "边缘厚度");
            Field("buildPolygonCollider", "生成多边形碰撞体");
            Field("colliderOffsetY", "碰撞体垂直偏移");
            Field("colliderStride", "碰撞体采样间隔");

            InspectorL10n.Section("实时更新");
            Field("rebuildOnValidate", "参数变化时重建");

            InspectorL10n.Section("Scene 视图");
            Field("showSceneHandles", "显示控制点");
            Field("sceneHandleStride", "控制点间隔");
            Field("smoothDrag", "平滑拖动");
            Field("sceneHandleFalloff", "平滑影响范围");
            Field("showBoundaryHandles", "显示边界方块");

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(10);

            DrawEditModeUI(terrain);

            EditorGUILayout.Space(6);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("生成", GUILayout.Height(26)))
                {
                    Undo.RecordObject(terrain, "生成地形");
                    terrain.Generate();
                    MarkDirty(terrain);
                }

                if (GUILayout.Button("拍平", GUILayout.Height(26)))
                {
                    Undo.RecordObject(terrain, "拍平地形");
                    terrain.Flatten();
                    MarkDirty(terrain);
                }

                if (GUILayout.Button("重建", GUILayout.Height(26)))
                {
                    terrain.Rebuild();
                    SceneView.RepaintAll();
                }
            }

            if (!terrain.OutlineValid)
            {
                EditorGUILayout.HelpBox(
                    "轮廓自交了 —— 网格已经消失。\n\n" +
                    "左右边上的控制点拖过头，让轮廓自己交叉了。把点往回收一点就能恢复。",
                    MessageType.Error);
            }

            EditorGUILayout.HelpBox(
                "Heights[] 是地表、BottomHeights[] 是底部轮廓，LeftEdge / RightEdge 是左右两条边，" +
                "它们共同拼成一条闭合轮廓，网格和碰撞体都从它派生。\n\n" +
                "四条边都能加点：进编辑模式，Shift + 点任意一条边即可。\n\n" +
                "运行时：DestroyCircle(本地坐标, 半径) 挖坑，或直接写 " +
                "terrain.Heights[] / terrain.BottomHeights[] 后调用 Rebuild()。",
                MessageType.Info);
        }

        void DrawEditModeUI(HeightmapTerrain2D terrain)
        {
            bool editing = IsEditing(terrain);

            bool toggle = GUILayout.Toggle(
                editing,
                editing ? "退出编辑模式" : "进入编辑模式",
                "Button",
                GUILayout.Height(28));

            if (toggle != editing)
            {
                SessionState.SetBool(EditKey(terrain.GetInstanceID()), toggle);
                SceneView.RepaintAll();
                Repaint();
                editing = toggle;
            }

            if (!editing) return;

            EditorGUILayout.HelpBox(
                "编辑模式已开启（在 Scene 视图里操作）\n\n" +
                "· 拖蓝色圆点 = 改地表高度\n" +
                "· 拖紫色圆点 = 改底部轮廓（需打开「底部独立轮廓」）\n" +
                "· Shift + 点地表线/底线 = 在该处插入一个采样点\n" +
                "· Ctrl / Cmd + 点圆点 = 删除该采样点\n" +
                "· 拖绿色方块 = 改左右边界（水平）\n" +
                "· 拖橙色方块 = 改底边整体高度（垂直）\n\n" +
                "地表采样点 " + terrain.sampleCount + " 个　" +
                "左边 " + terrain.LeftEdge.Count + " 个点　" +
                "右边 " + terrain.RightEdge.Count + " 个点" +
                (terrain.useBottomProfile ? "　底部轮廓已开启" : ""),
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("细分 ×2", GUILayout.Height(24)))
                {
                    Undo.RecordObject(terrain, "细分地形");
                    terrain.Subdivide();
                    MarkDirty(terrain);
                }

                if (GUILayout.Button("简化一半", GUILayout.Height(24)))
                {
                    Undo.RecordObject(terrain, "简化地形");
                    terrain.Simplify();
                    MarkDirty(terrain);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("清空边加点", GUILayout.Height(24)))
                {
                    Undo.RecordObject(terrain, "清空边加点");
                    terrain.ClearEdgePoints();
                    MarkDirty(terrain);
                }

                if (GUILayout.Button("清空横向偏移", GUILayout.Height(24)))
                {
                    Undo.RecordObject(terrain, "清空横向偏移");
                    terrain.ClearXOffsets();
                    MarkDirty(terrain);
                }
            }
        }

        void MarkDirty(HeightmapTerrain2D terrain)
        {
            EditorUtility.SetDirty(terrain);
            Repaint();
            SceneView.RepaintAll();
        }

        void Field(string propertyName, string label)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null) return;

            EditorGUILayout.PropertyField(
                property,
                InspectorL10n.Label(ComponentType, property, label),
                true);
        }

        // ------------------------------------------------------------------ Scene 视图

        void OnSceneGUI()
        {
            var terrain = (HeightmapTerrain2D)target;

            int count = terrain.sampleCount;
            if (count < 2) return;

            bool editing = IsEditing(terrain);

            // 控制点全关的时候也只收控制点，不能把面板一起收走 ——
            // 退出编辑模式是个很平常的动作，面板不该跟着消失。
            if (!editing && !terrain.showSceneHandles && !terrain.showUVGrid)
            {
                DrawStagePanel(terrain);
                return;
            }

            Transform tr = terrain.transform;
            Event e = Event.current;

            // UV 网格和编辑模式无关 —— 它的用处就是在还没有贴图的时候，
            // 让人一眼看出弧长映射对不对。
            if (terrain.showUVGrid) DrawUVGrid(terrain, tr);

            // 拖动进行中：接管 MouseDrag / MouseUp。
            if (_dragKind != 0)
            {
                if (e.type == EventType.MouseDrag)
                {
                    ApplyDrag(terrain, tr, e);
                    e.Use();
                }
                else if (e.type == EventType.MouseUp && e.button == 0)
                {
                    EndDrag();
                    e.Use();
                }
            }

            int stride = editing ? 1 : Mathf.Max(1, terrain.sceneHandleStride);
            bool deleteMode = e.control || e.command;

            float[] top = terrain.Heights;
            float[] bottom = terrain.useBottomProfile ? terrain.BottomHeights : null;

            if (editing)
                DrawOutline(terrain, tr, top, bottom, count);

            // ---- 画控制点和边界方块，同时记录离鼠标最近的 ----
            // 分两套累计：控制点（1/2/8/9）和边界方块（3/4/5/6/7）。
            // 拾取时**控制点永远优先** —— 方块只是辅助手柄，不该挡住点的编辑。
            int pointKind = 0, pointIndex = -1;
            float pointDistance = float.MaxValue;
            Vector3 pointWorld = Vector3.zero;

            int cubeKind = 0, cubeIndex = -1;
            float cubeDistance = float.MaxValue;
            Vector3 cubeWorld = Vector3.zero;

            Color topColor = deleteMode ? DeleteColor : TopColor;
            for (int i = 0; i < count; i += stride)
            {
                Vector3 world = tr.TransformPoint(
                    new Vector3(terrain.TopXAt(i), top[i], 0f));

                float size = HandleUtility.GetHandleSize(world) * (editing ? 0.045f : 0.07f);
                float distance = DrawPoint(world, size, topColor);

                if (distance < pointDistance)
                {
                    pointDistance = distance;
                    pointKind = 1;
                    pointIndex = i;
                    pointWorld = world;
                }
            }

            // 底边的点。有独立折线时画折线上的点（它们和地表无关），
            // 否则画采样点那套的紫色点。
            if (editing && terrain.HasBottomEdge)
            {
                Color chainColor = deleteMode ? DeleteColor : BottomColor;

                var bottomChain = terrain.BottomEdge;
                for (int i = 0; i < bottomChain.Count; i++)
                {
                    Vector3 world = tr.TransformPoint(
                        new Vector3(bottomChain[i].x, bottomChain[i].y, 0f));

                    float size = HandleUtility.GetHandleSize(world) * 0.045f;
                    float distance = DrawPoint(world, size, chainColor);

                    if (distance < pointDistance)
                    {
                        pointDistance = distance;
                        pointKind = 10;
                        pointIndex = i;
                        pointWorld = world;
                    }
                }
            }
            else if (editing && bottom != null)
            {
                Color bottomColor = deleteMode ? DeleteColor : BottomColor;

                for (int i = 0; i < count; i += stride)
                {
                    // 底部 X 可能相对地表有偏移（斜边），所以用 BottomXAt。
                    Vector3 world = tr.TransformPoint(
                        new Vector3(terrain.BottomXAt(i), bottom[i], 0f));

                    float size = HandleUtility.GetHandleSize(world) * 0.045f;
                    float distance = DrawPoint(world, size, bottomColor);

                    if (distance < pointDistance)
                    {
                        pointDistance = distance;
                        pointKind = 2;
                        pointIndex = i;
                        pointWorld = world;
                    }
                }
            }

            // ---- 左右两条边上的控制点 ----
            if (editing)
            {
                Color edgePointColor = deleteMode ? DeleteColor : EdgePointColor;

                for (int s = 0; s < 2; s++)
                {
                    bool left = s == 0;
                    var chain = left ? terrain.LeftEdge : terrain.RightEdge;

                    for (int i = 0; i < chain.Count; i++)
                    {
                        Vector3 world = tr.TransformPoint(new Vector3(chain[i].x, chain[i].y, 0f));
                        float size = HandleUtility.GetHandleSize(world) * 0.045f;
                        float distance = DrawPoint(world, size, edgePointColor);

                        if (distance < pointDistance)
                        {
                            pointDistance = distance;
                            pointKind = left ? 8 : 9;
                            pointIndex = i;
                            pointWorld = world;
                        }
                    }
                }
            }

            // ---- 边界方块（绿 / 青 / 橙）----
            if (editing && !deleteMode && terrain.showBoundaryHandles)
            {
                int last = count - 1;
                float midX = terrain.originX + last * terrain.spacing * 0.5f;

                // 方块位置**跟着边上的控制点走**：取这条边折线的实际中点，
                // 而不是两端点的直线中点 —— 否则加点之后方块会飘到形状内部去。
                var side = new List<Vector2>();

                BuildSidePolyline(terrain, true, side);
                Vector2 leftMid = PolylineMidpoint(side);

                BuildSidePolyline(terrain, false, side);
                Vector2 rightMid = PolylineMidpoint(side);

                Vector3 leftWorld = tr.TransformPoint(new Vector3(leftMid.x, leftMid.y, 0f));
                Vector3 rightWorld = tr.TransformPoint(new Vector3(rightMid.x, rightMid.y, 0f));

                float distance = DrawCube(leftWorld, HandleUtility.GetHandleSize(leftWorld) * 0.09f, EdgeColor);
                if (distance < cubeDistance)
                {
                    cubeDistance = distance; cubeKind = 3; cubeIndex = -1; cubeWorld = leftWorld;
                }

                distance = DrawCube(rightWorld, HandleUtility.GetHandleSize(rightWorld) * 0.09f, EdgeColor);
                if (distance < cubeDistance)
                {
                    cubeDistance = distance; cubeKind = 4; cubeIndex = -1; cubeWorld = rightWorld;
                }

                if (!terrain.useBottomProfile)
                {
                    // 底边平直时，用一个方块整体上下移。
                    Vector3 bottomWorld = tr.TransformPoint(new Vector3(midX, terrain.bottomY, 0f));
                    distance = DrawCube(bottomWorld, HandleUtility.GetHandleSize(bottomWorld) * 0.09f, BottomEdgeColor);
                    if (distance < cubeDistance)
                    {
                        cubeDistance = distance; cubeKind = 5; cubeIndex = -1; cubeWorld = bottomWorld;
                    }
                }
                else
                {
                    // 开了底部轮廓后底边逐点编辑；这两个青色方块专门负责把左右边拖成斜的。
                    Vector3 leftCorner = tr.TransformPoint(
                        new Vector3(terrain.BottomXAt(0), terrain.BottomAt(0), 0f));
                    Vector3 rightCorner = tr.TransformPoint(
                        new Vector3(terrain.BottomXAt(last), terrain.BottomAt(last), 0f));

                    Vector3 leftSlant = leftCorner - tr.up * (HandleUtility.GetHandleSize(leftCorner) * 0.32f);
                    Vector3 rightSlant = rightCorner - tr.up * (HandleUtility.GetHandleSize(rightCorner) * 0.32f);

                    distance = DrawCube(leftSlant, HandleUtility.GetHandleSize(leftSlant) * 0.09f, SlantColor);
                    if (distance < cubeDistance)
                    {
                        cubeDistance = distance; cubeKind = 6; cubeIndex = -1; cubeWorld = leftSlant;
                    }

                    distance = DrawCube(rightSlant, HandleUtility.GetHandleSize(rightSlant) * 0.09f, SlantColor);
                    if (distance < cubeDistance)
                    {
                        cubeDistance = distance; cubeKind = 7; cubeIndex = -1; cubeWorld = rightSlant;
                    }
                }
            }

            // ---- 判定这次点到的是谁：控制点优先于方块 ----
            int pickKind = 0, pickIndex = -1;
            float pickDistance = float.MaxValue;
            Vector3 pickWorld = Vector3.zero;

            if (pointKind != 0 && pointDistance <= HandlePickRadius)
            {
                pickKind = pointKind;
                pickIndex = pointIndex;
                pickDistance = pointDistance;
                pickWorld = pointWorld;
            }
            else if (cubeKind != 0 && cubeDistance <= HandlePickRadius)
            {
                pickKind = cubeKind;
                pickIndex = cubeIndex;
                pickDistance = cubeDistance;
                pickWorld = cubeWorld;
            }

            // ---- 鼠标按下 ----
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt
                && _dragKind == 0 && GUIUtility.hotControl == 0)
            {
                if (deleteMode)
                {
                    if (pickDistance > HandlePickRadius)
                    {
                        // 没点到东西
                    }
                    else if (pickKind == 1 || pickKind == 2)
                    {
                        // 两端点是地形范围的锚，不允许删。
                        if (pickIndex > 0 && pickIndex < count - 1)
                        {
                            Undo.RecordObject(terrain, "删除地形采样点");
                            terrain.RemoveSample(pickIndex);
                            MarkDirty(terrain);
                            e.Use();
                        }
                    }
                    else if (pickKind == 8 || pickKind == 9)
                    {
                        Undo.RecordObject(terrain, "删除边界控制点");
                        terrain.RemoveEdgePoint(pickKind == 8, pickIndex);
                        MarkDirty(terrain);
                        e.Use();
                    }
                    else if (pickKind == 10)
                    {
                        Undo.RecordObject(terrain, "删除底边控制点");
                        terrain.RemoveBottomPoint(pickIndex);
                        MarkDirty(terrain);
                        e.Use();
                    }
                }
                else if (e.shift)
                {
                    if (editing) HandleInsert(terrain, tr, top, bottom, count, e);
                }
                else if (pickKind != 0 && pickDistance <= HandlePickRadius)
                {
                    BeginDrag(terrain, tr, pickKind, pickIndex, pickWorld, e);
                    e.Use();
                }
            }

            // 走到这里说明控制点至少画了一种，面板无条件画 —— 上面那个分支已经把
            // "什么都不画"的情况提前返回了。
            DrawStagePanel(terrain);
        }

        /// <summary>画一个可拖动的圆点，返回它到鼠标的屏幕距离。</summary>
        float DrawPoint(Vector3 world, float size, Color color)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            Vector2 gui = HandleUtility.WorldToGUIPoint(world);
            float distance = Vector2.Distance(gui, Event.current.mousePosition);

            HandleUtility.AddControl(id, distance);
            Handles.color = color;
            Handles.SphereHandleCap(id, world, Quaternion.identity, size, Event.current.type);

            return distance;
        }

        /// <summary>画一个可拖动的方块，返回它到鼠标的屏幕距离。</summary>
        float DrawCube(Vector3 world, float size, Color color)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            Vector2 gui = HandleUtility.WorldToGUIPoint(world);
            float distance = Vector2.Distance(gui, Event.current.mousePosition);

            HandleUtility.AddControl(id, distance);
            Handles.color = color;
            Handles.CubeHandleCap(id, world, Quaternion.identity, size, Event.current.type);

            return distance;
        }

        /// <summary>画网格线用的复用缓冲，避免每次重绘都 new 一个数组。</summary>
        static readonly Vector3[] _gridLine = new Vector3[2];

        /// <summary>
        /// 沿地表画一排等距刻度，间距 = 1/uvScale 个世界单位 —— 也就是贴图接缝的位置。
        ///
        /// 没有贴图的时候，这是唯一能验证弧长映射的办法：
        /// 平地上等距、**斜坡上沿着坡面看也仍然等距**，说明映射是对的；
        /// 如果斜坡上的刻度被拉稀或被挤密，说明 U 还在按世界 X 铺，不是按弧长。
        /// </summary>
        void DrawUVGrid(HeightmapTerrain2D terrain, Transform tr)
        {
            float total = terrain.SurfaceLength;
            float step = terrain.uvScale > 1e-5f ? 1f / terrain.uvScale : 1f;
            if (total <= 1e-5f || step <= 1e-5f) return;

            // 上限防止 uvScale 填得极小时画出上万条线把视口拖死。
            int count = Mathf.Min(2000, Mathf.FloorToInt(total / step) + 1);

            Handles.color = new Color(0.35f, 0.78f, 1f, 0.8f);

            for (int k = 0; k <= count; k++)
            {
                int index = IndexAtArcLength(terrain, k * step);

                float x = terrain.TopXAt(index);
                float y = terrain.Heights[index];

                // 只往下画一小段。画满整个厚度会把轮廓和控制点全挡住。
                float depth = Mathf.Min(0.8f, Mathf.Abs(y - terrain.BottomHeightAt(x)));

                _gridLine[0] = tr.TransformPoint(new Vector3(x, y, 0f));
                _gridLine[1] = tr.TransformPoint(new Vector3(x, y - depth, 0f));

                Handles.DrawAAPolyLine(1.5f, _gridLine);
            }
        }

        /// <summary>按弧长反查采样点下标。弧长是单调累加的，二分就行。</summary>
        static int IndexAtArcLength(HeightmapTerrain2D terrain, float distance)
        {
            int lo = 0;
            int hi = terrain.sampleCount - 1;

            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (terrain.ArcLengthAt(mid) <= distance) lo = mid;
                else hi = mid;
            }

            return lo;
        }

        void DrawOutline(HeightmapTerrain2D terrain, Transform tr, float[] top, float[] bottom, int count)
        {
            // 画完整闭合轮廓 —— 四条边都在里面，所以左右边加点后也看得到。
            var outline = new List<Vector2>();
            terrain.BuildOutline(outline);

            var points = new Vector3[outline.Count];
            for (int i = 0; i < outline.Count; i++)
                points[i] = tr.TransformPoint(new Vector3(outline[i].x, outline[i].y, 0f));

            Handles.color = new Color(1f, 0.45f, 0.10f, 0.95f);
            Handles.DrawAAPolyLine(4f, points);
            Handles.DrawAAPolyLine(4f, new[] { points[points.Length - 1], points[0] });

            // 底部轮廓再描一层紫色，方便区分哪边是"地"。
            if (bottom != null)
            {
                var bottomPoints = new Vector3[count];
                for (int i = 0; i < count; i++)
                {
                    bottomPoints[i] = tr.TransformPoint(new Vector3(
                        terrain.BottomXAt(i), bottom[i], 0f));
                }

                Handles.color = new Color(0.75f, 0.45f, 1f, 0.85f);
                Handles.DrawAAPolyLine(3f, bottomPoints);
            }
        }

        static GUIStyle _warnStyle;
        static GUIStyle _hintStyle;

        // ------------------------------------------------------------------ 阶段

        /// <summary>顺序必须和 HeightmapTerrain2D.UVMode 一致。</summary>
        static readonly string[] UvModeLabels = { "平面映射", "沿曲面平铺" };

        // ------------------------------------------------------------------ 阶段面板

        const string PanelOpenKey = "Terrain2D.PanelOpen";

        /// <summary>
        /// 折叠状态是**全局**的：面板跟着阶段走，不跟着物体走。
        /// 以前按 instanceId 存在 SessionState 里，换一块地形选就换一种折叠状态，很怪。
        /// </summary>
        static bool PanelOpen
        {
            get { return SessionState.GetBool(PanelOpenKey, true); }
            set { SessionState.SetBool(PanelOpenKey, value); }
        }

        static GUIStyle _noteStyle;

        /// <summary>
        /// 左上角的浮动面板，显示**当前阶段**的工具。
        ///
        /// 阶段选择器**不在**这里 —— 它在视口左下角那条固定栏上，由
        /// SortingOrderToolbar 画。分开的理由是两者的生命周期不同：
        /// 阶段是全局状态，一直在；这里的控件只对"选中的这块地形"有意义，
        /// 换掉选择就该换掉内容。
        /// </summary>
        void DrawStagePanel(HeightmapTerrain2D terrain)
        {
            var view = SceneView.currentDrawingSceneView;
            if (view == null) return;

            Rect rect = SceneToolbarLayout.Panel(view.position, SceneToolbarLayout.PanelWidth);

            Handles.BeginGUI();
            GUILayout.BeginArea(rect);

            GUILayout.BeginVertical("box");

            bool open = PanelOpen;

            GUILayout.BeginHorizontal();

            if (GUILayout.Button(open ? "▾" : "▸", EditorStyles.miniButton, GUILayout.Width(18f)))
            {
                open = !open;
                PanelOpen = open;
                SceneView.RepaintAll();
            }

            // 阶段名必须写在面板里：选择器在视口另一端，收起来之后光看内容
            // 分不清现在是哪个阶段。
            GUILayout.Label(TerrainStage.Labels[(int)TerrainStage.Current],
                            EditorStyles.miniBoldLabel);

            GUILayout.FlexibleSpace();

            GUILayout.Label(StageSummary(terrain), EditorStyles.miniLabel);

            GUILayout.EndHorizontal();

            if (open)
            {
                // 轮廓自交的警告贴在最上面 —— 正在拖点的人一眼就能看到。
                if (!terrain.OutlineValid) DrawOutlineWarning();

                switch (TerrainStage.Current)
                {
                    case WorkStage.Test:
                        DrawTestRows(terrain);
                        break;

                    case WorkStage.Asset:
                        DrawAssetRows(terrain);
                        break;

                    default:
                        DrawBlockoutRows(terrain);
                        break;
                }
            }

            GUILayout.EndVertical();
            GUILayout.EndArea();
            Handles.EndGUI();
        }

        /// <summary>窄面板里放得下的换行小字。miniLabel 默认不换行，长句会被直接裁掉。</summary>
        static GUIStyle NoteStyle
        {
            get
            {
                if (_noteStyle == null)
                {
                    _noteStyle = new GUIStyle(EditorStyles.miniLabel);
                    _noteStyle.wordWrap = true;
                }

                return _noteStyle;
            }
        }

        void DrawOutlineWarning()
        {
            if (_warnStyle == null)
            {
                _warnStyle = new GUIStyle(EditorStyles.boldLabel);
                _warnStyle.normal.textColor = new Color(1f, 0.48f, 0.38f);
                _warnStyle.wordWrap = true;
            }

            GUILayout.BeginVertical("box");
            GUILayout.Label("轮廓自交 —— 网格已停止更新", _warnStyle);
            GUILayout.Label("把越界的控制点拖回来即可恢复，或点 Inspector 里的「清空边加点」。",
                            NoteStyle);
            GUILayout.EndVertical();
        }

        /// <summary>标题行右侧的关键数字。窄面板放不下长句子，只留一个数。</summary>
        string StageSummary(HeightmapTerrain2D terrain)
        {
            switch (TerrainStage.Current)
            {
                case WorkStage.Test:
                {
                    // 场景里有几个测试角色是这里最该先看到的数 —— 复制粘贴测试角色之后
                    // 经常会忘掉多出来的那几个，然后"怎么镜头跟的是另一个"。
                    var rig = FindTestCamera();
                    TestCameraMode mode = rig != null ? rig.mode : TestCameraPrefs.Mode;

                    return "角色 " + TestPlayerCount +
                           "　" + TestCameraPrefs.Labels[(int)mode];
                }

                case WorkStage.Asset:
                {
                    var filter = terrain.GetComponent<MeshFilter>();
                    var mesh = filter != null ? filter.sharedMesh : null;

                    if (mesh == null) return "未生成";

                    // 用 vertexCount 而不是 mesh.triangles —— 后者每次都拷一份新数组，
                    // 而这一行每次重绘都会跑到。
                    return mesh.vertexCount + " 顶点";
                }

                default:
                    return "地表 " + terrain.sampleCount;
            }
        }

        // ------------------------------------------------------------------ 阶段一：搭建

        void DrawBlockoutRows(HeightmapTerrain2D terrain)
        {
            // ---- 外观 ----
            GUILayout.BeginHorizontal();

            GUILayout.Label("颜色", EditorStyles.miniLabel);

            EditorGUI.BeginChangeCheck();
            Color picked = EditorGUILayout.ColorField(
                GUIContent.none, terrain.color, true, true, false, GUILayout.Width(64f));

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(terrain, "改地形颜色");
                terrain.color = picked;
                terrain.ApplyVisuals();
                EditorUtility.SetDirty(terrain);
                Repaint();
            }

            // 和「任意 2D 物体」那条栏用同一个调色板。
            if (GUILayout.Button("▾", EditorStyles.miniButton, GUILayout.Width(18f)))
            {
                ColorPalette.ShowMenu(terrain.color, chosen =>
                {
                    Undo.RecordObject(terrain, "改地形颜色");
                    terrain.color = chosen;
                    terrain.ApplyVisuals();
                    EditorUtility.SetDirty(terrain);
                    SceneView.RepaintAll();
                });
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // ---- 编辑辅助 ----
            GUILayout.BeginHorizontal();

            bool boundary = GUILayout.Toggle(
                terrain.showBoundaryHandles, "边界方块",
                EditorStyles.miniButton, GUILayout.Width(64f));

            if (boundary != terrain.showBoundaryHandles)
            {
                Undo.RecordObject(terrain, "切换边界方块");
                terrain.showBoundaryHandles = boundary;
                EditorUtility.SetDirty(terrain);
                Repaint();
                SceneView.RepaintAll();
            }

            bool smooth = GUILayout.Toggle(
                terrain.smoothDrag, "平滑拖动",
                EditorStyles.miniButton, GUILayout.Width(64f));

            if (smooth != terrain.smoothDrag)
            {
                Undo.RecordObject(terrain, "切换平滑拖动");
                terrain.smoothDrag = smooth;
                EditorUtility.SetDirty(terrain);
                Repaint();
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // ---- 分层 ----
            DrawLayerRow(terrain);

            // ---- 操作按钮（和 Inspector 里那排一样）----
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("生成", EditorStyles.miniButton, GUILayout.Width(46f)))
                RunAction(terrain, "生成地形", terrain.Generate);

            if (GUILayout.Button("拍平", EditorStyles.miniButton, GUILayout.Width(46f)))
                RunAction(terrain, "拍平地形", terrain.Flatten);

            if (GUILayout.Button("重建", EditorStyles.miniButton, GUILayout.Width(46f)))
                RunAction(terrain, "重建地形", terrain.Rebuild);

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("细分 ×2", EditorStyles.miniButton, GUILayout.Width(62f)))
                RunAction(terrain, "细分地形", terrain.Subdivide);

            if (GUILayout.Button("简化一半", EditorStyles.miniButton, GUILayout.Width(62f)))
                RunAction(terrain, "简化地形", terrain.Simplify);

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("清空边加点", EditorStyles.miniButton, GUILayout.Width(78f)))
                RunAction(terrain, "清空边加点", terrain.ClearEdgePoints);

            if (GUILayout.Button("清空横向", EditorStyles.miniButton, GUILayout.Width(66f)))
                RunAction(terrain, "清空横向偏移", terrain.ClearXOffsets);

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // ---- 颜色图例 ----
            GUILayout.BeginHorizontal();

            Chip(TopColor, "地表");
            Chip(BottomColor, "底部");
            Chip(EdgePointColor, "边点");

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            Chip(EdgeColor, "移边界");
            Chip(SlantColor, "边变斜");
            if (terrain.useBottomProfile) Chip(SlantColor, "底边逐点");
            else Chip(BottomEdgeColor, "底边整体");

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Label("拖圆点改地表　Shift+点边加点　Ctrl/Cmd+点圆点删点", NoteStyle);
        }

        /// <summary>
        /// 层级 / Z 这一行。只在搭建阶段出现 —— 分层是灰盒阶段就该定下来的事，
        /// 到了测试和资产替换阶段再改层级，说明前面的规范没定住。
        /// </summary>
        void DrawLayerRow(HeightmapTerrain2D terrain)
        {
            GUILayout.BeginHorizontal();

            // 就是 sortingOrder。同层内数值大的画在上面。
            GUILayout.Label("层级", EditorStyles.miniLabel);

            EditorGUI.BeginChangeCheck();
            int sortingOrder = EditorGUILayout.IntField(terrain.sortingOrder, GUILayout.Width(44f));

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(terrain, "改地形层级");
                terrain.sortingOrder = sortingOrder;
                terrain.ApplyVisuals();
                EditorUtility.SetDirty(terrain);
                SceneView.RepaintAll();
            }

            GUILayout.Space(8f);

            // Z：前景 / 背景分层用。层级相同时 Unity 就是靠 Z 决定谁在前，
            // 所以它和 sortingOrder 是一套东西的两半。
            GUILayout.Label("Z", EditorStyles.miniLabel);

            EditorGUI.BeginChangeCheck();
            float depth = EditorGUILayout.FloatField(
                terrain.transform.position.z, GUILayout.Width(48f));

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(terrain.transform, "改地形 Z");
                Vector3 position = terrain.transform.position;
                position.z = depth;
                terrain.transform.position = position;
                EditorUtility.SetDirty(terrain.transform);
                SceneView.RepaintAll();
            }

            // 和「任意 2D 物体」那条栏用同一套 Z 预设。
            if (GUILayout.Button("▾", EditorStyles.miniButton, GUILayout.Width(18f)))
            {
                DepthPresets.ShowMenu(terrain.transform.position.z, chosen =>
                {
                    Undo.RecordObject(terrain.transform, "改地形 Z");
                    Vector3 position = terrain.transform.position;
                    position.z = chosen;
                    terrain.transform.position = position;
                    EditorUtility.SetDirty(terrain.transform);
                    SceneView.RepaintAll();
                });
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            DrawColliderToggle(terrain);

            GUILayout.FlexibleSpace();

            GUILayout.Label("层级大在上　Z 小在前", EditorStyles.miniLabel);

            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// 碰撞体总开关。搭建阶段和测试阶段都要有 —— 搭的时候用来临时关掉碰撞专心改形状，
        /// 测的时候用来确认"掉下去是不是因为碰撞压根没生成"。
        /// 抽成一份两边共用，免得语义漂移。
        /// </summary>
        void DrawColliderToggle(HeightmapTerrain2D terrain)
        {
            // 和「任意 2D 物体」那条工具栏语义对齐：
            // 取消勾选 = 不再自动生成 + 已有碰撞体全部禁用。
            bool collision = GUILayout.Toggle(
                terrain.buildColliders, "碰撞体", EditorStyles.miniButton, GUILayout.Width(64f));

            if (collision == terrain.buildColliders) return;

            // 碰撞体的 enabled 也要能撤销，所以地形和它的碰撞体一起记录。
            var colliders = terrain.GetComponents<Collider2D>();
            var targets = new UnityEngine.Object[colliders.Length + 1];
            targets[0] = terrain;
            for (int i = 0; i < colliders.Length; i++) targets[i + 1] = colliders[i];

            Undo.RecordObjects(targets, collision ? "启用地形碰撞" : "关闭地形碰撞");
            terrain.SetCollisionEnabled(collision);
            EditorUtility.SetDirty(terrain);
            SceneView.RepaintAll();
        }

        // ------------------------------------------------------------------ 阶段二：测试

        // ------------------------------------------------------------------ 阶段二：测试

        TestPlayer2D _testPlayer;

        /// <summary>
        /// 找场景里的测试角色。Unity 的"假 null"在这里刚好帮了忙：
        /// 物体被删掉之后这个引用会等于 null，下次自动重新找一遍，不用手动清缓存。
        /// </summary>
        TestPlayer2D FindTestPlayer()
        {
            if (_testPlayer == null)
                _testPlayer = UnityEngine.Object.FindObjectOfType<TestPlayer2D>(true);

            return _testPlayer;
        }

        /// <summary>
        /// 场景里一共几个测试角色。缓存起来是因为标题行每次重绘都要读，
        /// 而 FindObjectsOfType 每次都会分配一个数组 —— 拖控制点的时候重绘很频繁。
        /// 增删物体时由 <see cref="OnHierarchyChanged"/> 清掉。
        /// </summary>
        static int _testPlayerCount = -1;

        static int TestPlayerCount
        {
            get
            {
                if (_testPlayerCount < 0)
                    _testPlayerCount = UnityEngine.Object
                        .FindObjectsOfType<TestPlayer2D>(true).Length;

                return _testPlayerCount;
            }
        }

        static TestCamera2D _testCamera;

        static TestCamera2D FindTestCamera()
        {
            if (_testCamera == null)
                _testCamera = UnityEngine.Object.FindObjectOfType<TestCamera2D>();

            return _testCamera;
        }

        static HeightmapTerrain2DEditor()
        {
            // 只在"场景里的物体变了"时清缓存。挂在 static 构造里而不是
            // [InitializeOnLoad]，是因为这些缓存只有地形面板会读 —— 没选地形时
            // 没必要为它跑一遍查找。
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
        }

        static void OnHierarchyChanged()
        {
            _testPlayerCount = -1;
            _testCamera = null;
        }

        void DrawTestRows(HeightmapTerrain2D terrain)
        {
            var player = FindTestPlayer();

            // ---- 摄像机 ----
            //
            // 放在角色上面：角色和镜头是一套测试装备，而且镜头的模式是**创建时**用到的。
            // 先建角色再选模式的话，按下 Play 的头两秒人是跑出画面的。
            DrawCameraRow();

            // ---- 角色 ----
            GUILayout.BeginHorizontal();

            if (GUILayout.Button(player != null ? "选中角色" : "创建测试角色",
                                 EditorStyles.miniButton, GUILayout.Width(88f)))
            {
                if (player != null)
                {
                    Selection.activeGameObject = player.gameObject;
                    EditorGUIUtility.PingObject(player.gameObject);
                }
                else
                {
                    // 创建后立刻摆到地形左端 —— 刚造出来放在视口中心的话多半悬在半空，
                    // 还得自己拖过去。
                    //
                    // select 传 false：选中新物体会让这块地形的面板立刻消失
                    // （OnSceneGUI 只画在选中物体上），点一下按钮工具没了很突兀。
                    // 改用 PingObject 闪一下 Hierarchy，位置留在原地。
                    var created = Terrain2DMenuItems.BuildTestPlayer(null, false);
                    MovePlayerToStart(terrain, created);

                    // 角色和镜头是一套测试装备，创建角色就把镜头一起配好。
                    Terrain2DMenuItems.EnsureTestCamera(TestCameraPrefs.Mode, created.transform);

                    _testPlayer = created;
                    player = created;

                    EditorGUIUtility.PingObject(created.gameObject);
                }
            }

            using (new EditorGUI.DisabledScope(player == null))
            {
                if (GUILayout.Button("放到左端", EditorStyles.miniButton, GUILayout.Width(64f)))
                {
                    MovePlayerToStart(terrain, player);

                    // 人瞬移了，镜头再慢慢飘过去会有一秒钟什么都看不见。
                    var rig = FindTestCamera();
                    if (rig != null) rig.SnapToTarget();
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // ---- 碰撞体 ----
            GUILayout.BeginHorizontal();

            DrawColliderToggle(terrain);

            GUILayout.FlexibleSpace();

            GUILayout.Label("按 Play 试跳", EditorStyles.miniLabel);

            GUILayout.EndHorizontal();

            // ---- 手感参数：直接写 TestPlayer2D 的字段，Play 中拖动立刻生效 ----
            if (player != null)
            {
                DrawFloatSlider("跳跃高度", player.jumpHeight, 0.5f, 8f, "0.00", value =>
                {
                    Undo.RecordObject(player, "改跳跃高度");
                    player.jumpHeight = value;
                    EditorUtility.SetDirty(player);
                });

                DrawFloatSlider("移动速度", player.moveSpeed, 1f, 20f, "0.0", value =>
                {
                    Undo.RecordObject(player, "改移动速度");
                    player.moveSpeed = value;
                    EditorUtility.SetDirty(player);
                });

                DrawFloatSlider("空中操控", player.airControl, 0f, 1f, "0.00", value =>
                {
                    Undo.RecordObject(player, "改空中操控");
                    player.airControl = value;
                    EditorUtility.SetDirty(player);
                });
            }
            else
            {
                GUILayout.Label(
                    "场景里还没有测试角色。点「创建测试角色」，它自带占位方块，不需要美术资源。",
                    NoteStyle);
            }

            // ---- 碰撞精度 ----
            GUILayout.BeginHorizontal();

            GUILayout.Label("碰撞偏移", EditorStyles.miniLabel);

            EditorGUI.BeginChangeCheck();
            float offsetY = EditorGUILayout.FloatField(terrain.colliderOffsetY, GUILayout.Width(52f));

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(terrain, "改碰撞体偏移");
                terrain.colliderOffsetY = offsetY;
                terrain.Rebuild();
                MarkDirty(terrain);
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            GUILayout.Label("采样间隔", EditorStyles.miniLabel);

            EditorGUI.BeginChangeCheck();
            int stride = EditorGUILayout.IntSlider(
                terrain.colliderStride, 1, 16, GUILayout.Width(96f));

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(terrain, "改碰撞体采样间隔");
                terrain.colliderStride = stride;
                terrain.Rebuild();
                MarkDirty(terrain);
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Label("碰撞边缘和视觉边缘对不上时调「碰撞偏移」", NoteStyle);
        }

        /// <summary>
        /// 摄像机那一行：模式下拉 + 选中按钮。
        ///
        /// 还没有摄像机时下拉照样能改 —— 改的是"创建时用哪个模式"，
        /// 存在 EditorPrefs 里，所以默认值「跟随」在重启后仍然成立。
        /// </summary>
        void DrawCameraRow()
        {
            var rig = FindTestCamera();

            GUILayout.BeginHorizontal();

            GUILayout.Label("摄像机", EditorStyles.miniLabel);

            TestCameraMode current = rig != null ? rig.mode : TestCameraPrefs.Mode;

            EditorGUI.BeginChangeCheck();
            int picked = EditorGUILayout.Popup(
                (int)current, TestCameraPrefs.Labels, EditorStyles.popup, GUILayout.Width(78f));
            bool changed = EditorGUI.EndChangeCheck();

            if (changed)
            {
                var next = (TestCameraMode)picked;
                TestCameraPrefs.Mode = next;

                if (rig != null)
                {
                    Undo.RecordObject(rig, "改摄像机模式");
                    rig.SetMode(next);
                    EditorUtility.SetDirty(rig);
                }

                SceneView.RepaintAll();
            }

            using (new EditorGUI.DisabledScope(rig == null))
            {
                if (GUILayout.Button("选中", EditorStyles.miniButton, GUILayout.Width(42f)))
                {
                    Selection.activeGameObject = rig.gameObject;
                    EditorGUIUtility.PingObject(rig.gameObject);
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (rig == null)
            {
                GUILayout.Label("还没有测试摄像机，创建测试角色时会一起建。", NoteStyle);
                return;
            }

            // 固定视口没有"跟上的快慢"可言，藏起来免得让人以为它没用。
            if (rig.mode != TestCameraMode.Fixed)
            {
                DrawFloatSlider("跟随快慢", rig.smoothTime, 0f, 0.6f, "0.00", value =>
                {
                    Undo.RecordObject(rig, "改摄像机跟随");
                    rig.smoothTime = value;
                    EditorUtility.SetDirty(rig);
                });
            }
        }

        /// <summary>
        /// 一行「标签 + 滑块 + 数值」。窄面板里每个参数占一行，比三个并排好读。
        /// 值变了才回调，否则每帧都会 Undo.RecordObject。
        /// </summary>
        void DrawFloatSlider(string label, float value, float min, float max,
                             string format, System.Action<float> apply)
        {
            GUILayout.BeginHorizontal();

            GUILayout.Label(label, EditorStyles.miniLabel);

            float picked = Slider(value, min, max, 110f, format);

            if (!Mathf.Approximately(picked, value)) apply(picked);

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        void MovePlayerToStart(HeightmapTerrain2D terrain, TestPlayer2D player)
        {
            if (player == null) return;

            // 起点取地表最左端的采样点，抬高半米免得生成在地面里。
            //
            // SampleWorldPoint 返回的是 Vector2，**不能**直接和 Vector3 相加：
            // Unity 给 Vector2 / Vector3 双向都定义了隐式转换，编译器不知道该把
            // 左边升成 Vector3 还是把右边压成 Vector2，于是报 CS0034 ambiguous。
            // 拆成显式构造就没有歧义了。Z 跟着地形走，免得把人扔到 z=0 的平面上。
            Vector2 ground = terrain.SampleWorldPoint(terrain.TopXAt(0));

            Vector3 target = new Vector3(
                ground.x,
                ground.y + 0.6f,
                terrain.transform.position.z);

            var body = player.GetComponent<Rigidbody2D>();

            Undo.RecordObject(player.transform, "移动测试角色");
            if (body != null) Undo.RecordObject(body, "移动测试角色");

            player.transform.position = target;

            if (body != null)
            {
                // Play 模式下光改 transform 会被物理步进覆盖，必须同时告诉刚体。
                // Rigidbody2D.position 是 Vector2，只动 x/y，地形在别的 Z 上也不会被拉回 0。
                body.position = target;

                // 速度要一起清零，否则"放回起点"会带着旧速度飞出去。
                body.velocity = Vector2.zero;
            }

            EditorUtility.SetDirty(player.transform);
            SceneView.RepaintAll();
        }

        // ------------------------------------------------------------------ 阶段三：资产替换

        void DrawAssetRows(HeightmapTerrain2D terrain)
        {
            // ---- UV 模式 ----
            GUILayout.BeginHorizontal();

            GUILayout.Label("UV", EditorStyles.miniLabel);

            int mode = GUILayout.Toolbar(
                (int)terrain.uvMode, UvModeLabels, EditorStyles.miniButton,
                GUILayout.Width(150f), GUILayout.Height(18f));

            if (mode != (int)terrain.uvMode)
            {
                Undo.RecordObject(terrain, "改 UV 模式");
                terrain.uvMode = (HeightmapTerrain2D.UVMode)mode;
                terrain.Rebuild();
                MarkDirty(terrain);
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // ---- 缩放与网格 ----
            GUILayout.BeginHorizontal();

            GUILayout.Label("缩放", EditorStyles.miniLabel);

            EditorGUI.BeginChangeCheck();
            float uvScale = EditorGUILayout.FloatField(terrain.uvScale, GUILayout.Width(52f));

            if (EditorGUI.EndChangeCheck() && uvScale > 1e-4f)
            {
                Undo.RecordObject(terrain, "改 UV 缩放");
                terrain.uvScale = uvScale;
                terrain.Rebuild();
                MarkDirty(terrain);
            }

            bool grid = GUILayout.Toggle(
                terrain.showUVGrid, "显示 UV 网格", EditorStyles.miniButton, GUILayout.Width(92f));

            if (grid != terrain.showUVGrid)
            {
                Undo.RecordObject(terrain, "切换 UV 网格");
                terrain.showUVGrid = grid;
                EditorUtility.SetDirty(terrain);
                Repaint();
                SceneView.RepaintAll();
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // ---- 读数 ----
            float repeat = terrain.SurfaceLength * terrain.uvScale;

            GUILayout.Label("地表总长 " + terrain.SurfaceLength.ToString("0.0") +
                            "　重复 " + repeat.ToString("0.0") + " 次", EditorStyles.miniLabel);

            GUILayout.Label(
                terrain.uvMode == HeightmapTerrain2D.UVMode.Planar
                    ? "平面映射在斜坡上会把贴图拉长（1/cos 坡角）。"
                    : "弧长映射，斜坡上密度不变。",
                NoteStyle);

            // ---- 材质 ----
            GUILayout.BeginHorizontal();

            bool apply = GUILayout.Toggle(
                terrain.applyColor, "应用颜色", EditorStyles.miniButton, GUILayout.Width(72f));

            if (apply != terrain.applyColor)
            {
                Undo.RecordObject(terrain, "切换应用颜色");
                terrain.applyColor = apply;
                terrain.ApplyVisuals();
                MarkDirty(terrain);
            }

            var renderer = terrain.GetComponent<MeshRenderer>();
            var material = renderer != null ? renderer.sharedMaterial : null;

            // 内嵌材质 = 没有资产路径。AssetDatabase.Contains 正好能判这个，
            // 不用去比名字或者猜。
            bool embedded = material != null && !AssetDatabase.Contains(material);

            using (new EditorGUI.DisabledScope(!embedded))
            {
                if (GUILayout.Button("存为资产", EditorStyles.miniButton, GUILayout.Width(72f)))
                    SaveMaterialAsAsset(terrain, material);
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Label(
                material == null
                    ? "没有材质"
                    : embedded
                        ? "内嵌材质，Project 里找不到"
                        : "材质：" + material.name,
                NoteStyle);

            // ---- 只在真有问题时才出现的警告 ----
            DrawAssetWarnings(terrain, embedded);
        }

        void DrawAssetWarnings(HeightmapTerrain2D terrain, bool embeddedMaterial)
        {
            if (_hintStyle == null)
            {
                _hintStyle = new GUIStyle(EditorStyles.miniLabel);
                _hintStyle.normal.textColor = new Color(1f, 0.72f, 0.35f);
                _hintStyle.wordWrap = true;
            }

            Vector3 scale = terrain.transform.lossyScale;

            if (!Mathf.Approximately(scale.x, 1f) || !Mathf.Approximately(scale.y, 1f))
            {
                // 给两个具体数字，而不是只说"别缩放"：世界宽度是现在实际渲染出来的宽度，
                // 反算出来的间距是"想保持这个尺寸"该填的值。
                float localWidth = Mathf.Max(0f, (terrain.sampleCount - 1) * terrain.spacing);
                float worldWidth = localWidth * Mathf.Abs(scale.x);
                float suggest = terrain.sampleCount > 1
                    ? worldWidth / (terrain.sampleCount - 1)
                    : terrain.spacing;

                GUILayout.Label(
                    "⚠ Scale = " + scale.x.ToString("0.###") +
                    "：贴图密度和子物体尺寸会跟着一起缩。把 Scale 设成 1、采样间距设成 " +
                    suggest.ToString("0.####") + "，就能保住现在的 " +
                    worldWidth.ToString("0.0") + " 世界宽度。",
                    _hintStyle);
            }

            if (embeddedMaterial)
            {
                GUILayout.Label(
                    "⚠ 材质还是内嵌的，美术在 Project 里看不到也没法改。" +
                    "点「材质存为资产」把它落到磁盘上。",
                    _hintStyle);
            }
        }

        /// <summary>
        /// 把自动生成的内嵌材质存成项目资产。
        ///
        /// 为什么必须这一步：MeshUtil.EnsureMaterial 挂的是运行时 new 出来的 Material，
        /// 它没有资产路径 —— 美术在 Project 里找不到、改不了、版本控制也看不见。
        /// AssetDatabase.CreateAsset 是把这个对象**变成**资产（不是复制一份），
        /// 所以渲染器上的引用会跟着变成资产引用，不用重新拖一次。
        /// </summary>
        void SaveMaterialAsAsset(HeightmapTerrain2D terrain, Material material)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "把地形材质存为资产",
                terrain.gameObject.name + " 材质.mat",
                "mat",
                "存好之后，美术在 Project 里直接改这个材质就会生效。");

            if (string.IsNullOrEmpty(path)) return;

            // CreateAsset 要求路径上没有东西，否则会静默失败。
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null)
                AssetDatabase.DeleteAsset(path);

            AssetDatabase.CreateAsset(material, path);
            AssetDatabase.SaveAssets();

            EditorGUIUtility.PingObject(material);

            // 顺手关掉「应用颜色」。
            //
            // 不关的话 MaterialPropertyBlock 会一直把组件的颜色刷在材质颜色上面，
            // 美术第一步就会撞上"我改了材质没反应" —— 这正是这一整块功能要解决的问题。
            // 是可撤销的，日志里也说清楚，不偷偷改。
            if (terrain.applyColor)
            {
                Undo.RecordObject(terrain, "关闭应用颜色");
                terrain.applyColor = false;
                terrain.ApplyVisuals();
                EditorUtility.SetDirty(terrain);
            }

            Debug.Log(
                "Terrain 2D：材质已存为 " + path +
                "。「应用颜色」已自动关闭 —— 现在美术在这个材质里调的颜色不会被组件覆盖了。",
                material);
        }

        /// <summary>一个带数值显示的紧凑滑块。</summary>
        static float Slider(float value, float min, float max, float width, string format)
        {
            float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(width));
            GUILayout.Label(v.ToString(format), EditorStyles.miniLabel, GUILayout.Width(32f));
            return v;
        }

        /// <summary>跑一个会改地形数据的操作：登记撤销 + 标脏 + 刷新。</summary>
        void RunAction(HeightmapTerrain2D terrain, string undoName, System.Action action)
        {
            Undo.RecordObject(terrain, undoName);
            action();
            MarkDirty(terrain);
        }

        /// <summary>一个小色块 + 文字，用在工具栏里。</summary>
        static void Chip(Color color, string label)
        {
            Rect r = GUILayoutUtility.GetRect(10f, 12f, GUILayout.Width(10f));

            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(r.x + 1f, r.y + 3f, 8f, 8f), color);

            GUILayout.Label(label, EditorStyles.miniLabel);
        }

        // ------------------------------------------------------------------ 拖动

        void BeginDrag(HeightmapTerrain2D terrain, Transform tr, int kind, int index, Vector3 world, Event e)
        {
            _dragKind = kind;
            _dragIndex = index;
            _dragStartMouse = e.mousePosition;
            _dragStartWorld = world;

            // 左右边 / 底边折线上的控制点都是自由 2D 拖动：
            // 记下抓取点，和当时鼠标在本地平面上的位置。
            if (kind == 8 || kind == 9 || kind == 10)
            {
                var chain = kind == 8 ? terrain.LeftEdge
                          : kind == 9 ? terrain.RightEdge
                          : terrain.BottomEdge;

                if (index >= 0 && index < chain.Count)
                {
                    _dragGrabLocal = chain[index];
                    Vector2 mouseLocal;
                    if (!TryGetMouseLocal(tr, e.mousePosition, out mouseLocal)) mouseLocal = _dragGrabLocal;
                    _dragStartMouseLocal = mouseLocal;
                }
            }

            // 地表 / 底部的点也是自由 2D 拖动（横向改偏移，纵向改高度）。
            if (kind == 1 || kind == 2)
            {
                bool isTop = kind == 1;
                float pointX = isTop ? terrain.TopXAt(index) : terrain.BottomXAt(index);
                float pointY = isTop ? terrain.Heights[index] : terrain.BottomHeights[index];

                _dragGrabLocal = new Vector2(pointX, pointY);
                Vector2 mouseLocal;
                if (!TryGetMouseLocal(tr, e.mousePosition, out mouseLocal)) mouseLocal = _dragGrabLocal;
                _dragStartMouseLocal = mouseLocal;

                _dragBaseTopOffsetX = terrain.TopOffsetXSnapshot();
                _dragBaseBottomOffsetX = terrain.BottomOffsetXSnapshot();
            }

            // 左右边界和左右斜边沿水平轴拖，其余沿垂直轴拖。
            _dragAxis = (kind == 3 || kind == 4 || kind == 6 || kind == 7)
                ? tr.TransformDirection(Vector3.right)
                : tr.TransformDirection(Vector3.up);

            _dragBaseTop = (float[])terrain.Heights.Clone();
            _dragBaseBottom = terrain.useBottomProfile ? (float[])terrain.BottomHeights.Clone() : null;

            _dragOriginX = terrain.originX;
            _dragSpacing = terrain.spacing;
            _dragBottomY = terrain.bottomY;
            _dragLeftOffsetX = terrain.bottomLeftOffsetX;
            _dragRightOffsetX = terrain.bottomRightOffsetX;

            GUIUtility.hotControl = DragControlId;
            Undo.RecordObject(terrain, "雕刻地形");
        }

        void EndDrag()
        {
            if (GUIUtility.hotControl == DragControlId) GUIUtility.hotControl = 0;

            _dragKind = 0;
            _dragIndex = -1;
            _dragBaseTop = null;
            _dragBaseBottom = null;
            _dragBaseTopOffsetX = null;
            _dragBaseBottomOffsetX = null;
        }

        void ApplyDrag(HeightmapTerrain2D terrain, Transform tr, Event e)
        {
            // ---- 自由 2D 拖动：地表点 / 底部点 / 左右边和底边折线上的点 ----
            // 统一把鼠标投影到物体的本地 XY 平面，取相对抓取点的绝对位移。
            if (_dragKind == 1 || _dragKind == 2 || _dragKind == 8 || _dragKind == 9 || _dragKind == 10)
            {
                Vector2 current;
                if (!TryGetMouseLocal(tr, e.mousePosition, out current)) return;

                Vector2 localDelta = current - _dragStartMouseLocal;

                if (_dragKind == 8 || _dragKind == 9 || _dragKind == 10)
                {
                    var chain = _dragKind == 8 ? terrain.LeftEdge
                              : _dragKind == 9 ? terrain.RightEdge
                              : terrain.BottomEdge;
                    if (_dragIndex < 0 || _dragIndex >= chain.Count) return;

                    if (_dragKind == 10)
                        terrain.SetBottomPoint(_dragIndex, _dragGrabLocal + localDelta);
                    else
                        terrain.SetEdgePoint(_dragKind == 8, _dragIndex, _dragGrabLocal + localDelta);
                }
                else
                {
                    ApplyCurveDrag(terrain, localDelta, terrain.sampleCount);
                }

                EditorUtility.SetDirty(terrain);
                terrain.Rebuild();
                SceneView.RepaintAll();
                return;
            }

            // ---- 单轴拖动：边界方块 ----
            // 相对抓取点的绝对偏移：不累积，也不会自激。
            float offset = HandleUtility.CalcLineTranslation(
                _dragStartMouse, e.mousePosition, _dragStartWorld, _dragAxis);

            Vector3 worldDelta = _dragAxis * offset;
            int count = terrain.sampleCount;

            if (_dragKind == 3)
            {
                float delta = tr.InverseTransformVector(worldDelta).x;
                float right = _dragOriginX + (count - 1) * _dragSpacing;
                float newLeft = _dragOriginX + delta;
                float minWidth = (count - 1) * MinSpacing;
                if (right - newLeft < minWidth) newLeft = right - minWidth;

                terrain.originX = newLeft;
                terrain.spacing = (right - newLeft) / (count - 1);
            }
            else if (_dragKind == 4)
            {
                float delta = tr.InverseTransformVector(worldDelta).x;
                float newRight = _dragOriginX + (count - 1) * _dragSpacing + delta;
                terrain.spacing = Mathf.Max((newRight - _dragOriginX) / (count - 1), MinSpacing);
            }
            else if (_dragKind == 5)
            {
                float delta = tr.InverseTransformVector(worldDelta).y;
                float lowest = float.MaxValue;
                float[] h = terrain.Heights;
                for (int i = 0; i < count; i++)
                    if (h[i] < lowest) lowest = h[i];

                terrain.bottomY = Mathf.Min(_dragBottomY + delta, lowest - HeightmapTerrain2D.MinThickness);
            }
            else if (_dragKind == 6)
            {
                // 底部左端点水平偏移 -> 左边界变斜
                float delta = tr.InverseTransformVector(worldDelta).x;
                terrain.bottomLeftOffsetX = _dragLeftOffsetX + delta;
            }
            else if (_dragKind == 7)
            {
                // 底部右端点水平偏移 -> 右边界变斜
                float delta = tr.InverseTransformVector(worldDelta).x;
                terrain.bottomRightOffsetX = _dragRightOffsetX + delta;
            }

            EditorUtility.SetDirty(terrain);
            terrain.Rebuild();
            SceneView.RepaintAll();
        }

        void ApplyCurveDrag(HeightmapTerrain2D terrain, Vector2 localDelta, int count)
        {
            bool isTop = _dragKind == 1;

            float[] baseValues = isTop ? _dragBaseTop : _dragBaseBottom;
            if (baseValues == null) return;

            float[] baseOffsets = isTop ? _dragBaseTopOffsetX : _dragBaseBottomOffsetX;

            float[] live = isTop ? terrain.Heights : terrain.BottomHeights;
            float[] other = isTop
                ? (terrain.useBottomProfile ? terrain.BottomHeights : null)
                : terrain.Heights;

            // 横向：只动被拖的那一个点，不做平滑 —— 点只管自己。
            if (baseOffsets != null && _dragIndex >= 0 && _dragIndex < baseOffsets.Length
                && Mathf.Abs(localDelta.x) > 1e-6f)
            {
                float newOffset = baseOffsets[_dragIndex] + localDelta.x;

                if (isTop) terrain.SetTopOffsetX(_dragIndex, newOffset);
                else terrain.SetBottomOffsetX(_dragIndex, newOffset);
            }

            // 纵向：按（可选的）平滑权重写回。
            int radius = terrain.smoothDrag ? Mathf.Max(1, terrain.sceneHandleFalloff) : 0;

            for (int k = -radius; k <= radius; k++)
            {
                int index = _dragIndex + k;
                if (index < 0 || index >= count) continue;
                if (index >= baseValues.Length || index >= live.Length) continue;

                float weight = 1f - Mathf.Abs((float)k) / (radius + 1f);
                float value = baseValues[index] + localDelta.y * weight;

                // 保持厚度：地表必须在底部之上，底部必须在地表之下。
                if (other != null && index < other.Length)
                {
                    value = isTop
                        ? Mathf.Max(value, other[index] + HeightmapTerrain2D.MinThickness)
                        : Mathf.Min(value, other[index] - HeightmapTerrain2D.MinThickness);
                }

                live[index] = value;
            }
        }

        // ------------------------------------------------------------------ 插入 / 命中

        /// <summary>把鼠标位置投影到物体本地 XY 平面上，得到本地坐标。</summary>
        static bool TryGetMouseLocal(Transform tr, Vector2 mouseGUI, out Vector2 local)
        {
            local = Vector2.zero;

            Ray ray = HandleUtility.GUIPointToWorldRay(mouseGUI);
            var plane = new Plane(tr.forward, tr.position);

            float distance;
            if (!plane.Raycast(ray, out distance)) return false;

            Vector3 p = tr.InverseTransformPoint(ray.GetPoint(distance));
            local = new Vector2(p.x, p.y);
            return true;
        }

        /// <summary>
        /// 拼出某一条侧边（含两个端点）的折线。左链是"下 -> 上"，右链是"上 -> 下"。
        /// </summary>
        static void BuildSidePolyline(HeightmapTerrain2D terrain, bool left, List<Vector2> result)
        {
            result.Clear();

            int last = terrain.sampleCount - 1;
            float[] heights = terrain.Heights;

            if (left)
            {
                result.Add(new Vector2(terrain.BottomXAt(0), terrain.BottomAt(0)));
                var chain = terrain.LeftEdge;
                for (int i = 0; i < chain.Count; i++) result.Add(chain[i]);
                // 必须用 TopXAt(0) 而不是 originX —— 地表点横拖之后 TopXAt(0) 会带上
                // _topOffsetsX[0] 的偏移，而 originX 不会。用 originX 的话，这条用于
                // 命中判定的折线就和 DrawOutline 真正画出来的左边界错开一段 X，
                // 结果就是「左边界看得见、点不中」。右边没有这个问题（用的是 TopXAt(last)）。
                result.Add(new Vector2(terrain.TopXAt(0), heights[0]));
            }
            else
            {
                result.Add(new Vector2(terrain.TopXAt(last), heights[last]));
                var chain = terrain.RightEdge;
                for (int i = 0; i < chain.Count; i++) result.Add(chain[i]);
                result.Add(new Vector2(terrain.BottomXAt(last), terrain.BottomAt(last)));
            }
        }

        /// <summary>
        /// 拼出底边的折线（右 -> 左，**含两个角点**）。
        ///
        /// 有独立折线就直接用它；没有就退回采样点那套底部数据（`bottomY` 平直线，
        /// 或开了「底部独立轮廓」之后的逐点数据）。命中判定和加点都用这一条，
        /// 保证"看到哪条就点到哪条"。
        /// </summary>
        static void BuildBottomPolyline(HeightmapTerrain2D terrain, List<Vector2> result)
        {
            result.Clear();

            if (terrain.HasBottomEdge)
            {
                var chain = terrain.BottomEdge;
                for (int i = 0; i < chain.Count; i++) result.Add(chain[i]);
                return;
            }

            for (int i = terrain.sampleCount - 1; i >= 0; i--)
                result.Add(new Vector2(terrain.BottomXAt(i), terrain.BottomAt(i)));
        }

        /// <summary>
        /// 取折线沿长度的中点。用来把边界方块放在"这条边的实际中点上"，
        /// 这样加点或拖动之后方块会跟着点走，而不是飘在形状内部。
        /// </summary>
        static Vector2 PolylineMidpoint(List<Vector2> points)
        {
            if (points == null || points.Count == 0) return Vector2.zero;
            if (points.Count == 1) return points[0];

            float total = 0f;
            for (int i = 0; i < points.Count - 1; i++)
                total += Vector2.Distance(points[i], points[i + 1]);

            if (total <= 1e-6f) return points[0];

            float half = total * 0.5f;
            float walked = 0f;

            for (int i = 0; i < points.Count - 1; i++)
            {
                float segment = Vector2.Distance(points[i], points[i + 1]);
                if (walked + segment >= half)
                {
                    float t = segment > 1e-6f ? (half - walked) / segment : 0f;
                    return Vector2.Lerp(points[i], points[i + 1], t);
                }
                walked += segment;
            }

            return points[points.Count - 1];
        }

        void HandleInsert(HeightmapTerrain2D terrain, Transform tr, float[] top, float[] bottom,
                          int count, Event e)
        {
            int chain, index;
            if (!FindNearestSegment(terrain, tr, top, bottom, count, e.mousePosition, out chain, out index))
                return;

            if (chain == 0)
            {
                // 地表：等距高度场，插点就是细分 —— spacing 会重算，总宽不变。
                Undo.RecordObject(terrain, "插入地形采样点");
                terrain.InsertSample(index + 1);
            }
            else if (chain == 2)
            {
                // 底边：走**独立折线**，完全不碰地表。
                //
                // 以前这里和地表共用采样数组（InsertSample），所以往底边加点必然同时插
                // 一个地表点，而且 spacing 重算会把地表所有点横向挪位 ——
                // 用户看到的就是"我点底边，上面也跟着变了"。
                //
                // 现在：折线还是空的时候，先用当前底边把它种下来（形状零变化），再插点。
                // sampleCount / spacing / _heights / _topOffsetsX 一个都不动。
                var bottomLine = new List<Vector2>();
                BuildBottomPolyline(terrain, bottomLine);

                if (index < 0 || index + 1 >= bottomLine.Count) return;

                Undo.RecordObject(terrain, "在底边插入控制点");

                terrain.SeedBottomEdgeIfEmpty();

                // 种完之后折线上的点就是原来那些点，index 依然有效。
                BuildBottomPolyline(terrain, bottomLine);

                Vector2 bottomMid = (bottomLine[index] + bottomLine[index + 1]) * 0.5f;
                terrain.InsertBottomPoint(index + 1, bottomMid);
            }
            else
            {
                // 左 / 右边：插到对应的边链里，新点落在命中线段的中点。
                Undo.RecordObject(terrain, "插入地形采样点");

                bool left = chain == 3;
                var side = new List<Vector2>();
                BuildSidePolyline(terrain, left, side);

                Vector2 mid = (side[index] + side[index + 1]) * 0.5f;
                terrain.InsertEdgePoint(left, index, mid);
            }

            MarkDirty(terrain);
            e.Use();
        }

        /// <summary>
        /// 找离鼠标最近、且在命中范围内的轮廓线段。
        /// chain：0=地表 1=右边 2=底部 3=左边；index 是该条边内的线段下标。
        /// </summary>
        bool FindNearestSegment(HeightmapTerrain2D terrain, Transform tr, float[] top, float[] bottom,
                                int count, Vector2 mouse, out int chain, out int index)
        {
            chain = -1;
            index = -1;
            float bestDistance = float.MaxValue;

            // 地表
            for (int i = 0; i < count - 1; i++)
            {
                float xa = terrain.TopXAt(i);
                float xb = terrain.TopXAt(i + 1);

                Vector2 a = HandleUtility.WorldToGUIPoint(tr.TransformPoint(new Vector3(xa, top[i], 0f)));
                Vector2 b = HandleUtility.WorldToGUIPoint(tr.TransformPoint(new Vector3(xb, top[i + 1], 0f)));

                float distance = HandleUtility.DistancePointLine(mouse, a, b);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    chain = 0;
                    index = i;
                }
            }

            // 底部：单独走一条折线。
            // 有独立折线时它的点数和地表无关，所以 index 只能按折线自己的线段下标算，
            // 不能拿采样点下标去套。底边必须参与判定 —— 它同样是画出来的轮廓的一条边，
            // 用户会去点它（以前 `if (bottom == null) continue;` 把整条底边跳过了）。
            var bottomLine = new List<Vector2>();
            BuildBottomPolyline(terrain, bottomLine);

            for (int i = 0; i < bottomLine.Count - 1; i++)
            {
                Vector2 a = HandleUtility.WorldToGUIPoint(
                    tr.TransformPoint(new Vector3(bottomLine[i].x, bottomLine[i].y, 0f)));
                Vector2 b = HandleUtility.WorldToGUIPoint(
                    tr.TransformPoint(new Vector3(bottomLine[i + 1].x, bottomLine[i + 1].y, 0f)));

                float distance = HandleUtility.DistancePointLine(mouse, a, b);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    chain = 2;
                    index = i;
                }
            }

            // 左边 + 右边
            var side = new List<Vector2>();

            for (int s = 0; s < 2; s++)
            {
                bool left = s == 0;
                BuildSidePolyline(terrain, left, side);

                for (int i = 0; i < side.Count - 1; i++)
                {
                    Vector2 a = HandleUtility.WorldToGUIPoint(
                        tr.TransformPoint(new Vector3(side[i].x, side[i].y, 0f)));
                    Vector2 b = HandleUtility.WorldToGUIPoint(
                        tr.TransformPoint(new Vector3(side[i + 1].x, side[i + 1].y, 0f)));

                    float distance = HandleUtility.DistancePointLine(mouse, a, b);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        chain = left ? 3 : 1;
                        index = i;
                    }
                }
            }

            if (bestDistance > HandlePickRadius)
            {
                chain = -1;
                index = -1;
                return false;
            }

            return true;
        }
    }

    internal static class Terrain2DMenuItems
    {
        [MenuItem("GameObject/地形 2D/为选中的碰撞体添加多边形网格", false, 10)]
        static void AddPolygonMesh()
        {
            var collider = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<PolygonCollider2D>()
                : null;

            if (collider == null)
            {
                Debug.LogWarning("请先选中一个带有 Polygon Collider 2D 的物体。");
                return;
            }

            // PolygonMesh2D 上有 [RequireComponent]，MeshFilter 和 MeshRenderer 会自动补上。
            var mesh = Undo.AddComponent<PolygonMesh2D>(collider.gameObject);
            mesh.outline = collider;
            mesh.Rebuild();

            Selection.activeGameObject = collider.gameObject;
        }

        [MenuItem("GameObject/地形 2D/创建高度场地形", false, 11)]
        static void CreateHeightmapTerrain(MenuCommand command)
        {
            CreateTerrain(command.context as GameObject);
        }

        [MenuItem("GameObject/地形 2D/创建测试角色", false, 12)]
        static void CreateTestPlayer(MenuCommand command)
        {
            var player = BuildTestPlayer(command.context as GameObject, true);

            // 角色和镜头是一套测试装备。少了镜头，按 Play 跑两步人就没影了。
            EnsureTestCamera(TestCameraPrefs.Mode, player.transform);
        }

        [MenuItem("GameObject/地形 2D/创建测试摄像机", false, 13)]
        static void CreateTestCamera()
        {
            EnsureTestCamera(TestCameraPrefs.Mode, null);
        }

        /// <summary>
        /// 确保场景里有一台测试摄像机，并把它切到指定模式。
        ///
        /// 优先复用已有的 <see cref="TestCamera2D"/>；没有的话挂在 <c>Camera.main</c> 上；
        /// 连主摄像机都没有才新建一台。
        ///
        /// 为什么挂在主摄像机上而不是另建一台：同时存在两台启用的摄像机会双份渲染，
        /// 而 2D 项目的主摄像机本来就已经配好了正交尺寸和背景色，加一个跟随组件
        /// 是纯粹的增量 —— 不想要了把组件删掉就行，场景别的部分一点没动。
        /// </summary>
        public static TestCamera2D EnsureTestCamera(TestCameraMode mode, Transform target)
        {
            var rig = UnityEngine.Object.FindObjectOfType<TestCamera2D>();

            if (rig == null)
            {
                Camera cam = Camera.main;

                if (cam == null)
                {
                    var go = new GameObject("测试摄像机");
                    Undo.RegisterCreatedObjectUndo(go, "创建测试摄像机");
                    go.tag = "MainCamera";
                    cam = go.AddComponent<Camera>();
                    cam.orthographic = true;
                    cam.orthographicSize = 5f;
                }

                rig = Undo.AddComponent<TestCamera2D>(cam.gameObject);
            }

            if (target == null)
            {
                var player = UnityEngine.Object.FindObjectOfType<TestPlayer2D>(true);
                if (player != null) target = player.transform;
            }

            // 组件和 transform 都要记撤销：这一步会真的把相机挪位置，
            // 不记的话 Ctrl+Z 只撤掉组件、镜头停在被拽过去的地方。
            Undo.RecordObject(rig, "配置测试摄像机");
            Undo.RecordObject(rig.transform, "移动测试摄像机");

            rig.target = target;
            rig.SetMode(mode);
            rig.SnapToTarget();

            EditorUtility.SetDirty(rig);
            EditorUtility.SetDirty(rig.transform);
            SceneView.RepaintAll();

            return rig;
        }

        /// <summary>
        /// 造一个测试角色。菜单项和视口「测试」阶段工具栏都走这里 ——
        /// 后者没有 MenuCommand 可用，所以逻辑抽出来一份。
        /// </summary>
        /// <param name="parent">父物体，可以是 null</param>
        /// <param name="select">是否把新物体设为选中</param>
        public static TestPlayer2D BuildTestPlayer(GameObject parent, bool select)
        {
            var go = new GameObject("测试角色");
            GameObjectUtility.SetParentAndAlign(go, parent);
            Undo.RegisterCreatedObjectUndo(go, "创建测试角色");

            // 先按顺序把组件摆好，参数也就顺手定了；TestPlayer2D 上的
            // [RequireComponent] 只是兜底，不该指望它来配参数。
            var sprite = go.AddComponent<SpriteRenderer>();
            sprite.sortingOrder = 100;

            var body = go.AddComponent<Rigidbody2D>();
            body.gravityScale = 2.5f;
            body.freezeRotation = true;

            var capsule = go.AddComponent<CapsuleCollider2D>();
            capsule.direction = CapsuleDirection2D.Vertical;
            capsule.size = new Vector2(0.62f, 1f);

            var player = go.AddComponent<TestPlayer2D>();

            if (parent == null)
            {
                var view = SceneView.lastActiveSceneView;
                if (view != null)
                    go.transform.position = new Vector3(view.pivot.x, view.pivot.y, 0f);
            }

            if (select) Selection.activeGameObject = go;

            return player;
        }

        /// <summary>
        /// 创建一块高度场地形。parent 为父物体，可以是 null。
        /// 菜单项和视口工具栏的「创建高度场地形」按钮都走这里 —— 后者没有 MenuCommand 可用。
        /// </summary>
        public static HeightmapTerrain2D CreateTerrain(GameObject parent)
        {
            var go = new GameObject("高度场地形");
            GameObjectUtility.SetParentAndAlign(go, parent);
            Undo.RegisterCreatedObjectUndo(go, "创建高度场地形");

            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            var terrain = go.AddComponent<HeightmapTerrain2D>();
            terrain.Generate();

            // 只在没有父物体时摆到视口中心。从 Hierarchy 右键「创建为子物体」进来时
            // parent 不为空，那种情况下用户期望它就在父物体那儿，不能传送走。
            if (parent == null) PlaceAtViewCenter(go, terrain);

            Selection.activeGameObject = go;
            return terrain;
        }

        /// <summary>
        /// 放到当前 Scene 视图的正中，而不是原点 —— 否则点完「创建」还得自己去找它。
        ///
        /// 注意不能直接把 transform 摆到 pivot：地形的局部 X 是从 0 到
        /// (sampleCount-1)*spacing，左边缘在 0。直接对齐会让**左边缘**落在视口中心，
        /// 看上去偏右半个身位。所以左移半个宽度，让它在视口里真正居中。
        /// Y 同理，用 baseHeight 对齐 —— 地表是绕着 baseHeight 起伏的。
        /// </summary>
        static void PlaceAtViewCenter(GameObject go, HeightmapTerrain2D terrain)
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null) return;

            Vector3 pivot = view.pivot;
            float width = Mathf.Max(0f, (terrain.sampleCount - 1) * terrain.spacing);

            go.transform.position = new Vector3(
                pivot.x - width * 0.5f,
                pivot.y - terrain.baseHeight,
                0f);
        }
    }
}
