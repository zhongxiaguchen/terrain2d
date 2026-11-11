# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.20.0] - 2026-11-11

### Added

- **命令通道新增 `create` op：在空地上从零建一块地形**
  （`Editor/LDK2DLiveBridge.cs`）。

  之前通道只能改已有地形 —— 空地上什么都建不出来，"AI 搭关卡"这句话是空的。
  现在可以：

  ```jsonc
  {"op":"create","kind":"terrain","name":"城堡","x":32,"y":-7,
   "samples":192,"spacing":0.25,"values":[0,0,0,...]}
  ```

  `values` 给了就直接铺成这个形状（一次 `SetHeightsRange`），不给就按新参数
  重建一次噪声形状。

  创建逻辑**直接复用** `LDK2DToolsEditor.CreateTerrain`，不另写一份 ——
  否则"面板能做的 AI 做不到"，两边行为会慢慢分叉。唯一区别是位置：
  工具栏摆在视口中心，这里按命令给的坐标摆。

  和 `spawn` / `remove` 一样，`create` 不受"人正在拖这块地形"的排队规则限制
  （它操作的是还不存在的物体），但整条命令仍然是一步 Ctrl+Z。


## [0.19.1] - 2026-11-11

### Fixed

- **拖动地形点之后 Ctrl+Z 完全没反应**（`Editor/LDK2DToolsEditor.cs`）。

  面板上改颜色、改参数都能撤，唯独拖点撤不了。原因是 `Undo.RecordObject`
  记的是**字段级差异** —— 它先拍一份，改完再比对，判断"这个对象变没变"。

  而拖点改的是 `float[] _heights` 里的**单个元素**（`_heights[i] = 值`，数组对象
  本身没换）。这种情况 `RecordObject` 可能判定成"没变化"，于是**压根不建撤销
  记录**。面板改 `color` / `amplitude` 那种单字段操作它认得出来，所以症状是
  "其他的都正常，只有拖点撤不了"。

  改用 `Undo.RegisterCompleteObjectUndo` —— 不管差异，直接把整个组件完整序列化
  一份存进撤销栈，数组内容一定被拍进去。

  顺带把拖动变成**独占的撤销组**：

  | 以前 | 现在 |
  |---|---|
  | 挂在"当前组"上，组是谁的取决于拖动前点过什么 | `Undo.IncrementCurrentGroup()` 开新组 |
  | 组永远不闭合 | `EndDrag` 里 `Undo.CollapseUndoOperations` 收尾 |
  | 可能退成几十小步 | 整次拖动 = **一步** Ctrl+Z |

### Notes

- 只动了撤销登记，拖动逻辑、网格、碰撞体一行没改。
- `RegisterCompleteObjectUndo` 比 `RecordObject` 多存一次完整序列化（几 KB），
  但只在按下鼠标那一刻存一次，代价可以忽略。


## [0.19.0] - 2026-11-11

### Added

- **AI 实时协作通道：外部进程可以在编辑器跑着的时候直接改关卡**
  （新文件 `Editor/LDK2DLiveBridge.cs`，配套 `LDK2DLive/` 目录）。

  这是"AI 参与关卡编辑"的地基。之前 AI 想改地形只有一条路 —— 改 C# 代码，
  然后切回 Unity 等编译。一次就是一次域重载，Play 会断，实时性归零。

  现在走文件：外部往 `LDK2DLive/commands.jsonl` 追加一行 JSON，编辑器 16ms 内取走、
  应用、再往 `events.jsonl` 写一条回执。**没有编译，没有重启，Play 里也能用。**

  | 文件 | 谁写 | 内容 |
  |---|---|---|
  | `commands.jsonl` | 外部 → 编辑器 | 一行一条补丁 |
  | `events.jsonl` | 编辑器 → 外部 | 一行一个事件 |
  | `state.json` | 编辑器 | 全量快照，改动后 500ms 内刷新 |
  | `heartbeat.json` | 编辑器 | 每秒一次，看它活着没有 |
  | `cursor.json` | 编辑器 | 读到哪了，防重放 |

  **补丁的粒度是「哪块地形、哪个区间、什么值」，不是「重新生成整块地形」。**
  这条决定了它为什么快：不重建拓扑、不重算噪声，只改一段高度数组。

  支持的 op：`patch` / `flatten` / `raise` / `dig` / `smooth` / `generate` /
  `set` / `spawn` / `remove` / `describe`。

- **双向：编辑器也会把编辑现场推出去。**

  每块地形加了 `Revision` 计数器（每次 `Rebuild()` +1）。桥接层只比这个数字
  （O(1)），变了才 diff 出改动的区间。所以**人拖、AI 改、Inspector 改，走的都是
  同一条路径**，不需要谁去通知谁。

  事件流按地形节流到 10Hz；停手 200ms 后补一条 `settle` —— 那是"现在可以安全地改"
  的信号。`settle` 之前会先把 `state.json` 刷一遍，保证外部读到的快照和这条事件同帧。

- **参考代理 `LDK2DLive/agent.mjs`**：盯 `settle`，跑一遍「最大可行坡角」约束，
  把太陡的地方削平，只发改动区间的补丁。它是这条通道的参考实现 ——
  真接大模型时，替换掉 `decide()` 一个函数就行。

- **命令行工具 `LDK2DLive/send.mjs`**：发命令、看事件、看快照、看心跳。

### Changed

- `LDK2DHeightmapTerrain` 新增三个公开成员：
  `Revision`（改动指纹）、`IsInteractive`（是否正在拖）、
  `SetHeightsRange(start, values)`（外部补丁入口，越界自动夹取）。

### 设计约束

- **每帧最多花 2ms**（`FrameBudgetMs`）。外部发 1 条还是 500 条，编辑器都不会掉帧，
  只是生效得慢一点。这是"几乎无卡顿"的唯一硬保证。
- **人正在拖的地形，AI 的补丁进等待队列**，松手才应用。判据是 `IsInteractive`，
  不是"最近谁动过"。
- **一条命令 = 一步撤销**（`Undo.IncrementCurrentGroup` 只在真要改东西时调）。
- **`set` 有字段白名单**。名字 / 层级 / transform 故意不给改 —— 改了会让外部手里
  那个 `id` 当场失效，下一批命令全部打空。

### Notes

- 通道默认**常开**，没有开关。它只读一个文件的长度、写几个小文件，开销可以忽略。
- Play 模式下改的是运行时副本，要留下来仍需勾「实时编辑」（见 0.18.0）。
- 协议说明见 `LDK2DLive/README.md`。


## [0.18.2] - 2026-11-11

### Changed

- **拖动地形时碰撞体降频到 10Hz，视觉网格照常 60Hz**
  （改 `Scripts/LDK2DHeightmapTerrain.cs` / `Editor/LDK2DToolsEditor.cs`）。

  0.18.1 把分配压掉之后，`Rebuild()` 里最贵的一步就剩
  `PolygonCollider2D.SetPath` —— 它要重建物理形状并重新烘焙，Play 模式下还会
  连带触发一次物理重算。而人眼在拖动过程中**根本分辨不出**脚下碰撞体是不是
  慢了 100 毫秒，所以这笔开销可以省。

  | | 拖动中 | 松手瞬间 |
  |---|---|---|
  | 视觉网格 | 每帧重建（不变） | —— |
  | 碰撞体 | 每 100ms 一次 | 立刻补最后一次 |
  | 参数 / UV / 材质 | 每帧更新（不变） | —— |

  **"欠着"的记账**：被跳过的那一帧在组件上记 `_colliderDirty = true`。
  `EndInteractiveEdit()` 发现欠着就补一次，所以松手之后碰撞一定是准的 ——
  不会出现"拖完地形，人站在地面上往下掉"。

  **`Rebuild()` 不判断"在不在拖"，只判断 `_interactive` 标志**。编辑器侧
  在鼠标按下 / 松开时调 `BeginInteractiveEdit()` / `EndInteractiveEdit()`，
  运行时代码不知道"拖"这回事。`SetPath` 在运行时组件里，所以降频的判断
  必须也在那里。

### Fixed

- **拖动被中途打断后碰撞体永久停在旧形状**（`Editor/LDK2DToolsEditor.cs`）。

  切选中、编辑器失焦、脚本重编译都会让 `OnSceneGUI` 不再收到鼠标抬起事件，
  `EndDrag()` 永远不执行 —— 地形就卡在"拖动中"，碰撞体一直降频且没人补。
  表现是"改完地形，碰撞还是旧的"，Console 里一个字都没有。

  新增 `OnDisable()`：只要 `_dragKind != 0` 就强制 `EndDrag()`，补上欠的碰撞体。

### Notes

- 只改了更新节奏，碰撞体形状的计算逻辑没动。
- 想关掉降频、每次都重建：把 `InteractiveColliderInterval` 改成 `0f` 即可。


## [0.18.1] - 2026-11-11

### Changed

