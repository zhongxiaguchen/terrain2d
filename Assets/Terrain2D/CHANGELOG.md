# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
