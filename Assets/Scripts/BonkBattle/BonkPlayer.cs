using UnityEngine;

/// <summary>
/// BonkBattle player. WASD / arrows to move, Space / J / mouse click to bonk.
/// Faces movement direction, or snaps to nearest enemy when bonking.
/// Plays Fuwawa Animator states: Jogging while moving, Throw/Bash attack on bonk.
/// </summary>
public sealed class BonkPlayer : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 7f;
    public float arenaRadius = 12f;

    [Header("Bonk")]
    public float bonkRange = 2.8f;
    public float bonkArcDegrees = 110f;
    public float bonkCooldown = 0.32f;
    [Tooltip("攻撃モーションの再生時間（秒）。Bash/Throwを密度詰めで殴る")]
    public float attackDuration = 0.45f;

    [Header("Health")]
    public float maxHealth = 100f;
    [Tooltip("被ダメ後の無敵時間。長いほどHPがじわじわ減って見える")]
    public float invulnerableSeconds = 1.0f;

    private BonkBattleGameController game;
    private Animator animator;
    private Rigidbody body;
    private float cooldownTimer;
    private bool attackCoroutineRunning;
    private string currentAnimState = "";
    private float currentHealth;
    private float invulnTimer;

    public float MaxHealth => maxHealth;
    public float CurrentHealth => currentHealth;
    public float HealthNormalized => Mathf.Clamp01(currentHealth / maxHealth);

    public bool TakeDamage(float damage)
    {
        if (game == null || !game.IsPlaying) return false;
        if (invulnTimer > 0f) return false;
        currentHealth = Mathf.Max(0f, currentHealth - damage);
        invulnTimer = invulnerableSeconds;
        // 被ダメでよろける（小さいノックバック）
        if (body != null) body.linearVelocity = new Vector3(0f, 0f, 0f);
        return currentHealth <= 0f;
    }

    private void ResetHealth()
    {
        currentHealth = maxHealth;
        invulnTimer = 0f;
    }

    public float CooldownNormalized => Mathf.Clamp01(cooldownTimer / bonkCooldown);
    public bool IsBonkReady => cooldownTimer <= 0f;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        animator = GetComponentInChildren<Animator>();
        currentHealth = maxHealth;
    }

    public void Initialize(BonkBattleGameController controller)
    {
        game = controller;
        EnsureAnimator();
        body = GetComponent<Rigidbody>();
        ResetHealth();
    }

    private void EnsureAnimator()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }
        if (animator == null) return;
        animator.applyRootMotion = false;
        if (!animator.enabled)
        {
            animator.enabled = true;
        }
        if (animator.runtimeAnimatorController == null)
        {
#if UNITY_EDITOR
            var ctl = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/ThirdParty/FuwawaAbyssgard/FuwawaAnimator.controller");
            if (ctl != null) animator.runtimeAnimatorController = ctl;
#endif
        }
    }

    private void Update()
    {
        EnsureAnimator();
        if (game == null || !game.IsPlaying)
        {
            return;
        }

        cooldownTimer -= Time.deltaTime;
        if (invulnTimer > 0f) invulnTimer -= Time.deltaTime;

        var move = ReadMoveInput();
        HandleMovement(move);
        HandleBonkInput();
        UpdateAnimator(move);
    }

    private Vector3 ReadMoveInput()
    {
        var x = 0f;
        var z = 0f;
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null)
        {
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) z -= 1f;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) z += 1f;
        }
#else
        x = Input.GetAxisRaw("Horizontal");
        z = Input.GetAxisRaw("Vertical");