- **拖动地形不再每次扔 61 KB 垃圾**（新文件 `Scripts/LDK2DMeshBuffers.cs`，
  改 `LDK2DMeshUtil` / `LDK2DHeightmapTerrain` / `LDK2DPolygonMesh`）。

  以前每次 `Rebuild()` 都 `new List` 三份（顶点 / UV / 索引），`MeshUtil` 里再
  `new List` 两份（法线 / 切线）。512 个采样点一次约 **61 KB**，拖动时按 60Hz 算
  就是 **3.7 MB/s** —— 编辑器里"拖起来发涩"主要就是这一条。

  现在缓冲区挂在组件上，`Clear()` 不释放容量，稳态重建**零托管分配**。

  | 以前 | 现在 |
  |---|---|
  | 每次 new 5 个 List | 组件持有一份，Clear 复用 |
  | 法线 / 切线每次重填（其实全是常量） | 只在顶点数变化时填一次 |
  | 每次 `mesh.Clear()` + 全量重传 | 拓扑没变就只传顶点和 UV |
  | 碰撞轮廓每次 new 3 个 List + `ToArray()` | 复用列表 + 按需增长的目标数组 |

  **"拓扑没变"怎么判**：顶点数、索引数、`flipWinding` 三者都没变，且网格自己的
  顶点数对得上 —— 这时三角形索引必然一模一样，可以跳过 `Clear()` /
  `SetTriangles()` / 法线切线。拖动高度走的正是这条路。

  **哪些情况必须全量重建**：耳切那条路（左右边加过控制点）索引是算出来的，
  不保证"点数相同就索引相同"；三角带和耳切互相切换时索引来源也完全不同。
  两种都强制走全量，避免误用旧索引导致三角形全乱。

  顺带把法线 / 切线的填充从"每次重建"改成"顶点数变化时一次"。它们是常量
  （`Vector3.back` / `(1,0,0,-1)`），本来就不该重复算。

### Notes

- 只动了分配，没动算法和数据结构，网格输出和以前逐字节一致。
- `PolygonCollider2D.SetPath` 仍然会重建物理形状 —— 那是下一个瓶颈，
  交互期间降频是后续的事。


## [0.18.0] - 2026-11-11

### Added

- **实时编辑：Play 模式下改的关卡，退出 Play 之后留得住**（`Editor/LDK2DLiveEditSync.cs`，新文件）。

  以前 Play 里拖地形点，改的是运行时副本，一按停止全没了。Unity 就是这么设计的 ——
  进 Play 时给整个场景拍快照，退出时按快照恢复。所以三件事都得自己再对一遍：

  | Play 里做了什么 | Unity 退出时 | 现在补的动作 |
  |---|---|---|
  | 改（拖点 / 改参数 / 移动） | 数据被抹回进 Play 前 | 把 Play 里的数据写回去，再 `Rebuild()` |
  | 删 | 物体**复活** | 再删一次 |
  | 增 | 物体**销毁** | 按序列化数据重建 |

  三件都做，才算「Play 里看到的场景 = 退出后的场景」。管两种东西：
  `LDK2DHeightmapTerrain` 和 `LDK2DTestActor`。标记笔迹有自己那套
  （`LDK2DMarkupTool` 的 `RescueKey`），不重复。

  **没动过就不写回**：退出 Play 时先比一遍序列化字符串，一模一样就跳过 ——
  否则每次按 Play 都会把场景标脏，人什么都没干却要吃一个「有未保存的改动」提示。

  **整个回写是一步撤销**：写回前 `Undo.RegisterCompleteObjectUndo`，
  Play 里改乱了，退出后一个 Ctrl+Z 全撤回。

  实现细节：

  - 名单在 `ExitingEditMode` **无条件**拍一份（只记 instanceId + 层级路径 + transform），
    退出 Play 时再拍一份带数据的。这样中途把开关打开也能正常写回。
  - 用 `JsonUtility` 而不是 `EditorJsonUtility`。后者写的是**原生序列化布局**，
    带 `m_GameObject` / `m_Script` 这些 PPtr，拿它 `FromJsonOverwrite` 到一个新建组件上
    会把 `m_GameObject` 指到别的物体。`JsonUtility` 只写组件自己的可序列化字段，
    跨物体搬运才安全。
  - `transform` 单独存：`JsonUtility` 不含位置/缩放，而占位标记的尺寸就是靠
    `localScale` 撑出来的，不存的话重建出来全是默认大小。
  - 重建走「先 `SetActive(false)` → `AddComponent` → 灌数据 → `SetActive(true)`」。
    两个组件都带 `[ExecuteAlways]`，`AddComponent` 那一刻 `OnEnable` 是**同步**跑的，
    字段还没灌就会按默认值配一遍（占位标记会按默认的「敌人」挂刚体）。

- **「实时编辑」勾选框**（`Editor/LDK2DSortingOrderToolbar.cs`，测试阶段 · 什么都没选那块）。

  位置在「创建测试角色」下面，面板从 4 行变 5 行。它只管一件事：
  Play 里改的东西退出后要不要留。**不进 Play** —— 编辑器自带播放键，
  再放一套只会打架。所以暂停 / 停止 / 禁止输入都不做，那三件事 Unity 已经给了。

  存 `EditorPrefs` 的 `Terrain2D.LiveEdit`，默认**关**，跨重启活着。

### Changed

- **没开实时编辑时，Play 模式下不画地形手柄**（`Editor/LDK2DToolsEditor.cs` 的 `OnSceneGUI`）。

  跑图的时候视口要干净 —— 采样点、边界方块、轮廓线全在，根本看不清角色在跳什么。
  而且那时候改了也不会保留，手柄画出来只会让人以为能改，改完白改。

  面板留着（「显示 UV 网格」「显示控制点」这些开关还在里面）。
  以前"跑图想要干净视口只能手动关显示控制点"这个空档，现在由这个开关接管了。

### Notes

- Play 里改的 Inspector 参数也会一起回写 —— 数据面是同一个序列化字符串，不分来源。
- 重建的地形/标记会落在场景根，不还原原来的父物体层级。地形和标记本来就该在根上
  （地形有缩放，标记挂上去会跟着缩），所以实际碰不到。


## [0.17.0] - 2026-11-11

### Changed

- **橡皮改成"只擦经过的那一段"**（`Editor/LDK2DMarkupTool.cs`）。

  以前是"点到哪一笔就整笔删掉"，一笔长线中间写错一个字，得整条重画。
  现在橡皮是一个**圆**（`EraseRadius = 8px`）：按住拖过去，只把圈里的部分剪掉，
  剩下的按连续段自动拆成几条新笔迹，颜色和线宽都跟着走。

  顺带能**按住拖动连续擦**了 —— 以前只有点一下才生效。

  | 以前 | 现在 |
  |---|---|
  | 点中哪一笔 → 整笔消失 | 圈经过的地方消失，两头留着 |
  | 只能点，拖没用 | 按住拖，一路擦过去 |
  | 一次点击一步撤销 | **一次拖动**一步撤销 |

  实现上按**线段**算而不是按点算：采点间距是画的时候的屏幕 `2px`，视口放大之后
  相邻两点可能隔着几十像素 —— 只删"圈内的点"会整段跳过去，表现就是
  "擦了半天那一段还在"。所以对每一段解 `|a + t(b-a) - c|² = r²`，
  求出圆内的参数区间 `[t0, t1]`，只把区间内的部分剪掉。

  性能：判定前先用**世界坐标包围盒**粗筛（包围盒只会比真实范围大，不会漏），
  避免每帧对每一笔的每个点都做 `WorldToGUIPoint`。

### Removed

- `NearestStroke()` / `RemoveAt<T>()` —— 整笔删除的两个辅助方法，随上面一起没了。


## [0.16.1] - 2026-11-11

### Fixed

- **色盘那个控件上只剩一个吸管图标，看不到颜色**（`Editor/LDK2DMarkupTool.cs`）。

  `EditorGUI.ColorField(showEyedropper: true)` 是**左右排布**的：吸管占固定 `~18px`，
  剩下的宽度才给色块。上一版给整个控件 `18px`，色块被压成 `0` 宽 ——
  界面上就是一个白底 + 吸管图标，完全看不出这是个颜色按钮。

  现在给 `38px`：`18` 色块 + `18` 吸管 + 边框。

  顺带：`CustomSwatchWidth` 改名 `ColorFieldWidth`（它现在不是"色块宽度"，
  是整个控件的宽度）。

- **工具箱宽度 `264 → 288`**（`Editor/LDK2DViewportZoomSlider.cs`），给上面多出来的 `20px` 让位。


## [0.16.0] - 2026-11-11

### Changed

- **框的描边会随缩放自动加粗**（`Editor/LDK2DMarkupTool.cs`）。

  描边宽度是屏幕像素，所以视口一缩、框跟着缩、线却不变粗细 ——
  缩到能看全图时框只剩几十像素，2px 描边几乎看不出形状。

  现在按**框自己的屏幕尺寸**在 `2px ~ 8px` 之间线性插值：

  | 框在屏幕上的短边 | 描边 |
  |---|---|
  | `>= 120px` | `2px`（最细） |
  | `70px` | `5px` |
  | `<= 20px` | `8px`（最粗） |

  为什么不用"视口缩放倍数"当依据：天生画得很小的框，在 1:1 的视口里同样需要粗一点
  才看得见。该看的是**它现在多大**，不是相机在哪。

  取宽高里较小的那个 —— 细长条只要有短边撑不住，一样会看不清。

  再加一层兜底：线宽不许超过短边的 `30%`。不兜的话缩到极小时框会糊成一个实心色块，
  反而丢掉"这是个框"的信息。

- **颜色不再只有四个预设**，多了第五个色块 = **色盘**（`DrawColorWheelSwatch`）。

  点开是 Unity 自带的取色器：HSV 圆盘 + RGB / 十六进制输入 + 吸管 +
  存过的色板。吸管可以直接从视口里吸地形的颜色。

  四个预设留着：黄待办 / 红有问题 / 青已确认 / 白备注 —— 有语义、点一下就走，
  不该为了"自由选色"把这四个常用操作变成每次都要开色盘。

  选中的是自定义色时（`_colorIndex == -1`），色盘色块会描一圈白边 ——
  四个预设都没亮，得让人看出"现在用的是色盘这个"。

  色盘默认给一个紫（`0.72, 0.42, 1`），和四个预设都不撞。

- **工具箱宽度 `240 → 264`**（`Editor/LDK2DViewportZoomSlider.cs`）。

  第二行现在要放下 `笔 框 字` + 四个预设色 + 色盘 + 线宽滑条 + `擦`，
  240 塞不下。线宽滑条同时从 `58px` 收到 `48px` 让位。


