using UnityEngine;

namespace Terrain2D
{
    /// <summary>测试摄像机的跟随方式。顺序和编辑器里的下拉一致。</summary>
    public enum TestCameraMode
    {
        /// <summary>双轴跟随 —— 默认。角色往哪走，镜头往哪跟。</summary>
        Follow = 0,

        /// <summary>只跟水平，Y 锁死。横版最常用：跳起来镜头不跟着上下晃。</summary>
        HorizontalOnly = 1,

        /// <summary>只跟垂直，X 锁死。竖井 / 爬塔类关卡用。</summary>
        VerticalOnly = 2,

        /// <summary>完全不跟，固定在一个视口。用来反复试同一段单屏关卡。</summary>
        Fixed = 3,
    }

    /// <summary>
    /// 配合 <see cref="TestPlayer2D"/> 的测试摄像机。
    ///
    /// **它不是成品相机控制器。** 没有边界限制、没有前瞻、没有震屏，
    /// 只有"跟着谁、跟哪几个轴、多快跟上"三件事 —— 够验证地形，不够做游戏。
    ///
    /// 为什么需要一个：改完地形按 Play 试跳，如果镜头不动，人跑出画面就看不见了，
    /// 只能一边跑一边手动拖 Scene 视图。四个模式对应四种验证场景：
    /// 双轴看整体，仅水平看跳跃高度（镜头不晃才看得出到底跳多高），
    /// 仅垂直看竖井，固定用来反复试同一屏。
    ///
    /// 锁定的那一轴取的是**切到该模式时的相机位置**，不是世界 0 ——
    /// 所以「仅水平」会保留你当前摆好的构图高度，而不是把镜头拽到 y = 0。
    /// </summary>
    [AddComponentMenu("地形 2D/测试摄像机")]
    [DisallowMultipleComponent]
    public class TestCamera2D : MonoBehaviour
    {
        [Tooltip("跟随方式。")]
        public TestCameraMode mode = TestCameraMode.Follow;

        [Tooltip("跟谁。留空会自动找场景里的 TestPlayer2D。")]
        public Transform target;

        [Tooltip("在目标位置之上再加的偏移。默认往上抬 1 个单位，让人物在画面中间偏下。")]
        public Vector2 offset = new Vector2(0f, 1f);

        [Range(0f, 0.6f)]
        [Tooltip("跟上的快慢。0 = 硬跟（完全不插值），越大越飘。")]
        public float smoothTime = 0.12f;

        /// <summary>
        /// 被锁住那一轴的位置。切模式、启用、Reset 时抓一次当前相机位置。
        /// 存起来而不是每帧算，是因为"锁定"的语义就是"锁在切换那一刻的位置"。
        /// </summary>
        [SerializeField, HideInInspector]
        Vector2 _lockedPoint;

        Vector3 _velocity;

        /// <summary>把当前相机位置记成锁定点。</summary>
        public void CaptureLock()
        {
            _lockedPoint = transform.position;
        }

        /// <summary>换模式。会重新抓锁定点，所以"仅水平"保留的是切换瞬间的 Y。</summary>
        public void SetMode(TestCameraMode next)
        {
            mode = next;
            CaptureLock();
            _velocity = Vector3.zero;
        }

        /// <summary>立刻对准目标，不走插值。刚创建出来、或者点「放到左端」之后用。</summary>
        public void SnapToTarget()
        {
            Vector3 want = Desired();

            transform.position = want;
            _velocity = Vector3.zero;
        }

        /// <summary>
        /// 在场景里找一个测试角色当目标。
        ///
        /// 传 true 把未激活的也算上：跟随类模式的相机是绑在角色身上的，
        /// 角色临时被 SetActive(false) 一下不该让相机丢掉目标、退回原地。
        /// </summary>
        public Transform FindTarget()
        {
            var player = Object.FindObjectOfType<TestPlayer2D>(true);
            return player != null ? player.transform : null;
        }

        void Reset()
        {
            CaptureLock();
            target = FindTarget();
        }

        void OnEnable()
        {
            // 场景加载 / 重新启用时按当前位置重新锁定。存档时存的是位置，
            // 所以"锁定"在重启后仍然指向你上次摆好的构图。
            CaptureLock();
        }

        void LateUpdate()
        {
            // 只在 Play 下动 —— 编辑时镜头自己飘走会让人没法摆关卡。
            if (!Application.isPlaying) return;

            Vector3 want = Desired();

            Vector3 next = smoothTime <= 0.0001f
                ? want
                : Vector3.SmoothDamp(transform.position, want, ref _velocity, smoothTime);

            // Z 不参与跟随：2D 靠 Z 分层，镜头被拽到目标的 Z 上会整层错位。
            next.z = transform.position.z;

            transform.position = next;
        }

        Vector3 Desired()
        {
            Vector3 here = transform.position;

            switch (mode)
            {
                case TestCameraMode.Fixed:
                    return new Vector3(_lockedPoint.x, _lockedPoint.y, here.z);

                case TestCameraMode.HorizontalOnly:
                {
                    Transform t = ResolveTarget();
                    if (t == null) return here;

                    return new Vector3(t.position.x + offset.x, _lockedPoint.y, here.z);
                }

                case TestCameraMode.VerticalOnly:
                {
                    Transform t = ResolveTarget();
                    if (t == null) return here;

                    return new Vector3(_lockedPoint.x, t.position.y + offset.y, here.z);
                }

                default:
                {
                    Transform t = ResolveTarget();
                    if (t == null) return here;

                    return new Vector3(
                        t.position.x + offset.x,
                        t.position.y + offset.y,
                        here.z);
                }
            }
        }

        Transform ResolveTarget()
        {
            if (target == null) target = FindTarget();
            return target;
        }

        [ContextMenu("对准目标")]
        void ContextSnap()
        {
            SnapToTarget();
        }
    }
}
