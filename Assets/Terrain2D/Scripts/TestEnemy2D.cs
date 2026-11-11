using UnityEngine;

namespace Terrain2D
{
    /// <summary>敌人的行为。和占位标记里「敌人」的四个类型名一一对应。</summary>
    public enum EnemyBehaviour
    {
        [InspectorName("巡逻")] Patrol,
        [InspectorName("飞行")] Fly,
        [InspectorName("射击")] Shoot,
        [InspectorName("静止")] Idle
    }

    /// <summary>
    /// 测试敌人的行为。**只在 Play 模式下动**，编辑模式里就是个静止的方块。
    ///
    /// 四种行为都很简单，目的是把地形验证出来，不是做 AI：
    ///
    /// - **巡逻**：地面行走，前方撞墙或者前方没有地面就转身 —— 所以它不会自己走下悬崖。
    /// - **飞行**：不受重力，在出生点左右 <see cref="flyRange"/> 范围内来回，带一点上下浮动。
    /// - **射击**：站着不动，每隔 <see cref="fireInterval"/> 秒朝角色发一颗子弹。
    /// - **静止**：不动，只有碰撞。
    ///
    /// 撞到角色会把人弹开（<see cref="knockback"/>）。项目里还没有血量系统，
    /// 所以"受伤"暂时只表现成击退 —— 够用来验证"这个敌人的位置放得对不对"。
    /// </summary>
    [AddComponentMenu("地形 2D/测试敌人行为")]
    public class TestEnemy2D : MonoBehaviour
    {
        public EnemyBehaviour behaviour = EnemyBehaviour.Patrol;

        [Header("巡逻 / 飞行")]
        [Tooltip("水平速度（世界单位/秒）。")]
        public float moveSpeed = 1.6f;

        [Tooltip("飞行时以出生点为中心的左右范围。")]
        public float flyRange = 2.5f;

        [Tooltip("飞行时上下浮动的幅度。")]
        public float flyBob = 0.45f;

        [Header("射击")]
        [Tooltip("每隔几秒发一颗子弹。")]
        public float fireInterval = 1.6f;

        [Tooltip("子弹速度。")]
        public float bulletSpeed = 8f;

        [Header("伤害")]
        [Tooltip("撞到角色时把人弹开的速度。项目还没有血量系统，这就是「受伤」的全部表现。")]
        public float knockback = 9f;

        [Tooltip("两次击退之间的最短间隔，防止贴着敌人时每帧都被弹。")]
        public float hitCooldown = 0.6f;

        Rigidbody2D _body;
        BoxCollider2D _box;

        int _dir = 1;
        float _spawnX;
        float _nextFire;
        float _lastHit = -99f;

        // 探测用的复用缓冲，避免每个敌人每帧分配一个数组。
        static readonly RaycastHit2D[] _probeHits = new RaycastHit2D[8];

        Rigidbody2D Body
        {
            get { return _body != null ? _body : (_body = GetComponent<Rigidbody2D>()); }
        }

        BoxCollider2D Box
        {
            get { return _box != null ? _box : (_box = GetComponent<BoxCollider2D>()); }
        }

        void OnEnable()
        {
            _spawnX = transform.position.x;
            _nextFire = Time.time + fireInterval;
        }

        /// <summary>
        /// 在 Inspector 里改行为时，顺手把重力对上 —— 否则把「巡逻」改成「飞行」
        /// 之后它还挂着 1 倍重力，一 Play 就往下掉，看着像没生效。
        ///
        /// 只动 gravityScale，不加组件、不改别的参数。
        /// </summary>
        void OnValidate()
        {
            Rigidbody2D body = GetComponent<Rigidbody2D>();
            if (body == null) return;

            body.gravityScale = behaviour == EnemyBehaviour.Fly ? 0f : 1f;
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            if (behaviour != EnemyBehaviour.Shoot) return;
            if (Time.time < _nextFire) return;

            _nextFire = Time.time + fireInterval;
            Fire();
        }

        void FixedUpdate()
        {
            if (!Application.isPlaying) return;
            if (Body == null) return;

            switch (behaviour)
            {
                case EnemyBehaviour.Patrol: Patrol(); break;
                case EnemyBehaviour.Fly: Fly(); break;
                default: Stand(); break;
            }
        }