## [0.15.0] - 2026-11-11

### Added

- **框和文字可以缩放了**（`Editor/LDK2DMarkupTool.cs`）。

  选中之后会出现手柄：

  | 对象 | 手柄 | 拖动效果 |
  |---|---|---|
  | 框 | 四个角各一个白方块 | 改大小，**对角固定** |
  | 文字 | 右下角一个白方块 | 按距离比改字号（`6~96`） |

  手柄是屏幕像素（`8px`），不随视口缩放变 —— 缩到全图时手柄还是那么好点。

  框缩放用 `Min` / `Max` 兜住拖过头翻面的情况：直接拿"鼠标点"和"固定对角"
  算宽高，拖过对角之后会得到负数，框会翻过来。兜住之后就是压扁到最小尺寸。

  文字缩放按**距离比**（`nowDist / startDist`）而不是绝对位移 ——
  字号和鼠标位移不是线性关系，用绝对位移会前半段没反应、后半段猛跳。

  删除按钮跟着框的中心走（`_deleteAnchor += 中心位移`），不然缩完它就偏到一边。

- **手柄的命中优先级高于"重新选中"**。

  `MouseDown` 里先 `TryBeginResize` 再 `TrySelectAt`。顺序反了的话，
  想拉大框的时候会先把框重新选中一遍，手柄永远抓不住。

  缩放和拖动共用同一套 Undo 延迟策略（`_resizeUndoPushed`）：真的改了才拍快照。



## [0.14.0] - 2026-11-11

### Added

- **框和文字可以拖动了**（`Editor/LDK2DMarkupTool.cs`）。

  选中之后按住不放就能拖：点框的描边 / 点文字 → 选中 → 直接拖。
  位移按**世界坐标**算，不按屏幕像素 —— 视口缩放时同样的鼠标移动量对应的世界距离
  完全不一样，按像素算会在放大后拖不动、缩小后拖飞。

  删除按钮跟着走（`_deleteAnchor` 同步平移），不然拖完它就留在原地了。

- **双击改内容**。

  单击现在要留给"选中 + 拖动"，所以"改文字正文 / 改框标签"换成了双击。
  时间窗 `0.35s`、位置容差 `10px`，是 Unity 编辑器里双击的惯常值。

### Changed

- **文字工具下点文字不再直接进编辑**，改成单击选中、双击编辑。

  上一版是"文字工具下点文字 = 编辑"，和拖动冲突 —— 一按住鼠标就进编辑框，
  根本拖不动。现在单击统一是选中，编辑走双击，跟框的行为对齐了。

- **拖动的 Undo 推迟到真的动了才拍**。

  如果放在 `MouseDown` 里，单纯"点一下选中"也会拍一张快照，Ctrl+Z 会多出一堆
  "什么都没变"的步骤。现在用 `_moveUndoPushed` 标志，第一次 `MouseDrag` 时才
  `RegisterCompleteObjectUndo`，一次拖动仍然是一步。



## [0.13.0] - 2026-11-11

### Changed

- **框默认不填充了，只有选中时才有那层淡填充。**

  0.12.0 给框加填充是为了"缩小时更醒目"，但代价是**框里的地形被一层色蒙住** ——
  而框里恰恰是要看的东西。框存在的意义是圈出一块区域让人去看，不是把它盖掉。
  所以现在：平时只有 2px 描边，点中之后才补一层 `alpha 0.14` 的填充 + 描边提亮，
  用来确认"选的是这一个"。

- **框和文字改成"点出来再删"，橡皮只擦笔迹。**

  之前橡皮能擦掉任何东西。问题是框和文字不是"画"出来的，是"摆"出来的 ——
  手一滑擦掉一个框，里面圈的区域就白标了；擦掉一段文字，那句话就再也想不起来了。

  现在的规矩：

  | 对象 | 怎么删 | 怎么改 |
  |---|---|---|
  | 笔迹 | 橡皮点一下 | —— |
  | 框 | **点描边** → 出现 `×` 按钮 → 点按钮 | 文字工具下点框内部，改标签 |
  | 文字 | **点文字** → 出现 `×` 按钮 → 点按钮 | 文字工具下点文字，顺手进编辑 |

  **框只认描边**（容差 6px）：框是"圈区域"用的，大框的内部范围太大，
  点里面就选中会把里面的东西全挡掉。

  删除按钮钉在**点中它的那一下的位置**正上方，跟着场景走 —— 视口缩放时不会飘到别处。

- **`Tool.None` 时也接管左键**（只在点中标记时）。

  没选工具时，点框描边 / 点文字会选中它们；点空白则**放行给编辑器**（正常选物体）。
  之前 `Tool.None` 完全不处理输入，导致"没开工具就删不掉框和文字"。

- **切工具会清掉选中**。切到笔工具还挂着个删除按钮，会让人以为点一下画布就能删。



## [0.12.0] - 2026-11-11

### Added

- **标记从"只有笔"扩成三种：笔 / 框 / 文字**（`Editor/LDK2DMarkupTool.cs` +
  `Scripts/LDK2DMarkup.cs`）。

  上一版只有自由笔迹，实际摆关卡时会发现缺两样：**圈一片区域**（"这一段要重做"）
  和**写一句话**（"这里的坡度 15 度，别动"）。笔迹画圈不像圈，写字更写不出来。

  | 工具 | 怎么用 | 尺寸 |
  |---|---|---|
  | 笔 | 左键拖，自由画 | 线宽 1~8px，可调 |
  | 框 | 左键拖出一个矩形，松手直接进标签编辑 | 描边 2px + 半透明填充 |
  | 字 | 点一下，弹出输入框，回车确认 | 字号 14px |
  | 擦 | 点一下就删掉命中的那一个 | 命中半径 8px |

  **三种标记分开存三个列表**（`strokes` / `texts` / `boxes`），不塞进一个多态列表：
  Unity 的序列化不认多态，而且"按类型统一显示 / 隐藏"本来就需要能按类型遍历。

- **框带半透明填充，缩小时反而更醒目**。

  框缩小之后面积只剩几像素，1px 描边细得像没有 —— 而"缩到全图看一眼哪里有标记"
  恰恰是最常见的使用场景。所以框 = `DrawSolidRectangleWithOutline` 的
  **半透明填充**（alpha `0.14`）+ 自己画的 **2px 描边**。
  缩小时它退化成一块色块，比线条更容易被眼睛抓到。

  描边不直接用 `DrawSolidRectangleWithOutline` 的 outline 参数，是因为那个固定 1px、
  改不了粗细，所以传全透明、自己用 `DrawAAPolyLine` 闭合画一遍。

- **第三行：按类型统一显示 / 隐藏**。

  `笔画` / `文字` / `框` 三个开关，状态存 `EditorPrefs`
  （`LDK2D.MarkShowStrokes` / `Texts` / `Boxes`）—— 这是"我平时想看到什么"的偏好，
  不该跟着场景走。

  **不是"一键全关"**：三种标记的用途不一样（笔迹记想法、框圈区域、文字写结论），
  经常需要"只看框、把笔迹藏起来"。所以分开控制，全关也只是三次点击。

### Changed

- **工具改成互斥枚举**（`Tool.None / Pen / Box / Text / Erase`）。

  之前笔和橡皮是两个独立的布尔值，切来切去容易两个都亮着。现在一个枚举管四种状态，
  点已选中的按钮就是取消选中。切工具时会**丢掉手上没画完的一笔**并提交正在编辑的文字，
  否则会粘到下一次。

- **线宽滑条只在笔工具下出现**。框和文字各有固定尺寸，滑条对它们没有意义，
  摆在旁边只会让人以为能调。

- **工具箱第二行布局调整**：工具按钮宽度 `26` → `22`，色块 `20` → `18`，
  线宽滑条 `76` → `58`，为的是三个工具按钮挤进 `240` 的宽度里。
  新增 `LDK2DViewportZoomSlider.ExtraHeight` 委托 —— `BeginArea` 的高度必须在
  画之前定下来，画完再问就晚了（给少了会被静默裁掉）。



## [0.11.0] - 2026-11-11

### Added

- **场景实时标记：右下角工具箱第二行是笔**（`Editor/LDK2DMarkupTool.cs` +
  `Scripts/LDK2DMarkup.cs`）。

  摆关卡时最常冒出来的念头是"这里得补个跳板""这块坡不对"，以前只能靠记忆。
  现在按住左键直接在视口里画：颜色分四类语义（黄 = 待办、红 = 有问题、
  青 = 已确认、白 = 备注），线宽 1~8px 可调，橡皮点一下就擦掉整笔。

  **数据在场景里，绘制在 Handles 层。** 场景里只有一个 `_LDK2D_Marks` 对象挂
  `LDK2DMarkup` 存点集，**没有 MeshRenderer、不进 Game 视图、不参与 URP 2D 管线** ——
  视觉上就是浮在场景上面的一层。

  为什么不用 `LineRenderer`：它的宽度是**世界单位**，视口缩远了线变发丝、凑近了变马路，
  而标记恰恰要跟着视口缩放保持同样的视觉重量；URP 2D Renderer 下还得配
  `Sprite-Unlit-Default` 材质、把 sortingOrder 压到最高；画 50 笔就是 Hierarchy 里 50 个对象。

  为什么不用 GUI 层（`GUI.DrawTexture` / `EditorGUI.DrawRect`）：GUI 层只能画矩形，
  斜线全是锯齿，圆头得自己算旋转贴图。`Handles.DrawAAPolyLine` 的抗锯齿是白送的，
  线宽本来就是屏幕像素，所以"像 UI 一样浮着"和"用 Handles 画"不矛盾，后者质量更高。

  手感的三件事：

  - **采点按屏幕距离**（2px），不按世界距离 —— 世界距离在不同缩放下对应的像素数差很远，
    用世界距离会在放大时点太密、缩小时点太疏。
  - **收笔时 Chaikin 细分一轮**，把折线抹成弧线。
  - **画的时候 `Event.Use()`**，不然画一笔顺手把物体全选中了。
    `Alt + 左键` 放行给视口环绕；落在工具箱上的点击也不吃，否则点一下滑条会留一个点。

  **撤销是整笔退**：`MouseDown` 时 `RegisterCompleteObjectUndo` 拍快照，Ctrl+Z 退掉一整笔，
  不是退一个点。点一下就松手不算一笔，不会往场景里塞看不见的点。

  **Play 模式也能画，退出后标记还在**：Unity 退出 Play 会销毁运行时创建的对象、
  把编辑模式的对象恢复成进 Play 前的样子，所以 `ExitingPlayMode` 把数据抄成 JSON
  存进 `SessionState`（跨域重载也活着），`EnteredEditMode` 再写回场景对象。
  直接覆盖不合并 —— Play 场景是编辑模式场景的副本，数据本来就是超集。
  Play 里把标记对象删了就不复活，那是用户的意思。

