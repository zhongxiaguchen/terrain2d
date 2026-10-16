using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Terrain2D
{
    /// <summary>
    /// 共享的网格组装工具。让 PolygonMesh2D 和 HeightmapTerrain2D 产出的网格保持一致
    /// （法线、切线、索引格式、包围盒）。
    /// </summary>
    internal static class MeshUtil
    {
        /// <summary>
        /// 用给定缓冲区填充并返回 <paramref name="mesh"/>。
        /// 2D 网格朝 -Z，和 SpriteRenderer 生成的一致，这样 URP 2D 渲染器下的
        /// Sprite-Lit 类着色器表现和精灵完全一样。
        /// </summary>
        public static Mesh Build(string name, Mesh mesh,
                                 List<Vector3> verts,
                                 List<Vector2> uvs,
                                 List<int> tris,
                                 bool flipWinding)
        {
            if (mesh == null)
            {
                mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            }
            else
            {
                mesh.Clear();
                mesh.name = name;
            }

            mesh.indexFormat = verts.Count > 65000
                ? IndexFormat.UInt32
                : IndexFormat.UInt16;

            if (flipWinding)
            {
                for (int i = 0; i + 2 < tris.Count; i += 3)
                {
                    int t = tris[i];
                    tris[i] = tris[i + 1];
                    tris[i + 1] = t;
                }
            }

            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);

            var normals = new List<Vector3>(verts.Count);
            var tangents = new List<Vector4>(verts.Count);
            for (int i = 0; i < verts.Count; i++)
            {
                normals.Add(Vector3.back);
                tangents.Add(new Vector4(1f, 0f, 0f, -1f));
            }
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);

            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 没指定材质时自动挂一个合适的默认材质。
        /// 优先用 URP 2D 的着色器（本项目是 2D Renderer），最后兜底到内置的 Sprites/Default，
        /// 保证组件不会渲染成品红色。
        /// </summary>
        static readonly string[] DefaultShaders =
        {
            "Universal Render Pipeline/2D/Sprite-Lit-Default",
            "Universal Render Pipeline/2D/Sprite-Unlit-Default",
            "Sprites/Default",
        };

        public static void EnsureMaterial(MeshRenderer renderer)
        {
            if (renderer == null) return;
            if (renderer.sharedMaterial != null) return;

            for (int i = 0; i < DefaultShaders.Length; i++)
            {
                var shader = Shader.Find(DefaultShaders[i]);
                if (shader == null) continue;

                renderer.sharedMaterial = new Material(shader)
                {
                    name = "Terrain2D (" + shader.name + ")"
                };

                // 必须 mark dirty，否则编辑器里现造的这份材质**不会被保存**。
                //
                // 后果有两个，第二个是致命的：
                //   1. 每次加载场景 / 打开 prefab 都重新造一份材质 —— 材质泄漏；
                //   2. 材质没存进场景，着色器引用也就不在场景里。打包时这个着色器
                //      可能被剥离（它只被运行时的 Shader.Find 用到，构建管线看不见），
                //      地形到玩家机器上就渲染成粉红色。
                //
                // 存下来之后，着色器引用随场景一起进构建，问题从根上消失。
#if UNITY_EDITOR
                if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(renderer);
#endif
                return;
            }

            Debug.LogWarning("Terrain2D：找不到默认的精灵着色器，请手动指定材质。");
        }

        // 不同着色器给颜色属性起的名字不一样，挨个试。
        static readonly string[] ColorProperties = { "_Color", "_BaseColor", "_TintColor" };

        // 复用同一个 block，避免拖动时每帧都 new 一个。
        // 每次用之前 Clear()，所以不会把上一个渲染器的覆盖值带过来。
        static readonly MaterialPropertyBlock ColorBlock = new MaterialPropertyBlock();

        // 已经为"材质没有颜色属性"警告过的材质实例，避免每次重建都刷屏。
        static readonly HashSet<int> WarnedMaterials = new HashSet<int>();

        /// <summary>
        /// 把颜色应用到**这一个**渲染器上。
        ///
        /// 关键：用 MaterialPropertyBlock，而不是直接改材质对象。
        ///
        /// 以前写的是 `renderer.sharedMaterial.SetColor(...)` —— 那是写进 Material 本身。
        /// 而 EnsureMaterial 自动挂的是"内嵌材质"（不是项目里的材质资产），
        /// 复制物体、或者同一个 prefab 的多个实例，会指向**同一个 Material 对象**，
        /// 于是改一个颜色，所有共用它的物体一起变。
        ///
        /// MaterialPropertyBlock 是逐渲染器的覆盖值：不碰材质对象、不实例化新材质，
        /// 天生就是"每个物体一份"。颜色本身存在组件的 color 字段里（会序列化），
        /// 每次 OnEnable / Rebuild 都会重新刷一遍，所以不依赖 block 自己被保存。
        /// </summary>
        public static void ApplyColor(MeshRenderer renderer, Color color)
        {
            if (renderer == null) return;

            var material = renderer.sharedMaterial;
            if (material == null) return;

            ColorBlock.Clear();
            renderer.GetPropertyBlock(ColorBlock);

            for (int i = 0; i < ColorProperties.Length; i++)
            {
                int propertyId = Shader.PropertyToID(ColorProperties[i]);
                if (!material.HasProperty(propertyId)) continue;

                ColorBlock.SetColor(propertyId, color);
                renderer.SetPropertyBlock(ColorBlock);
                return;
            }

            // 材质上三个颜色属性一个都没有 —— 颜色工具对它完全无效，而且**不会报错**。
            // 用户手动指定了一个不带颜色的材质时就是这个情况，光看现象只会觉得"改了没反应"。
            // 每个材质只警告一次，免得拖动时每帧刷屏。
            if (!WarnedMaterials.Contains(material.GetInstanceID()))
            {
                WarnedMaterials.Add(material.GetInstanceID());
                Debug.LogWarning(
                    "Terrain 2D：材质 \"" + material.name + "\" 上没有 _Color / _BaseColor / " +
                    "_TintColor 任何一个属性，颜色工具对它无效。换一个精灵着色器，" +
                    "或者把材质槽留空让组件自动生成。",
                    renderer);
            }
        }

        /// <summary>
        /// 设置 2D 排序层与顺序。层名不存在时保持原样，避免 Unity 报错刷屏。
        /// </summary>
        public static void ApplySorting(MeshRenderer renderer, string layerName, int order)
        {
            if (renderer == null) return;

            renderer.sortingOrder = order;

            if (string.IsNullOrEmpty(layerName)) return;

            var layers = SortingLayer.layers;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].name != layerName) continue;
                renderer.sortingLayerName = layerName;
                return;
            }
        }
    }
}
