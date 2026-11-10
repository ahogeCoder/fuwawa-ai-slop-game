using UnityEngine;

public sealed class Projectile : MonoBehaviour
{
    private HordeGameController gameController;
    private Renderer cachedRenderer;
    private Vector3 direction;
    private float speed;
    private float damage;
    private float lifetime;
    private bool isActiveProjectile;

    private void Awake()
    {
        cachedRenderer = GetComponent<Renderer>();
    }

    public void Activate(HordeGameController controller, Vector3 origin, Vector3 moveDirection, float moveSpeed, float hitDamage)
    {
        gameController = controller;
        transform.position = origin;
        transform.localScale = Vector3.one * 0.35f;
        if (cachedRenderer != null)
        {
            cachedRenderer.material.color = new Color(1f, 0.95f, 0.35f);
        }

        direction = moveDirection.normalized;
        speed = moveSpeed;
        damage = hitDamage;
        lifetime = 2f;
        isActiveProjectile = true;
    }

    private void Update()
    {
        if (!isActiveProjectile || gameController == null || !gameController.IsPlaying)
        {
            if (isActiveProjectile)
            {
                gameController.ReleaseProjectile(this);
            }

            return;
        }

        var step = speed * Time.deltaTime;
        transform.position += direction * step;
        lifetime -= Time.deltaTime;

        if (lifetime <= 0f)
        {
            gameController.ReleaseProjectile(this);
            return;
        }

        var layerMask = LayerMask.GetMask("Enemy", "Environment");
        if (!Physics.SphereCast(transform.position, 0.25f, direction, out var hit, step, layerMask))
        {
            return;
        }

        if (hit.collider.TryGetComponent<EnemyController>(out var enemy))
        {
            enemy.TakeDamage(damage);
        }
        
        gameController.ReleaseProjectile(this);
    }

    public void Deactivate()
    {
        isActiveProjectile = false;
    }
}
