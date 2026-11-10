using UnityEditor;
using UnityEngine;

namespace Terrain2D.EditorTools
{
    /// <summary>
    /// 关卡制作的三段路。工具栏按阶段切换 —— 三个阶段的关注点完全不同，
    /// 把所有按钮平铺在一起，每次都得从头找一遍。
    /// </summary>
    internal enum WorkStage
    {
        /// <summary>搭建：改形状、定分层。灰盒阶段。</summary>
        Blockout = 0,

        /// <summary>测试：验证碰撞和跳跃距离。</summary>
        Test = 1,

        /// <summary>资产替换：UV、材质、交给美术。</summary>
        Asset = 2,
    }

    /// <summary>
    /// 阶段状态的唯一持有者，以及那条阶段选择器唯一的画法。
    ///
    /// 为什么值得单独拆一个文件：视口里有两处要用阶段 ——
    /// 左下角的固定栏（SortingOrderToolbar）和左上角的阶段面板
    /// （HeightmapTerrain2DEditor / SortingOrderToolbar 都会画）。阶段只有一个值，
    /// 所以状态和画法都不能挂在任何一个 Editor 实例上。
    /// </summary>
    internal static class TerrainStage
    {
        /// <summary>顺序必须和 <see cref="WorkStage"/> 一致。</summary>
        public static readonly string[] Labels = { "搭建", "测试", "资产替换" };

        const string Key = "Terrain2D.Stage";

        /// <summary>
        /// 当前阶段是**全局**的，不是每个地形一份 —— 阶段是"这块关卡走到哪一步了"，
        /// 不是某个物体的属性。场景里放了三块地形，选中哪块都不该把阶段重置。
        ///
        /// 存 EditorPrefs 而不是 SessionState：这个东西要跨 Editor 重启活着。
        /// 早上打开工程不该重新选一次"我在做资产替换"。
        /// </summary>
        public static WorkStage Current
        {
            get
            {
                int raw = EditorPrefs.GetInt(Key, (int)WorkStage.Blockout);
                return (WorkStage)Mathf.Clamp(raw, 0, Labels.Length - 1);
            }
            set
            {
                EditorPrefs.SetInt(Key, Mathf.Clamp((int)value, 0, Labels.Length - 1));
            }
        }

        /// <summary>
        /// 画那条三段选择器，返回 true 表示这一帧真的切换了阶段。
        ///
        /// 调用方要据此重绘：固定栏和阶段面板是两个独立的 Scene GUI 回调，
        /// 不主动 Repaint 的话，面板会停在旧阶段直到下一次鼠标事件。
        /// </summary>
        public static bool DrawSelector(float width = 170f)
        {
            // 单选，所以用 Toolbar 而不是三个 Toggle —— Toolbar 天然表达"只能选一个"。
            int picked = GUILayout.Toolbar(
                (int)Current, Labels, EditorStyles.miniButton,
                GUILayout.Width(width), GUILayout.Height(18f));

            if (picked == (int)Current) return false;

            Current = (WorkStage)picked;
            return true;
        }
    }
}
