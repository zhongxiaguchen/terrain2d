using UnityEditor;
using UnityEngine;

namespace Terrain2D.EditorTools
{
    /// <summary>
    /// 创建测试角色时用哪个摄像机模式，以及那条下拉的选项文字。
    ///
    /// 单独一个文件，是因为它被三个地方读：菜单项（创建角色）、地形面板（测试阶段）、
    /// 和"什么都没选"时的阶段面板。后两者在别的编辑器文件里 —— 让它们直接引用
    /// Terrain2DMenuItems 会产生编译期依赖，而 Unity 的增量编译出现过
    /// 「先编译、后导入」导致 `does not contain a definition`。中立的一小块没有这个问题。
    /// </summary>
    internal static class TestCameraPrefs
    {
        /// <summary>顺序必须和 <see cref="TestCameraMode"/> 一致。</summary>
        public static readonly string[] Labels = { "跟随", "仅水平", "仅垂直", "固定" };

        const string Key = "Terrain2D.TestCameraMode";

        /// <summary>
        /// 默认「跟随」。
        ///
        /// 存 EditorPrefs 而不是记在某个物体上：这个选择是"我习惯怎么试关卡"，
        /// 和具体是哪块地形无关。还没建摄像机时也能先选，创建时直接生效 ——
        /// 面板里就是靠这一点做到"先选镜头再建角色"。
        /// </summary>
        public static TestCameraMode Mode
        {
            get
            {
                int raw = EditorPrefs.GetInt(Key, (int)TestCameraMode.Follow);
                return (TestCameraMode)Mathf.Clamp(raw, 0, Labels.Length - 1);
            }
            set
            {
                EditorPrefs.SetInt(Key, Mathf.Clamp((int)value, 0, Labels.Length - 1));
            }
        }

        /// <summary>当前模式的中文名，用来拼按钮文字。</summary>
        public static string ModeLabel
        {
            get { return Labels[(int)Mode]; }
        }

        const string SyncKey = "Terrain2D.SyncCameraOnPlay";

        /// <summary>
        /// 按 Play 之前要不要把相机抄到 Scene 视图现在看的地方。默认开。
        ///
        /// 放在这里而不是 <see cref="PlayModeCameraSync"/> 里，是为了让读它的
        /// 面板代码只依赖这个早就存在的文件 —— 新文件被旧文件引用会踩
        /// 「先编译、后导入」的坑，反过来（新文件引用旧文件）不会。
        /// </summary>
        public static bool SyncCameraOnPlay
        {
            get { return EditorPrefs.GetBool(SyncKey, true); }
            set { EditorPrefs.SetBool(SyncKey, value); }
        }
    }
}
