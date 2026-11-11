using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LDK2D.EditorTools
{
    /// <summary>
    /// 编辑器 ↔ 外部（AI / 脚本）的实时双向通道。走**文件**，不走网络。
    ///
    /// 为什么不走 socket / MCP：这一层要能在**编辑模式和 Play 模式**同时活着，
    /// 而且不能引入任何依赖。一个轮询文件的编辑器服务，是唯一同时满足
    /// "零依赖 / 不用重启 / 能在 Play 里跑"的做法。
    ///
    /// 目录：`{项目根}/LDK2DLive/`
    ///
    /// | 文件 | 谁写 | 内容 |
    /// |---|---|---|
    /// | `commands.jsonl` | 外部 → 编辑器 | 追加写，一行一条补丁 |
    /// | `events.jsonl` | 编辑器 → 外部 | 追加写，一行一个事件 |
    /// | `state.json` | 编辑器 | 全量快照，改动后 500ms 内刷新 |
    /// | `heartbeat.json` | 编辑器 | 每秒一次，活着没有 / 排了多少 |
    /// | `cursor.json` | 编辑器 | 读到哪了，防重放 |
    ///
    /// **补丁的粒度是「哪个区间、什么值」，不是「重新生成整块地形」。**
    /// 这条决定了一切：命令层不需要知道怎么建网格，地形也不需要为 AI 开新接口。
    ///
    /// 三条硬规则：
    ///
    /// 1. **每帧最多花 2ms**（<see cref="FrameBudgetMs"/>）。AI 发 1 条还是 500 条，
    ///    编辑器都不会掉帧，只是生效得慢一点。这是"几乎无卡顿"的唯一保证。
    /// 2. **人正在拖的地形，AI 的补丁进等待队列**，松手才应用。判据是
    ///    <see cref="LDK2DHeightmapTerrain.IsInteractive"/>，不是"最近谁动过"。
    /// 3. **一条命令 = 一步撤销**。AI 改坏了，一次 Ctrl+Z 就能退。
    /// </summary>
    [InitializeOnLoad]
    public static class LDK2DLiveBridge
    {
        // ------------------------------------------------------------------ 常量

        /// <summary>多久看一次命令文件。16ms ≈ 60Hz，和编辑器刷新对齐。</summary>
        const double PollIntervalSec = 0.016;

        /// <summary>
        /// 一帧最多花在应用补丁上的时间。
        /// 这个数字是"无卡顿"的硬上限 —— 超过它，宁可下一帧继续。
        /// </summary>
        const double FrameBudgetMs = 2.0;

        /// <summary>同一个地形，事件最多 10Hz。不节流的话 AI 侧会被 60Hz 淹掉。</summary>
        const double EventIntervalSec = 0.1;

        /// <summary>停手多久算"稳定"，补一条 settle。AI 靠这条判断"人改完了，该你了"。</summary>
        const double SettleDelaySec = 0.2;

        /// <summary>全量快照最快多久刷一次。</summary>
        const double StateIntervalSec = 0.5;

        /// <summary>心跳周期。</summary>
        const double HeartbeatIntervalSec = 1.0;

        /// <summary>事件流超过这个大小就轮转一份，别无限长。</summary>
        const long EventLogLimit = 512 * 1024;

        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        // ------------------------------------------------------------------ 路径

        static string Dir { get { return Path.Combine(ProjectRoot, "LDK2DLive"); } }
        static string CommandsPath { get { return Path.Combine(Dir, "commands.jsonl"); } }
        static string EventsPath { get { return Path.Combine(Dir, "events.jsonl"); } }
        static string StatePath { get { return Path.Combine(Dir, "state.json"); } }
        static string HeartbeatPath { get { return Path.Combine(Dir, "heartbeat.json"); } }
        static string CursorPath { get { return Path.Combine(Dir, "cursor.json"); } }

        static string ProjectRoot
        {
            get
            {
                // Application.dataPath = <项目>/Assets，砍掉最后一段。
                string d = Application.dataPath;
                int cut = d.LastIndexOfAny(new[] { '/', '\\' });
                return cut > 0 ? d.Substring(0, cut) : d;
            }
        }

        // ------------------------------------------------------------------ 状态

        static long _cmdOffset;
        static readonly List<byte> _cmdCarry = new List<byte>(4096);

        static readonly Queue<Command> _queue = new Queue<Command>();
        static readonly Dictionary<int, Track> _tracks = new Dictionary<int, Track>();

        static LDK2DHeightmapTerrain[] _terrains = new LDK2DHeightmapTerrain[0];
        static bool _terrainsDirty = true;

        static double _nextPoll;
        static double _nextState;
        static double _nextHeartbeat;
        static double _nextCursorSave;

        static bool _stateDirty = true;
        static bool _started;

        static int _applied;
        static int _failed;
        static string _lastError = "";

        /// <summary>
        /// 刚被 AI 改过的地形（InstanceID）。下一轮 <see cref="WatchTerrains"/> 看到
        /// Revision 变了就取走这个标记，给事件打上 <c>src:"ai"</c>。
        ///
        /// 为什么不在应用时直接改 track：`Rebuild()` 是同步的，但事件是在**下一轮**
        /// Tick 才发出来的，中间那个"正在应用"的临时标记早就被清空了 ——
        /// 那样所有事件都会变成 "human"，AI 改完自己的改动看起来像是人在改。
        /// </summary>
        static readonly HashSet<int> _aiTouched = new HashSet<int>();

        /// <summary>
        /// 每块地形上次看到的状态。用 InstanceID 当键 —— 物体被删了 ID 就失效，
        /// 自然退出字典，不用手动清。
        /// </summary>
        class Track
        {
            public LDK2DHeightmapTerrain terrain;
            public int revision;
            public float[] heights;
            public double lastEmit;
            public double settleAt;
            public int from = int.MaxValue;
            public int to = -1;
            public bool pending;
            public bool fromAi;
        }

        /// <summary>一条命令。字段开得比实际用到的多，`JsonUtility` 会忽略不认识的。</summary>
        [Serializable]
        class Command
        {
            public string id;
            public string op;
            public string target;
            public string field;

            public float value;
            public int start;
            public float[] values;
            public int from;
            public int to;
            public float x;
            public float y;
            public float radius;
            public float delta;
            public int seed;
            public int passes;

            public string kind;
            public string name;
            public string note;
        }

        enum Result
        {
            Ok,
            Deferred,
            Failed
        }

        // ------------------------------------------------------------------ 生命周期

        static LDK2DLiveBridge()
        {
            try
            {
                EnsureDir();
                LoadCursor();
                RotateEventLogIfHuge();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LDK2D] 实时通道初始化失败：" + e.Message);
                return;
            }

            EditorApplication.update += Tick;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
            EditorApplication.quitting += SaveCursor;

            _started = true;

            Emit(sb =>
            {
                sb.Append("\"kind\":\"session\"");
                sb.Append(",\"unity\":");
                Str(sb, Application.unityVersion);
                sb.Append(",\"play\":").Append(Application.isPlaying ? "true" : "false");
                sb.Append(",\"project\":");
                Str(sb, ProjectRoot);
            });
        }

        static void OnHierarchyChanged()
        {
            _terrainsDirty = true;
            _stateDirty = true;
        }

        static void OnBeforeReload()
        {
            SaveCursor();
        }

        static void EnsureDir()
        {
            if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
            if (!File.Exists(CommandsPath)) File.WriteAllText(CommandsPath, "", Utf8);
            if (!File.Exists(EventsPath)) File.WriteAllText(EventsPath, "", Utf8);
        }

        // ------------------------------------------------------------------ 主循环

        static void Tick()
        {
            if (!_started) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

            double now = EditorApplication.timeSinceStartup;

            if (now >= _nextPoll)
            {
                _nextPoll = now + PollIntervalSec;
                ReadCommands();
                DrainQueue();
                WatchTerrains(now);
            }

            if (_stateDirty && now >= _nextState)
            {
                _nextState = now + StateIntervalSec;
                WriteState();
            }

            if (now >= _nextHeartbeat)
            {
                _nextHeartbeat = now + HeartbeatIntervalSec;
                WriteHeartbeat(now);
            }

            if (now >= _nextCursorSave)
            {
                _nextCursorSave = now + 1.0;
                SaveCursor();
            }
        }

        // ------------------------------------------------------------------ 读命令

        static void ReadCommands()
        {
            if (!File.Exists(CommandsPath)) return;

            long len;
            try
            {
                len = new FileInfo(CommandsPath).Length;
            }
            catch
            {
                return;
            }

            // 文件被外部重建过（长度变短），游标回到 0 重新读。
            if (len < _cmdOffset)
            {
                _cmdOffset = 0;
                _cmdCarry.Clear();
            }

            if (len == _cmdOffset) return;

            try
            {
                using (var fs = new FileStream(CommandsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    fs.Seek(_cmdOffset, SeekOrigin.Begin);

                    var buf = new byte[len - _cmdOffset];
                    int read = fs.Read(buf, 0, buf.Length);
                    if (read <= 0) return;

                    _cmdOffset += read;
                    _cmdCarry.AddRange(new ArraySegment<byte>(buf, 0, read));
                }
            }
            catch
            {
                return;
            }

            // 只消费到最后一个换行 —— 半个 JSON 读进来只会解析失败。
            // **在字节层找换行**，不要先解码：UTF-8 的多字节字符可能正好被切在中间。
            int lastNewline = -1;
            for (int i = _cmdCarry.Count - 1; i >= 0; i--)
            {
                if (_cmdCarry[i] == (byte)'\n')
                {
                    lastNewline = i;
                    break;
                }
            }

            if (lastNewline < 0) return;

            var block = new byte[lastNewline];
            _cmdCarry.CopyTo(0, block, 0, lastNewline);
            _cmdCarry.RemoveRange(0, lastNewline + 1);

            string text = Utf8.GetString(block);

            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line[0] == '#') continue;   // 允许注释行，方便手写测试
                Enqueue(line);
            }
        }

        static void Enqueue(string line)
        {
            Command cmd;
            try
            {
                cmd = JsonUtility.FromJson<Command>(line);
            }
            catch (Exception e)
            {
                Fail(null, "JSON 解析失败：" + e.Message);
                return;
            }

            if (cmd == null || string.IsNullOrEmpty(cmd.op))
            {
                Fail(null, "命令缺少 op 字段");
                return;
            }

            if (string.IsNullOrEmpty(cmd.id))
                cmd.id = "auto-" + Guid.NewGuid().ToString("N").Substring(0, 8);

            _queue.Enqueue(cmd);
        }

        // ------------------------------------------------------------------ 消化命令

        static void DrainQueue()
        {
            if (_queue.Count == 0) return;

            var sw = System.Diagnostics.Stopwatch.StartNew();

            // 最多绕一圈：被推迟的命令会回到队尾，不能在同一次 Tick 里无限转。
            int guard = _queue.Count;

            while (guard-- > 0 && _queue.Count > 0)
            {
                Command cmd = _queue.Dequeue();
                Result r = Apply(cmd);

                if (r == Result.Deferred) _queue.Enqueue(cmd);
                else if (r == Result.Ok) _applied++;
                else _failed++;

                if (sw.Elapsed.TotalMilliseconds >= FrameBudgetMs) break;
            }
        }

        static Result Apply(Command cmd)
        {
            try
            {
                switch (cmd.op)
                {
                    case "describe":
                    case "ping":
                        _stateDirty = true;
                        WriteState();
                        Emit(sb => sb.Append("\"kind\":\"described\""));
                        return Result.Ok;

                    case "spawn":
                        return DoSpawn(cmd);

                    case "remove":
                        return DoRemove(cmd);
                }

                LDK2DHeightmapTerrain terrain = ResolveTerrain(cmd.target);
                if (terrain == null)
                {
                    Fail(cmd, "找不到地形：" + (cmd.target ?? "(空)"));
                    return Result.Failed;
                }

                // 人在拖这块地形 → 让位。判据只看"现在有没有活跃拖动"，
                // 不看"最近谁动过"这种模糊条件。
                if (terrain.IsInteractive)
                    return Result.Deferred;

                switch (cmd.op)
                {
                    case "patch":
                        return DoPatch(cmd, terrain);
                    case "set":
                        return DoSet(cmd, terrain);
                    case "flatten":
                        return DoFlatten(cmd, terrain);
                    case "raise":
                        return DoCircle(cmd, terrain, subtract: false);
                    case "dig":
                        return DoCircle(cmd, terrain, subtract: true);
                    case "generate":
                        return DoGenerate(cmd, terrain);
                    case "smooth":
                        return DoSmooth(cmd, terrain);
                    default:
                        Fail(cmd, "不认识的 op：" + cmd.op);
                        return Result.Failed;
                }
            }
            catch (Exception e)
            {
                Fail(cmd, e.GetType().Name + "：" + e.Message);
                return Result.Failed;
            }
            finally
            {
                if (cmd.op != "describe" && cmd.op != "ping")
                    _stateDirty = true;
            }
        }

        /// <summary>
        /// 开一个新的撤销组。**一条命令 = 一步 Ctrl+Z**。
        ///
        /// 只在真的要改东西的时候调 —— 放在 <see cref="Apply"/> 开头的话，
        /// 被推迟的命令也会各留一个空组，撤销历史里全是噪声。
        /// </summary>
        static void BeginUndo()
        {
            Undo.IncrementCurrentGroup();
        }

        static void MarkAi(LDK2DHeightmapTerrain terrain)
        {
            if (terrain != null) _aiTouched.Add(terrain.GetInstanceID());
        }

        // ------------------------------------------------------------------ 各个 op

        static Result DoPatch(Command cmd, LDK2DHeightmapTerrain terrain)
        {
            if (cmd.values == null || cmd.values.Length == 0)
            {
                Fail(cmd, "patch 缺少 values");
                return Result.Failed;
            }

            int start = Mathf.Clamp(cmd.start, 0, terrain.sampleCount - 1);
            int count = Mathf.Min(cmd.values.Length, terrain.sampleCount - start);

            BeginUndo();
            Undo.RegisterCompleteObjectUndo(terrain, "AI 补丁");
            terrain.SetHeightsRange(start, cmd.values);
            MarkAi(terrain);

            EmitApplied(cmd, terrain, start, start + count - 1);
            return Result.Ok;
        }

        static Result DoFlatten(Command cmd, LDK2DHeightmapTerrain terrain)
        {
            int from = Mathf.Clamp(Mathf.Min(cmd.from, cmd.to), 0, terrain.sampleCount - 1);
            int to = Mathf.Clamp(Mathf.Max(cmd.from, cmd.to), 0, terrain.sampleCount - 1);

            var values = new float[to - from + 1];
            for (int i = 0; i < values.Length; i++) values[i] = cmd.y;

            BeginUndo();
            Undo.RegisterCompleteObjectUndo(terrain, "AI 压平");
            terrain.SetHeightsRange(from, values);
            MarkAi(terrain);

            EmitApplied(cmd, terrain, from, to);
            return Result.Ok;
        }

        static Result DoCircle(Command cmd, LDK2DHeightmapTerrain terrain, bool subtract)
        {
            if (cmd.radius <= 0f)
            {
                Fail(cmd, "半径必须大于 0");
                return Result.Failed;
            }

            BeginUndo();
            Undo.RegisterCompleteObjectUndo(terrain, subtract ? "AI 挖坑" : "AI 堆土");

            var center = new Vector2(cmd.x, cmd.y);
            if (subtract) terrain.DestroyCircle(center, cmd.radius);
            else terrain.RaiseCircle(center, cmd.radius);

            MarkAi(terrain);

            EmitApplied(cmd, terrain, -1, -1);
            return Result.Ok;
        }

        static Result DoGenerate(Command cmd, LDK2DHeightmapTerrain terrain)
        {
            BeginUndo();
            Undo.RegisterCompleteObjectUndo(terrain, "AI 重新生成");

            if (cmd.seed != 0) terrain.seed = cmd.seed;
            terrain.Generate();

            MarkAi(terrain);

            EmitApplied(cmd, terrain, 0, terrain.sampleCount - 1);
            return Result.Ok;
        }

        static Result DoSmooth(Command cmd, LDK2DHeightmapTerrain terrain)
        {
            int passes = Mathf.Clamp(cmd.passes <= 0 ? 1 : cmd.passes, 1, 8);

            // 平滑放在这里做，而不是给地形加一个公开方法 ——
            // 它只是"读高度、算平均、写回去"，复用 SetHeightsRange 就够了，
            // 运行时代码不需要为 AI 多长一个接口。
            float[] h = terrain.Heights;
            var work = new float[h.Length];
            Array.Copy(h, work, h.Length);

            for (int p = 0; p < passes; p++)
            {
                for (int i = 1; i < work.Length - 1; i++)
                    work[i] = (work[i - 1] + work[i] + work[i + 1]) / 3f;
            }

            BeginUndo();
            Undo.RegisterCompleteObjectUndo(terrain, "AI 平滑");
            terrain.SetHeightsRange(0, work);
            MarkAi(terrain);

            EmitApplied(cmd, terrain, 0, work.Length - 1);
            return Result.Ok;
        }

        static Result DoSet(Command cmd, LDK2DHeightmapTerrain terrain)
        {
            if (string.IsNullOrEmpty(cmd.field))
            {
                Fail(cmd, "set 缺少 field");
                return Result.Failed;
            }

            FieldInfo f = typeof(LDK2DHeightmapTerrain).GetField(
                cmd.field, BindingFlags.Public | BindingFlags.Instance);

            if (f == null)
            {
                Fail(cmd, "没有这个字段：" + cmd.field);
                return Result.Failed;
            }

            // 白名单：只允许改"不会把物体拆掉"的字段。
            // 名字、层级、transform 这些故意不给改 —— 改了会让外部拿着的 id 失效。
            switch (f.Name)
            {
                case "sampleCount":
                case "spacing":
                case "originX":
                case "bottomY":
                case "bottomLeftOffsetX":
                case "bottomRightOffsetX":
                case "useBottomProfile":
                case "seed":
                case "baseHeight":
                case "amplitude":
                case "frequency":
                case "octaves":
                case "persistence":
                case "lacunarity":
                case "smoothPasses":
                case "uvScale":
                case "uvMode":
                case "flipWinding":
                case "buildColliders":
                case "colliderOffsetY":
                case "colliderStride":
                case "applyColor":
                case "sortingOrder":
                    break;
                default:
                    Fail(cmd, "字段不允许外部修改：" + f.Name);
                    return Result.Failed;
            }

            BeginUndo();
            Undo.RegisterCompleteObjectUndo(terrain, "AI 改参数");

            if (f.FieldType == typeof(float)) f.SetValue(terrain, cmd.value);
            else if (f.FieldType == typeof(int)) f.SetValue(terrain, Mathf.RoundToInt(cmd.value));
            else if (f.FieldType == typeof(bool)) f.SetValue(terrain, cmd.value != 0f);
            else if (f.FieldType == typeof(string)) f.SetValue(terrain, cmd.name ?? "");
            else if (f.FieldType.IsEnum) f.SetValue(terrain, Enum.ToObject(f.FieldType, Mathf.RoundToInt(cmd.value)));
            else
            {
                Fail(cmd, "不支持的字段类型：" + f.FieldType.Name);
                return Result.Failed;
            }

            terrain.Rebuild();
            MarkAi(terrain);

            Emit(sb =>
            {
                sb.Append("\"kind\":\"applied\"");
                sb.Append(",\"cmd\":");
                Str(sb, cmd.id);
                sb.Append(",\"op\":\"set\",\"field\":");
                Str(sb, f.Name);
                sb.Append(",\"target\":");
                Str(sb, IdOf(terrain.gameObject));
            });

            return Result.Ok;
        }

        static Result DoSpawn(Command cmd)
        {
            LDK2DActorKind kind = ParseKind(cmd.kind);

            // 类型名可以用 name，也可以用 field（两条都留着，手写命令时少踩一个坑）。
            string typeName = !string.IsNullOrEmpty(cmd.name) ? cmd.name : cmd.field;

            BeginUndo();

            var pos = new Vector3(cmd.x, cmd.y, 0f);
            LDK2DTestActor actor = LDK2DTestActor.Create(kind, 0, pos);

            if (actor == null)
            {
                Fail(cmd, "创建失败");
                return Result.Failed;
            }

            if (!string.IsNullOrEmpty(typeName))
            {
                actor.typeName = typeName;
                actor.ApplyVisual();
            }

            Undo.RegisterCreatedObjectUndo(actor.gameObject, "AI 放置标记");

            Emit(sb =>
            {
                sb.Append("\"kind\":\"spawned\"");
                sb.Append(",\"cmd\":");
                Str(sb, cmd.id);
                sb.Append(",\"target\":");
                Str(sb, IdOf(actor.gameObject));
                sb.Append(",\"actorKind\":");
                Str(sb, actor.kind.ToString());
                sb.Append(",\"type\":");
                Str(sb, actor.typeName);
            });

            _terrainsDirty = true;
            return Result.Ok;
        }

        static Result DoRemove(Command cmd)
        {
            GameObject go = ResolveObject(cmd.target);
            if (go == null)
            {
                Fail(cmd, "找不到物体：" + (cmd.target ?? "(空)"));
                return Result.Failed;
            }

            string id = IdOf(go);

            BeginUndo();
            Undo.DestroyObjectImmediate(go);

            Emit(sb =>
            {
                sb.Append("\"kind\":\"removed\"");
                sb.Append(",\"cmd\":");
                Str(sb, cmd.id);
                sb.Append(",\"target\":");
                Str(sb, id);
            });

            _terrainsDirty = true;
            return Result.Ok;
        }

        static LDK2DActorKind ParseKind(string s)
        {
            if (string.IsNullOrEmpty(s)) return LDK2DActorKind.Enemy;

            switch (s.Trim().ToLowerInvariant())
            {
                case "npc":
                case "n":
                    return LDK2DActorKind.Npc;
                case "reward":
                case "r":
                case "coin":
                    return LDK2DActorKind.Reward;
                default:
                    return LDK2DActorKind.Enemy;
            }
        }

        // ------------------------------------------------------------------ 监视改动

        /// <summary>
        /// 每块地形都有一个 <see cref="LDK2DHeightmapTerrain.Revision"/> 计数器，
        /// 每次 <c>Rebuild()</c> 加一。这里只比这个数字，**不去比高度数组** ——
        /// 比较是 O(1)，比完之后才 diff 出改动的区间。
        ///
        /// 这样人拖、AI 改、Inspector 改，走的都是同一条路径，不需要谁去通知谁。
        /// </summary>
        static void WatchTerrains(double now)
        {
            if (_terrainsDirty) RefreshTerrains();

            for (int i = 0; i < _terrains.Length; i++)
            {
                LDK2DHeightmapTerrain t = _terrains[i];
                if (t == null) continue;

                int key = t.GetInstanceID();

                Track track;
                if (!_tracks.TryGetValue(key, out track) || track.terrain != t)
                {
                    track = new Track
                    {
                        terrain = t,
                        revision = t.Revision,
                        heights = (float[])t.Heights.Clone(),
                        lastEmit = now,
                        settleAt = 0
                    };
                    _tracks[key] = track;

                    EmitTerrainIntro(t, track);
                    continue;
                }

                if (t.Revision != track.revision)
                {
                    DiffInto(track, t.Heights);

                    track.revision = t.Revision;
                    track.settleAt = now + SettleDelaySec;
                    track.pending = true;
                    track.fromAi = _aiTouched.Remove(key);
                }

                if (!track.pending) continue;

                if (now >= track.settleAt)
                {
                    // 先刷快照再发事件 —— 顺序很重要。外部收到 settle 之后会立刻去读
                    // state.json，如果快照还是 500ms 前的，它算出来的补丁就是错的。
                    WriteState();
                    EmitChange(track, now, true);
                    track.pending = false;
                    track.from = int.MaxValue;
                    track.to = -1;
                    continue;
                }

                if (now - track.lastEmit >= EventIntervalSec)
                    EmitChange(track, now, false);
            }

            // 物体被删掉的地形，把 track 清掉，避免 InstanceID 复用后错配。
            if (_tracks.Count > _terrains.Length)
            {
                var dead = new List<int>();
                foreach (var kv in _tracks)
                {
                    if (kv.Value.terrain == null) dead.Add(kv.Key);
                }
                for (int i = 0; i < dead.Count; i++) _tracks.Remove(dead[i]);
            }
        }

        static void RefreshTerrains()
        {
            _terrains = UnityEngine.Object.FindObjectsOfType<LDK2DHeightmapTerrain>(true);
            _terrainsDirty = false;
        }

        static void DiffInto(Track track, float[] current)
        {
            int n = Mathf.Min(track.heights.Length, current.Length);

            for (int i = 0; i < n; i++)
            {
                if (Mathf.Abs(track.heights[i] - current[i]) <= 1e-6f) continue;
                if (i < track.from) track.from = i;
                if (i > track.to) track.to = i;
            }

            if (current.Length != track.heights.Length)
            {
                track.from = 0;
                track.to = current.Length - 1;
            }

            track.heights = (float[])current.Clone();
        }

        // ------------------------------------------------------------------ 事件输出

        static void EmitTerrainIntro(LDK2DHeightmapTerrain t, Track track)
        {
            float minY = float.MaxValue, maxY = float.MinValue;
            BoundsOf(track.heights, ref minY, ref maxY);

            Emit(sb =>
            {
                sb.Append("\"kind\":\"terrain\"");
                sb.Append(",\"target\":");
                Str(sb, IdOf(t.gameObject));
                sb.Append(",\"samples\":").Append(t.sampleCount);
                sb.Append(",\"spacing\":").Append(Num(t.spacing));
                sb.Append(",\"originX\":").Append(Num(t.originX));
                sb.Append(",\"minY\":").Append(Num(minY));
                sb.Append(",\"maxY\":").Append(Num(maxY));
            });
        }

        static void EmitChange(Track track, double now, bool settle)
        {
            // 物体可能在两次 Tick 之间被删掉。
            if (track.terrain == null) return;

            float minY = float.MaxValue, maxY = float.MinValue;
            BoundsOf(track.heights, ref minY, ref maxY);

            int from = track.from == int.MaxValue ? 0 : track.from;
            int to = track.to < 0 ? track.heights.Length - 1 : track.to;

            string id = IdOf(track.terrain.gameObject);

            track.lastEmit = now;

            Emit(sb =>
            {
                sb.Append(settle ? "\"kind\":\"settle\"" : "\"kind\":\"change\"");
                sb.Append(",\"target\":");
                Str(sb, id);
                sb.Append(",\"range\":[").Append(from).Append(',').Append(to).Append(']');
                sb.Append(",\"revision\":").Append(track.revision);
                sb.Append(",\"minY\":").Append(Num(minY));
                sb.Append(",\"maxY\":").Append(Num(maxY));
                sb.Append(",\"src\":").Append(track.fromAi ? "\"ai\"" : "\"human\"");
            });
        }

        static void EmitApplied(Command cmd, LDK2DHeightmapTerrain terrain, int from, int to)
        {
            Emit(sb =>
            {
                sb.Append("\"kind\":\"applied\"");
                sb.Append(",\"cmd\":");
                Str(sb, cmd.id);
                sb.Append(",\"op\":");
                Str(sb, cmd.op);
                sb.Append(",\"target\":");
                Str(sb, IdOf(terrain.gameObject));
                if (from >= 0)
                    sb.Append(",\"range\":[").Append(from).Append(',').Append(to).Append(']');
            });
        }

        static void Fail(Command cmd, string message)
        {
            _lastError = message;

            string id = cmd != null ? cmd.id : "";

            Emit(sb =>
            {
                sb.Append("\"kind\":\"error\"");
                sb.Append(",\"cmd\":");
                Str(sb, id ?? "");
                sb.Append(",\"message\":");
                Str(sb, message);
            });

            Debug.LogWarning("[LDK2D] 实时通道：" + message);
        }

        static void Emit(Action<StringBuilder> body)
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"t\":").Append(Num((float)EditorApplication.timeSinceStartup));
            sb.Append(',');
            body(sb);
            sb.Append("}\n");

            try
            {
                File.AppendAllText(EventsPath, sb.ToString(), Utf8);
            }
            catch
            {
                // 外部正在读 / 文件被占用，丢一条事件比抛出去好。
            }
        }

        static void BoundsOf(float[] heights, ref float minY, ref float maxY)
        {
            for (int i = 0; i < heights.Length; i++)
            {
                float v = heights[i];
                if (v < minY) minY = v;
                if (v > maxY) maxY = v;
            }

            if (minY > maxY)
            {
                minY = 0f;
                maxY = 0f;
            }
        }

        static void RotateEventLogIfHuge()
        {
            try
            {
                var fi = new FileInfo(EventsPath);
                if (!fi.Exists || fi.Length < EventLogLimit) return;

                string prev = Path.Combine(Dir, "events.prev.jsonl");
                if (File.Exists(prev)) File.Delete(prev);
                File.Move(EventsPath, prev);
                File.WriteAllText(EventsPath, "", Utf8);
            }
            catch
            {
                // 轮转失败无所谓，继续追加。
            }
        }

        // ------------------------------------------------------------------ 全量快照

        static void WriteState()
        {
            if (_terrainsDirty) RefreshTerrains();

            var sb = new StringBuilder(8192);

            sb.Append("{\n");
            sb.Append("  \"t\": ").Append(Num((float)EditorApplication.timeSinceStartup)).Append(",\n");
            sb.Append("  \"play\": ").Append(Application.isPlaying ? "true" : "false").Append(",\n");
            sb.Append("  \"scene\": ");
            Str(sb, UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
            sb.Append(",\n");

            sb.Append("  \"terrains\": [\n");
            bool first = true;
            for (int i = 0; i < _terrains.Length; i++)
            {
                LDK2DHeightmapTerrain t = _terrains[i];
                if (t == null) continue;

                if (!first) sb.Append(",\n");
                first = false;

                float[] h = t.Heights;

                sb.Append("    {\"id\": ");
                Str(sb, IdOf(t.gameObject));
                sb.Append(", \"samples\": ").Append(t.sampleCount);
                sb.Append(", \"spacing\": ").Append(Num(t.spacing));
                sb.Append(", \"originX\": ").Append(Num(t.originX));
                sb.Append(", \"bottomY\": ").Append(Num(t.bottomY));
                sb.Append(", \"useBottomProfile\": ").Append(t.useBottomProfile ? "true" : "false");
                sb.Append(", \"uvMode\": ");
                Str(sb, t.uvMode.ToString());
                sb.Append(", \"buildColliders\": ").Append(t.buildColliders ? "true" : "false");
                sb.Append(", \"revision\": ").Append(t.Revision);
                sb.Append(", \"interactive\": ").Append(t.IsInteractive ? "true" : "false");

                Vector3 p = t.transform.position;
                sb.Append(", \"position\": [").Append(Num(p.x)).Append(',').Append(Num(p.y)).Append(',').Append(Num(p.z)).Append(']');

                sb.Append(",\n     \"heights\": [");
                for (int k = 0; k < h.Length; k++)
                {
                    if (k > 0) sb.Append(',');
                    sb.Append(Num(h[k]));
                }
                sb.Append("],\n     \"topX\": [");
                for (int k = 0; k < h.Length; k++)
                {
                    if (k > 0) sb.Append(',');
                    sb.Append(Num(t.TopXAt(k)));
                }
                sb.Append("]}");
            }
            sb.Append("\n  ],\n");

            sb.Append("  \"actors\": [\n");
            var actors = UnityEngine.Object.FindObjectsOfType<LDK2DTestActor>(true);
            bool firstActor = true;
            for (int i = 0; i < actors.Length; i++)
            {
                LDK2DTestActor a = actors[i];
                if (a == null) continue;

                if (!firstActor) sb.Append(",\n");
                firstActor = false;

                Vector3 p = a.transform.position;
                sb.Append("    {\"id\": ");
                Str(sb, IdOf(a.gameObject));
                sb.Append(", \"actorKind\": ");
                Str(sb, a.kind.ToString());
                sb.Append(", \"type\": ");
                Str(sb, a.typeName);
                sb.Append(", \"position\": [").Append(Num(p.x)).Append(',').Append(Num(p.y)).Append(',').Append(Num(p.z)).Append("]}");
            }
            sb.Append("\n  ]\n");

            sb.Append("}\n");

            try
            {
                File.WriteAllText(StatePath, sb.ToString(), Utf8);
                _stateDirty = false;
            }
            catch
            {
                // 下次再写。
            }
        }

        static void WriteHeartbeat(double now)
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"t\":").Append(Num((float)now));
            sb.Append(",\"play\":").Append(Application.isPlaying ? "true" : "false");
            sb.Append(",\"terrains\":").Append(_terrains.Length);
            sb.Append(",\"queued\":").Append(_queue.Count);
            sb.Append(",\"applied\":").Append(_applied);
            sb.Append(",\"failed\":").Append(_failed);
            sb.Append(",\"compiling\":").Append(EditorApplication.isCompiling ? "true" : "false");
            sb.Append(",\"lastError\":");
            Str(sb, _lastError ?? "");
            sb.Append("}\n");

            try
            {
                File.WriteAllText(HeartbeatPath, sb.ToString(), Utf8);
            }
            catch
            {
            }
        }

        // ------------------------------------------------------------------ 游标

        static void LoadCursor()
        {
            try
            {
                if (!File.Exists(CursorPath)) return;

                string text = File.ReadAllText(CursorPath);
                int at = text.IndexOf("\"cmdOffset\"", StringComparison.Ordinal);
                if (at < 0) return;

                at = text.IndexOf(':', at);
                if (at < 0) return;

                int end = text.IndexOfAny(new[] { ',', '}' }, at);
                string num = end < 0 ? text.Substring(at + 1) : text.Substring(at + 1, end - at - 1);

                long v;
                if (long.TryParse(num.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                    _cmdOffset = v;
            }
            catch
            {
                _cmdOffset = 0;
            }
        }

        static void SaveCursor()
        {
            try
            {
                File.WriteAllText(CursorPath,
                    "{\"cmdOffset\":" + _cmdOffset.ToString(CultureInfo.InvariantCulture) + "}\n", Utf8);
            }
            catch
            {
            }
        }

        // ------------------------------------------------------------------ 解析目标

        static void EnsureTerrains()
        {
            if (_terrainsDirty) RefreshTerrains();
        }

        static LDK2DHeightmapTerrain ResolveTerrain(string target)
        {
            GameObject go = ResolveObject(target);
            return go != null ? go.GetComponent<LDK2DHeightmapTerrain>() : null;
        }

        /// <summary>
        /// 按 id 找物体。id 就是层级路径（<c>/地形_A/子物体</c>），三级匹配：
        /// 完整路径 → 物体名 → 名字唯一前缀。
        /// </summary>
        static GameObject ResolveObject(string target)
        {
            if (string.IsNullOrEmpty(target)) return null;

            string want = target.Trim().TrimStart('/');

            EnsureTerrains();

            // 1) 完整层级路径
            for (int i = 0; i < _terrains.Length; i++)
            {
                if (_terrains[i] == null) continue;
                if (IdOf(_terrains[i].gameObject) == target) return _terrains[i].gameObject;
            }

            // 2) 物体名（标记也走这条，所以要在全部物体里找）
            var all = UnityEngine.Object.FindObjectsOfType<Transform>(true);

            GameObject byName = null;
            int nameHits = 0;

            for (int i = 0; i < all.Length; i++)
            {
                Transform tr = all[i];
                if (tr == null) continue;

                if (tr.name == want)
                {
                    byName = tr.gameObject;
                    nameHits++;
                    if (nameHits > 1) break;
                }
            }

            if (nameHits == 1) return byName;
            if (nameHits > 1) return null;   // 重名，拒绝猜

            // 3) 唯一前缀
            GameObject byPrefix = null;
            int prefixHits = 0;

            for (int i = 0; i < all.Length; i++)
            {
                Transform tr = all[i];
                if (tr == null || !tr.name.StartsWith(want, StringComparison.Ordinal)) continue;
                byPrefix = tr.gameObject;
                prefixHits++;
                if (prefixHits > 1) break;
            }

            return prefixHits == 1 ? byPrefix : null;
        }

        static string IdOf(GameObject go)
        {
            if (go == null) return "";

            var sb = new StringBuilder(64);
            Transform tr = go.transform;

            while (tr != null)
            {
                sb.Insert(0, "/" + tr.name);
                tr = tr.parent;
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------------ JSON 小工具

        static string Num(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return "0";
            return v.ToString("0.######", CultureInfo.InvariantCulture);
        }

        static void Str(StringBuilder sb, string s)
        {
            if (s == null)
            {
                sb.Append("null");
                return;
            }

            sb.Append('"');

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }

            sb.Append('"');
        }
    }
}
