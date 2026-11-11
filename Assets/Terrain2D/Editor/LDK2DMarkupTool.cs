using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LDK2D.EditorTools
{
    /// <summary>
    /// 场景实时标记：笔 / 框 / 文字三种，在视口里直接画。
    ///
    /// **数据在 <see cref="LDK2DMarkup"/>，绘制在这里。** 场景里没有 MeshRenderer、
    /// 不进 Game 视图、不参与 URP 2D 管线 —— 视觉上就是浮在场景上面的一层，
    /// 但它是用 `Handles` 画的，不是 GUI 层。
    ///
    /// 为什么不用 GUI 层（`GUI.DrawTexture` / `EditorGUI.DrawRect`）：
    ///   - GUI 层只能画矩形，斜线全是锯齿，圆头得自己算旋转贴图；
    ///   - `DrawAAPolyLine` 的抗锯齿是白送的，线宽本来就是**屏幕像素**，
    ///     视口缩到全图还是放到 25 倍，线都是同样的粗细。
    /// 所以"像 UI 一样浮着"和"用 Handles 画"不矛盾，后者只是质量更高。
    ///
    /// 三种标记的尺寸都用**屏幕像素**：
    ///   - 笔迹：线宽 1~8px
    ///   - 文字：字号 14px
    ///   - 框：描边 2~8px，**框在屏幕上越小、线越粗**（见 `BoxStrokeWidth`）——
    ///     固定 2px 的话，视口缩到能看全图时框只剩几十像素，描边会细到看不出形状。
    ///
    /// 手感的三件事：
    ///   1. **采点按屏幕距离**，不按世界距离 —— 世界距离在不同缩放下对应的像素数差很远，
    ///      用世界距离会在放大时点太密、缩小时点太疏。
    ///   2. **收笔时 Chaikin 细分一轮**，把折线抹成弧线。
    ///   3. **画的时候吃掉鼠标事件**，不然画一笔顺手把物体全选中了。
    ///
    /// Play 模式：能画。Unity 退出 Play 会销毁运行时创建的对象，所以
    /// `ExitingPlayMode` 把数据抢救到 <see cref="SessionState"/>，
    /// `EnteredEditMode` 再写回场景里的标记对象。
    /// </summary>
    [InitializeOnLoad]
    internal static class LDK2DMarkupTool
    {
        /// <summary>当前选中的工具。互斥 —— 同一时间只能用一种画。</summary>
        enum Tool
        {
            None,
            Pen,
            Box,
            Text,
            Erase,
        }

        /// <summary>标记容器的对象名。放在场景根，一眼能看出是工具对象。</summary>
        const string ContainerName = "_LDK2D_Marks";

        const string RescueKey = "LDK2D.MarkupRescue";

        // 显示开关存 EditorPrefs：这是"我平时想看到什么"的偏好，不该跟着场景走。
        const string PrefShowStrokes = "LDK2D.MarkShowStrokes";
        const string PrefShowTexts = "LDK2D.MarkShowTexts";
        const string PrefShowBoxes = "LDK2D.MarkShowBoxes";

        /// <summary>采点阈值，屏幕像素。小于这个距离不记点 —— 否则点又密又涩。</summary>
        const float SampleDistance = 2f;

        /// <summary>
        /// 橡皮的半径，屏幕像素。橡皮是**一个圆**：按住拖过去，只擦掉圈经过的那一段，
        /// 不是整笔删掉。
        /// </summary>
        const float EraseRadius = 8f;

        /// <summary>框的"点到描边"容差，屏幕像素。按像素算，缩放时手感一致。</summary>
        const float EdgeTolerance = 6f;

        /// <summary>缩放手柄的边长，屏幕像素。跟视口缩放无关 —— 手柄永远是这么点大。</summary>
        const float HandleSize = 8f;

        /// <summary>手柄的命中半径，比画出来的大一圈，好点。</summary>
        const float HandleHitRadius = 7f;

        /// <summary>字号缩放范围。太小看不清，太大糊一屏。</summary>
        const float MinFontSize = 6f;
        const float MaxFontSize = 96f;

        const float MinWidth = 1f;
        const float MaxWidth = 8f;

        /// <summary>框的最小边长，世界单位。防止手抖拖出一个 0 面积的框。</summary>
        const float MinBoxExtent = 0.15f;

        /// <summary>
        /// 框被选中时的填充透明度。**平时框没有填充** —— 一填色就把框里的地形盖住了，
        /// 而框里恰恰是要看的东西。只有"点中了它、准备删它"的时候才需要这层填充
        /// 来确认"选的是这一个"。
        /// </summary>
        const float BoxFillAlpha = 0.14f;

        /// <summary>文字标注的字号，屏幕像素。</summary>
        const float TextSize = 14f;

        /// <summary>
        /// 框描边的粗细范围，屏幕像素。**框在屏幕上越小，描边越粗。**
        ///
        /// 为什么要变：描边宽度是屏幕像素，视口缩到能看全图时框只剩几十像素，
        /// 固定 2px 会细到看不出这是个框。
        ///
        /// 为什么按"框自己的屏幕尺寸"而不是"视口缩放倍数"：一个天生就画得很小的框，
        /// 哪怕在 1:1 的视口里也一样需要粗一点才看得见 —— 该看的是它现在多大，
        /// 不是相机在哪。
        /// </summary>
        const float BoxStrokeMin = 2f;      // 框够大时
        const float BoxStrokeMax = 8f;      // 框缩到很小时
        const float BoxStrokeTightPx = 20f; // 小于这个屏幕尺寸 → 最粗
        const float BoxStrokeWidePx = 120f; // 大于这个屏幕尺寸 → 最细

        /// <summary>
        /// 色盘控件的宽度，屏幕像素。**必须够宽放下"色块 + 吸管"两样东西。**
        ///
        /// `EditorGUI.ColorField(showEyedropper: true)` 是左右排布的：
        /// 吸管占固定 `~18px`，剩下的才给色块。给 `18px` 的话色块被压成 0 宽 ——
        /// 界面上只剩一个吸管图标，看着根本不像颜色按钮。
        /// 所以给 `38px`：`18` 色块 + `18` 吸管 + 边框。
        /// </summary>
        const float ColorFieldWidth = 38f;

        const string EditControlName = "LDK2DMarkEditField";

        /// <summary>四种颜色分别对应四类语义，不是随便挑的好看色。</summary>
        static readonly Color[] Palette =
        {
            new Color(1f, 0.85f, 0.20f, 1f),   // 黄：待办
            new Color(1f, 0.35f, 0.35f, 1f),   // 红：有问题
            new Color(0.35f, 0.85f, 1f, 1f),   // 青：已确认
            new Color(0.92f, 0.92f, 0.92f, 1f),// 白：备注
        };

        static readonly Color HandleColor = new Color(1f, 1f, 1f, 0.92f);

        static Tool _tool;
        static int _colorIndex;
        static float _width = 3f;

        /// <summary>
        /// 色盘里挑出来的颜色。`_colorIndex == -1` 时用它。
        /// 默认给个紫 —— 和四个预设（黄 / 红 / 青 / 白）都不撞，一眼能分出"这是自定义的"。
        /// </summary>
        static Color _customColor = new Color(0.72f, 0.42f, 1f, 1f);

        /// <summary>当前画标记用的颜色。预设点中就用预设，否则用色盘里挑的。</summary>
        static Color CurrentColor
        {
            get
            {
                return _colorIndex >= 0 && _colorIndex < Palette.Length
                    ? Palette[_colorIndex]
                    : _customColor;
            }
        }

        // ---- 笔迹 ----
        static readonly List<Vector3> _current = new List<Vector3>();
        static LDK2DMarkup _target;

        // ---- 框 ----
        static bool _boxDragging;
        static Vector3 _boxStart;
        static Vector3 _boxEnd;

        // ---- 文字编辑 ----
        // 文字和框的标签共用一套编辑状态：同一时间只可能在编辑一个东西。
        static LDK2DMarkText _editingText;
        static LDK2DMarkBox _editingBox;
        static string _editBuffer = string.Empty;
        static bool _editFocusGiven;

        // ---- 统一显示 ----
        static bool _showStrokes = true;
        static bool _showTexts = true;
        static bool _showBoxes = true;

        // ---- 选中 ----
        // 框和文字没有"橡皮"，删它们要先点出来。同一时间最多选中一个。
        static LDK2DMarkBox _selectedBox;
        static LDK2DMarkText _selectedText;

        /// <summary>删除按钮钉在哪个世界坐标上 —— 就是点中它的那一下的位置。</summary>
        static Vector3 _deleteAnchor;

        // ---- 拖动 ----
        // 选中之后按住不放就能拖。位移按**世界坐标**算，不按屏幕像素 ——
        // 视口缩放时同样的鼠标移动量对应的世界距离是不一样的。
        static bool _moving;
        static LDK2DMarkup _moveOwner;
        static Vector3 _moveStartWorld;
        static Vector3 _moveOrigin;
        static Vector3 _moveOriginAnchor;
        static bool _moveUndoPushed;

        // ---- 双击 ----
        static double _lastClickTime;
        static Vector2 _lastClickPoint;

        // ---- 缩放 ----
        // 框：拖四个角之一，**对角固定**。文字：拖右下角，按距离比改字号。
        static bool _resizing;
        static Vector3 _resizeAnchor;      // 框：固定不动的那个对角
        static Vector3 _resizeStartWorld;  // 按下时的世界点
        static Vector3 _resizeOrigin;      // 文字：缩放中心（文字锚点）
        static float _resizeStartFontSize;
        static bool _resizeUndoPushed;

        // ---- 橡皮 ----
        // 按住拖动**连续**擦。一次拖动算一步撤销，所以快照推迟到真的擦到了才拍。
        static bool _erasing;
        static bool _eraseUndoPushed;

        static GUIStyle _textStyle;

        static LDK2DMarkupTool()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            SceneView.duringSceneGui += OnSceneGui;

            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;

            _showStrokes = EditorPrefs.GetBool(PrefShowStrokes, true);
            _showTexts = EditorPrefs.GetBool(PrefShowTexts, true);
            _showBoxes = EditorPrefs.GetBool(PrefShowBoxes, true);

            // 挂进右下角工具箱。为 null 时工具箱只有第一行。
            LDK2DViewportZoomSlider.ExtraRow = DrawExtraRows;
            LDK2DViewportZoomSlider.ExtraHeight = MeasureExtraRows;
        }

        /// <summary>文字绘制用的样式。字号和颜色每次画之前改，复用同一个实例。</summary>
        static GUIStyle TextStyle
        {
            get
            {
                if (_textStyle == null)
                {
                    _textStyle = new GUIStyle(EditorStyles.boldLabel);
                    _textStyle.richText = false;
                    _textStyle.clipping = TextClipping.Overflow;
                }

                return _textStyle;
            }
        }

        // ------------------------------------------------------------------ 主循环

        static void OnSceneGui(SceneView view)
        {
            if (view == null) return;
            if (Event.current == null) return;

            LDK2DMarkup markup = FindMarkup();

            if (markup != null) DrawAll(markup);

            DrawLivePreview();

            // 选中态的删除按钮是个**真控件**，必须先把事件给它，再处理画布输入。
            // 顺序反了的话点按钮会先被当成"在场景里画一笔"。
            DrawSelection();

            HandleInput(view, markup);

            DrawEditField();
        }

        static void DrawAll(LDK2DMarkup markup)
        {
            // 标记要盖在所有地形之上，否则被挡住就白标了。
            CompareFunction oldZ = Handles.zTest;
            Handles.zTest = CompareFunction.Always;

            if (_showBoxes && markup.boxes != null)
            {
                for (int i = 0; i < markup.boxes.Count; i++)
                    if (markup.boxes[i] != null) DrawBox(markup.boxes[i]);
            }

            if (_showStrokes && markup.strokes != null)
            {
                for (int i = 0; i < markup.strokes.Count; i++)
                    if (markup.strokes[i] != null) DrawStroke(markup.strokes[i]);
            }

            if (_showTexts && markup.texts != null)
            {
                for (int i = 0; i < markup.texts.Count; i++)
                    if (markup.texts[i] != null) DrawText(markup.texts[i]);
            }

            Handles.zTest = oldZ;
        }

        /// <summary>正在画的那一笔 / 那个框。收笔之前也要看得见。</summary>
        static void DrawLivePreview()
        {
            Color color = CurrentColor;

            if (_current.Count > 1)
            {
                Color oldColor = Handles.color;
                CompareFunction oldZ = Handles.zTest;

                Handles.color = color;
                Handles.zTest = CompareFunction.Always;
                Handles.DrawAAPolyLine(_width, _current.ToArray());

                Handles.zTest = oldZ;
                Handles.color = oldColor;
            }

            if (!_boxDragging) return;

            Rect box = BoxFromDrag();
            if (box.width <= 0f || box.height <= 0f) return;

            DrawBoxShape(box, 0f, color, false);
        }

        // ------------------------------------------------------------------ 三种标记的绘制

        static void DrawStroke(LDK2DMarkStroke stroke)
        {
            if (stroke.points == null || stroke.points.Count < 2) return;

            // 收笔之后点集不再变，缓存一次就够 —— 每帧 ToArray 会制造垃圾。
            if (stroke.cache == null || stroke.cache.Length != stroke.points.Count)
                stroke.cache = stroke.points.ToArray();

            Color old = Handles.color;
            Handles.color = stroke.color;
            Handles.DrawAAPolyLine(stroke.width, stroke.cache);
            Handles.color = old;
        }

        static void DrawText(LDK2DMarkText text)
        {
            if (string.IsNullOrEmpty(text.text)) return;

            GUIStyle style = TextStyle;
            style.fontSize = Mathf.Max(6, Mathf.RoundToInt(text.size));
            style.normal.textColor = text.color;

            Handles.Label(text.position, text.text, style);
        }

        static void DrawBox(LDK2DMarkBox box)
        {
            Rect rect = BoxRect(box);
            DrawBoxShape(rect, box.center.z, box.color, box == _selectedBox);

            if (string.IsNullOrEmpty(box.label)) return;

            GUIStyle style = TextStyle;
            style.fontSize = Mathf.RoundToInt(TextSize);
            style.normal.textColor = box.color;

            // 标签贴在框的内侧左上角，跟着框走 —— 框挪了字也跟着挪。
            Handles.Label(
                new Vector3(rect.xMin + 0.08f, rect.yMax - 0.08f, box.center.z),
                box.label, style);
        }

        /// <summary>
        /// 框 = 2~8px 描边（随屏幕尺寸变），**平时没有填充**。
        ///
        /// 填充只在选中时出现。为什么默认不给：框是用来圈区域的，一填色就把框里的东西
        /// 盖住了 —— 而框里恰恰是你要看的地形。只有"点中了它、准备删它"的时候，
        /// 才需要那层填充来确认"选的是这一个"。
        ///
        /// 描边不用 `DrawSolidRectangleWithOutline` 的 outline 参数：那个固定 1px，
        /// 视口缩到看全图的时候细得像没有，改不了粗细。
        /// </summary>
        static void DrawBoxShape(Rect rect, float z, Color color, bool selected)
        {
            Vector3[] corners =
            {
                new Vector3(rect.xMin, rect.yMin, z),
                new Vector3(rect.xMin, rect.yMax, z),
                new Vector3(rect.xMax, rect.yMax, z),
                new Vector3(rect.xMax, rect.yMin, z),
            };

            if (selected)
            {
                Color face = color;
                face.a = BoxFillAlpha;

                // outline 传全透明：只要填充，描边自己画，好控制粗细。
                Handles.DrawSolidRectangleWithOutline(corners, face, new Color(0f, 0f, 0f, 0f));
            }

            Color old = Handles.color;
            Handles.color = selected ? Color.Lerp(color, Color.white, 0.45f) : color;

            // 闭合：最后一个点回到起点。
            Handles.DrawAAPolyLine(
                BoxStrokeWidth(rect, z),
                corners[0], corners[1], corners[2], corners[3], corners[0]);

            Handles.color = old;
        }

        /// <summary>
        /// 框描边的粗细：**框在屏幕上越小，线越粗**（2px → 8px）。
        ///
        /// 线宽是屏幕像素，所以视口一缩，框跟着缩、线却不变粗细，
        /// 缩到能看全图时框只剩几十像素、2px 描边几乎看不出形状。
        ///
        /// 判断依据用**框自己的屏幕尺寸**，不用视口缩放倍数 ——
        /// 天生画得很小的框，在 1:1 视口里同样需要粗一点才看得见。
        /// 取宽高里较小的那个：细长条只要有短边撑不住，一样会看不清。
        /// </summary>
        static float BoxStrokeWidth(Rect world, float z)
        {
            Vector2 a = HandleUtility.WorldToGUIPoint(new Vector3(world.xMin, world.yMin, z));
            Vector2 b = HandleUtility.WorldToGUIPoint(new Vector3(world.xMax, world.yMax, z));

            float minDim = Mathf.Min(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));

            // 小 → 0（取最粗），大 → 1（取最细）。
            float t = Mathf.InverseLerp(BoxStrokeTightPx, BoxStrokeWidePx, minDim);
            float stroke = Mathf.Lerp(BoxStrokeMax, BoxStrokeMin, t);

            // 再兜一层：线不许超过框自己短边的 30%，否则框会糊成一个实心色块、
            // 看不出它是"框"。缩到极小时宁可细一点，也别丢掉"这是个框"的信息。
            return Mathf.Min(stroke, Mathf.Max(BoxStrokeMin, minDim * 0.3f));
        }

        // ------------------------------------------------------------------ 输入

        static void HandleInput(SceneView view, LDK2DMarkup markup)
        {
            Event e = Event.current;

            // 正在编辑文字时不接管画布 —— 键盘归输入框，鼠标也不该画东西。
            if (IsEditing) return;

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button != 0) return;

                    // Alt + 左键是视口环绕，别抢。
                    if (e.alt) return;

                    // 落在右下角工具箱上的点击是 UI 的，不是画布上的。
                    // 不判断的话点一下滑条就会在场景里留一个点。
                    if (LDK2DViewportZoomSlider.ToolboxRect.Contains(e.mousePosition)) return;

                    // 手柄优先于"重新选中"：不先判它的话，想拉大框的时候
                    // 会先把框重新选中一遍，手柄永远抓不住。
                    if (TryBeginResize(e.mousePosition))
                    {
                        e.Use();
                        return;
                    }

                    // 点中已有标记就选中它，不画新的。框要求点在**描边**上 ——
                    // 框是用来圈区域的，点里面就选中会让大框把里面的东西全挡掉。
                    if (TrySelectAt(markup, e.mousePosition))
                    {
                        // 双击 = 改内容（文字的正文 / 框的标签）。
                        // 单击只选中，这样"点一下"永远可以接着按住拖。
                        if (IsDoubleClick(e.mousePosition))
                        {
                            BeginEdit(_selectedText, _selectedBox);
                            e.Use();
                            return;
                        }

                        // 选中之后按住就能拖。Undo 在这里拍快照 ——
                        // 一次拖动是一步，不是每个 MouseDrag 一步。
                        BeginMove(markup, e.mousePosition);

                        e.Use();
                        return;
                    }

                    // 没点中任何标记 → 取消选中。
                    ClearSelection();

                    // 没选工具时左键归编辑器（选物体），别吃。
                    if (_tool == Tool.None) return;

                    e.Use();
                    DispatchClick(view, markup, e.mousePosition);
                    break;

                case EventType.MouseDrag:
                    if (e.button != 0) return;

                    if (_erasing)
                    {
                        e.Use();
                        ContinueErase(null, e.mousePosition);
                        break;
                    }

                    if (_resizing)
                    {
                        e.Use();
                        ContinueResize(e.mousePosition);
                        break;
                    }

                    if (_moving)
                    {
                        e.Use();
                        MoveTo(e.mousePosition);
                        break;
                    }

                    if (!_boxDragging && _current.Count == 0) return;

                    e.Use();
                    ContinueDrag(view, e.mousePosition);
                    break;

                case EventType.MouseUp:
                    if (e.button != 0) return;

                    if (_erasing)
                    {
                        e.Use();
                        EndErase();
                        break;
                    }

                    if (_resizing)
                    {
                        e.Use();
                        EndResize();
                        break;
                    }

                    if (_moving)
                    {
                        e.Use();
                        EndMove();
                        break;
                    }

                    if (!_boxDragging && _current.Count == 0) return;

                    e.Use();
                    EndDrag(view);
                    break;

                case EventType.KeyDown:
                    // 编辑框里有回车 / Esc 时走编辑逻辑，这里只兜底"画到一半按 Esc 取消"。
                    if (e.keyCode == KeyCode.Escape && (_boxDragging || _current.Count > 0))
                    {
                        CancelDrag();
                        e.Use();
                    }
                    break;
            }
        }

        static void DispatchClick(SceneView view, LDK2DMarkup markup, Vector2 guiPoint)
        {
            switch (_tool)
            {
                case Tool.Erase:
                    BeginErase(markup, guiPoint);
                    break;

                case Tool.Pen:
                    BeginStroke(view, markup, guiPoint);
                    break;

                case Tool.Box:
                    BeginBox(view, markup, guiPoint);
                    break;

                case Tool.Text:
                    // 文字已经被 TrySelectAt 拦掉了，走到这里只可能是点在**框的内部** ——
                    // 那就是"改这个框的标签"，不是新建文字。
                    if (TryBeginEditBoxAt(markup, guiPoint)) break;
                    CreateText(markup, guiPoint);
                    break;
            }
        }

        // ---- 笔迹 ----

        static void BeginStroke(SceneView view, LDK2DMarkup markup, Vector2 guiPoint)
        {
            markup = markup != null ? markup : EnsureMarkup();
            if (markup == null) return;

            // 在动手之前拍快照：Ctrl+Z 要能**整笔**退掉，不是退一个点。
            Undo.RegisterCompleteObjectUndo(markup, "画标记");

            _target = markup;
            _current.Clear();
            _current.Add(ToWorld(guiPoint));

            view.Repaint();
        }

        static void ContinueStroke(SceneView view, Vector2 guiPoint)
        {
            if (_current.Count == 0) return;

            // **按屏幕距离判断**，不按世界距离：世界距离在不同缩放下对应的像素数差很远，
            // 用世界距离会在放大时点太密、缩小时点太疏。
            Vector2 lastGui = HandleUtility.WorldToGUIPoint(_current[_current.Count - 1]);
            if ((lastGui - guiPoint).sqrMagnitude < SampleDistance * SampleDistance) return;

            _current.Add(ToWorld(guiPoint));
            view.Repaint();
        }

        static void EndStroke()
        {
            if (_target == null || _current.Count < 2)
            {
                // 点一下就松手 = 没画。别往场景里塞一个看不见的点。
                _current.Clear();
                _target = null;
                return;
            }

            List<Vector3> smoothed = Smooth(_current);

            var stroke = new LDK2DMarkStroke
            {
                color = CurrentColor,
                width = _width,
                points = smoothed,
            };
            stroke.cache = smoothed.ToArray();

            _target.strokes.Add(stroke);
            EditorUtility.SetDirty(_target);

            _current.Clear();
            _target = null;

            SceneView.RepaintAll();
        }

        // ---- 框 ----

        static void BeginBox(SceneView view, LDK2DMarkup markup, Vector2 guiPoint)
        {
            markup = markup != null ? markup : EnsureMarkup();
            if (markup == null) return;

            Undo.RegisterCompleteObjectUndo(markup, "画框标记");

            _target = markup;
            _boxDragging = true;
            _boxStart = ToWorld(guiPoint);
            _boxEnd = _boxStart;

            view.Repaint();
        }

        static void ContinueBox(SceneView view, Vector2 guiPoint)
        {
            _boxEnd = ToWorld(guiPoint);
            view.Repaint();
        }

        static void EndBox()
        {
            _boxDragging = false;

            if (_target == null)
            {
                return;
            }

            Rect box = BoxFromDrag();

            if (box.width < MinBoxExtent || box.height < MinBoxExtent)
            {
                // 拖得太小 = 手抖，不当成一次画框。
                _target = null;
                return;
            }

            var mark = new LDK2DMarkBox
            {
                center = new Vector3(box.center.x, box.center.y, 0f),
                size = new Vector2(box.width, box.height),
                color = CurrentColor,
                label = string.Empty,
            };

            _target.boxes.Add(mark);
            EditorUtility.SetDirty(_target);

            // 画完框直接进入标签编辑 —— 空框没有意义，多一步点击就是多一步摩擦。
            BeginEdit(null, mark);

            _target = null;
            SceneView.RepaintAll();
        }

        /// <summary>拖拽起止点 → 世界空间矩形（z 取 0，标记都在场景平面上）。</summary>
        static Rect BoxFromDrag()
        {
            float xMin = Mathf.Min(_boxStart.x, _boxEnd.x);
            float xMax = Mathf.Max(_boxStart.x, _boxEnd.x);
            float yMin = Mathf.Min(_boxStart.y, _boxEnd.y);
            float yMax = Mathf.Max(_boxStart.y, _boxEnd.y);

            return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        static Rect BoxRect(LDK2DMarkBox box)
        {
            return new Rect(
                box.center.x - box.size.x * 0.5f,
                box.center.y - box.size.y * 0.5f,
                box.size.x,
                box.size.y);
        }

        // ---- 文字 ----

        static void CreateText(LDK2DMarkup markup, Vector2 guiPoint)
        {
            markup = markup != null ? markup : EnsureMarkup();
            if (markup == null) return;

            Undo.RegisterCompleteObjectUndo(markup, "添加文字标记");

            var text = new LDK2DMarkText
            {
                position = ToWorld(guiPoint),
                color = CurrentColor,
                size = TextSize,
                text = string.Empty,
            };

            markup.texts.Add(text);
            EditorUtility.SetDirty(markup);

            BeginEdit(text, null);
        }

        /// <summary>
        /// 点中已有标记就选中。**框只认描边**，点内部不算 ——
        /// 框是"圈区域"用的，大框的内部控制范围太大，点里面就选中会把里面的东西全挡掉。
        /// </summary>
        static bool TrySelectAt(LDK2DMarkup markup, Vector2 guiPoint)
        {
            if (markup == null) return false;

            // 文字优先：面积最小，命中就是明确的意图。
            for (int i = markup.texts.Count - 1; i >= 0; i--)
            {
                LDK2DMarkText text = markup.texts[i];
                if (text == null || string.IsNullOrEmpty(text.text)) continue;
                if (!TextGuiRect(text).Contains(guiPoint)) continue;

                _selectedText = text;
                _selectedBox = null;
                _deleteAnchor = text.position;
                SceneView.RepaintAll();
                return true;
            }

            for (int i = markup.boxes.Count - 1; i >= 0; i--)
            {
                LDK2DMarkBox box = markup.boxes[i];
                if (box == null) continue;
                if (!HitBoxEdge(box, guiPoint)) continue;

                _selectedBox = box;
                _selectedText = null;
                _deleteAnchor = ToWorld(guiPoint);
                SceneView.RepaintAll();
                return true;
            }

            return false;
        }

        static void ClearSelection()
        {
            CancelMove();
            CancelResize();

            if (_selectedBox == null && _selectedText == null) return;

            _selectedBox = null;
            _selectedText = null;
            SceneView.RepaintAll();
        }

        /// <summary>
        /// 文字工具下点在**框的内部** = 改这个框的标签。
        ///
        /// 框本身是靠点描边选中的，选中只出删除按钮 —— 想改标签走这条路。
        /// 没有这条的话，框的标签写错了就只能删了重画。
        /// </summary>
        static bool TryBeginEditBoxAt(LDK2DMarkup markup, Vector2 guiPoint)
        {
            if (markup == null) return false;

            for (int i = markup.boxes.Count - 1; i >= 0; i--)
            {
                LDK2DMarkBox box = markup.boxes[i];
                if (box == null) continue;
                if (!BoxGuiRect(box).Contains(guiPoint)) continue;

                BeginEdit(null, box);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 选中态的视觉：框补一层淡填充 + 描边提亮；文字只出按钮不动字。
        /// 删除按钮钉在**点中它的那一下的位置**，跟着场景走 —— 视口缩放时不会飘到别处。
        /// </summary>
        static void DrawSelection()
        {
            if (_selectedBox == null && _selectedText == null) return;
            if (Event.current == null) return;

            // 选中的东西可能已经被别的操作删掉了（撤销、Hierarchy 里删容器）。
            LDK2DMarkup markup = FindMarkup();
            if (markup == null ||
                (_selectedBox != null && !markup.boxes.Contains(_selectedBox)) ||
                (_selectedText != null && !markup.texts.Contains(_selectedText)))
            {
                ClearSelection();
                return;
            }

            Handles.BeginGUI();

            // 缩放手柄：框四个角，文字一个角。
            if (_selectedBox != null) DrawBoxHandles(_selectedBox);
            else if (_selectedText != null) DrawTextHandle(_selectedText);

            // 按钮摆在命中点的**正上方**，不压住刚点中的那个东西。
            Vector2 gui = HandleUtility.WorldToGUIPoint(_deleteAnchor);
            Rect rect = new Rect(gui.x - 10f, gui.y - 28f, 20f, 20f);

            if (GUI.Button(rect, "×", EditorStyles.miniButton))
                DeleteSelected();

            Handles.EndGUI();
        }

        /// <summary>框的四个角各画一个小方块。尺寸是屏幕像素，不随视口缩放变。</summary>
        static void DrawBoxHandles(LDK2DMarkBox box)
        {
            Vector3[] corners = BoxCorners(box);
            float half = HandleSize * 0.5f;

            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 gui = HandleUtility.WorldToGUIPoint(corners[i]);

                EditorGUI.DrawRect(
                    new Rect(gui.x - half, gui.y - half, HandleSize, HandleSize),
                    HandleColor);
            }
        }

        static void DrawTextHandle(LDK2DMarkText text)
        {
            Vector2 gui = TextHandleGui(text);
            float half = HandleSize * 0.5f;

            EditorGUI.DrawRect(
                new Rect(gui.x - half, gui.y - half, HandleSize, HandleSize),
                HandleColor);
        }

        static void DeleteSelected()
        {
            LDK2DMarkup markup = FindMarkup();

            if (markup != null)
            {
                Undo.RegisterCompleteObjectUndo(markup, "删除标记");

                if (_selectedText != null) markup.texts.Remove(_selectedText);
                else if (_selectedBox != null) markup.boxes.Remove(_selectedBox);

                EditorUtility.SetDirty(markup);
            }

            ClearSelection();
            SceneView.RepaintAll();
        }

        /// <summary>点到框的**描边**才算命中。容差按屏幕像素算，缩放时手感一致。</summary>
        static bool HitBoxEdge(LDK2DMarkBox box, Vector2 guiPoint)
        {
            Rect rect = BoxGuiRect(box);

            // 先粗筛：连框外扩一个容差的范围都没进，直接排除。
            if (guiPoint.x < rect.xMin - EdgeTolerance || guiPoint.x > rect.xMax + EdgeTolerance) return false;
            if (guiPoint.y < rect.yMin - EdgeTolerance || guiPoint.y > rect.yMax + EdgeTolerance) return false;

            return Mathf.Abs(guiPoint.x - rect.xMin) <= EdgeTolerance
                || Mathf.Abs(guiPoint.x - rect.xMax) <= EdgeTolerance
                || Mathf.Abs(guiPoint.y - rect.yMin) <= EdgeTolerance
                || Mathf.Abs(guiPoint.y - rect.yMax) <= EdgeTolerance;
        }

        // ---- 拖拽调度 ----

        static void ContinueDrag(SceneView view, Vector2 guiPoint)
        {
            if (_boxDragging) ContinueBox(view, guiPoint);
            else ContinueStroke(view, guiPoint);
        }

        static void EndDrag(SceneView view)
        {
            if (_boxDragging) EndBox();
            else EndStroke();
        }

        static void CancelDrag()
        {
            _boxDragging = false;
            _current.Clear();
            _target = null;
            EndErase();
        }

        // ---- 拖动已有的框 / 文字 ----

        static void BeginMove(LDK2DMarkup markup, Vector2 guiPoint)
        {
            if (markup == null) return;
            if (_selectedBox == null && _selectedText == null) return;

            _moving = true;
            _moveOwner = markup;
            _moveStartWorld = ToWorld(guiPoint);
            _moveOriginAnchor = _deleteAnchor;
            _moveUndoPushed = false;

            _moveOrigin = _selectedBox != null ? _selectedBox.center : _selectedText.position;
        }

        /// <summary>
        /// 位移按**世界坐标**算，不按屏幕像素 —— 视口缩放时同样的鼠标移动量
        /// 对应的世界距离完全不一样，按像素算会在放大后拖不动、缩小后拖飞。
        /// </summary>
        static void MoveTo(Vector2 guiPoint)
        {
            // Undo 推迟到**真的动了**才拍。点一下只是选中、没有拖动的话，
            // 在这里拍快照会让 Ctrl+Z 多出一堆"什么都没变"的步骤。
            if (!_moveUndoPushed && _moveOwner != null)
            {
                Undo.RegisterCompleteObjectUndo(_moveOwner, "移动标记");
                _moveUndoPushed = true;
            }

            Vector3 delta = ToWorld(guiPoint) - _moveStartWorld;

            if (_selectedBox != null) _selectedBox.center = _moveOrigin + delta;
            else if (_selectedText != null) _selectedText.position = _moveOrigin + delta;
            else { CancelMove(); return; }

            // 删除按钮跟着走，不然拖完它就留在原地了。
            _deleteAnchor = _moveOriginAnchor + delta;

            SceneView.RepaintAll();
        }

        static void EndMove()
        {
            if (!_moving) return;

            _moving = false;

            if (_moveOwner != null) EditorUtility.SetDirty(_moveOwner);
            _moveOwner = null;

            SceneView.RepaintAll();
        }

        static void CancelMove()
        {
            _moving = false;
            _moveOwner = null;
        }

        // ---- 缩放已有的框 / 文字 ----

        /// <summary>框的四个角，顺序：左下 / 右下 / 右上 / 左上。对角是 `(i + 2) % 4`。</summary>
        static Vector3[] BoxCorners(LDK2DMarkBox box)
        {
            Rect r = BoxRect(box);
            float z = box.center.z;

            return new[]
            {
                new Vector3(r.xMin, r.yMin, z),
                new Vector3(r.xMax, r.yMin, z),
                new Vector3(r.xMax, r.yMax, z),
                new Vector3(r.xMin, r.yMax, z),
            };
        }

        static int HitHandle(LDK2DMarkBox box, Vector2 guiPoint)
        {
            Vector3[] corners = BoxCorners(box);

            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 gui = HandleUtility.WorldToGUIPoint(corners[i]);
                if ((gui - guiPoint).sqrMagnitude <= HandleHitRadius * HandleHitRadius) return i;
            }

            return -1;
        }

        /// <summary>文字的手柄只有一个，钉在文字右下角。</summary>
        static Vector2 TextHandleGui(LDK2DMarkText text)
        {
            Rect r = TextGuiRect(text);
            return new Vector2(r.xMax, r.yMax);
        }

        static bool TryBeginResize(Vector2 guiPoint)
        {
            if (_selectedBox != null)
            {
                int handle = HitHandle(_selectedBox, guiPoint);
                if (handle < 0) return false;

                _resizing = true;
                _resizeUndoPushed = false;

                // 对角固定：拖左下角时右上角不动，这是缩放的默认直觉。
                _resizeAnchor = BoxCorners(_selectedBox)[(handle + 2) % 4];
                _resizeStartWorld = ToWorld(guiPoint);
                return true;
            }

            if (_selectedText != null)
            {
                Vector2 handle = TextHandleGui(_selectedText);
                if ((handle - guiPoint).sqrMagnitude > HandleHitRadius * HandleHitRadius) return false;

                _resizing = true;
                _resizeUndoPushed = false;
                _resizeStartWorld = ToWorld(guiPoint);
                _resizeOrigin = _selectedText.position;
                _resizeStartFontSize = _selectedText.size;
                return true;
            }

            return false;
        }

        static void ContinueResize(Vector2 guiPoint)
        {
            if (!_resizing) return;

            LDK2DMarkup markup = FindMarkup();
            if (markup == null || (_selectedBox == null && _selectedText == null))
            {
                CancelResize();
                return;
            }

            // 和拖动一样：真的改了才拍快照，纯点一下不留空撤销步骤。
            if (!_resizeUndoPushed)
            {
                Undo.RegisterCompleteObjectUndo(markup, "缩放标记");
                _resizeUndoPushed = true;
            }

            if (_selectedBox != null) ResizeBox(ToWorld(guiPoint));
            else ResizeText(ToWorld(guiPoint));
        }

        /// <summary>
        /// 框缩放：拖动的点和**固定的对角**拼成新矩形。
        /// `Min` / `Max` 兜住拖过头翻面的情况 —— 直接算会得到负的宽高。
        /// </summary>
        static void ResizeBox(Vector3 now)
        {
            LDK2DMarkBox box = _selectedBox;

            float xMin = Mathf.Min(now.x, _resizeAnchor.x);
            float xMax = Mathf.Max(now.x, _resizeAnchor.x);
            float yMin = Mathf.Min(now.y, _resizeAnchor.y);
            float yMax = Mathf.Max(now.y, _resizeAnchor.y);

            Vector3 oldCenter = box.center;

            box.size = new Vector2(
                Mathf.Max(MinBoxExtent, xMax - xMin),
                Mathf.Max(MinBoxExtent, yMax - yMin));

            box.center = new Vector3((xMin + xMax) * 0.5f, (yMin + yMax) * 0.5f, oldCenter.z);

            // 删除按钮跟着框的中心走，不然缩完它就偏到一边了。
            _deleteAnchor += box.center - oldCenter;

            SceneView.RepaintAll();
        }

        /// <summary>
        /// 文字缩放：按"鼠标到文字的**距离比**"改字号。
        /// 用绝对位移不行 —— 字号和距离不是线性关系，拖起来会前半段没反应、后半段猛跳。
        /// </summary>
        static void ResizeText(Vector3 now)
        {
            float startDist = Vector2.Distance(
                new Vector2(_resizeStartWorld.x, _resizeStartWorld.y),
                new Vector2(_resizeOrigin.x, _resizeOrigin.y));

            float nowDist = Vector2.Distance(
                new Vector2(now.x, now.y),
                new Vector2(_resizeOrigin.x, _resizeOrigin.y));

            float ratio = startDist > 0.0001f ? nowDist / startDist : 1f;

            _selectedText.size = Mathf.Clamp(
                _resizeStartFontSize * ratio, MinFontSize, MaxFontSize);

            SceneView.RepaintAll();
        }

        static void EndResize()
        {
            if (!_resizing) return;

            _resizing = false;
            _resizeUndoPushed = false;

            LDK2DMarkup markup = FindMarkup();
            if (markup != null) EditorUtility.SetDirty(markup);

            SceneView.RepaintAll();
        }

        static void CancelResize()
        {
            _resizing = false;
            _resizeUndoPushed = false;
        }

        /// <summary>
        /// 双击判定。单击只选中、可以接着拖，所以"改内容"必须换个手势。
        /// 时间窗 `0.35s` 是 Unity 编辑器里双击的惯常值。
        /// </summary>
        static bool IsDoubleClick(Vector2 guiPoint)
        {
            double now = EditorApplication.timeSinceStartup;

            bool isDouble =
                now - _lastClickTime <= 0.35 &&
                (guiPoint - _lastClickPoint).sqrMagnitude <= 100f;

            _lastClickTime = now;
            _lastClickPoint = guiPoint;

            return isDouble;
        }

        // ------------------------------------------------------------------ 文字编辑

        static bool IsEditing
        {
            get { return _editingText != null || _editingBox != null; }
        }

        static void BeginEdit(LDK2DMarkText text, LDK2DMarkBox box)
        {
            _editingText = text;
            _editingBox = box;
            _editBuffer = text != null ? text.text : (box != null ? box.label : string.Empty);
            _editFocusGiven = false;
        }

        static Vector3 EditAnchor
        {
            get
            {
                if (_editingText != null) return _editingText.position;
                if (_editingBox != null) return BoxRect(_editingBox).min + Vector2.up * _editingBox.size.y;
                return Vector3.zero;
            }
        }

        /// <summary>
        /// 编辑框画在标记旁边。**必须放在 `Handles.BeginGUI` 之后** —— 它是个真控件，
        /// 要在 GUI 层才能收键盘。
        /// </summary>
        static void DrawEditField()
        {
            if (!IsEditing) return;
            if (Event.current == null) return;

            HandleEditKeys();

            Vector2 gui = HandleUtility.WorldToGUIPoint(EditAnchor);

            Handles.BeginGUI();

            const float width = 220f;
            const float height = 20f;

            Rect rect = new Rect(gui.x, gui.y - height - 4f, width, height);

            GUI.SetNextControlName(EditControlName);
            _editBuffer = GUI.TextField(rect, _editBuffer);

            if (!_editFocusGiven)
            {
                GUI.FocusControl(EditControlName);
                _editFocusGiven = true;
            }

            GUIStyle hint = EditorStyles.miniLabel;
            GUI.Label(new Rect(rect.x, rect.yMax + 1f, width, 16f), "回车确认 · Esc 取消", hint);

            Handles.EndGUI();

            // 点到别处就提交 —— 编辑框失焦说明用户已经在做别的事了。
            if (Event.current.type == EventType.MouseDown && !rect.Contains(Event.current.mousePosition))
            {
                CommitEdit();
                Event.current.Use();
            }
        }

        static void HandleEditKeys()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown) return;

            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                CommitEdit();
                e.Use();
            }
            else if (e.keyCode == KeyCode.Escape)
            {
                CancelEdit();
                e.Use();
            }
        }

        static void CommitEdit()
        {
            if (!IsEditing) return;

            LDK2DMarkup markup = FindMarkup();

            if (markup != null)
            {
                Undo.RegisterCompleteObjectUndo(markup, "编辑标记文字");

                if (_editingText != null)
                {
                    _editingText.text = _editBuffer;

                    // 编辑完变成空的就当没这个标记 —— 空文字在场景里是不可见也不可点的。
                    if (string.IsNullOrEmpty(_editingText.text))
                        markup.texts.Remove(_editingText);
                }
                else if (_editingBox != null)
                {
                    _editingBox.label = _editBuffer;
                }

                EditorUtility.SetDirty(markup);
            }

            ClearEdit();
        }

        static void CancelEdit()
        {
            LDK2DMarkup markup = FindMarkup();

            // 新建出来的空文字要撤掉；改旧内容的话 Esc 就是"不保存"，原值不动。
            if (markup != null && _editingText != null && string.IsNullOrEmpty(_editingText.text))
                markup.texts.Remove(_editingText);

            ClearEdit();
        }

        static void ClearEdit()
        {
            _editingText = null;
            _editingBox = null;
            _editBuffer = string.Empty;
            _editFocusGiven = false;

            GUIUtility.keyboardControl = 0;
            SceneView.RepaintAll();
        }

        // ------------------------------------------------------------------ 命中测试

        static Rect TextGuiRect(LDK2DMarkText text)
        {
            GUIStyle style = TextStyle;
            style.fontSize = Mathf.Max(6, Mathf.RoundToInt(text.size));

            Vector2 size = style.CalcSize(new GUIContent(text.text));
            Vector2 gui = HandleUtility.WorldToGUIPoint(text.position);

            return new Rect(gui.x, gui.y, Mathf.Max(1f, size.x), Mathf.Max(1f, size.y));
        }

        static Rect BoxGuiRect(LDK2DMarkBox box)
        {
            Rect world = BoxRect(box);

            Vector2 a = HandleUtility.WorldToGUIPoint(new Vector3(world.xMin, world.yMin, box.center.z));
            Vector2 b = HandleUtility.WorldToGUIPoint(new Vector3(world.xMax, world.yMax, box.center.z));

            return Rect.MinMaxRect(
                Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        // ------------------------------------------------------------------ 橡皮

        /// <summary>
        /// 橡皮**只擦笔迹**，而且**只擦圈经过的那一段** —— 不是把整笔删掉。
        ///
        /// 框和文字不是"画"出来的，是"摆"出来的 —— 手一滑擦掉一个框，里面圈的区域就白标了；
        /// 擦掉一段文字，那句话就再也想不起来了。这两个走"点出来再删"的路子：
        /// 点框的描边 / 点文字 → 出现删除按钮 → 点按钮。
        /// </summary>
        static void BeginErase(LDK2DMarkup markup, Vector2 guiPoint)
        {
            _erasing = true;
            _eraseUndoPushed = false;

            ContinueErase(markup, guiPoint);
        }

        /// <summary>
        /// 按住拖动时连续擦。**整次拖动只算一步撤销** —— 每个 `MouseDrag` 都拍快照的话，
        /// 擦一下要按几十次 Ctrl+Z 才退得回来。
        /// </summary>
        static void ContinueErase(LDK2DMarkup markup, Vector2 guiPoint)
        {
            markup = markup != null ? markup : FindMarkup();
            if (markup == null) return;

            if (!_eraseUndoPushed)
            {
                // 快照必须在**改之前**拍，所以先问一句"这一下会不会擦到东西"。
                // 反过来先拍再擦的话，点在空白处也会留下一步什么都没变的撤销。
                if (!AnyStrokeHit(markup, guiPoint)) return;

                Undo.RegisterCompleteObjectUndo(markup, "擦除笔迹");
                _eraseUndoPushed = true;
            }

            if (!EraseAt(markup, guiPoint)) return;

            EditorUtility.SetDirty(markup);
            SceneView.RepaintAll();
        }

        static void EndErase()
        {
            _erasing = false;
            _eraseUndoPushed = false;
        }

        static bool AnyStrokeHit(LDK2DMarkup markup, Vector2 guiPoint)
        {
            for (int i = 0; i < markup.strokes.Count; i++)
            {
                LDK2DMarkStroke stroke = markup.strokes[i];
                if (stroke == null || stroke.points == null || stroke.points.Count < 2) continue;
                if (StrokeHit(stroke, guiPoint)) return true;
            }

            return false;
        }

        /// <summary>
        /// 把每一笔里**落在橡皮圈内**的部分剪掉，剩下的按连续段拆成几条新笔迹。
        /// 返回是否真的改动了什么。
        ///
        /// 从后往前遍历：插入新笔迹会让后面的下标变大，倒着走就不受影响。
        /// </summary>
        static bool EraseAt(LDK2DMarkup markup, Vector2 guiPoint)
        {
            bool changed = false;

            for (int s = markup.strokes.Count - 1; s >= 0; s--)
            {
                LDK2DMarkStroke stroke = markup.strokes[s];
                if (stroke == null || stroke.points == null || stroke.points.Count < 2) continue;
                if (!StrokeHit(stroke, guiPoint)) continue;

                List<List<Vector3>> pieces = ClipStroke(stroke, guiPoint);

                markup.strokes.RemoveAt(s);

                // 插回**原来的位置**，别都堆到列表末尾 —— 那会改掉笔迹之间的叠放顺序。
                int at = s;
                for (int p = 0; p < pieces.Count; p++)
                {
                    if (pieces[p].Count < 2) continue;   // 一个点画不出线

                    var piece = new LDK2DMarkStroke
                    {
                        color = stroke.color,
                        width = stroke.width,
                        points = pieces[p],
                    };
                    piece.cache = pieces[p].ToArray();

                    markup.strokes.Insert(at++, piece);
                }

                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// 这一笔有没有被橡皮圈碰到。
        ///
        /// 先用**世界坐标的包围盒**粗筛：判定要逐点 `WorldToGUIPoint`，每帧对每一笔的
        /// 每个点都算一遍太贵，而绝大多数笔离橡皮很远，不值得换算。
        /// 包围盒只会比真实范围大，所以粗筛不会漏。
        /// </summary>
        static bool StrokeHit(LDK2DMarkStroke stroke, Vector2 guiPoint)
        {
            Vector3 center = ToWorld(guiPoint);
            float radius = WorldEraseRadius(guiPoint);

            float xMin = float.MaxValue, xMax = float.MinValue;
            float yMin = float.MaxValue, yMax = float.MinValue;

            for (int i = 0; i < stroke.points.Count; i++)
            {
                Vector3 p = stroke.points[i];
                if (p.x < xMin) xMin = p.x;
                if (p.x > xMax) xMax = p.x;
                if (p.y < yMin) yMin = p.y;
                if (p.y > yMax) yMax = p.y;
            }

            if (xMax < center.x - radius || xMin > center.x + radius) return false;
            if (yMax < center.y - radius || yMin > center.y + radius) return false;

            for (int i = 0; i < stroke.points.Count - 1; i++)
            {
                Vector2 a = HandleUtility.WorldToGUIPoint(stroke.points[i]);
                Vector2 b = HandleUtility.WorldToGUIPoint(stroke.points[i + 1]);

                if (SegmentCircleRange(a, b, guiPoint, EraseRadius, out _, out _)) return true;
            }

            return false;
        }

        /// <summary>橡皮半径从屏幕像素换算成世界单位。正交视图下这个换算是等比的。</summary>
        static float WorldEraseRadius(Vector2 guiPoint)
        {
            Vector3 center = ToWorld(guiPoint);
            Vector3 edge = ToWorld(guiPoint + new Vector2(EraseRadius, 0f));

            return Vector3.Distance(center, edge);
        }

        /// <summary>
        /// 剪一笔：逐段求"落在圆内的参数区间"，圆外的部分留下，再把首尾相接的段串成折线。
        ///
        /// 为什么按**段**算而不是按点算：采点间距是画的时候的屏幕 2px，
        /// 视口放大之后这两个点可能隔着几十像素 —— 只删"圈内的点"会整段跳过去，
        /// 表现就是"擦了半天那一段还在"。
        /// </summary>
        static List<List<Vector3>> ClipStroke(LDK2DMarkStroke stroke, Vector2 guiPoint)
        {
            // 先攒"留下来的小段"，每项两个世界点。
            var kept = new List<Vector3[]>();

            for (int i = 0; i < stroke.points.Count - 1; i++)
            {
                Vector3 wa = stroke.points[i];
                Vector3 wb = stroke.points[i + 1];

                Vector2 a = HandleUtility.WorldToGUIPoint(wa);
                Vector2 b = HandleUtility.WorldToGUIPoint(wb);

                // 和圆没交集：整段原样留下。
                if (!SegmentCircleRange(a, b, guiPoint, EraseRadius, out float t0, out float t1))
                {
                    kept.Add(new[] { wa, wb });
                    continue;
                }

                // 圈外的前半截。
                if (t0 > 0f) kept.Add(new[] { wa, Vector3.Lerp(wa, wb, t0) });

                // 圈外的后半截。
                if (t1 < 1f) kept.Add(new[] { Vector3.Lerp(wa, wb, t1), wb });
            }

            // 串成折线：上一段的终点和下一段的起点重合就接上，否则断开成新的一笔。
            var pieces = new List<List<Vector3>>();
            List<Vector3> current = null;

            for (int i = 0; i < kept.Count; i++)
            {
                Vector3[] seg = kept[i];

                if (current != null && current[current.Count - 1] == seg[0])
                {
                    current.Add(seg[1]);
                    continue;
                }

                if (current != null) pieces.Add(current);

                current = new List<Vector3> { seg[0], seg[1] };
            }

            if (current != null) pieces.Add(current);

            return pieces;
        }

        /// <summary>
        /// 线段 `a→b` 落在圆（圆心 `c`、半径 `r`）**内部**的参数区间 `[t0, t1]`。
        /// 返回 `false` = 和圆没有交集（整段都在外面）。
        ///
        /// 解 `|a + t(b-a) - c|² = r²` 这个一元二次方程：两根之间就是圆内。
        /// </summary>
        static bool SegmentCircleRange(
            Vector2 a, Vector2 b, Vector2 c, float r, out float t0, out float t1)
        {
            t0 = 0f;
            t1 = 1f;

            Vector2 d = b - a;
            Vector2 f = a - c;

            float A = Vector2.Dot(d, d);

            // 退化成一个点：看它在不在圈里。
            if (A < 1e-6f) return f.sqrMagnitude <= r * r;

            float B = 2f * Vector2.Dot(f, d);
            float C = Vector2.Dot(f, f) - r * r;
            float disc = B * B - 4f * A * C;

            // 没有实根：要么整段在圆外，要么整段在圆内（端点 a 在圈内 ⇒ 整段在内）。
            if (disc <= 0f) return C <= 0f;

            float root = Mathf.Sqrt(disc);
            float ta = (-B - root) / (2f * A);
            float tb = (-B + root) / (2f * A);

            // 圆内区间截到 [0, 1] 上。
            t0 = Mathf.Max(0f, ta);
            t1 = Mathf.Min(1f, tb);

            // 截完什么都不剩 = 圆没碰到这一段。
            return t0 < t1;
        }

        // ------------------------------------------------------------------ 坐标

        /// <summary>
        /// 屏幕坐标 → 世界坐标。2D 关卡里标记落在 z = 0 平面上。
        ///
        /// 不能直接用 `GUIPointToWorldRay().origin`：那是相机近平面上的点，
        /// 正交视图下离场景还有一段距离。必须沿射线投到 z = 0。
        /// </summary>
        static Vector3 ToWorld(Vector2 guiPoint)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(guiPoint);

            // 射线平行于 XY 平面时投不到 z = 0，直接用起点兜底。
            if (Mathf.Abs(ray.direction.z) < 0.0001f) return ray.origin;

            float t = -ray.origin.z / ray.direction.z;
            return ray.origin + ray.direction * t;
        }

        /// <summary>
        /// Chaikin 细分：每个线段切成 25% / 75% 两个点，再补回首尾。
        /// 一轮就够把折线抹成弧线了，两轮点会翻四倍，没必要。
        /// </summary>
        static List<Vector3> Smooth(List<Vector3> src)
        {
            if (src.Count < 3) return new List<Vector3>(src);

            var result = new List<Vector3>(src.Count * 2);
            result.Add(src[0]);

            for (int i = 0; i < src.Count - 1; i++)
            {
                Vector3 a = src[i];
                Vector3 b = src[i + 1];

                result.Add(Vector3.Lerp(a, b, 0.25f));
                result.Add(Vector3.Lerp(a, b, 0.75f));
            }

            result.Add(src[src.Count - 1]);
            return result;
        }

        // ------------------------------------------------------------------ 场景对象

        static LDK2DMarkup FindMarkup()
        {
            return Object.FindObjectOfType<LDK2DMarkup>(false);
        }

        /// <summary>第一次画的时候才建容器 —— 没画过标记的场景里不该多出一个空对象。</summary>
        static LDK2DMarkup EnsureMarkup()
        {
            LDK2DMarkup found = FindMarkup();
            if (found != null) return found;

            var go = new GameObject(ContainerName);
            Undo.RegisterCreatedObjectUndo(go, "创建标记容器");

            return go.AddComponent<LDK2DMarkup>();
        }

        // ------------------------------------------------------------------ 工具箱

        /// <summary>工具箱第二行：工具 + 颜色 + 线宽 + 橡皮。</summary>
        static void DrawExtraRows()
        {
            DrawToolRow();
            DrawVisibilityRow();
        }

        /// <summary>第二、三行占多高。给 `LDK2DViewportZoomSlider` 算工具箱总高度用。</summary>
        static float MeasureExtraRows()
        {
            return EditorGUIUtility.singleLineHeight * 2f + 8f;
        }

        static void DrawToolRow()
        {
            GUILayout.BeginHorizontal();

            ToolButton(Tool.Pen, "笔");
            ToolButton(Tool.Box, "框");
            ToolButton(Tool.Text, "字");

            // 工具没选时后面的控件全是噪音，不预先铺开。
            if (_tool != Tool.None)
            {
                // 四个预设色对应四类语义，点一下就走，不用每次开色盘。
                for (int i = 0; i < Palette.Length; i++)
                {
                    Color old = GUI.backgroundColor;
                    GUI.backgroundColor = Palette[i];

                    bool on = GUILayout.Toggle(
                        _colorIndex == i, GUIContent.none,
                        EditorStyles.miniButton, GUILayout.Width(18f));

                    GUI.backgroundColor = old;

                    if (on && _colorIndex != i) _colorIndex = i;
                }

                // 第五个 = 色盘。空一点距离，让人看出它不是第五个预设。
                GUILayout.Space(3f);
                DrawColorWheelSwatch();

                // 线宽只对笔迹有意义；框和文字各有固定尺寸。
                if (_tool == Tool.Pen)
                    _width = GUILayout.HorizontalSlider(
                        _width, MinWidth, MaxWidth, GUILayout.Width(48f));

                ToolButton(Tool.Erase, "擦");
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// 色盘：左边是当前色块（点它开取色器），右边是吸管（从视口里吸颜色）。
        ///
        /// 里面是 Unity 自带的取色器 —— HSV 圆盘 + RGB / 十六进制输入 +
        /// 存过的色板。要什么色自己调。
        ///
        /// 为什么用 `EditorGUI.ColorField` 而不是自己画色盘：它已经把取色器的
        /// 那套东西（圆盘、色板、吸管、撤销）都接好了，自己画一遍只会更差。
        /// </summary>
        static void DrawColorWheelSwatch()
        {
            Rect rect = GUILayoutUtility.GetRect(
                ColorFieldWidth, ColorFieldWidth,
                GUILayout.Width(ColorFieldWidth),
                GUILayout.Height(EditorGUIUtility.singleLineHeight));

            // 自定义色生效时描一圈白边 —— 四个预设都没亮，得让人看出"现在用的是色盘这个"。
            if (_colorIndex < 0)
                EditorGUI.DrawRect(
                    new Rect(rect.x - 1f, rect.y - 1f, rect.width + 2f, rect.height + 2f),
                    HandleColor);

            Color picked = EditorGUI.ColorField(
                rect, GUIContent.none, _customColor,
                true,   // 吸管：从视口里吸地形颜色
                false,  // 不要透明度：标记是纯色，半透明反而看不清
                false); // 不要 HDR

            if (ColorEquals(picked, _customColor)) return;

            _customColor = picked;
            _colorIndex = -1;
            SceneView.RepaintAll();
        }

        static bool ColorEquals(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 1e-4f
                && Mathf.Abs(a.g - b.g) < 1e-4f
                && Mathf.Abs(a.b - b.b) < 1e-4f
                && Mathf.Abs(a.a - b.a) < 1e-4f;
        }

        static void ToolButton(Tool tool, string label)
        {
            bool on = GUILayout.Toggle(
                _tool == tool, label, EditorStyles.miniButton, GUILayout.Width(22f));

            if (on == (_tool == tool)) return;

            SetTool(on ? tool : Tool.None);
        }

        static void SetTool(Tool tool)
        {
            if (_tool == tool) return;

            // 切工具时把手上没画完的东西丢掉，别让它粘到下一次。
            CancelDrag();
            CommitEdit();
            ClearSelection();

            _tool = tool;
            SceneView.RepaintAll();
        }

        /// <summary>
        /// 第三行：按类型统一显示 / 隐藏。**不是"一键全关"** ——
        /// 三种标记的用途不一样（笔迹记想法、框圈区域、文字写结论），
        /// 经常需要"只看框、把笔迹藏起来"，所以分开控制。
        /// </summary>
        static void DrawVisibilityRow()
        {
            GUILayout.BeginHorizontal();

            bool strokes = GUILayout.Toggle(
                _showStrokes, new GUIContent("笔画", "统一显示 / 隐藏所有笔迹"),
                EditorStyles.miniButton, GUILayout.Width(48f));

            bool texts = GUILayout.Toggle(
                _showTexts, new GUIContent("文字", "统一显示 / 隐藏所有文字"),
                EditorStyles.miniButton, GUILayout.Width(48f));

            bool boxes = GUILayout.Toggle(
                _showBoxes, new GUIContent("框", "统一显示 / 隐藏所有框"),
                EditorStyles.miniButton, GUILayout.Width(48f));

            if (strokes != _showStrokes)
            {
                _showStrokes = strokes;
                EditorPrefs.SetBool(PrefShowStrokes, strokes);
                SceneView.RepaintAll();
            }

            if (texts != _showTexts)
            {
                _showTexts = texts;
                EditorPrefs.SetBool(PrefShowTexts, texts);
                SceneView.RepaintAll();
            }

            if (boxes != _showBoxes)
            {
                _showBoxes = boxes;
                EditorPrefs.SetBool(PrefShowBoxes, boxes);
                SceneView.RepaintAll();
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        // ------------------------------------------------------------------ Play 模式

        /// <summary>
        /// Play 模式下画的标记，退出时要活下来。
        ///
        /// 退出 Play 会销毁运行时创建的对象、并把编辑模式的对象恢复成进 Play 前的样子，
        /// 所以 Play 期间画的东西会一起没掉。这里在退出**之前**把数据抄成 JSON 存进
        /// <see cref="SessionState"/>（跨域重载也活着），回到编辑模式再写回去。
        ///
        /// 直接覆盖而不是合并：Play 场景是编辑模式场景的副本，它的数据本来就是超集。
        /// </summary>
        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                LDK2DMarkup markup = FindMarkup();
                if (markup != null)
                    SessionState.SetString(RescueKey, JsonUtility.ToJson(markup));

                return;
            }

            if (state != PlayModeStateChange.EnteredEditMode) return;

            string json = SessionState.GetString(RescueKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return;

            SessionState.EraseString(RescueKey);

            LDK2DMarkup target = FindMarkup();

            // Play 里把标记对象删了，就别复活它 —— 那是用户的意思。
            if (target == null) return;

            Undo.RegisterCompleteObjectUndo(target, "同步 Play 模式标记");
            JsonUtility.FromJsonOverwrite(json, target);
            EditorUtility.SetDirty(target);
        }
    }
}
