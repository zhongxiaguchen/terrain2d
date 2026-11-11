using UnityEditor;
using UnityEngine;

namespace LDK2D.EditorTools
{
    /// <summary>
    /// 按 Play 之前，把相机摆到 Scene 视图现在看的地方 —— 位置取视口焦点沿视线
    /// 反方向退 10 单位，朝向和正交尺寸一起抄过来。
    ///
    /// **为什么是自动的**：这解决的是"按 Play 一片蓝"这种一眼就懵的状态。
    /// 它不该是一个要人先想到、再去找、再去点的操作。
    ///
    /// **只管固定视口**。除了固定视口，其余三种模式（跟随 / 仅水平 / 仅垂直）
    /// 相机都绑在角色身上，位置由 <see cref="LDK2DTestCamera"/> 每帧算 ——
    /// 在这里覆盖掉会和它打架，而且用户要的是"跟着角色跑"，不是"停在我摆的地方"。
    ///
    /// **为什么在 ExitingEditMode 而不是运行时**：Scene 视图的 pivot / rotation / size
    /// 只有编辑器知道，运行时的 <see cref="LDK2DTestCamera"/> 拿不到。这个回调发生在
    /// Unity 给场景拍快照之前，所以改出来的位置会被带进 Play。
    ///
    /// 副作用是场景会变脏（相机真的被移动了）。这是预期行为 —— 这个视角本来就是要
    /// 保存下来的东西，不是临时状态。
    /// </summary>
    [InitializeOnLoad]
    internal static class LDK2DPlayModeCameraSync
    {
        static LDK2DPlayModeCameraSync()
        {
            // 静态构造在域重载后会再跑一次，所以先减再加，避免重复订阅。
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            // 只要"即将进入 Play"这一个时机。EnteredPlayMode 时场景已经跑起来了，
            // 再动相机就会和 LDK2DTestCamera 的 LateUpdate 打架。
            if (state != PlayModeStateChange.ExitingEditMode) return;

            SceneView view = SceneView.lastActiveSceneView;
            if (view == null) return;

            Camera cam = Camera.main;
            if (cam == null) return;

            // 除了固定视口，其余模式相机都绑在角色身上，交给 LDK2DTestCamera 自己跟。
            var rig = cam.GetComponent<LDK2DTestCamera>();
            if (rig != null && rig.mode != LDK2DTestCameraMode.Fixed) return;

            LDK2DMenuItems.AlignCameraToSceneView(cam);
        }
    }
}
