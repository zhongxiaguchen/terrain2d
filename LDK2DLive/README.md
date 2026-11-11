# LDK2D 实时协作通道

AI（或任何脚本）在 Unity 运行期间**直接改关卡**的通道。走文件，不走网络，零依赖。

> 打开 Unity 就自动生效。没有开关，没有按钮 —— 编辑器里挂了一个
> `LDK2DLiveBridge`，它每 16ms 看一次 `commands.jsonl`。

---

## 一句话原理

> **外部发的不是"命令"，是"补丁"；编辑器不是"执行"，是"按帧预算慢慢消化"。**

补丁的粒度是「**哪块地形、哪个区间、什么值**」，不是「重新生成整块地形」。
这一条决定了它为什么快：不需要重建拓扑，不需要重新生成噪声，只需要改一段高度数组。

---

## 目录

```
LDK2DLive/
  commands.jsonl     ← 外部写。一行一条命令，追加写
  events.jsonl       ← 编辑器写。一行一个事件，追加写
  state.json         ← 编辑器写。全量快照，改动后 500ms 内刷新
  heartbeat.json     ← 编辑器写。每秒一次，看它活着没有
  cursor.json        ← 编辑器写。读到哪了，防重放
  agent.mjs          ← 参考代理：看到人改完就自动削坡
  send.mjs           ← 手动发命令 / 看事件的命令行工具
  start-agent.bat    ← 双击启动代理
```

`*.jsonl` / `*.json` / `*.log` 都在 `.gitignore` 里 —— 它们是运行时产物。

---

## 三分钟上手

1. **打开 Unity**，确认 `LDK2DLive/` 下出现了 `state.json` 和 `heartbeat.json`。
2. **双击 `start-agent.bat`**，黑窗口里会打出 `等待 Unity 打开`。
3. 在 Scene 视图里**把某个地形点往上拖成一根尖刺**，松手。
4. 松手后约 200ms，黑窗口打出：

   ```
   /地形_A 坡度超限（最陡 10.41，上限 2） → 补丁 [36..51]，最大改动 2.469
   ```

   同时 Unity 里那根尖刺**自己塌下去**了。这就是一次完整的实时协作：
   人改 → 编辑器推事件 → 代理判断 → 发补丁 → 编辑器应用 → 画面变化。
   全程不到 0.3 秒，没有编译、没有 Play 重启。

---

## 命令格式

一行一个 JSON。`op` 必填，`id` 可以不填（自动生成）。

```jsonc
{"op":"patch",    "target":"/地形_A", "start":40, "values":[1.2,1.4,1.6]}
{"op":"flatten",  "target":"/地形_A", "from":40, "to":60, "y":2.0}
{"op":"raise",    "target":"/地形_A", "x":1.5, "y":2.0, "radius":2.0}
{"op":"dig",      "target":"/地形_A", "x":1.5, "y":2.0, "radius":1.2}
{"op":"smooth",   "target":"/地形_A", "passes":2}
{"op":"generate", "target":"/地形_A", "seed":777}
{"op":"set",      "target":"/地形_A", "field":"amplitude", "value":4.5}
{"op":"spawn",    "kind":"enemy", "name":"巡逻", "x":3.2, "y":1.0}
{"op":"remove",   "target":"/敌人·巡逻 2"}
{"op":"describe"}
```

| op | 干什么 | 走的路径 |
|---|---|---|
| `patch` | 改一段地表高度 | `SetHeightsRange` → `Rebuild` |
| `flatten` | 把 `from..to` 压到同一个 Y | 同上 |
| `raise` / `dig` | 圆形堆土 / 挖坑 | `RaiseCircle` / `DestroyCircle` |
| `smooth` | 整体平滑 N 次 | 读高度、算平均、写回 |
| `generate` | 换种子重新生成 | `Generate()` |
| `set` | 改一个参数 | 反射 + 白名单 |
| `spawn` / `remove` | 放置 / 删除占位标记 | `LDK2DTestActor.Create` / `DestroyObjectImmediate` |
| `describe` | 强制刷一次 `state.json` | —— |

### `target` 怎么填

三级匹配，越靠前越准：

1. **完整层级路径** —— `/地形_A/子物体`（`state.json` 里 `id` 字段就是它）
2. **物体名** —— `地形_A`
3. **唯一前缀** —— `地形` 只在有一个匹配时生效

重名会**直接拒绝**，不猜。这是故意的：猜错等于改错地形。

### `set` 允许改哪些字段

`sampleCount` `spacing` `originX` `bottomY` `bottomLeftOffsetX` `bottomRightOffsetX`
`useBottomProfile` `seed` `baseHeight` `amplitude` `frequency` `octaves` `persistence`
`lacunarity` `smoothPasses` `uvScale` `uvMode` `flipWinding` `buildColliders`
`colliderOffsetY` `colliderStride` `applyColor` `sortingOrder`

