using UnityEditor;
using UnityEngine;

namespace Terrain2D.EditorTools
{
    /// <summary>
    /// 按 Play 之前，把主相机摆到 Scene 视图现在看的地方 —— 位置取视口焦点沿视线
    /// 反方向退 10 单位，朝向和正交尺寸一起抄过来。
    ///
    /// **为什么是自动而不是按钮**：这解决的是"按 Play 一片蓝"这种一眼就懵的状态，
    /// 而懵的时候人是不会想到先去找按钮点一下的。所见即所得才是默认行为该有的样子。
    ///
    /// **为什么在 ExitingEditMode 而不是运行时**：Scene 视图的 pivot / rotation / size
    /// 只有编辑器知道，运行时的 <see cref="TestCamera2D"/> 拿不到。这个回调发生在
    /// Unity 给场景拍快照之前，所以改出来的位置会被带进 Play。
    ///
    /// 副作用是场景会变脏（相机真的被移动了）。这是预期行为 —— 这个视角本来就是要
    /// 保存下来的东西，不是临时状态。
    /// </summary>
    [InitializeOnLoad]
    internal static class PlayModeCameraSync
    {
        static PlayModeCameraSync()
        {
            // 静态构造在域重载后会再跑一次，所以先减再加，避免重复订阅。
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            // 只要"即将进入 Play"这一个时机。EnteredPlayMode 时场景已经跑起来了，
            // 再动相机就会和 TestCamera2D 的 LateUpdate 打架。
            if (state != PlayModeStateChange.ExitingEditMode) return;

            if (!TestCameraPrefs.SyncCameraOnPlay) return;

            SceneView view = SceneView.lastActiveSceneView;
            if (view == null) return;

            Camera cam = Camera.main;
            if (cam == null) return;

            // 相机上挂着跟随组件、而且已经设了目标时不动它 ——
            // 那种情况用户要的是"跟着角色跑"，把他摆的构图覆盖掉反而更糟。
            var rig = cam.GetComponent<TestCamera2D>();
            if (rig != null && rig.target != null) return;

            Terrain2DMenuItems.AlignCameraToSceneView(cam);
        }
    }
}
