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

        // 排序层名字缓存。
        // SortingLayer.layers 每次访问都会分配一个新数组，而视口工具栏是每帧重绘的，
        // 所以缓存起来。用户新加了排序层时，靠 OnEnable（切换选中物体 / 域重载）刷新。
        static string[] _sortingLayerNames;

        void OnEnable()
        {
            _sortingLayerNames = null;
        }

        static string[] SortingLayerNames()
        {
            if (_sortingLayerNames == null || _sortingLayerNames.Length == 0)
            {
                var layers = SortingLayer.layers;
                _sortingLayerNames = new string[layers.Length];
                for (int i = 0; i < layers.Length; i++) _sortingLayerNames[i] = layers[i].name;
            }
            return _sortingLayerNames;
        }

        static int IndexOfSortingLayer(string name)
        {
            string[] names = SortingLayerNames();
            for (int i = 0; i < names.Length; i++)
                if (names[i] == name) return i;
            return 0;
        }

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
        /// Scene 视图底部那条工具栏就整个消失，看起来像"脚本文件丢了"，实际只是这个开关。
        ///
        /// 所以默认给 true：选中地形物体就应该能直接看到工具栏。
        /// 想临时收起来，用 Inspector 里的「退出编辑模式」按钮。
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
            if (!editing && !terrain.showSceneHandles) return;

            Transform tr = terrain.transform;
            Event e = Event.current;

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

            if (editing) DrawSceneToolbar(terrain);
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

        static string ToolbarKey(int instanceId) => "Terrain2D.Toolbar." + instanceId;

        /// <summary>
        /// 视口底部的横排工具栏，仿 Unity 粒子特效那个：贴底、横排、左边有个折叠箭头。
        /// </summary>
        void DrawSceneToolbar(HeightmapTerrain2D terrain)
        {
            var view = SceneView.currentDrawingSceneView;
            if (view == null) return;

            int id = terrain.GetInstanceID();
            bool open = SessionState.GetBool(ToolbarKey(id), true);

            Rect viewRect = view.position;
            float width = viewRect.width;
            float height = viewRect.height;

            Handles.BeginGUI();

            // 用一个覆盖整个视口的透明区域 + FlexibleSpace 把内容顶到底部。
            // 这样不用去猜"Scene 视图工具栏有多高"，位置永远是贴底的。
            float areaHeight = Mathf.Max(90f, height - 32f);
            GUILayout.BeginArea(new Rect(8f, 0f, width - 16f, areaHeight));
            GUILayout.FlexibleSpace();

            // 轮廓自交的警告贴在工具栏正上方 —— 正在拖点的人一眼就能看到。
            if (!terrain.OutlineValid)
            {
                if (_warnStyle == null)
                {
                    _warnStyle = new GUIStyle(EditorStyles.boldLabel);
                    _warnStyle.normal.textColor = new Color(1f, 0.48f, 0.38f);
                }

                GUILayout.BeginVertical("box");
                GUILayout.Label("轮廓自交 —— 网格已停止更新", _warnStyle);
                GUILayout.Label("把越界的控制点拖回来即可恢复，或点 Inspector 里的「清空边加点」。",
                                EditorStyles.miniLabel);
                GUILayout.EndVertical();
            }

            GUILayout.BeginVertical("box");

            // ---- 第一行：折叠箭头 + 标题 + 颜色 + 边界方块开关 + 计数 ----
            GUILayout.BeginHorizontal();

            if (GUILayout.Button(open ? "▾" : "▸", EditorStyles.miniButton, GUILayout.Width(20f)))
            {
                open = !open;
                SessionState.SetBool(ToolbarKey(id), open);
                SceneView.RepaintAll();
            }

            GUILayout.Label("地形编辑工具", EditorStyles.miniBoldLabel);

            if (open)
            {
                GUILayout.Label("颜色", EditorStyles.miniLabel);

                EditorGUI.BeginChangeCheck();
                Color picked = EditorGUILayout.ColorField(
                    GUIContent.none, terrain.color, true, true, false, GUILayout.Width(70f));

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(terrain, "改地形颜色");
                    terrain.color = picked;
                    terrain.ApplyVisuals();
                    EditorUtility.SetDirty(terrain);
                    Repaint();
                }

                bool boundary = GUILayout.Toggle(
                    terrain.showBoundaryHandles, "边界方块",
                    EditorStyles.miniButton, GUILayout.Width(68f));

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
                    EditorStyles.miniButton, GUILayout.Width(68f));

                if (smooth != terrain.smoothDrag)
                {
                    Undo.RecordObject(terrain, "切换平滑拖动");
                    terrain.smoothDrag = smooth;
                    EditorUtility.SetDirty(terrain);
                    Repaint();
                }
            }

            GUILayout.FlexibleSpace();

            GUILayout.Label("地表 " + terrain.sampleCount +
                            "　左 " + terrain.LeftEdge.Count +
                            "　右 " + terrain.RightEdge.Count,
                            EditorStyles.miniLabel);

            GUILayout.EndHorizontal();

            if (open)
            {
                // ---- 第二行：渲染层级（作用于本地形）----
                // 这一行改的是**本地形自己**的层级。整个场景所有 2D 对象的绘制顺序，
                // 用右边的「层级管理器」—— 它会把排序顺序写成一串干净的 0,1,2…
                GUILayout.BeginHorizontal();

                GUILayout.Label("层级", EditorStyles.miniLabel);

                string[] layerNames = SortingLayerNames();
                int layerIndex = IndexOfSortingLayer(terrain.sortingLayerName);

                // 只用最基础的 Popup / IntField 重载（不带 GUIStyle）：
                // 这个文件以前没画过这两个控件，不拿没验证过的重载去赌编译。
                int pickedLayer = EditorGUILayout.Popup(layerIndex, layerNames, GUILayout.Width(104f));

                if (pickedLayer != layerIndex && pickedLayer >= 0 && pickedLayer < layerNames.Length)
                {
                    Undo.RecordObject(terrain, "改地形排序层");
                    terrain.sortingLayerName = layerNames[pickedLayer];
                    terrain.ApplyVisuals();
                    EditorUtility.SetDirty(terrain);
                    SceneView.RepaintAll();
                }

                GUILayout.Label("顺序", EditorStyles.miniLabel);

                EditorGUI.BeginChangeCheck();
                int sortingOrder = EditorGUILayout.IntField(terrain.sortingOrder, GUILayout.Width(48f));

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(terrain, "改地形排序顺序");
                    terrain.sortingOrder = sortingOrder;
                    terrain.ApplyVisuals();
                    EditorUtility.SetDirty(terrain);
                    SceneView.RepaintAll();
                }

                GUILayout.Space(10f);

                if (GUILayout.Button("层级管理器", EditorStyles.miniButton, GUILayout.Width(84f)))
                    SortingOrderWindow.Open();

                GUILayout.FlexibleSpace();

                GUILayout.Label("整场景所有 2D 对象的绘制顺序 →「层级管理器」", EditorStyles.miniLabel);

                GUILayout.EndHorizontal();

                // ---- 第三行：操作按钮（和 Inspector 里那排一样）----
                GUILayout.BeginHorizontal();

                if (GUILayout.Button("生成", EditorStyles.miniButton, GUILayout.Width(46f)))
                    RunAction(terrain, "生成地形", terrain.Generate);

                if (GUILayout.Button("拍平", EditorStyles.miniButton, GUILayout.Width(46f)))
                    RunAction(terrain, "拍平地形", terrain.Flatten);

                if (GUILayout.Button("重建", EditorStyles.miniButton, GUILayout.Width(46f)))
                    RunAction(terrain, "重建地形", terrain.Rebuild);

                GUILayout.Space(10f);

                if (GUILayout.Button("细分 ×2", EditorStyles.miniButton, GUILayout.Width(58f)))
                    RunAction(terrain, "细分地形", terrain.Subdivide);

                if (GUILayout.Button("简化一半", EditorStyles.miniButton, GUILayout.Width(58f)))
                    RunAction(terrain, "简化地形", terrain.Simplify);

                if (GUILayout.Button("清空边加点", EditorStyles.miniButton, GUILayout.Width(74f)))
                    RunAction(terrain, "清空边加点", terrain.ClearEdgePoints);

                if (GUILayout.Button("清空横向", EditorStyles.miniButton, GUILayout.Width(62f)))
                    RunAction(terrain, "清空横向偏移", terrain.ClearXOffsets);

                GUILayout.FlexibleSpace();

                GUILayout.EndHorizontal();

                // ---- 第四行：颜色图例 + 操作说明 ----
                GUILayout.BeginHorizontal();

                Chip(TopColor, "地表");
                Chip(BottomColor, "底部轮廓");
                Chip(EdgePointColor, "左右边");
                GUILayout.Space(10f);
                Chip(EdgeColor, "移边界");
                Chip(SlantColor, "边变斜");
                if (terrain.useBottomProfile) Chip(SlantColor, "底边逐点");
                else Chip(BottomEdgeColor, "底边整体");

                GUILayout.FlexibleSpace();

                GUILayout.Label("Shift + 点边 = 加点　Ctrl / Cmd + 点圆点 = 删点",
                                EditorStyles.miniLabel);

                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
            GUILayout.EndArea();

            Handles.EndGUI();
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
            var go = new GameObject("高度场地形");
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "创建高度场地形");

            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            var terrain = go.AddComponent<HeightmapTerrain2D>();
            terrain.Generate();

            Selection.activeGameObject = go;
        }
    }
}