#endif
        var v = new Vector3(x, 0f, z);
        if (v.sqrMagnitude > 1f) v = v.normalized;
        return v;
    }

    [Header("Fall")]
    [Tooltip("床の半径。これを超えると落下します")]
    public float floorRadius = 12f;
    public float fallGravity = -20f;
    public float fallRespawnY = -6f;
    public float fallDamage = 20f;

    private float fallVelocityY;
    private bool isFalling;

    private void HandleMovement(Vector3 move)
    {
        // 落下中は移動入力を無効化して重力で落とす
        if (isFalling)
        {
            fallVelocityY += fallGravity * Time.deltaTime;
            var p = transform.position;
            p.y += fallVelocityY * Time.deltaTime;
            transform.position = p;
            transform.Rotate(0f, 240f * Time.deltaTime, 0f);
            if (p.y < fallRespawnY) RespawnAfterFall();
            return;
        }

        if (move.sqrMagnitude > 0.01f)
        {
            var targetPos = transform.position + move * moveSpeed * Time.deltaTime;
            // ステージの外側へ歩ける（落下あり）。Yは床で0固定
            targetPos.y = 0f;
            transform.position = targetPos;
            transform.rotation = Quaternion.LookRotation(move, Vector3.up);
        }

        // 端を超えたら落下開始
        float dist = Mathf.Sqrt(transform.position.x * transform.position.x + transform.position.z * transform.position.z);
        if (dist > floorRadius + 0.2f)
        {
            isFalling = true;
            fallVelocityY = 0f;
        }
    }

    private void RespawnAfterFall()
    {
        var p = transform.position;
        transform.position = Vector3.zero;
        transform.rotation = Quaternion.identity;
        fallVelocityY = 0f;
        isFalling = false;
        if (game != null) game.DamagePlayer(fallDamage);
    }

    private void HandleBonkInput()
    {
        if (cooldownTimer > 0f) return;

        var pressed = false;
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null)
        {
            if (kb.spaceKey.wasPressedThisFrame || kb.jKey.wasPressedThisFrame || kb.kKey.wasPressedThisFrame)
                pressed = true;
        }
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse != null)
        {
            if (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
                pressed = true;
        }
#else
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.K)
            || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
        {
            pressed = true;
        }
#endif

        if (pressed) TryBonk();
    }

    public bool TryBonk()
    {
        if (cooldownTimer > 0f || game == null || !game.IsPlaying) return false;

        // snap-face nearest enemy in range (arc check relaxed for feel)
        var target = game.FindClosestEnemy(transform.position, bonkRange);
        if (target != null)
        {
            var dir = (target.transform.position - transform.position);
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            game.Bonk(target, transform.position + Vector3.up * 1f + dir.normalized);
        }
        else
        {
            game.BonkMiss(transform.position);
        }

        cooldownTimer = bonkCooldown;
        PlayAttack();
        if (game != null) game.OnPlayerAttack();
        return true;
    }

    private void UpdateAnimator(Vector3 move)
    {
        if (animator == null) return;
        if (attackCoroutineRunning) return;
        var target = move.sqrMagnitude > 0.05f ? "Jogging" : "Idle";
        if (target == currentAnimState) return;
        currentAnimState = target;
        animator.speed = 1f;
        animator.CrossFadeInFixedTime(target, 0.08f, 0, 0f);
    }

    private void PlayAttack()
    {
        if (animator == null) return;
        if (attackCoroutineRunning) return;
        StartCoroutine(AttackRoutine());
    }

    private System.Collections.IEnumerator AttackRoutine()
    {
        attackCoroutineRunning = true;
        // Throw(2.17s)多め + Bash(4.0s)でバリエーション。打撃感重視で短く密度詰め
        bool useThrow = Random.value < 0.7f;
        string state = useThrow ? "Throw" : "Bash";
        float clipLen = useThrow ? 2.17f : 4.0f;
        float speed = clipLen / attackDuration;
        currentAnimState = state;
        animator.speed = speed;
        animator.CrossFadeInFixedTime(state, 0.06f, 0, 0f);
        // ヒットストップ中でもリアル時間で進める
        yield return new WaitForSecondsRealtime(attackDuration);
        animator.speed = 1f;
        attackCoroutineRunning = false;
        currentAnimState = "";
    }
}
