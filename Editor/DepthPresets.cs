using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Terrain2D.EditorTools
{
    /// <summary>One saved Z value.</summary>
    [Serializable]
    internal class DepthPreset
    {
        public string name;
        public float value;
    }

    /// <summary>Wrapper so JsonUtility has a single root object to serialise.</summary>
    [Serializable]
    internal class DepthPresetStore
    {
        public List<DepthPreset> entries = new List<DepthPreset>();
    }

    /// <summary>
    /// Named Z values — "background / ground / foreground" without remembering the numbers.
    ///
    /// Same storage decision as <see cref="ColorPalette"/>: EditorPrefs, so it belongs to this
    /// user on this machine rather than travelling with the project. A depth table in the
    /// project folder would show up in version control and merge badly between machines.
    /// </summary>
    internal static class DepthPresets
    {
        const string PrefKey = "Terrain2D.DepthPresets";

        static DepthPresetStore _store;

        static DepthPresetStore Data
        {
            get
            {
                if (_store != null) return _store;

                string json = EditorPrefs.GetString(PrefKey, string.Empty);
                if (!string.IsNullOrEmpty(json))
                {
                    // A corrupted pref must not take the window down.
                    try { _store = JsonUtility.FromJson<DepthPresetStore>(json); }
                    catch (Exception) { _store = null; }
                }

                return _store ?? (_store = new DepthPresetStore());
            }
        }

        /// <summary>Adds a preset, replacing any existing entry with the same name.</summary>
        public static void Add(string name, float value)
        {
            if (string.IsNullOrEmpty(name)) return;

            name = name.Trim();
            if (name.Length == 0) return;

            var entries = Data.entries;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].name != name) continue;
                entries[i].value = value;
                Flush();
                return;
            }

            entries.Add(new DepthPreset { name = name, value = value });
            Flush();
        }

        static void Flush()
        {
            EditorPrefs.SetString(PrefKey, JsonUtility.ToJson(Data));
        }

        /// <summary>Drops down the saved Z values, with "save the current one" at the end.</summary>
        public static void ShowMenu(float current, Action<float> onPick)
        {
            var menu = new GenericMenu();

            var entries = Data.entries;
            if (entries.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("（还没有保存的 Z）"));
            }
            else
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    // Local copy: the lambda must not capture the loop variable.
                    DepthPreset preset = entries[i];

                    // The number is shown next to the name because a Z value is meaningless
                    // to look at in isolation - you need to see what it resolves to.
                    menu.AddItem(
                        new GUIContent(preset.name + "   (" + preset.value.ToString("0.##") + ")"),
                        false, () => onPick(preset.value));
                }
            }

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("保存当前 Z…"), false,
                () => NameDialog.Show("保存 Z", name => DepthPresets.Add(name, current)));

            menu.ShowAsContext();
        }
    }
}
