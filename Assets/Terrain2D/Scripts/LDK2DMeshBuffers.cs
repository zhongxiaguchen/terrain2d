using System.Collections.Generic;
using UnityEngine;

namespace LDK2D
{
    /// <summary>
    /// 重建网格时复用的缓冲区。**一个组件持有一份**，不要跨物体共用。
    ///
    /// **为什么要有它**：以前每次重建都 `new List` 三份，`LDK2DMeshUtil` 里再
    /// `new List` 两份 —— 512 个采样点一次就是 ~61 KB 垃圾。拖动时按 60Hz 算，
    /// 等于每秒 3.7 MB。编辑器里"拖起来发涩"主要就是这一条。
    /// 改成字段级复用之后，稳态下重建**零分配**。
    ///
    /// **法线和切线是常量**：2D 网格所有顶点的法线都是 `Vector3.back`、
    /// 切线都是 `(1,0,0,-1)`。只在顶点数变化时重填一次，平时一次都不碰。
    ///
    /// **拓扑没变就不清网格**：顶点数、索引数、翻转开关都没变时，三角形的索引
    /// 必然一模一样 —— 直接跳过 `Clear()` / `SetTriangles()` / 法线切线，
    /// 只传顶点和 UV。拖动高度走的正是这条路。
    /// </summary>
    internal sealed class LDK2DMeshBuffers
    {
        public readonly List<Vector3> Verts = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<int> Tris = new List<int>();

        readonly List<Vector3> _normals = new List<Vector3>();
        readonly List<Vector4> _tangents = new List<Vector4>();
        int _constantCount = -1;

        public List<Vector3> Normals => _normals;
        public List<Vector4> Tangents => _tangents;

        /// <summary>上一次提交时的顶点数 / 索引数 / 翻转状态，用来判断拓扑变没变。</summary>
        public int LastVertCount = -1;
        public int LastTriCount = -1;
        public bool LastFlip;

        /// <summary>
        /// 开始填数据。只 `Clear()` **不释放容量**，所以稳态下不会重新分配。
        /// </summary>
        public void Begin(int expectedVerts, int expectedTris)
        {
            Verts.Clear();
            Uvs.Clear();
            Tris.Clear();

            if (Verts.Capacity < expectedVerts) Verts.Capacity = expectedVerts;
            if (Uvs.Capacity < expectedVerts) Uvs.Capacity = expectedVerts;
            if (Tris.Capacity < expectedTris) Tris.Capacity = expectedTris;
        }

        /// <summary>法线 / 切线是常量，只在顶点数变化时重填。</summary>
        public void EnsureConstants(int count)
        {
            if (_constantCount == count) return;

            _normals.Clear();
            _tangents.Clear();

            if (_normals.Capacity < count) _normals.Capacity = count;
            if (_tangents.Capacity < count) _tangents.Capacity = count;

            for (int i = 0; i < count; i++)
            {
                _normals.Add(Vector3.back);
                _tangents.Add(new Vector4(1f, 0f, 0f, -1f));
            }

            _constantCount = count;
        }

        /// <summary>
        /// 强制下一次 `LDK2DMeshUtil.Build` 走全量重建。
        ///
        /// 耳切那条路要用它：索引是算出来的，**不保证"点数相同就索引相同"**，
        /// 所以不能吃"拓扑没变"的复用快路径。
        /// </summary>
        public void Invalidate()
        {
            LastVertCount = -1;
            LastTriCount = -1;
        }
    }
}