### Changed

- **右下角工具箱变成两行**（`Editor/LDK2DViewportZoomSlider.cs`）。

  第一行管视口（眼睛 + 缩放滑条），第二行管标记（笔 + 颜色 + 线宽 + 橡皮）。
  笔没激活时第二行只有一个笔按钮，颜色 / 线宽 / 橡皮不预先铺开 ——
  不画标记的时候它们全是噪音。工具箱宽度 `210` → `240`。

  第二行通过 `ExtraRow` 静态委托挂进来：`LDK2DViewportZoomSlider` 比 `LDK2DMarkupTool` 老，
  **旧文件引用新类型**会在 Unity 增量编译「先编译、后导入」那一轮报
  `does not contain a definition`，所以反过来由新文件往旧文件的委托上挂。
  同时暴露 `ToolboxRect`，让笔判断"这一下点击是不是落在工具箱上"。



## [0.10.0] - 2026-11-11

### Removed

- **视口右上角的缩略地图整个拿掉了**（0.9.0 加的那个，`Editor/LDK2DSceneMinimap.cs` 已删除）。

  问题不在画得对不对，在于**它解决的不是真问题**。要看"关卡铺到哪"，本质上需要的是
  把视口缩到能看全 —— 一张额外的缩略图等于要求人同时在两个坐标系里思考，
  而且它的尺寸上限被"不能挡住视口"卡死，永远不够看清细节。
  换成右下角那根视口缩放滑条之后，它就是多余的。

- **视口右下角的「碰撞体」按钮也拿掉了**（`LDK2DColliderDisplayOverlay.DrawToggle`）。

  这颗按钮和「场景显示层级」面板里的勾选框本来就是**同一个状态的两个入口**，
  两处显示不一致时反而要先排查"到底哪个是真的"。现在只留面板里那一个入口，
  `LDK2DColliderDisplayOverlay.Show` 属性本身没动，绘制逻辑一行没改。
  右下角那块位置腾出来给缩放滑条。

### Added

- **视口右下角多了一个全局工具箱，第一件是视口缩放滑条**
  （`Editor/LDK2DViewportZoomSlider.cs`）。

  滚轮缩放**没有刻度**：缩着缩着就不知道自己是放大了还是缩小了，也不知道还能不能再缩出去；
  想把整个关卡一眼看全，只能来回滚、靠感觉停。这根滑条把"缩放到哪里"变成一个
  可以看见、可以拉回去的值：

  | 位置 | 效果 |
  |---|---|
  | 拉到最右 | **整个场景刚好装下** |
  | 拉到最左 | 放到最大，约 25 倍变焦范围 |

  几个取舍：

  - **"装下全场景"是算出来的，不是拍脑袋的常量**。取所有 `Renderer` 的包围盒并集，
    高度和宽度各算一遍取大的 —— 只看高度的话宽扁的关卡会左右溢出。
    被关掉的渲染器不参与：场景显示层级把某层藏了之后，拉满应该只装下看得见的部分。
  - **拉满时顺手把视图中心对到场景中心，只对一次**。视野够了但中心偏着还是看不全；
    但每帧都对的话，用户拉满之后就没法平移视图了，镜头会被一直拽回去。
    用 `_pinned` 记"已经对过"，滑条离开满值才复位。
  - **滑条值从 `view.size` 反算，不另存一份**。存一份就迟早跟真实视口对不上 ——
    用滚轮缩放之后滑条必须跟着动，这才是"等价于滚动缩放"该有的样子。
  - **正交和透视分开换算**。正交下 `view.size` 就是半高；透视下它是相机到焦点的距离，
    得按 FOV 折算成半高再比。
  - **左边的眼睛是标识，不是按钮**。用 `Label` 而不是 `Toggle` / `Button` ——
    点它不会有任何反应，也不该有。它只说明这条滑条管的是"视口看得见多大"，
    跟以后可能放进这个工具箱的其它条目区分开。

## [0.9.0] - 2026-11-11

### Added

- **视口右上角多了一张缩略地图**（`Editor/LDK2DSceneMinimap.cs`）。

  地形摆到 9 块之后，Scene 视图里永远只能看到一块。想确认"整体是不是拉得太长"
  "敌人是不是漏在某个角落"，只能缩得很小或者来回滚。这张图直接把整个场景压成一小块：

  - **地形**：用 `BuildOutline()` 的地表折线逐段画成竖条，拼出**剪影**。
    不画包围盒方框 —— 方框会把不规则地形画成一块砖，看不出坡往哪边倒；
    而"这块地形是怎么斜的"恰恰是摆关卡时最想从缩略图里看出来的。
    地表顶边压一条亮色线，把"地表"和"体块"分开，否则整块一个色，看不出高低。
  - **角色**：测试角色画 4px 青色方块（只有一个，最该一眼找到），
    敌人 / NPC / 奖励点按 `LDK2DTestActor.ColorOf()` 的配色画 3px 方块。

  **范围**取所有地形包围盒的并集，等比缩放居中。只算地形、不算"所有 Renderer" ——
  场景里还有一堆当参考图用的 Sprite，把它们算进来范围会被撑得很大，地形反而缩成一条线。
  一块地形都没有时退回角色位置。

  **位置**：右边贴边距，顶部往下让 6.5 行高 —— 2022.3 的右上角被方向指示器
  （Orientation gizmo）和叠加工具条占着，贴顶会重叠。面板高度按场景长宽比算，
  夹在 72~150 之间：这个关卡是 40 宽 × 4.4 高，严格按比例只有 20px 高，
  给个下限免得看着像坏了。

  **纯展示，不接受任何输入。** 没有按钮、没有开关、不响应点击 —— 它只是"另一双眼睛"。
  场景里没有地形也没有角色时整块不画，空盒子比不画更像坏了。

  性能：对象列表只在 `hierarchyChanged` 时重新 `FindObjectsOfType`；
  轮廓和包围盒**每帧重算**，因为地形雕刻之后 `renderer.bounds` 会变但不触发层级变化，
  缓存住就会画成旧形状。轮廓接进复用的 `List<Vector2>`，不产生堆分配。

## [0.8.0] - 2026-11-11

### Changed (breaking)

- **命名空间和全部类型加了 `LDK2D` 前缀，防止装进别的项目时和已有类型重名。**

  以前命名空间叫 `Terrain2D`，类型叫 `HeightmapTerrain2D` / `MeshUtil` / `Triangulator` /
  `ActorKind` 这类通用名，放进别人的工程很容易撞上 —— 尤其是 `Triangulator` 和三个枚举。
  现在统一前缀：

  | 旧 | 新 |
  |---|---|
  | `namespace Terrain2D` | `namespace LDK2D` |
  | `namespace Terrain2D.EditorTools` | `namespace LDK2D.EditorTools` |
  | `HeightmapTerrain2D` | `LDK2DHeightmapTerrain` |
  | `PolygonMesh2D` | `LDK2DPolygonMesh` |
  | `MeshUtil` | `LDK2DMeshUtil` |
  | `Triangulator` | `LDK2DTriangulator` |
  | `TestActor2D` | `LDK2DTestActor` |
  | `TestEnemy2D` | `LDK2DTestEnemy` |
  | `TestPlayer2D` | `LDK2DTestPlayer` |
  | `TestCamera2D` | `LDK2DTestCamera` |
  | `TestProjectile2D` | `LDK2DTestProjectile` |
  | `ActorKind` | `LDK2DActorKind` |
  | `EnemyBehaviour` | `LDK2DEnemyBehaviour` |
  | `TestCameraMode` | `LDK2DTestCameraMode` |
  | `UVMode` / `OutlineSource` | `LDK2DUVMode` / `LDK2DOutlineSource` |
  | `TerrainStage` / `WorkStage` | `LDK2DTerrainStage` / `LDK2DWorkStage` |
  | `Terrain2DToolsEditor` | `LDK2DToolsEditor` |
  | `Terrain2DMenuItems` | `LDK2DMenuItems` |
  | `SortingOrderToolbar` | `LDK2DSortingOrderToolbar` |
  | `SceneToolbarLayout` | `LDK2DSceneToolbarLayout` |
  | `SceneDepthVisibility` | `LDK2DSceneDepthVisibility` |
  | `ColliderDisplayOverlay` | `LDK2DColliderDisplayOverlay` |
  | `DepthPresets` / `DepthPreset` / `DepthPresetStore` | `LDK2DDepthPresets` / `LDK2DDepthPreset` / `LDK2DDepthPresetStore` |
  | `ColorPalette` / `PaletteEntry` / `PaletteStore` | `LDK2DColorPalette` / `LDK2DPaletteEntry` / `LDK2DPaletteStore` |
  | `NameDialog` / `InspectorL10n` / `TestCameraPrefs` / `PlayModeCameraSync` | 同名加 `LDK2D` |

  文件名跟着改，`.cs` 和 `.cs.meta` 同步重命名 —— **GUID 不变**，所以场景里挂着组件的物体
  （9 块地形、测试角色、测试摄像机、敌人、金币）一个引用都不会丢。序列化字段名没有动，
  Inspector 上调过的值也都还在。

