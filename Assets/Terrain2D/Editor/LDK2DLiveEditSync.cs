using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LDK2D.EditorTools
{
    /// <summary>
    /// 「实时编辑」的落地：Play 模式下改的东西，退出 Play 之后要留下来。
    ///
    /// **为什么需要它**：Unity 进 Play 时给整个场景拍一张快照，退出时按快照恢复。
    /// 于是 Play 里发生的事全被倒带 ——
    ///
    /// | Play 里做了什么 | Unity 退出时 | 这里要补的动作 |
    /// |---|---|---|
    /// | 改（拖点 / 改参数 / 移动） | 数据被抹回进 Play 前的样子 | 把 Play 里的数据写回去，再 Rebuild |
    /// | 删 | 物体**复活** | 再删一次 |
    /// | 增 | 物体**销毁** | 按序列化数据重建 |
    ///
    /// 三件事都做，才算「Play 里看到的场景 = 退出后的场景」。
    ///
    /// **只处理** <see cref="LDK2DHeightmapTerrain"/> 和 <see cref="LDK2DTestActor"/>。
    /// 标记笔迹有自己那一套（`LDK2DMarkupTool` 的 RescueKey），不在这里重复。
    ///
    /// **名单按 instanceId 对齐**：进出 Play 是同一个编辑器进程、同一批场景对象，
    /// instanceId 不会变。极端情况下对不上时按层级路径兜底。
    ///
    /// **开关以退出 Play 那一刻为准**。名单在进 Play 时**无条件**拍一份，
    /// 所以中途把开关打开也能正常写回；中途关掉则不写回。
    /// </summary>
    [InitializeOnLoad]
    internal static class LDK2DLiveEditSync
    {
        const string BaselineKey = "LDK2D.LiveEditBaseline";
        const string SnapshotKey = "LDK2D.LiveEditSnapshot";

        /// <summary>
        /// 名单里的一项。
        ///
        /// `json` 只在「退出 Play」那一份里有 —— 基线那份只记身份，不记数据，
        /// 免得每次按 Play 都序列化一遍整张场景。
        ///
        /// **transform 要单独存**：JsonUtility 只序列化组件自己的字段，
        /// 不含位置/旋转/缩放。而占位标记的尺寸就是靠 localScale 撑出来的，
        /// 不存的话重建出来全是默认大小。
        /// </summary>
        [System.Serializable]
        class Item
        {
            public int id;
            public string name;
            public string path;
            public Vector3 position;
            public Vector3 eulerAngles;
            public Vector3 localScale;
            public string json;
        }

        [System.Serializable]
        class Roster
        {
            /// <summary>
            /// 拍名单时**当前激活场景**的路径。
            ///
            /// 用来挡住"Play 里换了场景"这种情况：换了场景之后，基线里那些物体
            /// 已经不是同一批了，按 id 对齐会把原场景的地形判成"被删了"再删一次 ——
            /// 那是直接毁关卡。所以两边场景不一致就整次放弃。
            /// </summary>
            public string scenePath;

            public List<Item> terrains = new List<Item>();
            public List<Item> actors = new List<Item>();
        }

        static LDK2DLiveEditSync()
        {
            // 静态构造在域重载后会再跑一次，先减再加，避免重复订阅。
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    // 进 Play 之前拍身份名单。**不看开关** —— 中途把开关打开也要能用。
                    SessionState.SetString(BaselineKey, JsonUtility.ToJson(Capture(false)));
                    break;

                case PlayModeStateChange.ExitingPlayMode:
                    if (!LDK2DTerrainStage.LiveEdit)
                    {
                        // 没开实时编辑 = 纯跑图。基线也一并清掉，别留在 SessionState 里。
                        SessionState.EraseString(BaselineKey);
                        SessionState.EraseString(SnapshotKey);
                        return;
                    }

                    SessionState.SetString(SnapshotKey, JsonUtility.ToJson(Capture(true)));
                    break;

                case PlayModeStateChange.EnteredEditMode:
                    Restore();
                    break;
            }
        }

        // ------------------------------------------------------------------ 拍名单

        static Roster Capture(bool withData)
        {
            var roster = new Roster
            {
                // 用运行时的 SceneManager，不猜 EditorSceneManager 上有没有同名方法。
                scenePath = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path
            };

            foreach (var terrain in Object.FindObjectsOfType<LDK2DHeightmapTerrain>(true))
            {
                if (terrain == null) continue;
                roster.terrains.Add(MakeItem(terrain.transform, withData ? (Component)terrain : null));
            }

            foreach (var actor in Object.FindObjectsOfType<LDK2DTestActor>(true))
            {
                if (actor == null) continue;
                roster.actors.Add(MakeItem(actor.transform, withData ? (Component)actor : null));
            }

            return roster;
        }

        static Item MakeItem(Transform tr, Component component)
        {
            var item = new Item
            {
                id = tr.gameObject.GetInstanceID(),
                name = tr.gameObject.name,
                path = PathOf(tr),
                position = tr.position,
                eulerAngles = tr.eulerAngles,
                localScale = tr.localScale
            };

            // 用 JsonUtility 而不是 EditorJsonUtility：
            // 后者写出来的是**原生序列化布局**，带 m_GameObject / m_Script 这些 PPtr。
            // 拿它 FromJsonOverwrite 到一个新建的组件上，等于把 m_GameObject 指到别的物体，
            // 轻则被忽略、重则把组件搞坏。JsonUtility 只写组件自己的可序列化字段，
            // 跨物体搬运才是安全的。
            if (component != null) item.json = JsonUtility.ToJson(component);

            return item;
        }

        /// <summary>层级路径，`根/子/孙`。instanceId 对不上时用来兜底找物体。</summary>
        static string PathOf(Transform tr)
        {
            string path = tr.name;
            Transform parent = tr.parent;

            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }

        // ------------------------------------------------------------------ 写回

        static void Restore()
        {
            string rawSnapshot = SessionState.GetString(SnapshotKey, string.Empty);
            string rawBaseline = SessionState.GetString(BaselineKey, string.Empty);

            // 先清再处理：中途抛异常也不会留一份过期的数据，下次进 Play 重新拍。
            SessionState.EraseString(SnapshotKey);
            SessionState.EraseString(BaselineKey);

            Roster final = Parse(rawSnapshot);
            if (final == null) return;

            Roster baseline = Parse(rawBaseline);
            if (baseline == null) baseline = new Roster();

            // Play 里换了场景：基线那批物体和现在这批已经不是一回事了，
            // 按 id 对齐会把**原场景的地形**判成"被删了"再删一次 —— 那是直接毁关卡。
            // 整次放弃，宁可这次不写回。
            if (!string.IsNullOrEmpty(baseline.scenePath) &&
                baseline.scenePath != final.scenePath)
                return;

            int touched = 0;
            touched += ApplyTerrains(final.terrains, baseline.terrains);
            touched += ApplyActors(final.actors, baseline.actors);

            if (touched > 0) SceneView.RepaintAll();
        }

        static Roster Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                return JsonUtility.FromJson<Roster>(json);
            }
            catch (System.Exception)
            {
                // 存档格式对不上（改过字段名、或者被别的东西写坏）就当没有。
                // 宁可这次不写回，也不能在退出 Play 的时候抛异常打断编辑器。
                return null;
            }
        }

        static int ApplyTerrains(List<Item> final, List<Item> baseline)
        {
            HashSet<int> baseIds = Ids(baseline);
            HashSet<int> finalIds = Ids(final);

            int touched = 0;

            // ---- 改 + 增 ----
            for (int i = 0; i < final.Count; i++)
            {
                Item item = final[i];
                // 基线里有它 → 「改」：找回来，把 Play 里的数据盖上去。
                // 基线里没有 → 「增」：Play 里新建的，退出时已经被 Unity 销毁，按数据重建。
                //
                // **必须按"基线里有没有"分流，不能按"找没找到"分流**：新建的物体要是
                // 和老物体重名，路径兜底会误抓到老物体，把老物体的数据盖成新的。
                if (!baseIds.Contains(item.id))
                {
                    touched += Recreate(item, go =>
                    {
                        var terrain = go.AddComponent<LDK2DHeightmapTerrain>();
                        JsonUtility.FromJsonOverwrite(item.json, terrain);

                        // 打开 → OnEnable 跑一次。_initialized 已经是 true，
                        // 所以走的是 Rebuild 而不是 Generate（Generate 会把形状重新随机一遍）。
                        go.SetActive(true);
                        terrain.Rebuild();
                    });

                    continue;
                }

                var live = Resolve<LDK2DHeightmapTerrain>(item);

                // 基线里有、退出后却找不回来：宁可不动，也不新建一个来顶替。
                if (live == null) continue;

                // 没动过就别写回。否则每次按 Play 再退出都会把场景标脏 ——
                // 用户什么都没干，却要处理一个「有未保存的改动」提示。
                if (Unchanged(live, item)) continue;

                // 改：数据整个盖回去。先拍 Undo，退出后一个 Ctrl+Z 能把整次回写撤掉。
                Undo.RegisterCompleteObjectUndo(live, "同步实时编辑");
                Undo.RecordObject(live.transform, "同步实时编辑");

                ApplyTransform(live.transform, item);
                JsonUtility.FromJsonOverwrite(item.json, live);
                live.Rebuild();

                EditorUtility.SetDirty(live);
                EditorSceneManager.MarkSceneDirty(live.gameObject.scene);
                touched++;
            }

            // ---- 删 ----
            for (int i = 0; i < baseline.Count; i++)
            {
                Item item = baseline[i];
                if (finalIds.Contains(item.id)) continue;

                var victim = Resolve<LDK2DHeightmapTerrain>(item);
                if (victim == null) continue;

                Undo.DestroyObjectImmediate(victim.gameObject);
                touched++;
            }

            return touched;
        }

        static int ApplyActors(List<Item> final, List<Item> baseline)
        {
            HashSet<int> baseIds = Ids(baseline);
            HashSet<int> finalIds = Ids(final);

            int touched = 0;

            for (int i = 0; i < final.Count; i++)
            {
                Item item = final[i];
                // 分流规则同上：基线里没有的就是 Play 里新造的。
                if (!baseIds.Contains(item.id))
                {
                    touched += Recreate(item, go =>
                    {
                        var actor = go.AddComponent<LDK2DTestActor>();
                        JsonUtility.FromJsonOverwrite(item.json, actor);
                        actor.ApplyVisual();

                        // 打开 → OnEnable → EnsurePhysics，按 kind 配碰撞
                        // （奖励点触发器、敌人刚体 + 行为脚本）。
                        go.SetActive(true);
                    });

                    continue;
                }

                var live = Resolve<LDK2DTestActor>(item);
                if (live == null) continue;

                if (Unchanged(live, item)) continue;

                Undo.RegisterCompleteObjectUndo(live, "同步实时编辑");
                Undo.RecordObject(live.transform, "同步实时编辑");

                ApplyTransform(live.transform, item);
                JsonUtility.FromJsonOverwrite(item.json, live);
                live.ApplyVisual();

                EditorUtility.SetDirty(live);
                EditorSceneManager.MarkSceneDirty(live.gameObject.scene);
                touched++;
            }

            for (int i = 0; i < baseline.Count; i++)
            {
                Item item = baseline[i];
                if (finalIds.Contains(item.id)) continue;

                var victim = Resolve<LDK2DTestActor>(item);
                if (victim == null) continue;

                Undo.DestroyObjectImmediate(victim.gameObject);
                touched++;
            }

            return touched;
        }

        /// <summary>
        /// 建一个**先关着**的物体，让调用方把组件和数据都装好再打开。
        ///
        /// 先关着是必须的：这两个组件都带 `[ExecuteAlways]`，AddComponent 那一刻
        /// OnEnable 会**同步**跑一遍。这时候字段还是默认值 —— 占位标记会按默认的
        /// 「敌人」去配刚体，地形会按默认形状先建一次网格。装好再打开，那一次
        /// OnEnable 才是拿到正确数据跑的。
        /// </summary>
        static int Recreate(Item item, System.Action<GameObject> build)
        {
            var go = new GameObject(item.name);
            go.SetActive(false);
            ApplyTransform(go.transform, item);

            build(go);

            Undo.RegisterCreatedObjectUndo(go, "同步实时编辑");
            EditorSceneManager.MarkSceneDirty(go.scene);

            return 1;
        }

        /// <summary>
        /// Play 里到底动过没有。
        ///
        /// 比的是**序列化字符串**：同一个物体、同一个序列化器、同一批值，
        /// 出来的字符串就是一模一样的。不用逐字段比较，也不用担心漏了哪个字段 ——
        /// 将来给地形加新参数，这里自动跟着生效。
        /// </summary>
        static bool Unchanged(Component live, Item item)
        {
            if (JsonUtility.ToJson(live) != item.json) return false;

            Transform tr = live.transform;

            // Vector3 的 == 是近似比较（1e-5），正是这里想要的。
            return tr.position == item.position
                && tr.eulerAngles == item.eulerAngles
                && tr.localScale == item.localScale;
        }

        static void ApplyTransform(Transform tr, Item item)
        {
            tr.position = item.position;
            tr.eulerAngles = item.eulerAngles;
            tr.localScale = item.localScale;
        }

        static HashSet<int> Ids(List<Item> items)
        {
            var set = new HashSet<int>();
            for (int i = 0; i < items.Count; i++) set.Add(items[i].id);
            return set;
        }

        static T Resolve<T>(Item item) where T : Component
        {
            // 同一个编辑器进程里，场景物体的 instanceId 进出 Play 不变 —— 这是主路径。
            var direct = EditorUtility.InstanceIDToObject(item.id) as T;
            if (direct != null) return direct;

            // 兜底：按层级路径扫描。
            //
            // 不用 GameObject.Find：它找不到未激活的物体，而这里要能找的就是
            // 「刚才被 Unity 恢复回来」的那些，状态不确定。直接遍历同类型物体
            // 比路径字符串猜更可靠，数量也就几十个，O(n²) 无所谓。
            if (string.IsNullOrEmpty(item.path)) return null;

            foreach (var candidate in Object.FindObjectsOfType<T>(true))
            {
                if (candidate == null) continue;
                if (PathOf(candidate.transform) == item.path) return candidate;
            }

            return null;
        }
    }
}
