using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Terrain2D.EditorTools
{
    /// <summary>One saved colour.</summary>
    [Serializable]
    internal class PaletteEntry
    {
        public string name;
        public Color color;
    }

    /// <summary>Wrapper so JsonUtility has a single root object to serialise.</summary>
    [Serializable]
    internal class PaletteStore
    {
        public List<PaletteEntry> entries = new List<PaletteEntry>();
    }

    /// <summary>
    /// A named colour palette shared by every colour field in the package.
    ///
    /// Kept in EditorPrefs rather than in the project folder: a swatch list is a personal
    /// thing, and putting it in the project would mean it shows up in version control and
    /// merges badly between machines. The trade-off is that it does not travel with the
    /// project — if a team ever wants a shared palette, that is a separate, deliberate feature.
    /// </summary>
    internal static class ColorPalette
    {
        const string PrefKey = "Terrain2D.ColorPalette";

        static PaletteStore _store;

        static PaletteStore Data
        {
            get
            {
                if (_store != null) return _store;

                string json = EditorPrefs.GetString(PrefKey, string.Empty);
                if (!string.IsNullOrEmpty(json))
                {
                    // A corrupted pref must not take the whole window down.
                    try { _store = JsonUtility.FromJson<PaletteStore>(json); }
                    catch (Exception) { _store = null; }
                }

                return _store ?? (_store = new PaletteStore());
            }
        }

        public static int Count
        {
            get { return Data.entries.Count; }
        }

        /// <summary>Adds a colour, replacing any existing entry with the same name.</summary>
        public static void Add(string name, Color color)
        {
            if (string.IsNullOrEmpty(name)) return;

            name = name.Trim();
            if (name.Length == 0) return;

            var entries = Data.entries;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].name != name) continue;
                entries[i].color = color;
                Flush();
                return;
            }

            entries.Add(new PaletteEntry { name = name, color = color });
            Flush();
        }

        static void Flush()
        {
            EditorPrefs.SetString(PrefKey, JsonUtility.ToJson(Data));
        }

        /// <summary>
        /// Drops down the saved colours, with a "save the current colour" item at the end.
        /// </summary>
        public static void ShowMenu(Color current, Action<Color> onPick)
        {
            var menu = new GenericMenu();

            var entries = Data.entries;
            if (entries.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("（还没有保存的颜色）"));
            }
            else
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    // Local copy: the lambda must not capture the loop variable.
                    PaletteEntry entry = entries[i];
                    menu.AddItem(new GUIContent(entry.name), false, () => onPick(entry.color));
                }
            }

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("保存当前颜色…"), false,
                () => NameDialog.Show("保存颜色", name =>
                {
                    ColorPalette.Add(name, current);
                    onPick(current);
                }));

            menu.ShowAsContext();
        }
    }
}