- **菜单路径 `GameObject/地形 2D/…` 改成 `GameObject/LDK2D/…`**，
  `AddComponentMenu` 的 `地形 2D/…` 改成 `LDK2D/…`。菜单项文字本身仍然是中文。

- **程序集名改成 `LDK2D.Runtime` / `LDK2D.Editor`**，`rootNamespace` 同步改成
  `LDK2D` / `LDK2D.EditorTools`，Editor 程序集对 Runtime 的引用路径一起更新。

### 有意没改的

- **`EditorPrefs` 的键仍然是 `Terrain2D.*`。** 键是字符串，不会造成编译冲突；改了会让已经存下的
  Z 预设（`Terrain2D.DepthPresets` 里的「地形」「背景层」）、调色板条目和阶段状态全部读不到。
  要改成 `LDK2D.*` 得配一次读旧键的迁移，单独做。

- **目录仍是 `Assets/Terrain2D/`，`package.json` 的 `name` 仍是 `com.zhongxiaguchen.terrain2d`。**
  改目录和包名属于打包发布层面的事，和类型冲突无关，需要时单独处理。

## [0.7.0] - 2026-11-11

### Added

- **资产替换阶段多了「场景显示层级」**（`Editor/SceneDepthVisibility.cs`）。

  位置是**「什么都没选」时的左上角面板**（`SortingOrderToolbar.DrawStageActionPanel`
  的资产替换分支），不是选中地形的那块。理由：它管的是**整个场景**的显隐，和选中哪块
  地形无关；而且人检查整体效果时通常什么都不选，放进选中地形的面板等于要先点一块地形
  才能看整层，多绕一步。选中地形时那块面板保持原样（7 行 UV / 材质）。

  按 `transform.position.z` 筛出这个 Z 上的渲染器，统一显示 / 隐藏：

  ```
  场景显示层级
  Z [0      ] ▾
  24 个    [显示]
  [ ] 显示碰撞体
  场景里的 Z：-1、0、0.5
  关层级只关渲染器，碰撞体始终保留。
  ```

  **Z 由人指定，不自动扫描。** 两种给法：

  - 输入框直接输，默认 `0`（存 `EditorPrefs` 的 `Terrain2D.TargetZ`，跨重启）
  - 点 `▾` 从 `DepthPresets` 里选已经保存的命名 Z（「地形 (0)」这种）——
    和「层级 / Z」那一行用的是**同一张预设表**，存过的"背景 / 地面 / 前景"直接复用

  为什么不自动列出所有 Z：场景里拖出来的 Z 常常是 `0.5001` / `0.25` / `0.0499` 这种碎值，
  自动分组会冒出一长串没人认识的层。而层级本来就是设计时定好的档位，`DepthPresets`
  里已经存着了。面板底部仍然把场景里实际存在的 Z 列成一行**只读**参考
  （最多 6 个，多的用 `…` 收掉），省得靠猜该输什么。

  点一下开关就把这个 Z 上的渲染器统一打开或关掉；一个都没匹配到时开关置灰，
  左边显示 `0 个`。

  **只写 `Renderer.enabled`，碰撞体一个字都不碰。** 关掉一层是为了看清下面那层在画什么；
  如果连碰撞一起关掉，就会变成"我明明关了背景，角色却掉下去了" —— 物理和视觉必须是两件事。

  Z 用**世界坐标**比较，不是 `localPosition`：物体挂到带偏移的父物体下面时，
  `localPosition` 和眼睛看到的层次对不上。比较容差 `0.005`（半个 0.01），
  手拖出来的 `0.5001` 和 `0.5` 算同一层。

- **「碰撞体」的显示范围从「只有地形」扩大到「场景里所有 `Collider2D`」**
  （`Editor/ColliderDisplayOverlay.cs`）。

  排查"这块地方为什么走不过去"的时候，光看地形轮廓不够 —— 还得知道是不是某个敌人 /
  NPC / 奖励点的碰撞盒挡在那儿。现在按类型分色：

  | 颜色 | 含义 |
  |---|---|
  | 橙黄 | `PolygonCollider2D`（地形体块，实心） |
  | 青色 | `BoxCollider2D`（角色 / 占位标记） |
  | 品红 | `EdgeCollider2D`（旧版本残留的开链，两端各画十字） |
  | 灰蓝 | 触发器（`isTrigger`，不挡人） |
  | 暗红 | 组件被禁用（视觉还在、物理是空的） |

  `BoxCollider2D` 的 `size` / `offset` 是**本地**值，交给 `TransformPoint` 去乘缩放 ——
  占位标记的尺寸是靠 `transform.localScale` 给的，直接拿 size 画会错。

  禁用的 `EdgeCollider2D` 仍然不画：地形换成体块碰撞之后它只可能是历史残留，
  画出来只会跟体块轮廓混在一起。

  开关状态还是同一个 `EditorPrefs` key（`Terrain2D.ShowColliders`）。视口右下角的按钮和
  资产替换面板里的勾选框控制的是**同一个状态**，不会出现"面板里关了但右下角还亮着"。

- **Z 预设现在能删了**（`Editor/DepthPresets.cs`）。

  下拉菜单底部多了「删除预设 ▸」子菜单，列出所有存过的名字，点一个删一个；最后一项
  「全部清空」会先弹确认框。

  删除收进子菜单而不是和"选一个"平铺：数据在 `EditorPrefs` 里，**删了没有撤销**，
  误点一下就把存好的档位删掉，代价太大。

  这个菜单是共用的 —— 「层级 / Z」那一行和资产替换面板的「场景显示层级」都走
  `DepthPresets.ShowMenu`，所以两处都能删。

- **「选中非地形物体」面板的碰撞体开关，没有碰撞体时也能开了**
  （`Editor/SortingOrderToolbar.cs`）。

  以前没有碰撞体时这颗按钮是 `EditorGUI.DisabledScope` 里的灰色开关 —— 摆着不能动，
  等于没做。摆关卡时给背景装饰临时补个碰撞盒是很常做的事，不该逼人回 Inspector。

  现在**永远是同一颗 `Toggle`**，和地形层的 `SetCollisionEnabled` 一个心智：

  - **勾上** → 缺 `Collider2D` 的补一个 `BoxCollider2D`，然后统一 `enabled = true`
  - **取消** → 只 `enabled = false`，**组件留在物体上**，不删

  不删是有意的：摆关卡时经常要临时让某个装饰物"能挡 / 不能挡"来回试，删了再想加回来
  得重新调尺寸和偏移，一来一回就把节奏打断了。而且地形层也是这么做的 —— 两块面板的
  开关行为一致，人才不用记两套规则。

  也没有按"当前有没有碰撞体"换成另一种控件（比如 `+ 碰撞体`）：换控件会让这一行在
  点下去的一瞬间跳一下，还得多记一个按钮的位置。

  补出来的 `BoxCollider2D` 的 `size` / `offset` 取自渲染器的 `localBounds`
  （本地空间，和 `Collider2D` 是同一套坐标，不用乘缩放）；`localBounds` 是 0 时
  兜底成 1×1，否则碰撞盒尺寸是 0，在视口里点都点不到。

  只加 `BoxCollider2D`：最通用也最可预测，Sprite 和 Mesh 都适用。形状不贴的场合
  （多边形网格）自己在 Inspector 里换成 `PolygonCollider2D` —— 面板给的是
  "有个碰撞体"，不是"猜你要哪种"。

  加不上时会 `Debug.LogWarning`：物体上已经有 3D 碰撞体时 Unity 拒绝加 2D 的，
  而 `Undo.AddComponent` 失败是返回 `null` 而不是抛异常。

  `AllCollidersEnabled()` 现在要求**每个选中的物体都有碰撞体且都开着**才算"开"。
  以前只看已有的那些碰撞体是不是全 `enabled` —— 多选时只要有一个物体身上没碰撞体，
  开关照样显示"开"，实际那个物体是穿得过去的。点一下（变成关）再点一下（变成开）
  才补得出来。

### Fixed

- **选中被隐藏的物体时，左上角面板会整块消失。**

  `SortingOrderToolbar.RebuildSelection` 原来把 `!renderer.enabled` 的物体过滤掉了。
  以前没人在编辑器里批量隐藏渲染器，这个过滤等于没有影响；现在按 Z 分层隐藏成了常规操作，
  隐藏一层之后再选中里面的东西就变成"选中了、但面板是空的"，看着像坏了。

  改成只要物体上有 `Renderer` 就收进来 —— 层级 / Z / 颜色对这些物体照样有意义。

## [0.6.0] - 2026-11-11

### Changed (breaking)

- **碰撞体改成整个体块的 `PolygonCollider2D`，删掉 `EdgeCollider2D` 那条开链。**

  旧的边缘碰撞是沿地表的一条**开链**：没有内外之分，从下方、侧面都能穿进去；
  地表一旦有折（相邻采样点的 X 反过来 —— 横向拖动就会造成），链会在折的地方断开，
  表现就是"走一半突然没有碰撞体了"。而视觉网格一直是用 `BuildOutline()` 铺的
  完整轮廓，两套点从根上就不是一回事。

  现在 `BuildColliders()` 只生成一条闭合轮廓，点来自 `BuildOutline()`（没有左右链、
  没有独立底边、也没有横向拖动时，走"采样点直铺"的等价路径，省掉耳切）。
  碰撞和渲染共用同一份数据，不可能再分叉。

  删掉的公开字段：`buildEdgeCollider`、`edgeRadius`、`buildPolygonCollider`。
  保留 `buildColliders`（总开关）、`colliderOffsetY`、`colliderStride`。
  场景里已经挂着的旧 `EdgeCollider2D` 会被自动禁用（不销毁，方便对照）。