**白名单外的字段一律拒绝**。名字、层级、transform 故意不给改 —— 改了会让外部
手里那个 `id` 当场失效，下一批命令全部打空。

---

## 事件格式

```jsonc
{"t":1712.3, "kind":"session",  "unity":"2022.3.53f1", "play":false, "project":"D:/..."}
{"t":1712.4, "kind":"terrain",  "target":"/地形_A", "samples":128, "spacing":0.25, "minY":-0.4, "maxY":4.8}
{"t":1713.0, "kind":"change",   "target":"/地形_A", "range":[41,53], "revision":7, "minY":-0.4, "maxY":5.1, "src":"human"}
{"t":1731.8, "kind":"settle",   "target":"/地形_A", "range":[41,53], "revision":7, "minY":-0.4, "maxY":5.1, "src":"human"}
{"t":1732.0, "kind":"applied",  "cmd":"agent-x-1", "op":"patch", "target":"/地形_A", "range":[40,54]}
{"t":1732.1, "kind":"error",    "cmd":"cli-y-2", "message":"找不到地形：/地形_B"}
{"t":1732.2, "kind":"spawned",  "cmd":"cli-z-3", "target":"/敌人·巡逻 2", "actorKind":"Enemy", "type":"巡逻"}
```

| kind | 什么时候发 | 外部该怎么用 |
|---|---|---|
| `session` | 编辑器域加载完 | 清空本地缓存，重新拉 `state.json` |
| `terrain` | 第一次看到某块地形 | 记下 `samples` / `spacing`，算下标用 |
| `change` | 地形变了，**最多 10Hz** | 知道"正在变"，一般不用处理 |
| `settle` | 停手 200ms 后 | **这是行动信号**：现在可以安全地改 |
| `applied` | 一条命令执行成功 | 确认补丁落地了 |
| `error` | 命令失败 | 看 `message` |

`src` 是 `"human"` 或 `"ai"` —— **代理必须忽略 `ai` 的 settle**，否则会自激振荡
（自己改 → 收到事件 → 再改 → 无限循环）。

### 节流规则

- 拖动中：每块地形最多 **10 条/秒** `change`
- 停手 **200ms** 后补一条 `settle`
- `settle` 之前**先把 `state.json` 刷一遍** —— 外部收到 `settle` 立刻读快照，
  拿到的一定是这一帧的数据，不会拿到 500ms 前的旧值

---

## 人机冲突规则

| 情况 | 规则 |
|---|---|
| 人正在拖这块地形 | AI 的补丁**进等待队列**，松手后才应用 |
| 两者改同一区间 | **人的值赢**（AI 的补丁压根不会在拖动中被执行） |
| AI 连发 100 条 | 每帧最多花 **2ms**，剩下的排队，编辑器不掉帧 |
| AI 改坏了 | **一条命令 = 一步 Ctrl+Z** |

判据是 `LDK2DHeightmapTerrain.IsInteractive`（拖动期间为 true），
不是"最近谁动过"这种模糊条件。

---

## 硬约束（改代码前先看）

1. **每帧 2ms 预算**（`FrameBudgetMs`）。这是"几乎无卡顿"的唯一保证 ——
   不管外部一次发多少，编辑器这一帧都不会超时。
2. **不要给命令加"操作"粒度**（`subdivide` / `regenerate` 这种）。
   一次就是全量重建，实时性归零。粒度必须是补丁。
3. **不要让 AI 碰 `.unity` / `.asset`**。GUID / FileID 太脆，而且绕过 Undo。
4. **不要为了实时牺牲正确性**。碰撞体可以降频，但"轮廓自交时保留上一次碰撞体"
   这条不能动 —— 那是防穿模的底线。

---

## Play 模式下能改吗

能，而且这正是设计目标。但要注意：

Play 里改的是**运行时副本**。退出 Play 时 Unity 会按进 Play 时的快照恢复，
所以要让改动留下来，必须勾上测试阶段的「**实时编辑**」（`LDK2DTerrainStage.LiveEdit`）。
勾了之后 `LDK2DLiveEditSync` 会在退出 Play 时把改动写回场景，整个回写是一步撤销。

不勾也能改 —— 只是退出 Play 就没了，适合"跑着看效果"。

---

## 常见问题

**`state.json` 一直不出现**
Unity 没编译完，或者编译报错。看 Console。

**命令发了没反应**
`send.mjs --ping` 看心跳。`--tail` 看有没有 `error` 事件。
最常见的是 `target` 写错 —— 用 `--state` 看实际的 `id`。

**改了地形，画面没变**
`Rebuild()` 没跑。检查命令是不是走了 `patch` 之外的路径，
或者 `sampleCount` 被改成了 0。

**代理把地形削得太狠**
调环境变量：`set LDK2D_MAX_SLOPE=3` 再启动（3.0 ≈ 71°）。

**代理一直不动作**
它只响应 `src:"human"` 的 `settle`。AI 自己的改动不会触发它（防止自激）。
