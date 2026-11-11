using UnityEditor;
using UnityEngine;

namespace LDK2D.EditorTools
{
    /// <summary>
    /// 视口**右下角**的缩放滑条：等价于在视口里滚滚轮，但能一眼看出"现在缩到哪了"。
    ///
    /// 为什么需要它：滚轮缩放**没有刻度**。缩着缩着就不知道自己是放大了还是缩小了，
    /// 也不知道还能不能再缩出去；想把整个关卡一眼看全，只能来回滚、靠感觉停。
    /// 这根滑条把"缩放到哪里"变成一个**可以看见、可以拉回去**的值：
    ///
    ///   - 拉到最右 = **整个场景刚好装下**（不是"大概"，是按所有 Renderer 的包围盒算出来的）
    ///   - 拉到最左 = 放到最大，约 25 倍变焦
    ///
    /// 左边的眼睛图标是**标识**，不是按钮 —— 说明这条滑条管的是"视口看得见多大"，
    /// 跟旁边可能出现的其它工具箱条目区分开。**不要把它做成开关。**
    ///
    /// 滑条和滚轮是**同一个状态的两个视图**：滚动滚轮之后滑条会跟着动，
    /// 因为每帧都是从 `view.size` 反算当前值，不自己存一份 —— 存一份迟早会跟真实视口对不上。
    /// </summary>
    [InitializeOnLoad]
    internal static class LDK2DViewportZoomSlider
    {
        /// <summary>
        /// 整条工具箱的宽度。第一行是滑条；第二行要放下 笔 / 框 / 字 +
        /// 四个预设色 + 色盘（色块 + 吸管）+ 线宽滑条 + 擦，所以比只放滑条宽不少。
        /// </summary>
        const float BarWidth = 288f;

        /// <summary>眼睛图标占的宽度。</summary>
        const float EyeWidth = 20f;

        /// <summary>
        /// 放到最大时的视野 = 装下全场景所需视野的 4%。也就是约 25 倍变焦范围。
        /// 再大滑条每一像素跨的倍率太多，拉起来会跳。
        /// </summary>
        const float MinRatio = 0.04f;

        /// <summary>装下全场景时四周留一点，免得关卡边界正好压在视口边上。</summary>
        const float FitPadding = 1.06f;

        /// <summary>拉到这个值就算"到底了"。浮点滑条不会精确给 1。</summary>
        const float FullThreshold = 0.999f;

        static readonly GUIContent EyeIcon = ResolveEyeIcon();

        /// <summary>
        /// 工具箱第二行。由 `LDK2DMarkupTool` 在域加载时挂上来。
        ///
        /// 为什么不直接调用：这个文件比 `LDK2DMarkupTool` 老，**旧文件引用新类型**
        /// 会在 Unity 增量编译「先编译、后导入」那一轮报 `does not contain a definition`。
        /// 把委托放在这里，新文件挂进来就行，两边都不越界。
        ///
        /// 为 null 时工具箱只有一行，高度也跟着少一行。
        /// </summary>
        public static System.Action ExtraRow;

        /// <summary>
        /// <see cref="ExtraRow"/> 需要多少高度。
        ///
        /// 为什么要单独问一次：`GUILayout.BeginArea` 的矩形高度必须在**画之前**定下来，
        /// 画完再问就晚了 —— 给少了会被静默裁掉，给多了底下多一块空白。
        /// 为 null 时按 0 算。
        /// </summary>
        public static System.Func<float> ExtraHeight;

        /// <summary>
        /// 工具箱当前占据的矩形（视口坐标）。给别的工具判断"这一下点击是不是落在工具箱上"用 ——
        /// 不判断的话，点一下滑条会在场景里留下一个标记点。
        ///
        /// 每帧开头清空：没有场景内容时工具箱根本不画，这时候不该有"上一个位置的残影"。
        /// </summary>
        public static Rect ToolboxRect;

        // FindObjectsOfType 会分配数组，而 OnSceneGui 每个鼠标事件都跑 —— 缓存，
        // 层级变了才重新找。bounds 不缓存：地形雕刻、物体移动都会改它，但不触发层级变化。
        static Renderer[] _renderers;

        /// <summary>
        /// 已经在对齐状态。拉到最右时把视图中心对到场景中心，**只对一次** ——
        /// 每帧都对的话，用户拉满之后就没法平移视图了，镜头会被一直拽回去。
        /// </summary>
        static bool _pinned;

        static LDK2DViewportZoomSlider()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            SceneView.duringSceneGui += OnSceneGui;

            EditorApplication.hierarchyChanged -= Invalidate;
            EditorApplication.hierarchyChanged += Invalidate;
        }

        static void Invalidate()
        {
            _renderers = null;
        }

        static void OnSceneGui(SceneView view)
        {
            if (view == null) return;
            if (Event.current == null) return;

            ToolboxRect = new Rect();

            Bounds bounds;
            if (!TryGetSceneBounds(out bounds)) return;

            float aspect = AspectOf(view);
            float halfHeight = FitHalfHeight(bounds, aspect);
            if (halfHeight <= 0f) return;

            Draw(view, bounds, halfHeight);
        }