- Inspector 的「碰撞」区从 6 行缩到 3 行：生成碰撞体 / 碰撞体垂直偏移 / 碰撞体采样间隔。

### Fixed

- **物体上挂着 3D 碰撞体时，`BuildColliders()` 抛 NullReferenceException，整块地形没有碰撞。**

  `AddComponent<EdgeCollider2D>()` 在物体已经挂着 `MeshCollider` 时会失败并返回 `null`
  （Unity 不允许 2D 和 3D 碰撞体共存），而下一行 `_edge.edgeRadius = edgeRadius;` 直接 NRE，
  **把整个 `Rebuild()` 打断**。表现是那块地形完全没有碰撞、走过去直接掉下去，
  而 Inspector 上一点异常都看不出来。

  现场：`高度场地形 (2)`，Console 里只有一句
  `Can't add component 'EdgeCollider2D' to 高度场地形 (2) because it conflicts with the
  existing 'MeshCollider' derived component!`，后面跟着 3 次 NRE。

  现在 `AddComponent` 之后判空，失败时用 `Debug.LogWarning` 把原因和该删哪个组件说清楚，
  然后安全退出，不再打断 Rebuild。

- **在加入行为逻辑之前创建的敌人，按 Play 只有碰撞、一动不动。**

  旧版本的物理配置写在 `Create()` 里，只在"从工具栏创建"那一刻执行一次。
  已经躺在场景里的敌人身上只有 `TestActor2D`，没有 `Rigidbody2D`、也没有
  `TestEnemy2D` —— Play 之后自然只有一坨碰撞，没有行为。

  现在 `EnsurePhysics()` 是**幂等**的：缺什么补什么，已经有的不动，并在
  `TestActor2D.OnEnable()` 里跑一遍。老场景不用手动重建，重新编译一次就补齐了。
  只有**刚补上的** `TestEnemy2D` 才按 `typeName` 反推行为（飞行 / 射击 / 静止，
  其余算巡逻），已经在的不会重置 —— 免得每次域重载把人在 Inspector 里调过的值吃掉。

  顺带两处：

  - `PhysicsMaterial2D` 改成只在 Play 模式下赋。它是运行时 `new` 出来的
    （`HideAndDontSave`），编辑模式下塞给组件会被写进场景再丢成空引用，每次
    重编译都把场景标脏 —— 和 `TestPlayer2D` 的处理保持一致。
  - `TestEnemy2D` 加了 `OnValidate`，在 Inspector 里把行为改成「飞行」时顺手把
    `gravityScale` 归零。不然改完一 Play 它照样往下掉，看着像没生效。

- **NPC 和奖励点（金币等）被错挂上 `Rigidbody2D` + `TestEnemy2D`。**

  `Create()` 原来是「先 `AddComponent`，后赋 `kind`」：

  ```csharp
  var actor = go.AddComponent<TestActor2D>();   // OnEnable 在这里同步跑完了
  actor.kind = kind;                            // 之后才赋值
  ```

  `TestActor2D` 带 `[ExecuteAlways]`，`new GameObject` 出来又是激活的，所以
  `AddComponent` 那一刻 `OnEnable → EnsurePhysics()` 就执行了，**这时 `kind` 还是
  字段默认值 `ActorKind.Enemy`（枚举第 0 项）**。于是不管要建的是什么，
  都会被当成敌人挂上刚体和行为脚本。

  等 `Create()` 回头把 `kind` 改成 `Reward` 再调一次 `EnsurePhysics()`，那次只是把
  `box.isTrigger` 设成 `true` 然后 `return` —— `EnsurePhysics` 只补不删，
  残留的两个组件就留在金币身上了。

  表现：金币有动态刚体（Play 后受重力往下掉）、有巡逻行为（会左右走）、
  玩家碰到还会被 `TryHit` 击退。

  修了两处：

  - `Create()` 改成 `go.SetActive(false)` → `AddComponent` → 赋完字段 → `SetActive(true)`。
    物体关着的时候 `OnEnable` 不会跑，打开时 `kind` 已经就位。
  - `EnsurePhysics()` 在 `kind != Enemy` 时调用新增的 `StripEnemyParts()`，
    把残留的 `TestEnemy2D` / `Rigidbody2D` 摘掉（编辑模式 `DestroyImmediate`，
    运行时 `Destroy`）。**场景里已经存在的那些金币 / NPC 靠这一步自愈，不用删了重建**，
    进一次 Play 或重编译即可。

    注意这个清理只在 `OnEnable` 里触发 —— 在 Inspector 里把 `kind` 从敌人改成
    奖励点之后，同样要进一次 Play 才生效（Unity 禁止在 `OnValidate` 里
    `DestroyImmediate`）。

### Added

- **测试阶段多了三行占位标记：敌人 / NPC / 奖励点**（`Scripts/TestActor2D.cs`）。

  位置是**「什么都没选」时的左上角面板**（`SortingOrderToolbar.DrawStageActionPanel`
  的测试分支）。选中地形时那块面板保持原样 —— 那里已经有角色、碰撞、手感参数，
  再塞三行就挤了。

  每行一个类型下拉 + 一个「创建」。类型表写死在 `TestActor2D.TypeTable`：
  敌人 = 巡逻/飞行/射击/静止，NPC = 对话/商贩/任务，奖励点 = 金币/血包/存档点。
  选中的类型存 `EditorPrefs`，重启后还记得。

  创建出来的是**纯占位方块**：自己生成一张 16×16 白色 sprite，按种类染色
  （敌人红 / NPC 蓝 / 奖励点金）。位置取当前 Scene 视图中心，**Z 归零**；
  不挂父物体（地形常有 0.06~0.3 的缩放，挂上去标记会缩成一点）；重名自动加序号。

- **占位标记有了碰撞，敌人有了行为**（`Scripts/TestEnemy2D.cs`、`TestProjectile2D.cs`）。

  - 敌人 / NPC：实心 `BoxCollider2D`，走过去会被挡住
  - 奖励点：`isTrigger`，不挡人（拾取判定以后挂这儿）
  - 敌人额外挂 `Rigidbody2D` + `TestEnemy2D`，类型名直接映射成行为：
    **巡逻**（前方撞墙或没有地面就转身，不会走下悬崖）、**飞行**（无重力，
    出生点左右 `flyRange` 往返 + 上下浮动）、**射击**（定时朝角色发子弹）、
    **静止**。全部只在 Play 模式下跑。
  - 撞到角色会把人弹开（`knockback`），带 `hitCooldown` 防止贴着敌人每帧被弹。
    项目还没有血量系统，「受伤」暂时只有击退这一种表现。
  - 子弹是触发器，4 秒或被撞就销毁；穿过其他敌人 / 占位标记，不会卡在自己身上。
  - 巡逻的悬崖检测会跳过自身碰撞盒和所有触发器，否则脚下的射线会打在自己身上，
    悬崖判断直接失效。

- **视口右下角的「碰撞体」开关**（`Editor/ColliderDisplayOverlay.cs`）。把每块地形
  实际生效的碰撞折线画出来：橙黄 = 体块轮廓，暗红 = 被禁用，品红 = 手动加的
  `EdgeCollider2D`（开链两端各画一个十字）。`Handles.zTest = Always`，压在网格上
  也看得见；`FindObjectsOfType` 结果缓存，层级变化才重找。状态存 `EditorPrefs`，默认关。

## [0.5.3] - 2026-11-10

### Fixed

- **「有时候踏空」—— 碰撞折线缺了地形两端的边界链顶点。**
  `BuildOutline()` 画视觉轮廓时，顶部是从**左边链的顶点**开始的，然后才连到
  地表第一个采样点。而 `BuildColliders()` 只从地表采样点开始建 `EdgeCollider2D`。

  只要用户把左边界拖离地表端点，两者之间那段就在视觉上有地面、碰撞上是空的 ——
  走过去直接掉下去。实测某块地形左边链顶点在 `x ≈ 1.8`、地表第一个采样点在
  `x ≈ 14.8`，中间 13 个单位全是"看得见踩不着"。

  现在碰撞折线按 `左链顶点 → 地表采样点 → 右链顶点` 组装，和 `BuildOutline()`
  用同一套点。两端没有链时行为不变。

- **关掉「生成碰撞体」之后，已经生成的碰撞体仍然生效。**
  `Rebuild()` 原来只写了 `if (buildColliders) BuildColliders();` —— 关掉时直接跳过，
  之前挂上去的 `EdgeCollider2D` 还留在物体上、还是 `enabled`，于是"我明明关了碰撞，
  走过去照样被挡住"。

  只有 `SetCollisionEnabled()` 那条路径会主动禁用，而直接改 Inspector 的勾选框走的是
  `OnValidate → Rebuild()`，绕过了它。现在 `Rebuild()` 在关掉时调用
  `DisableColliders()` 兜住。

## [0.5.2] - 2026-11-10

### Fixed

