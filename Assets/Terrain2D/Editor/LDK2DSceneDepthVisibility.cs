using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LDK2D.EditorTools
{
    /// <summary>
    /// 资产替换阶段（**什么都没选**时）面板里的「场景显示层级」：
    /// 指定一个 Z 值，把这个 Z 上的渲染器统一显示 / 隐藏。
    ///
    /// **只动 <c>Renderer.enabled</c>，绝不碰 <c>Collider2D</c>。** 这一条是硬要求：
    /// 关掉一层是为了看清下面那层在画什么，如果连碰撞也一起关掉，就会变成
    /// "我明明关了背景，角色却掉下去了" —— 视觉和物理必须是两件事。
    ///
    /// **Z 由人给，不自动扫描。** 两种给法：
    ///   1. 直接输入，默认 0
    ///   2. 从 <see cref="LDK2DDepthPresets"/> 里选已经保存的命名 Z（「地形 (0)」这种）
    ///
    /// 为什么不自动列出所有 Z：场景里拖出来的 Z 常常是 0.5001 / 0.25 / 0.0499 这种碎值，
    /// 自动分组会冒出一长串没人认识的层。而层级本来就是设计时定好的档位
    /// （背景 / 地面 / 前景），`LDK2DDepthPresets` 里已经存着了，直接复用那一份。
    /// 面板底部仍然把场景里实际存在的 Z 列出来当参考，省得靠猜。
    ///
    /// 为什么挂到 <see cref="LDK2DSceneToolbarLayout.AssetPanelExtra"/> 而不是被
    /// `LDK2DSortingOrderToolbar` 直接调用：那个文件比这个文件老，旧文件引用新类型
    /// 会在 Unity 增量编译「先编译、后导入」的那一轮报 `does not contain a definition`。
    /// </summary>
    [InitializeOnLoad]
    internal static class LDK2DSceneDepthVisibility
    {
        const string ZPrefKey = "Terrain2D.TargetZ";

        /// <summary>Z 比较的容差。半个 0.01 —— 手拖出来的 0.5001 和 0.5 应该算同一层。</summary>
        const float ZEpsilon = 0.005f;

        static Renderer[] _renderers;

        static float _targetZ;

        static readonly List<Renderer> _matched = new List<Renderer>();

        // 列「场景里的 Z」用的临时表，复用避免每次重绘都分配。
        static readonly List<float> _knownZ = new List<float>();

        static GUIStyle _noteStyle;

        static LDK2DSceneDepthVisibility()
        {
            _targetZ = EditorPrefs.GetFloat(ZPrefKey, 0f);

            LDK2DSceneToolbarLayout.AssetPanelExtra = DrawPanel;

            EditorApplication.hierarchyChanged -= Invalidate;
            EditorApplication.hierarchyChanged += Invalidate;
        }

        static void Invalidate()
        {
            _renderers = null;
        }

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

        static void DrawPanel()
        {
            Renderer[] all = ResolveRenderers();
            if (all == null || all.Length == 0) return;

            GUILayout.Space(4f);

            GUILayout.Label("场景显示层级", EditorStyles.miniBoldLabel);

            DrawZRow();

            CollectMatched(all);

            DrawToggleRow();

            // ---- 碰撞体显示 ----
            // 这里是**唯一**入口，视口右下角不再放按钮。
            bool showColliders = GUILayout.Toggle(
                LDK2DColliderDisplayOverlay.Show, "显示碰撞体",
                EditorStyles.miniButton, GUILayout.Width(96f));

            if (showColliders != LDK2DColliderDisplayOverlay.Show)
                LDK2DColliderDisplayOverlay.Show = showColliders;

            DrawKnownZ(all);

            GUILayout.Label("关层级只关渲染器，碰撞体始终保留。", NoteStyle);
        }

        /// <summary>Z 输入框 + 预设下拉。两条路都只是把 <see cref="_targetZ"/> 改掉。</summary>
        static void DrawZRow()
        {
            GUILayout.BeginHorizontal();

            GUILayout.Label("Z", EditorStyles.miniLabel, GUILayout.Width(14f));

            EditorGUI.BeginChangeCheck();
            float typed = EditorGUILayout.FloatField(_targetZ, GUILayout.Width(58f));
            if (EditorGUI.EndChangeCheck()) SetTargetZ(typed);

            if (GUILayout.Button("▾", EditorStyles.miniButton, GUILayout.Width(18f)))
            {
                // 复用「层级 / Z」那一行用的同一张预设表。玩家存过的
                // "背景 / 地面 / 前景" 本来就是给这个用的，不该再存一份。
                // 菜单里也带「保存当前 Z…」，可以当场把正在看的这个值记下来。
                LDK2DDepthPresets.ShowMenu(_targetZ, SetTargetZ);
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        static void DrawToggleRow()
        {
            GUILayout.BeginHorizontal();

            GUILayout.Label(_matched.Count + " 个", EditorStyles.miniLabel, GUILayout.Width(46f));

            using (new EditorGUI.DisabledScope(_matched.Count == 0))
            {
                bool allOn = AllEnabled();

                bool now = GUILayout.Toggle(
                    allOn, allOn ? "显示" : "隐藏",
                    EditorStyles.miniButton, GUILayout.Width(48f));

                // 部分开、部分关时 allOn 为 false，点一下就是"全开"。
                if (_matched.Count > 0 && now != allOn) SetEnabled(now);
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        static void SetTargetZ(float z)
        {
            if (Mathf.Approximately(_targetZ, z)) return;

            _targetZ = z;
            EditorPrefs.SetFloat(ZPrefKey, _targetZ);
            SceneView.RepaintAll();
        }

        static void CollectMatched(Renderer[] all)
        {
            _matched.Clear();

            for (int i = 0; i < all.Length; i++)
            {
                Renderer renderer = all[i];
                if (renderer == null) continue;

                // 世界 Z，不是 localPosition —— 物体挂到带偏移的父物体下面时，
                // localPosition 和眼睛看到的层次对不上。
                if (Mathf.Abs(renderer.transform.position.z - _targetZ) > ZEpsilon) continue;

                _matched.Add(renderer);
            }
        }

        /// <summary>
        /// 把场景里实际存在的 Z 列出来，当参考。
        ///
        /// 这一段是**只读**的：不自动分组、也不给每层做开关。层级档位应该由人来定
        /// （用输入框或者预设），这里只是让"该输什么"有个依据，不用靠猜。
        /// </summary>
        static void DrawKnownZ(Renderer[] all)
        {
            _knownZ.Clear();

            for (int i = 0; i < all.Length; i++)
            {
                Renderer renderer = all[i];
                if (renderer == null) continue;

                float z = Mathf.Round(renderer.transform.position.z * 100f) / 100f;

                bool seen = false;

                for (int k = 0; k < _knownZ.Count; k++)
                {
                    if (Mathf.Abs(_knownZ[k] - z) > ZEpsilon) continue;
                    seen = true;
                    break;
                }

                if (!seen) _knownZ.Add(z);
            }

            if (_knownZ.Count == 0) return;

            _knownZ.Sort();

            string text = "场景里的 Z：";
            int shown = Mathf.Min(_knownZ.Count, 6);

            for (int i = 0; i < shown; i++)
            {
                if (i > 0) text += "、";
                text += _knownZ[i].ToString("0.##");
            }

            if (_knownZ.Count > shown) text += " …";

            GUILayout.Label(text, NoteStyle);
        }

        static Renderer[] ResolveRenderers()
        {
            if (_renderers != null)
            {
                // 物体被删掉时数组里会留 null，顺手作废重找。
                for (int i = 0; i < _renderers.Length; i++)
                {
                    if (_renderers[i] == null)
                    {
                        _renderers = null;
                        break;
                    }
                }
            }

            if (_renderers == null)
            {
                // 未激活的物体本来就不渲染，它的 enabled 状态也没有意义，不纳入统计。
                _renderers = Object.FindObjectsOfType<Renderer>(false);
            }

            return _renderers;
        }

        static bool AllEnabled()
        {
            for (int i = 0; i < _matched.Count; i++)
            {
                Renderer renderer = _matched[i];
                if (renderer != null && !renderer.enabled) return false;
            }

            return true;
        }

        /// <summary>
        /// 统一切换这个 Z 上的显示。**只写 <c>Renderer.enabled</c>**，碰撞体一个字都不碰。
        /// </summary>
        static void SetEnabled(bool on)
        {
            var objects = new UnityEngine.Object[_matched.Count];

            for (int i = 0; i < _matched.Count; i++) objects[i] = _matched[i];

            Undo.RecordObjects(objects, on ? "显示这一层" : "隐藏这一层");

            for (int i = 0; i < _matched.Count; i++)
            {
                Renderer renderer = _matched[i];
                if (renderer == null) continue;

                renderer.enabled = on;
                EditorUtility.SetDirty(renderer);
            }

            SceneView.RepaintAll();
        }
    }
}