        static void Draw(SceneView view, Bounds bounds, float fitHalfHeight)
        {
            float width = Mathf.Min(BarWidth, view.position.width * 0.5f);
            if (width < EyeWidth + 60f) return;

            float row = EditorGUIUtility.singleLineHeight;

            // 额外行有没有，决定整条工具箱多高。没有额外行时不留空位 ——
            // 留空位的话底下会多出一条谁也不知道干什么的空白。
            float extra = ExtraHeight != null ? ExtraHeight() : 0f;
            float height = row + 8f + extra;

            Rect rect = new Rect(
                view.position.width - width - LDK2DSceneToolbarLayout.SideInset,
                view.position.height - height - LDK2DSceneToolbarLayout.BottomInset,
                width,
                height);

            ToolboxRect = rect;

            Handles.BeginGUI();

            GUILayout.BeginArea(rect);
            GUILayout.BeginVertical("box");

            // ---- 第一行：视口 ----
            GUILayout.BeginHorizontal();

            DrawEye();

            float current = CurrentT(view, fitHalfHeight);
            float next = GUILayout.HorizontalSlider(
                current, 0f, 1f, GUILayout.Height(EditorGUIUtility.singleLineHeight));

            GUILayout.EndHorizontal();

            // ---- 第二行：标记 ----
            if (ExtraRow != null) ExtraRow();

            GUILayout.EndVertical();
            GUILayout.EndArea();

            Handles.EndGUI();

            if (Mathf.Approximately(next, current)) return;

            Apply(view, bounds, fitHalfHeight, next);
        }

        /// <summary>
        /// 左边的眼睛。**是标识不是按钮** —— 用 Label 而不是 Toggle/Button，
        /// 点它不会有任何反应，也不该有。
        /// </summary>
        static void DrawEye()
        {
            if (EyeIcon != null && EyeIcon.image != null)
            {
                GUILayout.Label(
                    EyeIcon, EditorStyles.label,
                    GUILayout.Width(EyeWidth),
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
                return;
            }

            // 图标名在不同 Unity 版本里不一定都还在，拿不到就退回文字。
            GUILayout.Label(
                "视口", EditorStyles.miniLabel,
                GUILayout.Width(EyeWidth + 8f),
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
        }

        /// <summary>
        /// 当前滑条值。**从 `view.size` 反算**，不另存一份 ——
        /// 滚轮缩放之后滑条要跟着动，存一份就迟早对不上。
        /// </summary>
        static float CurrentT(SceneView view, float fitHalfHeight)
        {
            float minHalf = fitHalfHeight * MinRatio;
            float now = SizeToHalfHeight(view);

            return Mathf.InverseLerp(minHalf, fitHalfHeight, now);
        }

        /// <summary>
        /// 把滑条值写回视口。
        ///
        /// 正交和透视要分开换算：正交下 `view.size` 就是半高，
        /// 透视下它是相机到焦点的距离，得按 FOV 折算。
        /// </summary>
        static void Apply(SceneView view, Bounds bounds, float fitHalfHeight, float t)
        {
            float minHalf = fitHalfHeight * MinRatio;
            float targetHalf = Mathf.Lerp(minHalf, fitHalfHeight, t);

            view.size = HalfHeightToSize(view, targetHalf);

            // 拉到最右 = 请求"看全整个场景"。视野够了但中心没对上的话还是看不全，
            // 所以这里顺手把视图中心对到场景中心，**只在刚拉满的那一下做**。
            if (t >= FullThreshold)
            {
                if (!_pinned)
                {
                    _pinned = true;
                    view.pivot = new Vector3(bounds.center.x, bounds.center.y, view.pivot.z);
                }
            }
            else
            {
                _pinned = false;
            }

            view.Repaint();
        }

        // ------------------------------------------------------------------ 换算

        static float AspectOf(SceneView view)
        {
            if (view.camera != null && view.camera.aspect > 0.0001f) return view.camera.aspect;

            return Mathf.Max(
                0.0001f, view.position.width / Mathf.Max(1f, view.position.height));
        }

        /// <summary>
        /// 装下整个场景需要多大的视野（半高）。
        /// 高度和宽度各算一遍取大的 —— 只看高度的话，宽扁的场景会左右溢出。
        /// </summary>
        static float FitHalfHeight(Bounds bounds, float aspect)
        {
            return Mathf.Max(
                bounds.extents.y,
                bounds.extents.x / Mathf.Max(0.0001f, aspect)) * FitPadding;
        }

        static float HalfHeightToSize(SceneView view, float halfHeight)
        {
            if (view.orthographic) return halfHeight;

            float fov = view.camera != null ? view.camera.fieldOfView : 60f;
            return halfHeight / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
        }

        static float SizeToHalfHeight(SceneView view)
        {
            if (view.orthographic) return view.size;

            float fov = view.camera != null ? view.camera.fieldOfView : 60f;
            return view.size * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
        }

        // ------------------------------------------------------------------ 场景范围

        /// <summary>
        /// 整个场景的渲染范围。**用所有 Renderer**，不像缩略图那样只算地形 ——
        /// 这里要的是"拉满能看到整个图"，图指的就是场景里看得见的一切。
        /// </summary>
        static bool TryGetSceneBounds(out Bounds bounds)
        {
            if (_renderers == null)
                _renderers = Object.FindObjectsOfType<Renderer>(false);

            bounds = new Bounds();
            bool has = false;

            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i];
                if (renderer == null) continue;

                // 被关掉的渲染器不参与 —— 场景显示层级把某层藏了之后，
                // 拉满应该只装下看得见的部分。
                if (!renderer.enabled) continue;

                if (!has) { bounds = renderer.bounds; has = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            return has;
        }

        static GUIContent ResolveEyeIcon()
        {
            string[] names =
            {
                "animationvisibilitytoggleon",
                "scenevis_visible_hover",
                "ViewMode",
            };

            for (int i = 0; i < names.Length; i++)
            {
                GUIContent content = EditorGUIUtility.IconContent(names[i]);
                if (content != null && content.image != null)
                    return new GUIContent(content.image, "视口缩放");
            }

            return null;
        }
    }
}