- **「按 Play 一片蓝」的真正原因：相机 Z 贴着地形平面，整块被近裁面裁掉了。**
  地形都在 `z = 0`，而对准逻辑抄的是 `SceneView.pivot` —— 2D 视图里这个 pivot 的
  z 实测是 `≈ 9.8`，退 10 单位之后相机落在 `z ≈ -0.2`。和地形只差 0.2，比相机的
  近裁面 0.3 还近，于是画面里一个像素都渲染不出来，只剩相机背景色
  （`0.19, 0.30, 0.47` 那片蓝）。把相机 Z 手动调到 -0.2 以下就正常，正是这个原因。

  **Z 不再从视图状态推导。** 新增 `TestCamera2D.ScenePlaneZ()`：取场景里所有地形
  Transform 的 Z 平均值作为"场景平面"，相机永远站在它前面 `CameraDepth`（10）处。
  这个值不随 Scene 视图怎么平移而变化。

  四种摄像机模式统一用这一条规则 —— Z 描述的是"镜头离场景多远"，和"跟不跟角色"
  无关。`Fixed` 模式锁的是 X/Y，Z 照样按平面算。

  排查过程中排除过的：相机数量、正交尺寸、near/far、2D 光照、材质资产、
  运行时的 `HideFlags.DontSave` 网格（`OnEnable` 里有 `Rebuild`，是设计如此）。
  Y 轴也一度被怀疑，实际地形 Y 范围 `-18 ~ -7` 和相机 Y 的错位是同一个错误位置
  造成的表象，Z 修好之后 X/Y 抄 pivot 本来就没问题。

### Added

- **按 Play 自动对准视口**（`PlayModeCameraSync`）。在 `ExitingEditMode` 回调里把
  主相机抄到 Scene 视图的位置：位置取视口焦点沿视线反方向退 10 单位（2D 视口
  forward 就是 +Z，pivot 的 z 通常是 0，正好落在相机默认的 `z = -10` 上），
  朝向和正交尺寸一起抄过来。这个回调发生在 Unity 给场景拍快照之前，所以改出来的
  位置会被带进 Play。

  **只作用于固定视口。** 除了固定视口，其余三种模式（跟随 / 仅水平 / 仅垂直）
  相机都绑在角色身上，位置由 `TestCamera2D` 每帧算 —— 在这里覆盖会和它打架，
  而且那时用户要的是"跟着角色跑"，不是"停在我摆的地方"。

  没有按钮、没有开关、没有菜单项：懵的时候人是不会想到先去找个东西点的，
  所见即所得应该是不需要操作的默认行为。

### Changed

- `TestCamera2D.FindTarget` 改成 `FindObjectOfType<TestPlayer2D>(true)`，把未激活的
  也算上。跟随类模式的相机绑在角色身上，角色临时 `SetActive(false)` 一下不该让
  相机丢掉目标、退回原地。

## [0.5.1] - 2026-11-10

### Changed

- **摄像机模式在创建之前选。** 测试阶段的面板把「摄像机」挪到了「创建测试角色」上面，
  什么都没选时的阶段面板也多了一行摄像机下拉，创建按钮上直接带着模式名
  （`创建测试角色（跟随）`）。之前模式只能建完之后改，而建完再改意味着按下 Play
  的头两秒人已经跑出画面了 —— 摄像机是创建时就要定下来的东西。
- `TestCameraPrefs`（Editor）单独持有模式选项和 `EditorPrefs` 值。菜单项、地形面板、
  阶段面板三处都要读，让后两者直接引用 `Terrain2DMenuItems` 会产生编译期依赖，
  而 Unity 的增量编译出现过「先编译、后导入」导致 `does not contain a definition`。

## [0.5.0] - 2026-11-10

### Added

- **`TestCamera2D` — the test rig now has a camera.** Without one, pressing Play meant
  the character walked off screen in two seconds and you chased it with the Scene view.
  Four modes, chosen when the rig is created:

  | 模式 | 跟什么 | 用来验证 |
  |---|---|---|
  | **跟随**（默认） | 双轴 | 整段关卡 |
  | **仅水平** | 只跟 X，Y 锁死 | 跳跃高度 —— 镜头不上下晃才看得出到底跳多高 |
  | **仅垂直** | 只跟 Y，X 锁死 | 竖井 / 爬塔 |
  | **固定** | 不跟 | 反复试同一屏 |

  锁定的那一轴取的是**切到该模式时的相机位置**，不是世界 0 —— 所以「仅水平」保留你
  当前摆好的构图高度，而不是把镜头拽到 `y = 0`。
- **创建测试角色时一起配好摄像机**（面板按钮和菜单项都是）。优先复用已有的
  `TestCamera2D`，没有就挂在 `Camera.main` 上，连主摄像机都没有才新建一台。
  不另建一台是因为两台启用的摄像机会双份渲染，而主摄像机本来就已经配好了正交尺寸
  和背景色 —— 加一个跟随组件是纯增量，删掉组件场景一点没动。
- **测试阶段标题行显示场景里测试角色有几个** + 当前摄像机模式。
  复制粘贴测试角色之后经常忘掉多出来的那几个，然后纳闷"镜头怎么跟的是另一个"。
- 菜单 `GameObject ▸ 地形 2D ▸ 创建测试摄像机`。

### Changed

- 测试阶段面板新增「摄像机」一行（模式下拉 + 选中）和「跟随快慢」滑块；
  「固定」模式下不显示跟随快慢，因为那个模式没有快慢可言。
- 模式选择存在 `EditorPrefs` 的 `Terrain2D.TestCameraMode`，默认跟随。
  还没建摄像机时也能先选，创建时直接生效。

## [0.4.0] - 2026-11-10

### Changed

- **The viewport is now two fixed corners instead of one bottom bar.**
  - **左下角 · 固定栏** holds nothing but 关卡设计 + the 搭建 / 测试 / 资产替换
    selector. It is always there, because the stage describes how far the level has got,
    not what is selected.
  - **左上角 · 阶段面板** holds that stage's actual tools, and the stage selector is
    deliberately *not* in it — the panel's contents change with the selection, and the
    stage must not. The panel opens with a fold arrow and the stage name, since the
    selector now sits in the opposite corner.
- The panel is laid out for a 280px column (one or two controls per row) instead of four
  full-width rows, so it can sit in the corner without covering the level. Long hints
  moved from the right edge of each row into wrapped notes at the bottom.
- **层级 / Z / 颜色 / 碰撞体 moved from the bottom bar to the top-left panel.** They are
  properties of the selected object, not of the stage, so they show whenever a non-terrain
  2D renderer is selected.
- Panel fold state is global (`SessionState` `Terrain2D.PanelOpen`) rather than per object —
  the panel follows the stage, so per-object folding meant it changed shape when you picked
  a different terrain.
- `SceneToolbarLayout.PanelWidth` is shared by both drawers of the panel, so switching
  selection does not make the panel jump width.

### Fixed

- The panel is no longer clipped by an unknown-height area: it is anchored to the top-left
  corner and given a fixed generous height, instead of being pushed to the bottom with
  `FlexibleSpace`.

## [0.3.1] - 2026-11-10

### Added

- **The stage selector is now always on screen.** It used to appear only while a terrain
  was selected, so deselecting to look at the whole level made the current stage vanish —
  the one piece of state that describes where the level is, not which object you happen to
  be clicking. The empty-selection bar now opens with the same 搭建 / 测试 / 资产替换
  selector, and offers the one action that stage needs first:
  - **搭建** → 创建高度场地形
  - **测试** → 创建测试角色
  - **资产替换** → a hint, since this stage starts from a terrain that already exists
- `TerrainStage` (Editor) owns the stage enum, the persisted value, and the selector's
  drawing. Two mutually exclusive toolbars share it, so the state cannot live on either.

### Fixed

- Selecting a terrain while edit mode was off left the bottom of the viewport completely
  empty — the terrain toolbar was gated on edit mode and the sorting toolbar stands down
  for terrain selections, so both backed off at once. The toolbar now draws regardless;
  edit mode only controls whether the control points are drawn.

## [0.3.0] - 2026-11-10

### Added

- **Three-stage viewport toolbar.** The bottom toolbar now opens with a stage selector —
  搭建 / 测试 / 资产替换 — and only shows that stage's controls. The three phases look at
  completely different numbers, so stacking two dozen controls in one place meant hunting
  through all of them every time. The shell is now a dispatcher
  (`DrawStageBar` → `DrawBlockoutRows` / `DrawTestRows` / `DrawAssetRows`), so a fourth
  stage is one more `case`.
  - **搭建** keeps what was there (colour, edit aids, sorting layer / Z, generate /
    flatten / rebuild / subdivide / simplify / clear) plus the collider toggle.
  - **测试** gets the test character: create or select, drop at the left end of the
    terrain, and live sliders for jump height / move speed / air control that write
    straight to `TestPlayer2D` — adjustable mid-Play, which is the whole point of a test
    phase. Also carries `colliderOffsetY` and `colliderStride`, because "I cleared the gap
    but still fell" is usually a collider/visual mismatch.
  - **资产替换** gets UV mode, `uvScale`, the UV grid toggle, a readout of how many times
    the texture repeats along the surface, the `applyColor` switch, and the material
    handoff state.
