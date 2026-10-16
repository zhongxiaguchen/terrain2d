using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace Terrain2D.EditorTools
{
    /// <summary>
    /// Scene-wide 2D draw-order manager.
    ///
    /// Unity gives you no way to see or edit the render order of a whole scene: every
    /// renderer's sortingOrder is set one object at a time, and when they are all left
    /// at 0 the draw order falls back to undocumented rules (Z, then hierarchy order,
    /// then instance id). That "mostly works" until it doesn't — and then it is very
    /// hard to reason about, because nothing in the UI tells you what is in front.
    ///
    /// This window lists every renderer in the open scene(s) in draw order, front-most
    /// at the top (like a layer stack). Drag a row to change the order; the window writes
    /// the result back as a clean run of sortingOrder values.
    ///
    /// It only ever touches sortingLayerName / sortingOrder. It never moves objects in Z,
    /// so it is safe on a scene that already uses Z for depth.
    /// </summary>
    public class SortingOrderWindow : EditorWindow
    {
        const float RowHeight = 22f;
        const float MarkerWidth = 4f;
        const float OrderWidth = 54f;
        const float LayerWidth = 118f;

        [SerializeField] bool _includeInactive = true;
        [SerializeField] string _search = string.Empty;

        readonly List<Renderer> _all = new List<Renderer>();
        readonly List<Renderer> _rows = new List<Renderer>();

        ReorderableList _list;
        string[] _layerNames = new string[0];
        int[] _layerIds = new int[0];
        Vector2 _scroll;
        bool _dirty = true;
        int _ambiguous;

        // Cached because OnGUI runs many times per second and a fresh GUIStyle per row
        // per frame is pure garbage.
        static GUIStyle _rowLabel;

        /// <summary>Opens the window. Also called from the terrain toolbar.</summary>
        public static void Open()
        {
            var window = GetWindow<SortingOrderWindow>("2D Sorting");
            window.minSize = new Vector2(470f, 320f);
        }

        // Registered in three places on purpose: "Window" is where people look for windows,
        // "Tools" is the usual home for editor tools, and the GameObject submenu is where
        // this package's other entries live.
        //
        // Three separate methods rather than three attributes on one method: [MenuItem] is
        // not documented as AllowMultiple, and guessing at editor API surface is how this
        // file got its first round of compile errors.
        [MenuItem("Window/2D Sorting Manager")]
        static void OpenFromWindowMenu() { Open(); }

        [MenuItem("Tools/2D Sorting Manager")]
        static void OpenFromToolsMenu() { Open(); }

        // The submenu name has to stay Chinese to land next to the package's existing
        // entries; it gets renamed along with the rest of the code base.
        [MenuItem("GameObject/地形 2D/2D Sorting Manager", false, 12)]
        static void OpenFromGameObjectMenu() { Open(); }

        void OnEnable()
        {
            EditorApplication.hierarchyChanged += MarkDirty;
            Rebuild();
        }

        void OnDisable()
        {
            EditorApplication.hierarchyChanged -= MarkDirty;
        }

        void MarkDirty()
        {
            _dirty = true;
        }

        // ------------------------------------------------------------------ scanning

        void Rebuild()
        {
            // SortingLayer.layers allocates a fresh array on every access, so snapshot it
            // once here instead of calling it from the draw loop.
            var layers = SortingLayer.layers;
            _layerNames = new string[layers.Length];
            _layerIds = new int[layers.Length];
            for (int i = 0; i < layers.Length; i++)
            {
                _layerNames[i] = layers[i].name;
                _layerIds[i] = layers[i].id;
            }

            CollectAll(_all, _includeInactive);
            ApplyFilter();
            _dirty = false;
        }

        /// <summary>
        /// Every Renderer in every loaded scene, plus the Prefab stage if one is open —
        /// the terrain lives inside a prefab, so ignoring the prefab stage would make the
        /// window look empty for exactly the objects people care about most.
        /// </summary>
        static void CollectAll(List<Renderer> result, bool includeInactive)
        {
            result.Clear();

            int sceneCount = SceneManager.sceneCount;
            for (int i = 0; i < sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                CollectFrom(scene.GetRootGameObjects(), includeInactive, result);
            }

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot != null)
                CollectFrom(new[] { stage.prefabContentsRoot }, includeInactive, result);
        }

        static void CollectFrom(GameObject[] roots, bool includeInactive, List<Renderer> result)
        {
            var buffer = new List<Renderer>();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null) continue;
                buffer.Clear();
                roots[i].GetComponentsInChildren(includeInactive, buffer);
                for (int k = 0; k < buffer.Count; k++)
                {
                    // Sorting only matters for renderers that actually draw something.
                    if (buffer[k] is SpriteRenderer ||
                        buffer[k] is MeshRenderer ||
                        buffer[k] is SkinnedMeshRenderer ||
                        buffer[k] is TilemapRenderer ||
                        buffer[k] is LineRenderer ||
                        buffer[k] is TrailRenderer ||
                        buffer[k] is ParticleSystemRenderer)
                    {
                        result.Add(buffer[k]);
                    }
                }
            }
        }

        void ApplyFilter()
        {
            _rows.Clear();

            for (int i = 0; i < _all.Count; i++)
            {
                var r = _all[i];
                if (r == null) continue;
                if (!string.IsNullOrEmpty(_search) &&
                    r.gameObject.name.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                _rows.Add(r);
            }

            _rows.Sort(CompareFrontFirst);
            _ambiguous = CountAmbiguous(_rows);
            BuildList();
        }

        // ------------------------------------------------------------------ ordering

        int LayerIndex(int id)
        {
            for (int i = 0; i < _layerIds.Length; i++)
                if (_layerIds[i] == id) return i;
            return 0;
        }

        /// <summary>Front-most first: later sorting layer wins, then higher order, then smaller Z.</summary>
        int CompareFrontFirst(Renderer a, Renderer b)
        {
            int la = LayerIndex(a.sortingLayerID);
            int lb = LayerIndex(b.sortingLayerID);
            if (la != lb) return lb.CompareTo(la);

            if (a.sortingOrder != b.sortingOrder) return b.sortingOrder.CompareTo(a.sortingOrder);

            // Same layer and same order: Unity falls back to Z. With the usual 2D setup
            // (orthographic camera at -Z looking towards +Z) a smaller Z is nearer, so it
            // draws in front.
            return a.transform.position.z.CompareTo(b.transform.position.z);
        }

        /// <summary>
        /// How many renderers share a layer + order + Z with at least one other renderer.
        /// Those are the ones whose relative draw order is undefined, i.e. the actual bug.
        /// </summary>
        static int CountAmbiguous(List<Renderer> rows)
        {
            var seen = new Dictionary<string, int>();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (r == null) continue;
                string key = r.sortingLayerID + "|" + r.sortingOrder + "|" +
                             r.transform.position.z.ToString("F4");
                int n;
                seen.TryGetValue(key, out n);
                seen[key] = n + 1;
            }

            int total = 0;
            foreach (var pair in seen)
                if (pair.Value > 1) total += pair.Value;
            return total;
        }

        // ------------------------------------------------------------------ UI

        void OnGUI()
        {
            // Rescan when the hierarchy changed. A hierarchy change never happens while a
            // row is being dragged (dragging only writes sortingOrder), so it is safe to
            // rebuild unconditionally here.
            if (_dirty) Rebuild();

            DrawToolbar();

            EditorGUILayout.HelpBox(
                "Front-most at the top. Drag a row to change what covers what — the order is " +
                "written back as a clean 0, 1, 2 ... run per sorting layer. Only sorting layer " +
                "and order are touched; Z is never modified.",
                MessageType.None);

            if (_rows.Count == 0)
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField(
                    _all.Count == 0
                        ? "No renderers found in the open scene(s)."
                        : "No renderer matches the filter.",
                    EditorStyles.miniLabel);
            }
            else
            {
                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                if (_list != null) _list.DoLayoutList();
                EditorGUILayout.EndScrollView();
            }

            DrawStatus();
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Rescan", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                {
                    Rebuild();
                    Repaint();
                }

                using (new EditorGUI.DisabledScope(_rows.Count == 0))
                {
                    if (GUILayout.Button("Sort by Y", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                    {
                        SortByY();
                    }

                    if (GUILayout.Button("Make unique", EditorStyles.toolbarButton, GUILayout.Width(84f)))
                    {
                        ApplyOrderFromList();
                        Repaint();
                    }

                    if (GUILayout.Button("Reset to 0", EditorStyles.toolbarButton, GUILayout.Width(74f)))
                    {
                        ResetAllOrders();
                    }
                }

                GUILayout.FlexibleSpace();

                // Toggling either of these has to re-run the scan / filter — they are read
                // during Rebuild / ApplyFilter, not while the rows are drawn.
                bool includeInactive = GUILayout.Toggle(
                    _includeInactive, "Inactive", EditorStyles.toolbarButton, GUILayout.Width(66f));
                if (includeInactive != _includeInactive)
                {
                    _includeInactive = includeInactive;
                    Rebuild();
                }

                string search = GUILayout.TextField(
                    _search, EditorStyles.toolbarSearchField, GUILayout.Width(140f));
                if (search != _search)
                {
                    _search = search;
                    ApplyFilter();
                }
            }
        }

        void DrawStatus()
        {
            EditorGUILayout.Space(2f);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    _rows.Count + " renderer(s) listed", EditorStyles.miniLabel, GUILayout.Width(150f));

                if (_ambiguous > 0)
                {
                    var warn = new GUIStyle(EditorStyles.miniLabel);
                    warn.normal.textColor = new Color(0.85f, 0.55f, 0.15f);
                    EditorGUILayout.LabelField(
                        _ambiguous + " share the same layer / order / Z — their relative order is " +
                        "undefined. Drag rows to fix it, or hit Sort by Y for a top-down scene.",
                        warn);
                }
                else
                {
                    EditorGUILayout.LabelField("Draw order is unambiguous", EditorStyles.miniLabel);
                }
            }
        }

        void BuildList()
        {
            _list = new ReorderableList(_rows, typeof(Renderer), true, false, false, false);
            _list.elementHeight = RowHeight;
            _list.drawElementCallback = DrawRow;
            _list.onChangedCallback = OnListChanged;
        }

        void DrawRow(Rect rect, int index, bool isActive, bool isFocused)
        {
            var r = _rows[index];
            if (r == null) return;

            rect.y += 1f;
            rect.height = RowHeight - 2f;

            // Colour marker keyed to the sorting layer, so layers are readable at a glance.
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, MarkerWidth, rect.height), LayerColor(r.sortingLayerID));

            float right = rect.xMax;
            var orderRect = new Rect(right - OrderWidth, rect.y, OrderWidth, rect.height);
            right = orderRect.x - 4f;
            var layerRect = new Rect(right - LayerWidth, rect.y, LayerWidth, rect.height);
            right = layerRect.x - 4f;

            float nameX = rect.x + MarkerWidth + 6f;
            var nameRect = new Rect(nameX, rect.y, Mathf.Max(40f, right - nameX), rect.height);

            if (_rowLabel == null)
                _rowLabel = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft };

            if (GUI.Button(nameRect, r.gameObject.name, _rowLabel))
            {
                Selection.activeGameObject = r.gameObject;
                EditorGUIUtility.PingObject(r.gameObject);
            }

            EditorGUI.BeginChangeCheck();
            int newLayer = EditorGUI.Popup(layerRect, LayerIndex(r.sortingLayerID), _layerNames);
            if (EditorGUI.EndChangeCheck() && newLayer >= 0 && newLayer < _layerNames.Length)
            {
                Undo.RecordObject(r, "Change sorting layer");
                r.sortingLayerName = _layerNames[newLayer];
                MarkDirty(r);
            }

            EditorGUI.BeginChangeCheck();
            int newOrder = EditorGUI.IntField(orderRect, r.sortingOrder);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(r, "Change sorting order");
                r.sortingOrder = newOrder;
                MarkDirty(r);
            }
        }

        Color LayerColor(int layerId)
        {
            // Color.HSVToRGB, not EditorGUIUtility.HSVToRGB — the latter is obsolete.
            float hue = Mathf.Repeat(0.13f * LayerIndex(layerId), 1f);
            return Color.HSVToRGB(hue, 0.5f, 0.85f);
        }

        static void MarkDirty(Renderer r)
        {
            EditorUtility.SetDirty(r);
            var scene = r.gameObject.scene;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
        }

        // ------------------------------------------------------------------ actions

        void OnListChanged(ReorderableList list)
        {
            ApplyOrderFromList();
        }

        /// <summary>
        /// Walks the list bottom-up and hands out 0, 1, 2 ... within each sorting layer.
        /// The top of the list therefore ends up with the highest order, which is the one
        /// Unity draws last — i.e. in front.
        /// </summary>
        void ApplyOrderFromList()
        {
            var targets = new List<Renderer>();
            var orders = new List<int>();
            var nextOrder = new Dictionary<int, int>();

            for (int i = _rows.Count - 1; i >= 0; i--)
            {
                var r = _rows[i];
                if (r == null) continue;

                int order;
                if (!nextOrder.TryGetValue(r.sortingLayerID, out order)) order = 0;
                nextOrder[r.sortingLayerID] = order + 1;

                if (r.sortingOrder == order) continue;

                targets.Add(r);
                orders.Add(order);
            }

            if (targets.Count == 0) return;

            var objects = new UnityEngine.Object[targets.Count];
            for (int i = 0; i < targets.Count; i++) objects[i] = targets[i];
            Undo.RecordObjects(objects, "Reorder 2D sorting");

            for (int i = 0; i < targets.Count; i++)
            {
                targets[i].sortingOrder = orders[i];
                MarkDirty(targets[i]);
            }

            _ambiguous = CountAmbiguous(_rows);
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Top-down / isometric helper: an object lower on screen (smaller Y) is nearer the
        /// viewer, so it should draw in front. Not meaningful for a side-scroller — there Y
        /// is height, not depth — which is why this is a button and not automatic.
        /// </summary>
        void SortByY()
        {
            // OrderBy is a stable sort, so objects at the same Y keep their current order.
            var sorted = _rows.OrderBy(r => r.transform.position.y).ToList();
            _rows.Clear();
            _rows.AddRange(sorted);

            ApplyOrderFromList();
            Repaint();
        }

        void ResetAllOrders()
        {
            var objects = new List<UnityEngine.Object>();
            for (int i = 0; i < _all.Count; i++)
            {
                var r = _all[i];
                if (r != null && r.sortingOrder != 0) objects.Add(r);
            }

            if (objects.Count == 0) return;

            Undo.RecordObjects(objects.ToArray(), "Reset 2D sorting order");

            for (int i = 0; i < _all.Count; i++)
            {
                var r = _all[i];
                if (r == null) continue;
                r.sortingOrder = 0;
                MarkDirty(r);
            }

            Rebuild();
            Repaint();
            SceneView.RepaintAll();
        }
    }
}
