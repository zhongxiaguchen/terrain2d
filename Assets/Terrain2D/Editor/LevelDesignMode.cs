using UnityEditor;
using UnityEditor.Overlays;
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
    /// The state lives in EditorPrefs so it survives domain reloads and editor restarts. The
    /// overlay itself is never hidden, otherwise there would be no way to switch it back on.
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
    /// The Scene view toolbar entry for <see cref="LevelDesignMode"/>.
    ///
    /// The Scene view toolbar is built from overlays (Unity 2021.2+), and IMGUIOverlay lets this
    /// one be written with plain GUILayout instead of UI Toolkit. defaultDockZone puts it in the
    /// toolbar strip at the top; defaultDisplay is what makes it appear there at all — without
    /// it the overlay is created but stays hidden.
    /// </summary>
    [Overlay(typeof(SceneView), OverlayId, DisplayName,
             defaultDisplay = true, defaultDockZone = DockZone.TopToolbar)]
    internal class LevelDesignOverlay : IMGUIOverlay
    {
        const string OverlayId = "Terrain2D/Level Design";
        const string DisplayName = "关卡设计";

        public override void OnGUI()
        {
            GUILayout.BeginHorizontal();

            bool enabled = LevelDesignMode.Enabled;
            bool toggled = GUILayout.Toggle(
                enabled, "关卡设计", EditorStyles.toolbarButton, GUILayout.Width(72f));
            if (toggled != enabled) LevelDesignMode.Enabled = toggled;

            if (GUILayout.Button("选中地形", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                LevelDesignMode.FocusTerrain();

            GUILayout.EndHorizontal();
        }
    }

    /// <summary>
    /// Puts the overlay into the Scene view the first time this package is loaded.
    ///
    /// defaultDisplay only applies the first time an overlay is created, and Unity has usually
    /// already recorded a layout for it by then — so relying on the attribute alone leaves the
    /// button invisible with no hint that it exists. AddOverlayToActiveView puts it in the last
    /// focused Scene view and keeps it there.
    ///
    /// Runs once, guarded by an EditorPrefs flag: after that the user's own layout wins, so
    /// hiding the overlay sticks instead of being undone on every domain reload.
    /// </summary>
    [InitializeOnLoad]
    internal static class LevelDesignOverlayInstaller
    {
        const string InstalledKey = "Terrain2D.LevelDesignOverlayInstalled";

        static LevelDesignOverlayInstaller()
        {
            if (EditorPrefs.GetBool(InstalledKey, false)) return;
            EditorApplication.delayCall += Install;
        }

        static void Install()
        {
            EditorApplication.delayCall -= Install;

            SceneView.AddOverlayToActiveView(new LevelDesignOverlay());
            EditorPrefs.SetBool(InstalledKey, true);
        }
    }
}
