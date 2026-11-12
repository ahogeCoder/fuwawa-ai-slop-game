using UnityEngine;

/// <summary>
/// BonkBattle enemy. Seeks the player, takes one bonk, handles knockback/feedback.
/// References are assigned explicitly; no runtime discovery.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class BonkEnemy : MonoBehaviour
{
    public static readonly Color[] EnemyColors =
    {
        new Color(1f, 0.45f, 0.55f),
        new Color(0.55f, 0.95f, 1f),
        new Color(1f, 0.95f, 0.45f),
        new Color(0.75f, 1f, 0.6f),
        new Color(0.9f, 0.65f, 1f),
    };

    [Header("Contact Damage")]
    public float contactDamage = 10f;
    public float contactRange = 1.4f;
    public float contactCooldown = 0.8f;

    [Header("References")]
    [SerializeField] private Rigidbody body;
    [SerializeField] private Collider hitCollider;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Animator animator;

    private BonkBattleGameController game;
    private float speed;
    private float wobbleSeed;
    private float aliveTime;
    private bool isDead;
    private float knockbackTime;
    private Vector3 knockbackDir;
    private float spinSpeed;
    private float baseScale = 1.1f;
    private float bobSeed;
    private float contactTimer;
    private float fallVelocityY;
    private const float FallGravity = -20f;

    public bool IsDead => isDead;
    public bool IsActive => !isDead && game != null && game.IsPlaying;

    public void SetReferences(Rigidbody rb, Collider col, Transform visual, Animator anim)
    {
        if (rb != null) body = rb;
        if (col != null) hitCollider = col;
        if (visual != null) visualRoot = visual;
        if (anim != null)
        {
            animator = anim;
            animator.applyRootMotion = false;
            animator.SetBool("isWalking", true);
        }
    }

    public void Initialize(BonkBattleGameController controller, float moveSpeed, float scale, Color color)
    {
        game = controller;
        speed = moveSpeed;
        aliveTime = 0f;
        isDead = false;
        knockbackTime = 0f;
        spinSpeed = 0f;
        wobbleSeed = Random.Range(0f, Mathf.PI * 2f);
        bobSeed = Random.Range(0f, Mathf.PI * 2f);
        baseScale = scale;

        if (visualRoot != null)
            visualRoot.localScale = Vector3.one * scale;
        else
            transform.localScale = Vector3.one * scale;
    }

    private void Update()
    {
        if (isDead)
        {
            transform.position += knockbackDir * (10f * Time.deltaTime);
            transform.position += Vector3.up * (4f * Time.deltaTime);
            transform.Rotate(0f, spinSpeed * Time.deltaTime, spinSpeed * 0.6f * Time.deltaTime);
            if (knockbackTime > 0f) knockbackTime -= Time.deltaTime;
            return;
        }

        if (game == null || !game.IsPlaying) return;

        aliveTime += Time.deltaTime;
        var playerPos = game.PlayerPosition;
        var toPlayer = playerPos - transform.position;
        toPlayer.y = 0f;

        if (toPlayer.sqrMagnitude > 0.05f)
        {
            var dir = toPlayer.normalized;
            var perp = new Vector3(-dir.z, 0f, dir.x);
            var wobble = Mathf.Sin(aliveTime * 7f + wobbleSeed) * 0.35f;
            var move = (dir + perp * wobble).normalized * speed;
            transform.position += move * Time.deltaTime;

            float dist = Mathf.Sqrt(transform.position.x * transform.position.x + transform.position.z * transform.position.z);
            if (dist > game.ArenaRadius + 0.5f)
            {
                fallVelocityY += FallGravity * Time.deltaTime;
                float y = transform.position.y + fallVelocityY * Time.deltaTime;
                transform.position = new Vector3(transform.position.x, y, transform.position.z);
                transform.Rotate(0f, 320f * Time.deltaTime, 0f);
                if (y < -6f)
                {
                    game.ForgetEnemy(this);
                    return;
                }
            }
            else
            {
                var groundY = visualRoot != null ? 0f : 0.6f;
                transform.position = new Vector3(transform.position.x, groundY, transform.position.z);
                if (toPlayer.sqrMagnitude > 0.01f)
                {
                    transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                    transform.Rotate(0f, Mathf.Sin(aliveTime * 10f + wobbleSeed) * 8f * Time.deltaTime, 0f);
                }
            }
        }

        var pulse = 1f + Mathf.Sin(aliveTime * 10f + wobbleSeed) * 0.06f;
        var bob = Mathf.Abs(Mathf.Sin(aliveTime * 12f + bobSeed)) * 0.08f;
        if (visualRoot != null)
        {
            visualRoot.localScale = Vector3.one * baseScale * pulse;
            visualRoot.localPosition = new Vector3(0f, bob, 0f);
        }
        else
        {
            transform.localScale = Vector3.one * baseScale * pulse;
        }
        if (animator != null)
            animator.SetBool("isWalking", true);

        contactTimer -= Time.deltaTime;
        if (contactTimer <= 0f && toPlayer.sqrMagnitude < contactRange * contactRange)
        {
            contactTimer = contactCooldown;
            game.DamagePlayer(contactDamage);
            transform.position += -toPlayer.normalized * 0.4f;
        }
    }

    public void TakeBonk(Vector3 hitDirection)
    {
        if (isDead) return;
        isDead = true;
        knockbackDir = (hitDirection + Vector3.up * 0.55f).normalized;
        spinSpeed = Random.Range(440f, 720f) * (Random.value < 0.5f ? -1f : 1f);
        knockbackTime = 0.35f;

        if (body != null)
        {
            body.AddForce(knockbackDir * 9f, ForceMode.Impulse);
            body.AddTorque(Vector3.up * 10f, ForceMode.Impulse);
        }

        if (hitCollider != null) hitCollider.enabled = false;

        game.ReportEnemyBonked(this, transform.position);
    }

    public void Deactivate()
    {
        isDead = true;
        if (body != null && !body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        if (hitCollider != null) hitCollider.enabled = false;
    }
}