- **「材质存为资产」**: turns the auto-generated embedded material into a real project
  asset via `AssetDatabase.CreateAsset` (which converts the object in place rather than
  copying, so the renderer's reference follows). Without this the artist cannot see, edit,
  or version-control the material at all.
- **Stage-aware warnings** in the asset phase: a `Scale ≠ 1` warning that computes the
  spacing needed to preserve the current world width, and an embedded-material warning.
- `TestPlayer2D` can now be built from code (`Terrain2DMenuItems.BuildTestPlayer`), not
  just from the menu item, so the toolbar can reuse the exact same setup.

### Fixed

- Turning off 「应用颜色」 no longer leaves the old tint stuck on the renderer. The colour
  lives in a `MaterialPropertyBlock`, which belongs to the renderer and stays there until
  something clears it — `ApplyVisuals` only stopped *writing* new values, so the previous
  one kept overriding the material and the only way back was reloading the scene. It now
  calls the new `MeshUtil.ClearColor`. This mattered for the asset handoff: the artist
  would set a colour on the material and see nothing change.

### Notes

- The stage is stored in `EditorPrefs` and is **global**, not per-terrain. It describes
  how far the level has got, not a property of one object, so selecting a different
  terrain should not reset it.
- Switching stages does not change which handles are drawn — that is still controlled by
  edit mode. Stage only decides which toolbar is shown.

## [0.2.0] - 2026-11-10

### Added

- **Surface-following UVs (`uvMode`)**: a second UV mapping mode alongside the original
  planar one. `Planar` keeps the old behaviour — UV is world X/Y times `uvScale`, which is
  trivial but stretches the texture on slopes by `1 / cos(slope)`. `SurfaceTiling` walks
  the surface instead: U is the **arc length** from the start of the surface, V is the
  **perpendicular distance** below it (vertical depth times `cos(slope)`). Both are in
  world units, so `1 / uvScale` means the same thing everywhere and texel density is
  constant across flats, ramps, and cliffs. Defaults to `Planar`, so existing scenes look
  exactly as they did.
- `SurfaceLength`, `ArcLengthAt(index)`, `SlopeCosAt(index)`: the derived metrics behind
  the new mode, exposed publicly. `SurfaceLength` is the total walkable length of the
  surface; the other two read per-sample values. Rebuilt once per `Rebuild()` in O(n) —
  cheap enough to leave unconditional.
- **UV grid overlay (`showUVGrid`)**: draws a tick along the surface every `1 / uvScale`
  world units in the Scene view, so the mapping can be checked **before any texture
  exists**. Even spacing on a flat *and* even spacing measured along a ramp is the proof
  that U is arc-length driven; ticks that thin out on ramps mean it is still planar. Capped
  at 2000 lines so a tiny `uvScale` cannot lock up the viewport.
- **`TestPlayer2D`** — a controllable test character, created from
  `GameObject → 地形 2D → 创建测试角色`. Left/right, jump, coyote time, jump buffer, and an
  extra fall multiplier; jump height is expressed in world units (`v = sqrt(2gh)`) rather
  than raw velocity, so it can be compared directly against the gaps in the level.
  Ground detection uses `Rigidbody2D.IsTouching` with a normal-angle filter, so standing
  against a wall does not count as grounded and slopes up to `maxSlopeAngle` do.
  It generates its own 16×16 placeholder sprite and a frictionless `PhysicsMaterial2D`
  (both `HideAndDontSave`, shared per process), so it is visible and playable with zero art.
  Deliberately **not** a production controller — it exists to answer "can I jump that gap"
  immediately after editing the terrain.

### Notes

- `TestPlayer2D` uses the legacy `Input` API, matching this project's
  `activeInputHandler: 0`. A project on the new Input System needs the four lines in
  `Update()` replaced with `InputAction` reads.
- `TestPlayer2D` only touches the `Rigidbody2D` in Play mode. Configuring it from `Awake`
  in edit mode would dirty the scene on every recompile.

## [0.1.1] - 2026-11-09

### Added

- `HeightmapTerrain2D`: equal-spacing height field terrain with a closed
  four-edge outline (top / right / bottom / left).
  - `SampleHeight(localX)` for ground queries (spawn placement, pathfinding).
  - `DestroyCircle(localX, radius)` for runtime crater-style destruction.
  - Seeded procedural generation (Perlin, octaves / persistence / lacunarity).
  - Automatic `EdgeCollider2D` / `PolygonCollider2D` generation.
  - Per-point horizontal offsets, so surface points can be dragged freely in 2D.
- `PolygonMesh2D`: builds a mesh from any `PolygonCollider2D` outline,
  including holes, via a keyhole-free multi-path triangulation.
- `BottomEdge`: an independent polyline for the bottom edge. Adding a point
  there no longer touches the surface — the bottom's point count and positions
  are fully decoupled from the height field samples.
- Scene view toolbar pinned to the bottom of the viewport (collapsible), with
  live point counts, colour picker, and batch operations.
- `Triangulator`: ear-clipping triangulation that tolerates collinear vertices.
- **Sorting order field in the terrain viewport toolbar**: a new row with an order
  field for the terrain itself, so the draw order can be changed without going to the
  Inspector.
- **Sorting order for any 2D object**: a small toolbar pinned to the bottom of the Scene
  view whenever the selection contains renderers, showing just the sorting order (and
  handling multi-selection, including mixed values). It steps aside while a
  `HeightmapTerrain2D` is selected, because the terrain editor draws its own toolbar in
  the same spot.
- **Colour for any 2D object**: the same toolbar also carries a colour field. Where the
  colour lives depends on the object — `PolygonMesh2D` and `HeightmapTerrain2D` keep it on
  the component (and are told to refresh), a `SpriteRenderer` uses Unity's own `color`
  property so the Inspector stays in sync, and anything else is left alone rather than
  guessed at.
- **Z field next to the sorting order**: sorting order and Z are two halves of the same
  thing — when orders tie, Unity falls back to Z — so both toolbars now show a Z field
  beside the order. Written to world Z, because that is what the camera actually sorts
  by, which is what "put this in front" means. This is the groundwork for
  foreground / background layers.
- **Named Z presets**: the `▾` beside the Z field saves and recalls named depths —
  background / ground / foreground without remembering the numbers. Each entry shows its
  value next to the name, because a Z number means nothing on its own. Stored in
  `EditorPrefs` alongside the colour palette.
- **Collider toggle**: a checkbox in both toolbars that enables or disables the selected
  objects' `Collider2D`, so background decoration stops blocking the player without a trip
  to the Inspector. On the terrain it means both halves: unticking sets `buildColliders`
  to false *and* disables the colliders already attached — setting the flag alone only
  makes `Rebuild()` skip `BuildColliders()`, leaving the old colliders live, which looks
  like the checkbox did nothing. Greyed out rather than hidden when there is no collider.
- **Empty-selection toolbar**: with nothing selected, a small toolbar offers
  创建高度场地形, so starting a level does not mean hunting through the GameObject menu.
  A selection that has no renderer on it stays quiet instead.
- **Named colour palette**: the `▾` beside any colour field lists saved colours and can
  save the current one under a name. Kept in `EditorPrefs`, so it belongs to the user
  rather than to the project — a deliberate trade, since a swatch list in the project
  folder would show up in version control and merge badly.

### Fixed

- `DestroyCircle` no longer punches through a sculpted bottom edge. The crater floor used
  `BottomAt(index)`, which only knows about `_bottomHeights` and `bottomY` — it never
  looked at the independent `_bottomEdge` polyline. On a terrain whose bottom had been
  sculpted (a floating island, an overhang) a crater would cut straight through the
  visible bottom and leave a hole. There is now a `BottomHeightAt(localX)` that resolves
  the bottom the same way `BuildOutline` does, and the crater clamps against it.
- Bottom edge points can no longer be dragged above the surface. `ClampBottomProfile`
  only clamped `_bottomHeights`; `_bottomEdge` was left free, so a bottom point pulled
  above the top curve made the outline self-intersect, ear clipping failed, and the mesh
  silently froze. Both sources are clamped now, so the point simply refuses to go above
  the ground.
- `SampleHeight` no longer returns nonsense when surface points are dragged past each
  other. It assumed `TopXAt(i)` was monotonically increasing and binary-searched on that
  assumption; free horizontal dragging can break it. Monotonicity is now checked (and
  cached, invalidated on `Rebuild`), with a linear fallback that takes the highest surface
  crossing the query X.
- `Generate()` now resets the shape overrides it used to leave behind: `LeftEdge`,
  `RightEdge`, `BottomEdge`, and both horizontal offset arrays. Previously it only reset
  `_bottomHeights`, so regenerating on an already-sculpted terrain produced a new surface
  wearing the old outline. Changing `sampleCount` in the Inspector still goes through a
  new `GenerateHeights()` that only re-lays the surface and keeps the edges — that path
  is "same terrain, different density", not "throw my edges away".
- `PolygonMesh2D` no longer allocates every frame in the editor. Its live-update poll ran
  on every instance regardless of selection, and `ReadPaths()` allocates a `Vector2[]`
  per path per call — a level with a dozen terrains meant a dozen array allocations per
  frame. In edit mode the poll now only runs while the object is selected (the outline
  can only change while you are dragging its points); Play mode is unchanged, since
  runtime scripts can edit colliders at any time.
- Meshes created at runtime are now destroyed in `OnDestroy`. They carry
  `HideFlags.DontSave`, so Unity never reclaims them — every scene load and every Play
  mode round-trip leaked one mesh per terrain.
- The three scene-view toolbars no longer share a hard-coded `32f` bottom inset. It is
  now `SceneToolbarLayout.BottomInset`, derived from `EditorGUIUtility.singleLineHeight`
  so it follows UI scale and high-DPI displays instead of fighting them.

### Notes

- The edit-mode live-update gate in `PolygonMesh2D` means an unselected `PolygonMesh2D`
  will not pick up outline changes made by a script. Call `Rebuild()` yourself in that
  case, or leave the object selected.
- Verified on Unity 2022.3.53f1 with URP 14 (2D Renderer).

[Unreleased]: https://github.com/CHANGE_ME/terrain2d/commits/main
[0.6.0]: https://github.com/CHANGE_ME/terrain2d/compare/v0.5.3...v0.6.0
[0.5.1]: https://github.com/CHANGE_ME/terrain2d/compare/v0.5.0...v0.5.1
[0.5.0]: https://github.com/CHANGE_ME/terrain2d/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/CHANGE_ME/terrain2d/compare/v0.3.1...v0.4.0
[0.3.1]: https://github.com/CHANGE_ME/terrain2d/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/CHANGE_ME/terrain2d/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/CHANGE_ME/terrain2d/compare/v0.1.1...v0.2.0
[0.1.1]: https://github.com/CHANGE_ME/terrain2d/compare/v0.1.0...v0.1.1