        /// <summary>地面巡逻：撞墙转身、前方没地面也转身。</summary>
        void Patrol()
        {
            BoxCollider2D box = Box;
            Vector2 center = (Vector2)transform.position;

            // 碰撞盒的 size 是**本地**尺寸（填的是 1×1），世界里的半宽高要乘缩放。
            // 占位标记的尺寸是靠 transform.localScale 给的，直接拿 size 会算错。
            Vector2 half = box != null
                ? new Vector2(
                    Mathf.Abs(transform.lossyScale.x) * box.size.x * 0.5f,
                    Mathf.Abs(transform.lossyScale.y) * box.size.y * 0.5f)
                : new Vector2(0.4f, 0.4f);

            // 前方一点点，看有没有墙。
            Vector2 wallOrigin = new Vector2(center.x + _dir * (half.x + 0.03f), center.y);

            // 前脚掌下方，看还有没有地面。
            Vector2 ledgeOrigin = new Vector2(
                center.x + _dir * (half.x + 0.02f),
                center.y - half.y + 0.05f);

            if (HitSolid(wallOrigin, new Vector2(_dir, 0f), 0.14f, box) ||
                !HitSolid(ledgeOrigin, Vector2.down, half.y + 0.35f, box))
            {
                _dir = -_dir;
            }

            Body.velocity = new Vector2(_dir * moveSpeed, Body.velocity.y);
        }

        /// <summary>飞行：无重力，在出生点左右往返，带上下浮动。</summary>
        void Fly()
        {
            float x = transform.position.x;

            if (x > _spawnX + flyRange) _dir = -1;
            else if (x < _spawnX - flyRange) _dir = 1;

            float bob = Mathf.Sin(Time.time * 2f) * flyBob;
            Body.velocity = new Vector2(_dir * moveSpeed, bob);
        }

        /// <summary>站着不动，但保留重力 —— 不然会浮在空中。</summary>
        void Stand()
        {
            Body.velocity = new Vector2(0f, Body.velocity.y);
        }

        void Fire()
        {
            Vector2 from = (Vector2)transform.position;
            Vector2 dir = _dir > 0 ? Vector2.right : Vector2.left;

            TestPlayer2D player = FindPlayer();

            if (player != null)
            {
                Vector2 toPlayer = (Vector2)player.transform.position - from;
                if (toPlayer.sqrMagnitude > 0.0001f) dir = toPlayer.normalized;
            }

            // 从身体外面一点出生，否则子弹一出来就打在自己身上。
            float halfWidth = Box != null
                ? Mathf.Abs(transform.lossyScale.x) * Box.size.x * 0.5f
                : 0.4f;

            float offset = halfWidth + 0.3f;

            var sprite = GetComponent<SpriteRenderer>();
            Color color = sprite != null ? sprite.color : Color.red;

            TestProjectile2D.Spawn(from + dir * offset, dir * bulletSpeed, color);
        }

        static TestPlayer2D FindPlayer()
        {
            return Object.FindObjectOfType<TestPlayer2D>(true);
        }

        /// <summary>碰到角色就把他弹开。触发器和别的敌人不算。</summary>
        void OnCollisionEnter2D(Collision2D collision)
        {
            TryHit(collision.collider);
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            TryHit(other);
        }

        void TryHit(Collider2D other)
        {
            if (!Application.isPlaying || other == null) return;
            if (Time.time - _lastHit < hitCooldown) return;

            var player = other.GetComponent<TestPlayer2D>();
            if (player == null) return;

            var body = player.GetComponent<Rigidbody2D>();
            if (body == null) return;

            _lastHit = Time.time;

            Vector2 away = (Vector2)player.transform.position - (Vector2)transform.position;
            if (away.sqrMagnitude < 0.0001f) away = Vector2.up;
            away.Normalize();

            body.velocity = new Vector2(
                away.x * knockback,
                Mathf.Max(body.velocity.y, knockback * 0.55f));
        }

        /// <summary>
        /// 沿 <paramref name="dir"/> 探一段，碰到**非触发器**的实体就算命中。
        ///
        /// 触发器（子弹、奖励点）不参与，否则子弹飞过去会让巡逻的敌人以为前面有墙；
        /// 自己的身体也要跳过，不然脚下的探测永远打在自己身上，悬崖检测就失效了。
        /// </summary>
        static bool HitSolid(Vector2 origin, Vector2 dir, float distance, Collider2D self)
        {
            int count = Physics2D.RaycastNonAlloc(origin, dir, _probeHits, distance);

            for (int i = 0; i < count; i++)
            {
                Collider2D hit = _probeHits[i].collider;
                if (hit == null || hit == self || hit.isTrigger) continue;
                return true;
            }

            return false;
        }
    }
}
