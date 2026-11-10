using UnityEditor;
using UnityEngine;

namespace Terrain2D.EditorTools
{
    /// <summary>
    /// 视口里两块浮层的定位：左下角的固定栏，和左上角的阶段面板。
    ///
    /// 固定栏用「铺满视口 + FlexibleSpace 顶到底部」的老办法 ——
    /// 这样**不用**去猜 Unity 自己的 Scene 视图工具栏有多高，位置永远是贴底的。
    ///
    /// 面板反过来：贴左上角，高度给足、内容从顶往下排。因为面板的内容行数随阶段变，
    /// 从底部顶上去的话高度一改就整体跳一下。
    ///
    /// 唯一需要留的是底部那条缝。以前三个地方各自写死 `32f`，问题是这个值并不恒定：
    /// Scene 视图底部的浮层在不同 Unity 版本、不同 UI Scale、高 DPI 显示器上高度都不一样，
    /// 写死的 32px 迟早会跟它打架，或者反过来在没浮层的时候悬在半空。
    /// 现在按 singleLineHeight 换算，跟着 UI Scale 一起变。
    /// </summary>
    internal static class SceneToolbarLayout
    {
        /// <summary>左右留白。</summary>
        public const float SideInset = 8f;

        /// <summary>
        /// 左上角阶段面板的宽度。控件按"每行一两个"排，所以窄；再窄放不下一个
        /// slider 加数值，再宽就开始挡视线。
        ///
        /// 放在这里而不是放在两个编辑器类里：面板有两个绘制者（选中地形时是
        /// HeightmapTerrain2DEditor，没选时是 SortingOrderToolbar），宽度必须一致，
        /// 否则切换选择时面板会跳一下。
        /// </summary>
        public const float PanelWidth = 280f;

        /// <summary>底部留白，随 UI Scale 缩放（默认 UI Scale 下约 29px）。</summary>
        public static float BottomInset
        {
            get { return EditorGUIUtility.singleLineHeight * 1.6f; }
        }

        /// <summary>
        /// 铺满视口、底部留出 <see cref="BottomInset"/> 的区域。
        /// 调用方自己负责 GUILayout.BeginArea / EndArea。
        /// </summary>
        /// <param name="viewRect">SceneView.position</param>
        /// <param name="minHeight">视口被压得很扁时的兜底高度，别让内容被裁掉</param>
        public static Rect Area(Rect viewRect, float minHeight)
        {
            float height = Mathf.Max(minHeight, viewRect.height - BottomInset);

            return new Rect(
                SideInset,
                0f,
                Mathf.Max(1f, viewRect.width - SideInset * 2f),
                height);
        }

        /// <summary>
        /// 左上角浮动面板的位置。
        ///
        /// 必须避开 Unity 自己的竖排工具条（Pan / Move / Rotate / Scale / Rect / Transform），
        /// 那块默认在 x = 8..40、y = 42..200 一带，而且高度随 2D/3D 模式和自定义工具变。
        /// 所以这里**不往下让**，而是往右让 —— 高度会变，宽度不会。
        /// </summary>
        /// <param name="viewRect">SceneView.position</param>
        /// <param name="width">面板期望宽度，视口太窄时会被压小</param>
        public static Rect Panel(Rect viewRect, float width)
        {
            // 按行高换算，跟着 UI Scale 一起变。默认 UI Scale 下 left ≈ 47、top ≈ 43。
            float left = EditorGUIUtility.singleLineHeight * 2.6f;
            float top = EditorGUIUtility.singleLineHeight * 2.4f;

            float available = Mathf.Max(140f, viewRect.width - left - SideInset);

            // 高度给足就行：内容从顶往下排，多出来的部分是空的。
            // 给太小会静默裁掉最后几行，而 GUILayout 不会报错。
            float height = Mathf.Min(
                Mathf.Max(80f, viewRect.height - top - BottomInset),
                EditorGUIUtility.singleLineHeight * 26f);

            return new Rect(left, top, Mathf.Min(width, available), height);
        }
    }
}
