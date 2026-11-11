using UnityEngine;

namespace Terrain2D
{
    /// <summary>
    /// 「射击」型敌人打出去的子弹。只存在于 Play 模式，飞几秒或者撞到东西就消失。
    ///
    /// 撞到角色会把他弹开（和敌人本体一样，项目还没有血量系统）；
    /// 撞到地形直接消失；碰到别的敌人 / 占位标记也消失 —— 否则子弹会卡在自己身上。
    /// </summary>
    [AddComponentMenu("地形 2D/测试子弹")]
    public class TestProjectile2D : MonoBehaviour
    {
        [Tooltip("子弹存在几秒。防止没撞到东西的子弹永远留在场景里。")]
        public float life = 4f;

        [Tooltip("撞到角色时把人弹开的速度。")]
        public float knockback = 7f;

        float _dieAt;

        public static TestProjectile2D Spawn(Vector2 position, Vector2 velocity, Color color)
        {
            var go = new GameObject("子弹");
            go.transform.position = new Vector3(position.x, position.y, 0f);
            go.transform.localScale = new Vector3(0.18f, 0.18f, 1f);

            var sprite = go.AddComponent<SpriteRenderer>();
            sprite.sprite = TestActor2D.PlaceholderSprite;
            sprite.color = color;

            var box = go.AddComponent<BoxCollider2D>();
            box.isTrigger = true;

            var body = go.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            var bullet = go.AddComponent<TestProjectile2D>();
            body.velocity = velocity;

            return bullet;
        }

        void OnEnable()
        {
            _dieAt = Time.time + life;
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            if (Time.time >= _dieAt) Destroy(gameObject);
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (!Application.isPlaying || other == null) return;

            // 别的敌人 / 占位标记：不管，穿过去。子弹从枪口出来时可能还压在自己身上。
            if (other.GetComponent<TestEnemy2D>() != null) return;
            if (other.GetComponent<TestActor2D>() != null) return;

            var player = other.GetComponent<TestPlayer2D>();

            if (player != null)
            {
                var body = player.GetComponent<Rigidbody2D>();
                if (body != null)
                {
                    Vector2 away = (Vector2)player.transform.position - (Vector2)transform.position;
                    if (away.sqrMagnitude < 0.0001f) away = Vector2.up;
                    away.Normalize();

                    body.velocity = new Vector2(
                        away.x * knockback,
                        Mathf.Max(body.velocity.y, knockback * 0.5f));
                }

                Destroy(gameObject);
                return;
            }

            // 剩下的就是地形这类实体，撞上就消失。触发器（奖励点之类）不算。
            if (!other.isTrigger) Destroy(gameObject);
        }
    }
}
