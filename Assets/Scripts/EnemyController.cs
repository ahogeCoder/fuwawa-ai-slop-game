using UnityEngine;

public sealed class EnemyController : MonoBehaviour
{
    private HordeGameController gameController;
    private Rigidbody body;
    private Renderer cachedRenderer;
    private Collider cachedCollider;
    private float speed;
    private float health;
    private float maxHealth;
    private float contactDamage;
    private float attackCooldown;
    private int scoreReward;
    private float expReward;
    private bool isActiveEnemy;
    private Animator animator;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        cachedRenderer = GetComponent<Renderer>();
        cachedCollider = GetComponent<Collider>();
        animator = GetComponentInChildren<Animator>();
        gameObject.layer = 7; // Enemy
    }

    public void SetController(HordeGameController controller)
    {
        gameController = controller;
    }

    public void Activate(
        Vector3 position,
        float startingHealth,
        float moveSpeed,
        float damage,
        int rewardScore,
        float rewardExp,
        float scale,
        Color color)
    {
        transform.position = position;
        transform.localScale = Vector3.one * scale;
        if (cachedRenderer != null && cachedRenderer.gameObject == gameObject)
        {
            cachedRenderer.material.color = color;
        }

        health = startingHealth;
        maxHealth = startingHealth;
        speed = moveSpeed;
        contactDamage = damage;
        scoreReward = rewardScore;
        expReward = rewardExp;
        attackCooldown = 0f;
        isActiveEnemy = true;

        if (cachedCollider != null)
        {
            cachedCollider.enabled = true;
        }

        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }

    private void Update()
    {
        if (!isActiveEnemy || !gameController.IsPlaying)
        {
            body.linearVelocity = Vector3.zero;
            return;
        }

        var player = gameController.PlayerTransform;
        if (player == null)
        {
            body.linearVelocity = Vector3.zero;
            return;
        }

        var direction = player.position - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude > 0.1f)
        {
            var move = direction.normalized * speed;
            body.linearVelocity = new Vector3(move.x, body.linearVelocity.y, move.z);
            transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            if (animator != null) animator.SetBool("isWalking", true);
        }
        else
        {
            if (animator != null) animator.SetBool("isWalking", false);
        }

        attackCooldown -= Time.deltaTime;
    }

    public void TakeDamage(float damage)
    {
        if (!isActiveEnemy)
        {
            return;
        }

        health -= damage;

        var normalized = Mathf.Clamp01(health / maxHealth);
        var scale = transform.localScale.magnitude / Mathf.Sqrt(3f);
        transform.localScale = Vector3.one * Mathf.Max(scale * 0.994f, 0.55f + normalized * 0.85f);

        if (health > 0f)
        {
            return;
        }

        isActiveEnemy = false;
        if (cachedCollider != null)
        {
            cachedCollider.enabled = false;
        }

        gameController.ReportEnemyKilled(this, transform.position, scoreReward, expReward);
    }

    private void OnCollisionStay(Collision collision)
    {
        if (attackCooldown > 0f)
        {
            return;
        }

        if (collision.gameObject.TryGetComponent<PlayerController>(out var player))
        {
            player.TakeDamage(contactDamage);
            attackCooldown = 0.6f;
        }
    }

    public void Deactivate()
    {
        isActiveEnemy = false;
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        if (cachedCollider != null)
        {
            cachedCollider.enabled = false;
        }
    }
}
