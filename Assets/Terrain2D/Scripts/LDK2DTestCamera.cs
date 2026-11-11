using UnityEngine;

namespace LDK2D
{
    /// <summary>测试摄像机的跟随方式。顺序和编辑器里的下拉一致。</summary>
    public enum LDK2DTestCameraMode
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
    /// 配合 <see cref="LDK2DTestPlayer"/> 的测试摄像机。
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
    [AddComponentMenu("LDK2D/测试摄像机")]
    [DisallowMultipleComponent]
    public class LDK2DTestCamera : MonoBehaviour
    {
        [Tooltip("跟随方式。")]
        public LDK2DTestCameraMode mode = LDK2DTestCameraMode.Follow;

        [Tooltip("跟谁。留空会自动找场景里的 LDK2DTestPlayer。")]
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

        /// <summary>相机站在地形平面前面多远。2D 项目的约定值是 10。</summary>
        public const float CameraDepth = 10f;

        /// <summary>
        /// 场景里地形所在的 Z 平面 —— 所有地形 Transform 的 Z 平均值。
        ///
        /// **相机必须站在这个平面前面 <see cref="CameraDepth"/> 处才看得见东西。**
        /// 贴着平面站（或者站到平面后面）会被近裁面裁掉，画面就只剩相机背景色，
        /// 看起来和"渲染坏了"一模一样 —— 实际发生过一次，地形在 z = 0、
        /// 相机被摆在 z ≈ -0.2，距离 0.2 小于近裁面 0.3，满屏蓝。
        ///
        /// 没有地形时退回 0，和 Unity 新建场景的约定一致。
        /// </summary>
        public static float ScenePlaneZ()
        {
            var terrains = Object.FindObjectsOfType<LDK2DHeightmapTerrain>(true);
            if (terrains.Length == 0) return 0f;

            float sum = 0f;
            for (int i = 0; i < terrains.Length; i++) sum += terrains[i].transform.position.z;

            return sum / terrains.Length;
        }

        // 算一次就缓存：LateUpdate 每帧都要读，FindObjectsOfType 每帧调太贵。
        float _planeZ = float.NaN;

        float PlaneZ
        {
            get
            {
                if (float.IsNaN(_planeZ)) _planeZ = ScenePlaneZ();
                return _planeZ;
            }
        }

        /// <summary>把当前相机位置记成锁定点。</summary>
        public void CaptureLock()
        {
            _lockedPoint = transform.position;
        }

        /// <summary>换模式。会重新抓锁定点，所以"仅水平"保留的是切换瞬间的 Y。</summary>
        public void SetMode(LDK2DTestCameraMode next)
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
            var player = Object.FindObjectOfType<LDK2DTestPlayer>(true);
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

            // 地形可能被挪过 Z，缓存作废。
            _planeZ = float.NaN;
        }

        void LateUpdate()
        {
            // 只在 Play 下动 —— 编辑时镜头自己飘走会让人没法摆关卡。
            if (!Application.isPlaying) return;

            Vector3 want = Desired();

            Vector3 next = smoothTime <= 0.0001f
                ? want
                : Vector3.SmoothDamp(transform.position, want, ref _velocity, smoothTime);

            // Z 既不跟目标、也不跟当前位置 —— 按场景地形所在的平面退后固定距离。
            // 用当前位置的话，一旦相机被摆在贴着地形的地方（差 0.2 < 近裁面 0.3），
            // 它就永远看不见东西，而且会一直保持那个错误的值。
            next.z = PlaneZ - CameraDepth;

            transform.position = next;
        }

        Vector3 Desired()
        {
            Vector3 here = transform.position;

            // Z 只有一种取法：地形平面再往前 CameraDepth。四种模式都一样 ——
            // 它描述的是"镜头离场景多远"，不是"跟不跟角色"的一部分。
            float z = PlaneZ - CameraDepth;

            switch (mode)
            {
                case LDK2DTestCameraMode.Fixed:
                    return new Vector3(_lockedPoint.x, _lockedPoint.y, z);

                case LDK2DTestCameraMode.HorizontalOnly:
                {
                    Transform t = ResolveTarget();
                    if (t == null) return new Vector3(here.x, here.y, z);

                    return new Vector3(t.position.x + offset.x, _lockedPoint.y, z);
                }

                case LDK2DTestCameraMode.VerticalOnly:
                {
                    Transform t = ResolveTarget();
                    if (t == null) return new Vector3(here.x, here.y, z);

                    return new Vector3(_lockedPoint.x, t.position.y + offset.y, z);
                }

                default:
                {
                    Transform t = ResolveTarget();
                    if (t == null) return new Vector3(here.x, here.y, z);

                    return new Vector3(
                        t.position.x + offset.x,
                        t.position.y + offset.y,
                        z);
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
