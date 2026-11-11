using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Terrain2D.EditorTools
{
    /// <summary>
    /// 视口右下角的「碰撞体」开关：把场景里**实际生效的碰撞轮廓**画出来。
    ///
    /// 为什么需要它：Unity 自己的碰撞体 Gizmo 受 Gizmos 总开关、层级可见性、
    /// 以及 2D/3D 模式影响，而且在深色地面上几乎看不见。排查"走过去突然没碰撞了"
    /// 的时候，最想知道的就是**碰撞轮廓到底画到哪、有没有把整块包住**。
    ///
    /// 画的范围是**场景里所有 Collider2D**，不只是地形 —— 敌人 / NPC / 奖励点的
    /// 碰撞盒也一并画出来，这样"这块地方为什么走不过去"能一眼看到底是谁挡的。
    ///
    /// 颜色：
    ///   橙黄  PolygonCollider2D（地形体块，实心）
    ///   青色  BoxCollider2D（角色 / 占位标记）
    ///   品红  EdgeCollider2D（旧版本残留或手动加的开链，两端各画十字）
    ///   灰蓝  触发器（isTrigger，不挡人）
    ///   暗红  组件被禁用（视觉还在、物理是空的）
    ///
    /// 画的时候关掉深度测试，保证它压在网格上面看得见。
    /// </summary>
    [InitializeOnLoad]
    internal static class ColliderDisplayOverlay
    {
        const string PrefKey = "Terrain2D.ShowColliders";

        static readonly Color PolygonColor = new Color(1f, 0.72f, 0.12f, 1f);
        static readonly Color BoxColor = new Color(0.31f, 0.82f, 0.77f, 1f);
        static readonly Color EdgeColor = new Color(1f, 0.32f, 0.85f, 1f);
        static readonly Color TriggerColor = new Color(0.48f, 0.54f, 0.63f, 1f);
        static readonly Color DisabledColor = new Color(0.85f, 0.22f, 0.22f, 0.9f);

        static bool _show;

        // FindObjectsOfType 每次分配一个数组，而 OnSceneGui 每个鼠标事件都会跑，
        // 所以缓存一份，层级变了才重新找。
        static Collider2D[] _colliders;

        // 画线用的复用缓冲，避免每个碰撞体每次重绘都 new 一个数组。
        static readonly List<Vector3> _line = new List<Vector3>(256);

        /// <summary>
        /// 显示开关。视口右下角的按钮和资产替换面板里的勾选框**共用这一个状态** ——
        /// 两处入口控制同一件事，不会出现"面板里关掉了但右下角还亮着"。
        /// </summary>
        public static bool Show
        {
            get { return _show; }
            set
            {
                if (_show == value) return;

                _show = value;
                EditorPrefs.SetBool(PrefKey, _show);
                SceneView.RepaintAll();
            }
        }

        static ColliderDisplayOverlay()
        {
            _show = EditorPrefs.GetBool(PrefKey, false);

            SceneView.duringSceneGui -= OnSceneGui;
            SceneView.duringSceneGui += OnSceneGui;

            EditorApplication.hierarchyChanged -= Invalidate;
            EditorApplication.hierarchyChanged += Invalidate;
        }

        static void Invalidate()
        {
            _colliders = null;
        }

        static void OnSceneGui(SceneView view)
        {
            if (view == null) return;
            if (Event.current == null) return;

            if (_show) DrawColliders();

            DrawToggle(view);
        }

        /// <summary>右下角那颗开关。位置跟着 UI Scale 走，不用写死的像素值。</summary>
        static void DrawToggle(SceneView view)
        {
            const float width = 86f;
            float height = EditorGUIUtility.singleLineHeight + 10f;

            Rect rect = new Rect(
                view.position.width - width - SceneToolbarLayout.SideInset,
                view.position.height - height - SceneToolbarLayout.BottomInset,
                width,
                height);

            Handles.BeginGUI();

            GUILayout.BeginArea(rect);
            GUILayout.BeginVertical("box");

            bool now = GUILayout.Toggle(
                _show, "碰撞体", EditorStyles.miniButton,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));

            GUILayout.EndVertical();
            GUILayout.EndArea();

            Handles.EndGUI();

            if (now == _show) return;

            Show = now;
            view.Repaint();
        }

        static void DrawColliders()
        {
            Collider2D[] all = ResolveColliders();
            if (all == null || all.Length == 0) return;

            Color oldColor = Handles.color;
            UnityEngine.Rendering.CompareFunction oldZTest = Handles.zTest;

            // 碰撞线只有数学宽度，压在网格下面会看不见 —— 直接无视深度。
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

            for (int i = 0; i < all.Length; i++)
            {
                Collider2D col = all[i];
                if (col == null) continue;

                DrawOne(col);
            }

            Handles.color = oldColor;
            Handles.zTest = oldZTest;
        }

        static void DrawOne(Collider2D col)
        {
            Transform tr = col.transform;

            // 禁用的也画（暗红）—— "有碰撞体但被关掉了"本身就是最常要排查的状态。
            // 唯一的例外是 EdgeCollider2D：地形换成体块碰撞之后它只会是旧版本残留，
            // 禁用的那些画出来只会跟体块轮廓混在一起。
            var edge = col as EdgeCollider2D;

            if (edge != null)
            {
                if (!col.enabled) return;

                Handles.color = EdgeColor;

                Vector2[] points = edge.points;
                BuildLine(tr, points);
                Stroke(2.5f);

                // 开链的两个端点单独标出来：链被截断时一眼就能看出来。
                MarkEnd(tr, points, 0);
                MarkEnd(tr, points, points.Length - 1);
                return;
            }

            if (!col.enabled) Handles.color = DisabledColor;
            else if (col.isTrigger) Handles.color = TriggerColor;
            else if (col is PolygonCollider2D) Handles.color = PolygonColor;
            else Handles.color = BoxColor;

            var polygon = col as PolygonCollider2D;

            if (polygon != null)
            {
                for (int p = 0; p < polygon.pathCount; p++)
                {
                    BuildLine(tr, polygon.GetPath(p));
                    if (_line.Count < 2) continue;

                    _line.Add(_line[0]); // 闭合回去
                    Stroke(2f);
                }

                return;
            }

            var box = col as BoxCollider2D;

            if (box != null)
            {
                DrawBox(tr, box);
                return;
            }

            var circle = col as CircleCollider2D;

            if (circle != null) DrawCircle(tr, circle);
        }

        /// <summary>
        /// 矩形碰撞盒。size / offset 都是**本地**值，交给 TransformPoint 去乘缩放 ——
        /// 占位标记的尺寸是靠 transform.localScale 给的，直接拿 size 会画错。
        /// </summary>
        static void DrawBox(Transform tr, BoxCollider2D box)
        {
            Vector2 half = box.size * 0.5f;
            Vector2 c = box.offset;

            float left = c.x - half.x;
            float right = c.x + half.x;
            float bottom = c.y - half.y;
            float top = c.y + half.y;

            _line.Clear();
            _line.Add(tr.TransformPoint(new Vector3(left, bottom, 0f)));
            _line.Add(tr.TransformPoint(new Vector3(right, bottom, 0f)));
            _line.Add(tr.TransformPoint(new Vector3(right, top, 0f)));
            _line.Add(tr.TransformPoint(new Vector3(left, top, 0f)));
            _line.Add(_line[0]);

            Stroke(2f);
        }

        static void DrawCircle(Transform tr, CircleCollider2D circle)
        {
            const int segments = 28;

            Vector2 c = circle.offset;
            float r = circle.radius;

            _line.Clear();

            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                _line.Add(tr.TransformPoint(
                    new Vector3(c.x + Mathf.Cos(a) * r, c.y + Mathf.Sin(a) * r, 0f)));
            }

            Stroke(2f);
        }

        static Collider2D[] ResolveColliders()
        {
            if (_colliders != null)
            {
                // 物体被删掉时数组里会留 null，顺手作废重找。
                for (int i = 0; i < _colliders.Length; i++)
                {
                    if (_colliders[i] == null)
                    {
                        _colliders = null;
                        break;
                    }
                }
            }

            if (_colliders == null)
            {
                // 只要激活的：未激活的物体不参与物理，画出来会误导成"这里挡人"。
                _colliders = Object.FindObjectsOfType<Collider2D>(false);
            }

            return _colliders;
        }

        static void BuildLine(Transform tr, Vector2[] points)
        {
            _line.Clear();
            if (points == null) return;

            for (int i = 0; i < points.Length; i++)
                _line.Add(tr.TransformPoint(new Vector3(points[i].x, points[i].y, 0f)));
        }

        static void Stroke(float width)
        {
            if (_line.Count < 2) return;
            Handles.DrawAAPolyLine(width, _line.ToArray());
        }

        static void MarkEnd(Transform tr, Vector2[] points, int index)
        {
            if (points == null || points.Length < 2) return;
            if (index < 0 || index >= points.Length) return;

            Vector3 p = tr.TransformPoint(new Vector3(points[index].x, points[index].y, 0f));
            float s = HandleUtility.GetHandleSize(p) * 0.14f;

            Handles.DrawAAPolyLine(3f, new[] { p + Vector3.left * s, p + Vector3.right * s });
            Handles.DrawAAPolyLine(3f, new[] { p + Vector3.down * s, p + Vector3.up * s });
        }
    }
}
