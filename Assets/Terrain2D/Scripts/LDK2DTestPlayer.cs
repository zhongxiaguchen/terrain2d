using UnityEngine;

namespace LDK2D
{
    /// <summary>
    /// 一个能跑能跳的测试角色，用来在改完地形之后立刻验证「跳得过去吗」。
    ///
    /// **它不是成品角色控制器。** 只有左右移动、跳跃、土狼时间、跳跃缓冲四件事，
    /// 够验证地形，不够做游戏 —— 真实项目请把它换掉。
    ///
    /// 不依赖任何美术资源：没有指定 sprite 时会自己生成一张纯色方块，
    /// 所以从菜单创建出来立刻就能看见、就能跑，不用等美术。
    ///
    /// 用的是旧版 Input（Input.GetAxisRaw / GetButtonDown），因为这个项目
    /// 的 Active Input Handling 是 Input Manager。换新输入系统的话改 Update 里那几行。
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("LDK2D/测试角色")]
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Collider2D))]
    [RequireComponent(typeof(SpriteRenderer))]
    public class LDK2DTestPlayer : MonoBehaviour
    {
        [Header("移动")]
        [Tooltip("水平最大速度（世界单位/秒）。")]
        public float moveSpeed = 7f;

        [Tooltip("地面上的加速度。越大越跟手，越小越滑。")]
        public float acceleration = 90f;

        [Range(0f, 1f)]
        [Tooltip("空中的操控力度，相对地面加速度的比例。")]
        public float airControl = 0.6f;

        [Header("跳跃")]
        [Tooltip("跳起来能到多高（世界单位）。比直接填初速度好调得多。")]
        public float jumpHeight = 2.6f;

        [Tooltip("离开地面之后仍然允许起跳的宽限时间。没有它，边缘起跳会很难受。")]
        public float coyoteTime = 0.1f;

        [Tooltip("落地前提前按跳跃，落地瞬间自动生效的窗口。")]
        public float jumpBuffer = 0.12f;

        [Tooltip("下落时的重力倍率。大于 1 手感更干脆，不会飘。")]
        public float fallMultiplier = 1.8f;

        [Header("地面检测")]
        [Tooltip("哪些层算地面。默认全部。")]
        public LayerMask groundMask = ~0;

        [Range(1f, 89f)]
        [Tooltip("坡度超过这个角度就不算站得住。")]
        public float maxSlopeAngle = 50f;

        [Header("外观")]
        [Tooltip("没有指定 sprite 时，自动生成的方块用这个颜色。")]
        public Color fallbackColor = new Color(0.95f, 0.55f, 0.25f, 1f);

        // 整个进程共用一份 —— 只是占位方块，没必要每个角色造一张。
        static Sprite _fallbackSprite;
        static PhysicsMaterial2D _frictionless;

        Rigidbody2D _body;
        SpriteRenderer _sprite;

        float _coyoteTimer;
        float _bufferTimer;
        float _inputX;
        bool _grounded;

        void Awake()
        {
            // 编辑模式下不要改刚体参数 —— 那会把场景标记成已修改，
            // 每次重编译脚本都提示"有未保存的改动"。
            if (Application.isPlaying) ConfigureBody();
        }

        void OnEnable()
        {
            _body = GetComponent<Rigidbody2D>();
            _sprite = GetComponent<SpriteRenderer>();

            EnsureFallbackVisual();
        }

        void Reset()
        {
            _body = GetComponent<Rigidbody2D>();
            _sprite = GetComponent<SpriteRenderer>();

            ConfigureBody();
            EnsureFallbackVisual();
        }

        void OnValidate()
        {
            if (_sprite == null) return;

            // 只在还用着自动生成的占位方块时才刷颜色 ——
            // 用户自己换了 sprite 之后就别再动它了。
            if (_sprite.sprite != null && _sprite.sprite != _fallbackSprite) return;

            _sprite.color = fallbackColor;
        }

        void ConfigureBody()
        {
            if (_body == null) return;

            _body.freezeRotation = true;
            _body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            _body.interpolation = RigidbodyInterpolation2D.Interpolate;

            // 无摩擦材质：有摩擦的话角色会"贴"在斜坡上不动，或者爬墙。
            if (_body.sharedMaterial == null) _body.sharedMaterial = Frictionless;
        }

        static PhysicsMaterial2D Frictionless
        {
            get
            {
                if (_frictionless == null)
                {
                    _frictionless = new PhysicsMaterial2D("LDK2D 测试角色（无摩擦）")
                    {
                        friction = 0f,
                        bounciness = 0f,
                        hideFlags = HideFlags.HideAndDontSave
                    };
                }

                return _frictionless;
            }
        }

        void Update()
        {
            if (!Application.isPlaying) return;

            _inputX = Input.GetAxisRaw("Horizontal");

            if (Input.GetButtonDown("Jump")) _bufferTimer = jumpBuffer;
            else _bufferTimer -= Time.deltaTime;

            _coyoteTimer -= Time.deltaTime;
        }

        void FixedUpdate()
        {
            if (!Application.isPlaying || _body == null) return;

            _grounded = IsGrounded();
            if (_grounded) _coyoteTimer = coyoteTime;

            Vector2 velocity = _body.velocity;

            float targetX = _inputX * moveSpeed;
            float accel = _grounded ? acceleration : acceleration * airControl;
            velocity.x = Mathf.MoveTowards(velocity.x, targetX, accel * Time.fixedDeltaTime);

            if (_bufferTimer > 0f && _coyoteTimer > 0f)
            {
                // 用高度反推初速度：v = sqrt(2 g h)。这样改 jumpHeight 就是改"跳多高"，
                // 不用每次心算重力。
                float g = Mathf.Abs(Physics2D.gravity.y) * _body.gravityScale;
                velocity.y = Mathf.Sqrt(2f * g * jumpHeight);

                _bufferTimer = 0f;
                _coyoteTimer = 0f;
            }
            else if (velocity.y < 0f)
            {
                // 下落时额外补一点重力，跳跃曲线更利落。
                velocity.y += Physics2D.gravity.y * _body.gravityScale
                              * (fallMultiplier - 1f) * Time.fixedDeltaTime;
            }

            _body.velocity = velocity;
        }

        ContactFilter2D _groundFilter;

        /// <summary>
        /// 用"接触法线"判断是否站在地上，而不是简单射线检测。
        /// 好处是贴着墙时不会误判成落地，斜坡上也能正确判定。
        /// </summary>
        bool IsGrounded()
        {
            _groundFilter.useTriggers = false;
            _groundFilter.useLayerMask = true;
            _groundFilter.layerMask = groundMask;
            _groundFilter.useNormalAngle = true;
            _groundFilter.minNormalAngle = 90f - maxSlopeAngle;
            _groundFilter.maxNormalAngle = 90f + maxSlopeAngle;

            return _body.IsTouching(_groundFilter);
        }

        void EnsureFallbackVisual()
        {
            if (_sprite == null || _sprite.sprite != null) return;

            if (_fallbackSprite == null) _fallbackSprite = CreateFallbackSprite();

            _sprite.sprite = _fallbackSprite;
            _sprite.color = fallbackColor;
        }

        /// <summary>
        /// 生成一张 16×16 的白色方块当占位 sprite。
        /// pixelsPerUnit 取 16，所以它正好是 1×1 世界单位。
        /// </summary>
        static Sprite CreateFallbackSprite()
        {
            const int size = 16;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "LDK2D 测试角色",
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

            sprite.name = "LDK2D 测试角色";
            sprite.hideFlags = HideFlags.HideAndDontSave;

            return sprite;
        }

        /// <summary>当前是否站在地上。给外部脚本/调试用。</summary>
        public bool Grounded
        {
            get { return _grounded; }
        }
    }
}
