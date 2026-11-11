using UnityEngine;

namespace Terrain2D
{
    /// <summary>占位标记的种类。决定颜色、尺寸和可选的类型名。</summary>
    public enum ActorKind
    {
        Enemy,
        Npc,
        Reward
    }

    /// <summary>
    /// 关卡搭景用的**占位标记**：敌人 / NPC / 奖励点。
    ///
    /// 美术和 AI 都还没有，所以它自己生成一张纯色方块，颜色按种类区分
    /// （敌人红 / NPC 蓝 / 奖励点金）。
    ///
    /// **碰撞**：敌人和 NPC 是实心的（走过去会被挡住），奖励点是触发器（不挡人）。
    ///
    /// **行为**：只有敌人有 —— 挂在同物体上的 <see cref="TestEnemy2D"/> 负责
    /// 巡逻 / 飞行 / 射击 / 静止，撞到角色会把人弹开。NPC 和奖励点纯粹是标记，
    /// 没有任何逻辑（拾取、对话都还没做）。
    ///
    /// 方块尺寸用 transform.localScale 给，所以物体**不要挂到地形下面** ——
    /// 地形常有 0.06 ~ 0.3 的缩放，挂上去标记会跟着缩成一点。
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("地形 2D/占位标记")]
    public class TestActor2D : MonoBehaviour
    {
        [Tooltip("种类。决定默认颜色和类型列表。")]
        public ActorKind kind = ActorKind.Enemy;

        [Tooltip("类型名。敌人用它对上行为（巡逻 / 飞行 / 射击 / 静止），NPC 和奖励点只是标签。")]
        public string typeName = "巡逻";

        [Tooltip("占位方块的颜色。")]
        public Color color = new Color(0.85f, 0.25f, 0.25f, 1f);

        [Tooltip("占位方块的尺寸（世界单位）。")]
        public Vector2 size = new Vector2(0.8f, 0.8f);

        // 整个进程共用一张 —— 只是占位方块，没必要每个标记造一张纹理。
        static Sprite _fallbackSprite;

        SpriteRenderer _sprite;

        /// <summary>三个种类的显示名，顺序必须和 <see cref="ActorKind"/> 一致。</summary>
        public static readonly string[] KindLabels = { "敌人", "NPC", "奖励点" };

        /// <summary>每个种类可选的类型名。第一项是创建时的默认值。</summary>
        static readonly string[][] TypeTable =
        {
            new[] { "巡逻", "飞行", "射击", "静止" },
            new[] { "对话", "商贩", "任务" },
            new[] { "金币", "血包", "存档点" }
        };

        public static string[] TypesOf(ActorKind kind)
        {
            int index = (int)kind;
            if (index < 0 || index >= TypeTable.Length) return TypeTable[0];
            return TypeTable[index];
        }

        public static string LabelOf(ActorKind kind)
        {
            int index = (int)kind;
            return index >= 0 && index < KindLabels.Length ? KindLabels[index] : "标记";
        }

        /// <summary>种类的默认颜色。</summary>
        public static Color ColorOf(ActorKind kind)
        {
            switch (kind)
            {
                case ActorKind.Npc: return new Color(0.30f, 0.62f, 0.95f, 1f);
                case ActorKind.Reward: return new Color(0.95f, 0.78f, 0.22f, 1f);
                default: return new Color(0.85f, 0.25f, 0.25f, 1f);
            }
        }

        /// <summary>种类的默认尺寸。奖励点小一点，NPC 高一点。</summary>
        public static Vector2 SizeOf(ActorKind kind)
        {
            switch (kind)
            {
                case ActorKind.Npc: return new Vector2(0.7f, 1.05f);
                case ActorKind.Reward: return new Vector2(0.45f, 0.45f);
                default: return new Vector2(0.8f, 0.8f);
            }
        }

        /// <summary>标题，用来命名物体和做提示，比如「敌人·巡逻」。</summary>
        public string DisplayName
        {
            get { return LabelOf(kind) + "·" + typeName; }
        }

        /// <summary>
        /// 在 <paramref name="position"/> 创建一个占位标记。
        /// 不挂父物体 —— 地形有缩放，挂上去尺寸会跟着缩。
        /// </summary>
        public static TestActor2D Create(ActorKind kind, int typeIndex, Vector3 position)
        {
            string[] types = TypesOf(kind);
            typeIndex = Mathf.Clamp(typeIndex, 0, types.Length - 1);

            var go = new GameObject(UniqueName(LabelOf(kind) + "·" + types[typeIndex]));

            // **先关掉再挂组件。**
            //
            // TestActor2D 带 [ExecuteAlways]，而 new GameObject 出来是激活的 ——
            // AddComponent 那一刻 OnEnable 会**同步**跑一遍。如果先挂组件、后赋 kind，
            // 那一次 OnEnable 读到的 kind 还是字段默认值（ActorKind.Enemy），
            // NPC 和奖励点就会被错挂上 Rigidbody2D + TestEnemy2D，变成会走路、
            // 会掉、还会击退玩家的"金币"。
            //
            // 关掉物体再挂，OnEnable 就不会跑；字段全部就位后再打开，
            // 那才是拿正确 kind 跑的第一次。
            go.SetActive(false);

            go.transform.position = new Vector3(position.x, position.y, position.z);

            var actor = go.AddComponent<TestActor2D>();
            actor.kind = kind;
            actor.typeName = types[typeIndex];
            actor.color = ColorOf(kind);
            actor.size = SizeOf(kind);
            actor.ApplyVisual();

            // 碰撞盒靠 localScale 撑开，所以缩放要在打开之前定下来。
            go.transform.localScale = new Vector3(actor.size.x, actor.size.y, 1f);

            // 打开 → OnEnable 跑一次，EnsurePhysics 按正确的 kind 配好碰撞和（敌人的）行为。
            go.SetActive(true);

            // 新建的时候类型是明确的，直接按类型表下标指定行为 —— 比按名字反推准。
            TestEnemy2D enemy = actor.GetComponent<TestEnemy2D>();
            if (enemy != null)
            {
                enemy.behaviour = (EnemyBehaviour)Mathf.Clamp(typeIndex, 0, 3);
                actor.ApplyGravity();
            }

            return actor;
        }

        /// <summary>
        /// 配碰撞和物理。**幂等**：缺什么补什么，已经有的不动。
        ///
        /// - 敌人 / NPC：**实心**，走过去会被挡住
        /// - 奖励点：**触发器**，人不被挡，将来拾取判定就挂在这上面
        /// - 敌人额外挂 <see cref="TestEnemy2D"/>，行为按 <see cref="typeName"/> 反推
        ///
        /// 这个方法是给"自愈"用的：早先创建的敌人身上只有 <see cref="TestActor2D"/>，
        /// 没有刚体也没有行为组件，进了 Play 就是块不动的砖。放在 OnEnable 里补一次，
        /// 老场景不用手动重建。
        /// </summary>
        void EnsurePhysics()
        {
            BoxCollider2D box = GetComponent<BoxCollider2D>();
            if (box == null) box = gameObject.AddComponent<BoxCollider2D>();

            // 尺寸交给 transform.localScale：占位 sprite 正好 1×1 世界单位，
            // 所以碰撞盒填 1×1，缩放之后和方块严丝合缝。
            box.size = Vector2.one;
            box.offset = Vector2.zero;
            box.isTrigger = kind == ActorKind.Reward;

            if (kind != ActorKind.Enemy)
            {
                // 兜底：老版本 Create() 的时序 bug 会给 NPC / 奖励点错挂上敌人的
                // 刚体和行为脚本。场景里已经存在的那些靠这一步自愈，不用删了重建。
                StripEnemyParts();
                return;
            }

            Rigidbody2D body = GetComponent<Rigidbody2D>();
            if (body == null) body = gameObject.AddComponent<Rigidbody2D>();

            TestEnemy2D enemy = GetComponent<TestEnemy2D>();

            if (enemy == null)
            {
                enemy = gameObject.AddComponent<TestEnemy2D>();

                // 只有**刚补上的**才按名字猜行为。已经在的说明用户可能手动调过，
                // 每次域重载都重置会把人的修改吃掉。
                enemy.behaviour = BehaviourOf(typeName);
            }

            body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            // 无摩擦：有摩擦的话巡逻的敌人会贴在地面或者墙上不走。
            //
            // 只在 Play 模式下赋材质 —— 这份 PhysicsMaterial2D 是运行时 new 出来的
            // （HideAndDontSave），编辑模式下塞给组件会被写进场景再丢成空引用，
            // 每次重编译都会把场景标脏。TestPlayer2D 也是同样的处理。
            if (Application.isPlaying && body.sharedMaterial == null)
            {
                body.sharedMaterial = Frictionless;
            }

            ApplyGravity();
        }

        /// <summary>
        /// 摘掉敌人专属的组件。NPC 和奖励点不需要刚体，也不需要行为。
        ///
        /// 顺序有意义：先摘 <see cref="TestEnemy2D"/> 再摘 <see cref="Rigidbody2D"/>，
        /// 免得行为脚本在刚体已经消失的那一帧里还去读 <c>Body.velocity</c>。
        ///
        /// 只在 <see cref="OnEnable"/> 里触发，所以**在 Inspector 里把 kind 从敌人改成
        /// NPC / 奖励点之后，要进一次 Play（或者重编译）才会清理** —— 不能在
        /// OnValidate 里做这件事，Unity 明确禁止在 OnValidate 中 DestroyImmediate。
        /// </summary>
        void StripEnemyParts()
        {
            TestEnemy2D enemy = GetComponent<TestEnemy2D>();
            if (enemy != null) RemoveComponent(enemy);

            Rigidbody2D body = GetComponent<Rigidbody2D>();
            if (body != null) RemoveComponent(body);
        }

        /// <summary>
        /// 编辑模式没有"下一帧"，<c>Destroy</c> 排进去就永远不执行，必须 DestroyImmediate。
        /// </summary>
        static void RemoveComponent(Component component)
        {
            if (component == null) return;

            if (Application.isPlaying) UnityEngine.Object.Destroy(component);
            else UnityEngine.Object.DestroyImmediate(component);
        }

        /// <summary>飞行的那只不受重力，其余保留 —— 否则它得先掉到地上才「飞」得起来。</summary>
        void ApplyGravity()
        {
            Rigidbody2D body = GetComponent<Rigidbody2D>();
            if (body == null) return;

            TestEnemy2D enemy = GetComponent<TestEnemy2D>();
            EnemyBehaviour behaviour = enemy != null ? enemy.behaviour : BehaviourOf(typeName);

            body.gravityScale = behaviour == EnemyBehaviour.Fly ? 0f : 1f;
        }

        /// <summary>按类型名反推行为。老物体没有下标可用，只能看名字。</summary>
        static EnemyBehaviour BehaviourOf(string typeName)
        {
            switch (typeName)
            {
                case "飞行": return EnemyBehaviour.Fly;
                case "射击": return EnemyBehaviour.Shoot;
                case "静止": return EnemyBehaviour.Idle;
                default: return EnemyBehaviour.Patrol;
            }
        }

        static PhysicsMaterial2D _frictionless;

        static PhysicsMaterial2D Frictionless
        {
            get
            {
                if (_frictionless == null)
                {
                    _frictionless = new PhysicsMaterial2D("Terrain2D 占位标记（无摩擦）")
                    {
                        friction = 0f,
                        bounciness = 0f,
                        hideFlags = HideFlags.HideAndDontSave
                    };
                }

                return _frictionless;
            }
        }

        /// <summary>同名物体已经存在时加个序号，免得 Hierarchy 里一排「敌人·巡逻」分不清。</summary>
        static string UniqueName(string baseName)
        {
            if (GameObject.Find(baseName) == null) return baseName;

            for (int i = 2; i < 1000; i++)
            {
                string candidate = baseName + " " + i;
                if (GameObject.Find(candidate) == null) return candidate;
            }

            return baseName;
        }

        void OnEnable()
        {
            ApplyVisual();
            EnsurePhysics();
        }

        void OnValidate()
        {
            // 只刷颜色，**不**在这里补组件 —— 在 OnValidate 里 AddComponent 会被 Unity
            // 丢掉，还会顺带把场景弄脏。补组件的活留给 OnEnable / ApplyVisual。
            if (_sprite == null) _sprite = GetComponent<SpriteRenderer>();
            if (_sprite == null) return;

            _sprite.color = color;
        }

        /// <summary>没有 sprite 时补一张占位方块，并按种类刷颜色。</summary>
        public void ApplyVisual()
        {
            if (_sprite == null) _sprite = GetComponent<SpriteRenderer>();
            if (_sprite == null) _sprite = gameObject.AddComponent<SpriteRenderer>();

            if (_sprite.sprite == null) _sprite.sprite = PlaceholderSprite;

            _sprite.color = color;
        }

        /// <summary>
        /// 全进程共用的那张 16×16 白色方块。子弹之类的东西也要用，所以开成公开的。
        /// </summary>
        public static Sprite PlaceholderSprite
        {
            get
            {
                if (_fallbackSprite == null) _fallbackSprite = CreateFallbackSprite();
                return _fallbackSprite;
            }
        }

        /// <summary>
        /// 16×16 白色方块，pixelsPerUnit 取 16 → 正好 1×1 世界单位，
        /// 所以 size 填什么就是多大。
        /// </summary>
        static Sprite CreateFallbackSprite()
        {
            const int size = 16;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Terrain2D 占位标记",
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color32[size * size];
            var white = new Color32(255, 255, 255, 255);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = white;

            texture.SetPixels32(pixels);
            texture.Apply();

            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                size);

            sprite.name = "Terrain2D 占位标记";
            sprite.hideFlags = HideFlags.HideAndDontSave;

            return sprite;
        }
    }
}
