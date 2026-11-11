using System;
using UnityEditor;
using UnityEngine;

namespace LDK2D.EditorTools
{
    /// <summary>
    /// A one-field "give this a name" prompt.
    ///
    /// Unity has no built-in input dialog, and both the colour palette and the depth presets
    /// need exactly the same thing — a title, one text field, OK / Cancel. Shared so the two
    /// features do not each carry their own copy.
    /// </summary>
    internal class LDK2DNameDialog : EditorWindow
    {
        const float Width = 300f;
        const float Height = 88f;

        string _name = string.Empty;
        Action<string> _onAccept;

        public static void Show(string title, Action<string> onAccept)
        {
            var window = CreateInstance<LDK2DNameDialog>();
            window.titleContent = new GUIContent(title);
            window._onAccept = onAccept;
            window.minSize = new Vector2(Width, Height);
            window.maxSize = new Vector2(Width, Height);

            window.ShowUtility();

            // 必须显式定位。CreateInstance + ShowUtility 不给位置的话，窗口可能落在屏幕外，
            // 表现和"点了没反应"一模一样 —— 这条踩过。居中到主编辑器窗口。
            Rect main = EditorGUIUtility.GetMainWindowPosition();
            window.position = new Rect(
                main.center.x - Width * 0.5f,
                main.center.y - Height * 0.5f,
                Width, Height);

            window.Focus();
        }

        void OnGUI()
        {
            EditorGUILayout.Space(10f);
            _name = EditorGUILayout.TextField("名称", _name);
            EditorGUILayout.Space(8f);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("取消", GUILayout.Width(64f))) Close();

            // Empty or whitespace-only names would produce unusable entries, so the button
            // stays disabled until there is something to store.
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_name.Trim())))
            {
                if (GUILayout.Button("保存", GUILayout.Width(64f)))
                {
                    if (_onAccept != null) _onAccept(_name.Trim());
                    Close();
                }
            }

            GUILayout.EndHorizontal();
        }
    }
}
