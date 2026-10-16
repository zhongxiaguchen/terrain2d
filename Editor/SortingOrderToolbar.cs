using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Terrain2D.EditorTools
{
    /// <summary>
    /// A small 层级 (sorting order) field pinned to the bottom of the Scene view whenever the
    /// selection contains 2D renderers.
    ///
    /// Unity does expose "Order in Layer" in the Inspector for every renderer, but it sits in
    /// the middle of the SpriteRenderer / MeshRenderer block — a long way from the object you
    /// are actually looking at. This puts the one number you reach for while laying out a
    /// level directly under the viewport, where the terrain tool's toolbar already lives.
    ///
    /// Objects carrying a HeightmapTerrain2D are skipped, and the whole toolbar stays hidden
    /// while one is selected: the terrain editor draws its own toolbar in the same spot and
    /// two stacked toolbars would fight for space.
    /// </summary>
    [InitializeOnLoad]
    internal static class SortingOrderToolbar
    {
        static readonly List<Renderer> Selected = new List<Renderer>();
        static bool _dirty = true;

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

            if (!CollectSelection()) return;

            Rect viewRect = view.position;

            // Same trick as the terrain toolbar: cover the viewport with a transparent area and
            // push the content to the bottom with FlexibleSpace, so there is no need to know
            // how tall the Scene view toolbar is.
            float areaHeight = Mathf.Max(60f, viewRect.height - 32f);

            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(8f, 0f, viewRect.width - 16f, areaHeight));
            GUILayout.FlexibleSpace();

            GUILayout.BeginVertical("box");
            GUILayout.BeginHorizontal();

            GUILayout.Label("层级", EditorStyles.miniBoldLabel);

            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = HasMixedOrder();
            int order = EditorGUILayout.IntField(Selected[0].sortingOrder, GUILayout.Width(48f));
            EditorGUI.showMixedValue = false;

            if (EditorGUI.EndChangeCheck()) ApplyOrder(order);

            GUILayout.FlexibleSpace();

            GUILayout.Label(
                Selected.Count == 1 ? Selected[0].gameObject.name : Selected.Count + " 个物体",
                EditorStyles.miniLabel);

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

            // 模式开关在这里判、而不是在 RebuildSelection 里：切换模式不会改变选中对象，
            // 缓存不会失效，所以必须每次查一次。
            return LevelDesignMode.Enabled && Selected.Count > 0;
        }

        static void RebuildSelection()
        {
            Selected.Clear();

            GameObject[] objects = Selection.gameObjects;
            if (objects == null || objects.Length == 0) return;

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
                if (renderer != null && renderer.enabled) Selected.Add(renderer);
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
    }
}
