using System.Collections.Generic;
using UnityEngine;

namespace LDK2D
{
    /// <summary>
    /// 一笔自由笔迹。
    ///
    /// 点存**世界坐标**，不存屏幕坐标 —— 存屏幕坐标的话视口一缩放、一平移，
    /// 标记就跟着漂走了。绘制时由编辑器侧每帧投影回屏幕，
    /// 线宽才是屏幕像素，视觉上才像"浮在上面的一层"。
    /// </summary>
    [System.Serializable]
    public class LDK2DMarkStroke
    {
        public Color color = new Color(1f, 0.85f, 0.2f, 1f);

        /// <summary>线宽，**屏幕像素**。不随视口缩放变。</summary>
        public float width = 3f;

        public List<Vector3> points = new List<Vector3>();

        /// <summary>
        /// 绘制用的定长副本。**不序列化。**
        ///
        /// `Handles.DrawAAPolyLine` 只收 `Vector3[]`，每帧 `points.ToArray()` 会制造垃圾 ——
        /// 而 `duringSceneGui` 每个鼠标事件都跑，拖笔画的时候一帧可能跑好几次。
        /// 笔画收笔之后点集就不再变了，所以缓存一次就够。
        /// </summary>
        [System.NonSerialized] public Vector3[] cache;
    }

    /// <summary>
    /// 一条文字标注。字号是**屏幕像素**，所以视口缩到全图还是放到 25 倍，
    /// 字都是同样大小 —— 跟笔迹的线宽是同一个道理。
    /// </summary>
    [System.Serializable]
    public class LDK2DMarkText
    {
        public string text = string.Empty;
        public Vector3 position;
        public Color color = new Color(1f, 0.85f, 0.2f, 1f);

        /// <summary>字号，屏幕像素。</summary>
        public float size = 14f;
    }

    /// <summary>
    /// 一个框容器：框住一片区域，框里写这块是什么。
    ///
    /// 用 `center + size` 而不是 `min / max`：以后要拖动或缩放框的时候，
    /// 改 center 就是整体平移、改 size 就是缩放，不用同时动两个角点。
    /// </summary>
    [System.Serializable]
    public class LDK2DMarkBox
    {
        public Vector3 center;
        public Vector2 size = new Vector2(2f, 2f);

        /// <summary>框里的文字。空字符串就是纯框。</summary>
        public string label = string.Empty;

        public Color color = new Color(0.35f, 0.85f, 1f, 1f);
    }

    /// <summary>
    /// 场景标记的数据载体。**只存数据，不渲染。**
    ///
    /// 为什么不每笔一个 GameObject + LineRenderer：
    ///   - `LineRenderer` 的宽度是世界单位，视口缩远了线变发丝、凑近了变马路，
    ///     而标记恰恰要跟着视口缩放保持同样的视觉重量；
    ///   - URP 2D Renderer 下还得配 `Sprite-Unlit-Default` 材质、把 sortingOrder 压到最高，
    ///     否则被地形盖住；
    ///   - 画 50 笔就是 Hierarchy 里 50 个对象，还得管材质实例。
    ///
    /// 所以这里只留一个空组件当容器，绘制交给 `Editor/LDK2DMarkupTool.cs`
    /// 用 `Handles.DrawAAPolyLine` / `DrawSolidRectangleWithOutline` / `Label` 每帧重画 ——
    /// 抗锯齿、屏幕像素尺寸、`zTest = Always` 压在最上层，三样都是白送的。
    ///
    /// 三种标记**分开存三个列表**，不塞进一个多态列表：Unity 的序列化不认多态，
    /// 而且"按类型统一显示 / 隐藏"本来就需要能按类型遍历。
    ///
    /// 放在运行时程序集而不是 Editor 程序集：Editor 程序集里的 MonoBehaviour
    /// 打包后会变成 Missing Script，场景上挂着一堆报错。
    /// 这个组件运行时什么都不做，打包进去也无害。
    /// </summary>
    [DisallowMultipleComponent]
    public class LDK2DMarkup : MonoBehaviour
    {
        public List<LDK2DMarkStroke> strokes = new List<LDK2DMarkStroke>();
        public List<LDK2DMarkText> texts = new List<LDK2DMarkText>();
        public List<LDK2DMarkBox> boxes = new List<LDK2DMarkBox>();
    }
}
