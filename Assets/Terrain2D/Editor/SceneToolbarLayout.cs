using UnityEditor;
using UnityEngine;

namespace Terrain2D.EditorTools
{
    /// <summary>
    /// 视口底部工具栏的定位。
    ///
    /// 做法是铺一个覆盖整个视口的透明区域，再用 FlexibleSpace 把内容顶到底部 ——
    /// 这样**不用**去猜 Unity 自己的 Scene 视图工具栏有多高，位置永远是贴底的。
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
    }
}
