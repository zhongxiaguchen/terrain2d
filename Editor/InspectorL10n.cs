using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Terrain2D.EditorTools
{
    /// <summary>
    /// 在 Inspector 里用中文标签绘制字段。
    ///
    /// 字段名保持英文（编码规范），中文标题写在这里；
    /// 悬停提示则直接从组件字段上的 [Tooltip] 反射读取，所以不用维护两份文案。
    /// </summary>
    internal static class InspectorL10n
    {
        static readonly Dictionary<string, FieldInfo> FieldCache = new Dictionary<string, FieldInfo>();

        /// <summary>用中文标题 + 组件上 [Tooltip] 的文字构造一个 GUIContent。</summary>
        public static GUIContent Label(System.Type type, SerializedProperty property, string chineseName)
        {
            string key = type.FullName + "." + property.name;

            FieldInfo field;
            if (!FieldCache.TryGetValue(key, out field))
            {
                field = type.GetField(property.name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                FieldCache[key] = field;
            }

            var tooltip = field != null ? field.GetCustomAttribute<TooltipAttribute>() : null;
            return new GUIContent(chineseName, tooltip != null ? tooltip.tooltip : string.Empty);
        }

        /// <summary>分组小标题。</summary>
        public static void Section(string title)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        /// <summary>只读地显示脚本引用字段。</summary>
        public static void ScriptField(SerializedObject serializedObject)
        {
            var script = serializedObject.FindProperty("m_Script");
            if (script == null) return;

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.PropertyField(script, new GUIContent("脚本"));
            EditorGUI.EndDisabledGroup();
        }
    }
}
