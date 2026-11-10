using UnityEngine;

public sealed class PlayerController : MonoBehaviour
{
    private HordeGameController gameController;
    private Rigidbody body;
    private float fireCooldown;
    private float moveSpeed;
    private float shotInterval;
    private float projectileSpeed;
    private float projectileDamage;
    private int projectileCount;
    private Animator animator;

    public float CurrentHealth { get; private set; }
    public float MaxHealth { get; private set; }

    public void Initialize(HordeGameController controller)
    {
        gameController = controller;
        body = GetComponent<Rigidbody>();
        animator = GetComponentInChildren<Animator>();
        gameObject.layer = 6; // Player
        MaxHealth = gameController.GetPlayerBaseMaxHealth();
        CurrentHealth = MaxHealth;
        moveSpeed = gameController.GetPlayerBaseMoveSpeed();
        shotInterval = gameController.GetPlayerBaseShotInterval();
        projectileSpeed = gameController.GetPlayerBaseProjectileSpeed();
        projectileDamage = gameController.GetPlayerBaseProjectileDamage();
        projectileCount = 1;
        gameObject.SetActive(true);
    }

    private void Update()
    {
        if (CurrentHealth <= 0f || !gameController.IsPlaying)
        {
            return;
        }

        HandleMovement();
        HandleAutoFire();
    }

    public void TakeDamage(float damage)
    {
        if (CurrentHealth <= 0f)
        {
            return;
        }

        CurrentHealth -= damage;
        transform.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, Mathf.Clamp01(CurrentHealth / MaxHealth));

        if (CurrentHealth <= 0f)
        {
            gameController.ReportPlayerDied();
            gameObject.SetActive(false);
        }
    }

    public void ApplyLevelUp(int level)
    {
        MaxHealth += gameController.GetLevelUpHealthBonus();
        CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + gameController.GetLevelUpHealAmount());
        moveSpeed += gameController.GetLevelUpMoveSpeedBonus();
        projectileDamage += gameController.GetLevelUpProjectileDamageBonus() + level * gameController.GetLevelUpProjectileDamagePerLevel();
        projectileSpeed += gameController.GetLevelUpProjectileSpeedBonus();
        shotInterval = Mathf.Max(gameController.GetPlayerMaxShotInterval(), shotInterval - gameController.GetLevelUpShotIntervalReduction());

        if (level == 4 || level == 8 || level == 12)
        {
            projectileCount++;
        }
    }

    private void HandleMovement()
    {
        var input = GameInput.GetMoveVector();

        var targetVelocity = input * moveSpeed;
        var currentVelocity = body.linearVelocity;
        body.linearVelocity = new Vector3(targetVelocity.x, currentVelocity.y, targetVelocity.z);

        if (input.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.LookRotation(input, Vector3.up);
            if (animator != null) animator.SetBool("isWalking", true);
        }
        else
        {
            if (animator != null) animator.SetBool("isWalking", false);
        }
    }

    private void HandleAutoFire()
    {
        fireCooldown -= Time.deltaTime;
        if (fireCooldown > 0f)
        {
            return;
        }

        var target = gameController.FindClosestEnemy(transform.position, 14f);
        if (target == null)
        {
            return;
        }

        var direction = (target.transform.position - transform.position).normalized;
        direction.y = 0f;

        for (var i = 0; i < projectileCount; i++)
        {
            var spread = (i - (projectileCount - 1) * 0.5f) * 8f;
            var shotDirection = Quaternion.Euler(0f, spread, 0f) * direction;
            FireProjectile(shotDirection);
        }

        gameController.NotifyPlayerShot();

        transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        fireCooldown = shotInterval;
    }

    private void FireProjectile(Vector3 direction)
    {
        var spawnPosition = transform.position + Vector3.up * 0.7f + direction * 1.2f;
        gameController.SpawnProjectile(spawnPosition, direction, projectileSpeed, projectileDamage);
    }

}
