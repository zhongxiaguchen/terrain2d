using UnityEditor;
using UnityEngine;

namespace Terrain2D.EditorTools
{
    /// <summary>
    /// Master switch for the level-design tooling, plus a shortcut that jumps to the terrain.
    ///
    /// The overlays in this package (the terrain viewport toolbar, the 层级 field) are useful
    /// while laying out a level and in the way the rest of the time — when you are checking
    /// art, taking a screenshot, or recording. This is one switch to turn the lot off.
    ///
    /// The state lives in EditorPrefs so it survives domain reloads and editor restarts.
    /// </summary>
    internal static class LevelDesignMode
    {
        const string PrefKey = "Terrain2D.LevelDesignMode";

        // EditorPrefs is read from OnSceneGUI, which runs for every Scene view event, so the
        // value is cached and only re-read on the first access after a domain reload.
        static bool? _cached;

        /// <summary>On by default, so installing the package changes nothing until you opt out.</summary>
        public static bool Enabled
        {
            get
            {
                if (!_cached.HasValue) _cached = EditorPrefs.GetBool(PrefKey, true);
                return _cached.Value;
            }
            set
            {
                if (Enabled == value) return;

                _cached = value;
                EditorPrefs.SetBool(PrefKey, value);
                SceneView.RepaintAll();
            }
        }

        /// <summary>Selects the terrain in the scene and puts it into edit mode.</summary>
        public static void FocusTerrain()
        {
            var terrain = Object.FindObjectOfType<HeightmapTerrain2D>();
            if (terrain == null)
            {
                Debug.LogWarning("Terrain 2D：当前场景里没有高度场地形。");
                return;
            }

            Enabled = true;
            HeightmapTerrain2DEditor.SetEditing(terrain, true);
            Selection.activeGameObject = terrain.gameObject;
            EditorGUIUtility.PingObject(terrain.gameObject);
            SceneView.RepaintAll();
        }
    }

    /// <summary>
    /// The 关卡设计 switch, drawn just under the Scene view toolbar.
    ///
    /// This deliberately does NOT use the Scene view overlay system. An IMGUIOverlay with
    /// defaultDisplay and defaultDockZone = DockZone.TopToolbar was tried twice and never
    /// appeared: it compiled, it threw nothing, SceneView.AddOverlayToActiveView was called —
    /// and the button still never showed up. Without a way to debug the editor's overlay layout
    /// from outside, guessing a third time was not worth it.
    ///
    /// So this uses exactly the same technique as the terrain toolbar at the bottom of the
    /// viewport, which is known to work here: SceneView.duringSceneGui, Handles.BeginGUI, and a
    /// GUILayout area anchored to the edge. The trade-off is that the buttons sit directly under
    /// the toolbar rather than inside it.
    /// </summary>
    [InitializeOnLoad]
    internal static class LevelDesignToolbar
    {
        // The Scene view overlay toolbar occupies roughly the top 22 px of the view rect.
        const float ToolbarHeight = 22f;

        static LevelDesignToolbar()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            SceneView.duringSceneGui += OnSceneGui;
        }

        static void OnSceneGui(SceneView view)
        {
            if (view == null) return;
            if (Event.current == null) return;

            Rect viewRect = view.position;

            Handles.BeginGUI();

            // A single row pinned right under the toolbar, sized to its contents.
            GUILayout.BeginArea(new Rect(8f, ToolbarHeight, viewRect.width - 16f, 26f));
            GUILayout.BeginHorizontal();

            bool enabled = LevelDesignMode.Enabled;
            bool toggled = GUILayout.Toggle(
                enabled, "关卡设计", EditorStyles.miniButton, GUILayout.Width(72f));
            if (toggled != enabled) LevelDesignMode.Enabled = toggled;

            if (GUILayout.Button("选中地形", EditorStyles.miniButton, GUILayout.Width(72f)))
                LevelDesignMode.FocusTerrain();

            GUILayout.FlexibleSpace();

            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            Handles.EndGUI();
        }
    }
}
