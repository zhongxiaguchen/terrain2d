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

        /// <summary>
        /// 按名字删掉一条。不存在就什么也不做。
        ///
        /// 数据在 `EditorPrefs` 里，删了**没有撤销** —— 所以菜单里把它放在子菜单里，
        /// 不和"选一个"混在一起：误点一下就把存好的档位删掉，代价太大。
        /// </summary>
        public static void Remove(string name)
        {
            if (string.IsNullOrEmpty(name)) return;

            var entries = Data.entries;

            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].name != name) continue;

                entries.RemoveAt(i);
                Flush();
                return;
            }
        }

        /// <summary>清空全部。**调用方自己负责确认**，这里不弹对话框。</summary>
        public static void RemoveAll()
        {
            var entries = Data.entries;
            if (entries.Count == 0) return;

            entries.Clear();
            Flush();
        }

        static void Flush()
        {
            EditorPrefs.SetString(PrefKey, JsonUtility.ToJson(Data));
        }

        /// <summary>Drops down the saved Z values, then delete / save at the end.</summary>
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

                // 删除单独收进子菜单。存好的档位是设计时定的，误删比误选代价大得多。
                for (int i = 0; i < entries.Count; i++)
                {
                    DepthPreset preset = entries[i];
                    menu.AddItem(new GUIContent("删除预设/" + preset.name), false,
                        () => Remove(preset.name));
                }

                int total = entries.Count;

                menu.AddSeparator("删除预设/");
                menu.AddItem(new GUIContent("删除预设/全部清空"), false, () =>
                {
                    if (!EditorUtility.DisplayDialog(
                            "清空 Z 预设",
                            "删掉全部 " + total + " 个命名 Z？这一步不能撤销。",
                            "清空", "取消"))
                        return;

                    RemoveAll();
                });
            }

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("保存当前 Z…"), false,
                () => NameDialog.Show("保存 Z", name => DepthPresets.Add(name, current)));

            menu.ShowAsContext();
        }
    }
}
