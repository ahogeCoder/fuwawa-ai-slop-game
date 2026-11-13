using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Orthodox, scene-driven controller.
/// - Scene provides Camera/Lights/World roots/Audio/Canvas UI (wired via serialized refs).
/// - Gameplay entities spawned from prefabs/assets (Instantiate), not constructed from primitives.
/// - No runtime scene creation/finding fallbacks or Shader.Find.
/// </summary>
public sealed class BonkBattleGameController : MonoBehaviour
{
    private enum GameState { Title, Playing, Result }

    [Header("Scene References")]
    [SerializeField] private Camera sceneCamera;
    [SerializeField] private Transform worldRoot;
    [SerializeField] private Transform arenaRoot;
    [SerializeField] private Transform fxRoot;
    [SerializeField] private Transform spawnRoot;
    [SerializeField] private Transform audioRoot;

    [Header("UI")]
    [SerializeField] private GameObject hudRoot;
    [SerializeField] private GameObject titlePanel;
    [SerializeField] private GameObject resultPanel;
    [SerializeField] private Text timerText;
    [SerializeField] private Text countText;
    [SerializeField] private Text comboText;
    [SerializeField] private Text titleBodyText;
    [SerializeField] private Text resultHeadText;
    [SerializeField] private Text resultBodyText;
    [SerializeField] private Text hpText;
    [SerializeField] private Image timerFill;
    [SerializeField] private Image cooldownFill;
    [SerializeField] private Image hpFill;
    [SerializeField] private Image flashOverlay;

    [Header("Audio")]
    [SerializeField] private AudioSource bgmSource;
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioSource hurtSource;
    [SerializeField] private AudioSource attackSource;
    [SerializeField] private AudioClip bgmClip;
    [SerializeField] private AudioClip[] baubauClips;
    [SerializeField] private AudioClip[] hurtClips;
    [SerializeField] private AudioClip[] attackClips;

    [Header("Prefabs / Assets")]
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private GameObject fuwawaVisualPrefab;
    [SerializeField] private RuntimeAnimatorController fuwawaAnimatorController;
    [SerializeField] private GameObject enemyPrefab;
    [SerializeField] private GameObject[] enemyVisualPrefabs;
    [SerializeField] private RuntimeAnimatorController enemyAnimatorController;
    [SerializeField] private GameObject fxBurstPrefab;
    [SerializeField] private GameObject fxShockPrefab;
    [SerializeField] private GameObject fxSlashPrefab;
    [SerializeField] private GameObject popupPrefab;
    [SerializeField] private Material fxBurstMaterial;
    [SerializeField] private Material fxShockMaterial;
    [SerializeField] private Material fxSlashMaterial;

    [Header("Match")]
    public float matchDurationSeconds = 30f;
    public float arenaRadius = 12f;
    public int maxEnemiesAlive = 9;

    [Header("Spawning")]
    public float initialSpawnInterval = 0.85f;
    public float minSpawnInterval = 0.32f;
    public float spawnIntervalDecayPerSecond = 0.008f;
    public float enemyBaseSpeed = 3.1f;
    public float enemySpeedGrowth = 0.35f;

    [Header("FX")]
    public bool enableScreenShake = true;
    public bool enableHitstop = true;
    public float hitstopSeconds = 0.045f;
    public float shakeStrength = 0.28f;

    [Header("Camera Follow")]
    public bool enableCameraFollow = true;
    public Vector3 cameraFollowOffset = new Vector3(0f, 9f, -6.5f);
    public float cameraFollowSmooth = 5f;

    [Header("Result")]
    public float resultInputLockSeconds = 1.2f;

    public bool IsPlaying => state == GameState.Playing;
    public Vector3 PlayerPosition => player != null ? player.transform.position : Vector3.zero;
    public float EnemyBaseScale => 1.1f;
    public float ArenaRadius => arenaRadius;
    public int BonkedCount => bonkedCount;

    private GameState state = GameState.Title;
    private Vector3 cameraBasePos;
    private BonkPlayer player;
    private readonly List<BonkEnemy> enemies = new();

    private float elapsed;
    private float spawnTimer;
    private int bonkedCount;
    private int combo;
    private float comboTimer;
    private int best;
    private float shakeTime;
    private float shakeDuration;
    /// <summary>現在のシェイク量（shakeStrength は基準値として保持し、ここで上書きしない）</summary>
    private float shakeAmount;
    private float hitstopTimer;
    private float flashTimer;
    private float resultLockTimer;
    private int maxCombo;

    private static readonly string[] BauWords = { "BAU!" };

    private void Awake()
    {
        best = PlayerPrefs.GetInt("BonkBattleBest", 0);
        if (sceneCamera != null) cameraBasePos = sceneCamera.transform.position;
        PrepareBgm();
        EnterTitle();
    }

    private void PrepareBgm()
    {
        if (bgmSource != null && bgmClip != null) bgmSource.clip = bgmClip;
    }

    private void StartBgm() { if (bgmSource != null && bgmSource.clip != null && !bgmSource.isPlaying) bgmSource.Play(); }
    private void StopBgm() { if (bgmSource != null && bgmSource.isPlaying) bgmSource.Stop(); }

    private void EnterTitle()
    {
        state = GameState.Title; bonkedCount = 0; combo = 0; maxCombo = 0; elapsed = 0f;
        ResetShake();
        ClearEnemies(); SpawnPlayer();
        ApplyPanelState();
        UpdateHud();
    }

    private void StartRun()
    {
        ResetShake();
        ClearEnemies(); SpawnPlayer();
        state = GameState.Playing; bonkedCount = 0; combo = 0; comboTimer = 0f; maxCombo = 0; elapsed = 0f; spawnTimer = 0.15f;
        ApplyPanelState();
        StartBgm(); UpdateHud();
    }

    /// <summary>セッション切换時にシェイク量とヒットストップを初期化する</summary>
    private void ResetShake()
    {
        shakeTime = 0f;
        shakeAmount = 0f;
        hitstopTimer = 0f;
        Time.timeScale = 1f;
    }

    private void EnterResult(bool isGameOver = false)
    {
        state = GameState.Result; resultLockTimer = resultInputLockSeconds; StopBgm(); ClearEnemies();
        ResetShake();
        if (bonkedCount > best) { best = bonkedCount; PlayerPrefs.SetInt("BonkBattleBest", best); PlayerPrefs.Save(); }
        ApplyPanelState();
        if (resultHeadText != null) { resultHeadText.text = isGameOver ? "GAME OVER" : "TIME UP!"; resultHeadText.color = isGameOver ? new Color(1f, 0.35f, 0.35f) : Color.white; }
        float lasted = Mathf.Max(0f, Mathf.Min(elapsed, matchDurationSeconds));
        if (resultBodyText != null)
        {
            resultBodyText.text = isGameOver
                ? $"{lasted:0.0}秒で死んだ… {bonkedCount} 体 殴った！\nベスト：{best} 体\n最大コンボ： BAU! x{maxCombo}\n\nSPACE / ENTER / クリック：もう一回"
                : $"{lasted:0.0}秒で {bonkedCount} 体 殴った！\nベスト：{best} 体\n最大コンボ： BAU! x{maxCombo}\n\nSPACE / ENTER / クリック：もう一回";
        }
    }

    private void ApplyPanelState()
    {
        if (titlePanel != null) titlePanel.SetActive(state == GameState.Title);
        if (resultPanel != null) resultPanel.SetActive(state == GameState.Result);
        if (hudRoot != null) hudRoot.SetActive(state == GameState.Playing);
    }

    public void ReportPlayerDied() { if (state != GameState.Playing) return; EnterResult(true); }

    public void DamagePlayer(float damage)
    {
        if (state != GameState.Playing || player == null) return;
        float before = player.CurrentHealth; bool died = player.TakeDamage(damage);
        bool applied = player.CurrentHealth < before - 0.01f;
        if (applied) PlayHurt();
        TriggerFlash(0.35f, 0.3f); TriggerShake(0.25f, shakeStrength * 1.4f); UpdateHud();
        if (died) ReportPlayerDied();
    }

    private void PlayHurt()
    {
        if (hurtSource == null || hurtClips == null || hurtClips.Length == 0 || hurtSource.isPlaying) return;
        var clip = hurtClips[Random.Range(0, hurtClips.Length)]; if (clip == null) return;
        hurtSource.pitch = Random.Range(0.85f, 1.05f); hurtSource.PlayOneShot(clip, 1f);
    }

    private void Update()
    {
        TickShakeFlash(); TickCombo(); ApplyPanelState();
        if (state == GameState.Title) { if (GameInput.IsStartPressed()) StartRun(); return; }
        if (state == GameState.Result)
        {
            if (resultLockTimer > 0f) { resultLockTimer -= Time.unscaledDeltaTime; return; }
            if (GameInput.IsRetryPressed()) StartRun(); return;
        }
        elapsed += Time.deltaTime;
        if (elapsed >= matchDurationSeconds) { EnterResult(); return; }
        spawnTimer -= Time.deltaTime;
        if (spawnTimer <= 0f && enemies.Count < maxEnemiesAlive)
        {
            SpawnEnemy();
            spawnTimer = Mathf.Max(minSpawnInterval, initialSpawnInterval - elapsed * spawnIntervalDecayPerSecond);
        }
        for (var i = enemies.Count - 1; i >= 0; i--)
        {
            var e = enemies[i];
            if (e == null || e.IsDead) { enemies.RemoveAt(i); if (e != null) StartCoroutine(DestroyAfter(e.gameObject, 0.4f)); }
        }
        UpdateHud();
    }

    private IEnumerator DestroyAfter(GameObject go, float delay) { yield return new WaitForSeconds(delay); if (go != null) Destroy(go); }

    private Transform SpawnParent => spawnRoot != null ? spawnRoot : worldRoot;

    private void SpawnPlayer()
    {
        if (player != null) { Destroy(player.gameObject); player = null; }
        if (playerPrefab == null) return;
        var go = Instantiate(playerPrefab, SpawnParent);
        go.name = "FuwawaPlayer"; go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.identity; go.transform.localScale = Vector3.one * 1.6f;
        var rb = go.GetComponent<Rigidbody>();
        if (rb != null) { rb.isKinematic = true; rb.useGravity = false; rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; }
        Animator anim = null;
        if (fuwawaVisualPrefab != null)
        {
            var visual = Instantiate(fuwawaVisualPrefab, go.transform);
            visual.name = "FuwawaVisual";
            // Transform は Prefab 側の設定（位置・回転・スケール）をそのまま使う
            // アニメータの取得と設定（Prefab に SkinnedMeshRenderer がアタッチ済みと仮定）
            anim = visual.GetComponentInChildren<Animator>(true);
            if (anim != null)
            {
                anim.applyRootMotion = false; anim.enabled = true; anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                if (fuwawaAnimatorController != null) anim.runtimeAnimatorController = fuwawaAnimatorController;
            }
        }
        var bp = go.GetComponent<BonkPlayer>();
        if (bp == null) bp = go.AddComponent<BonkPlayer>();
        bp.arenaRadius = arenaRadius; bp.floorRadius = arenaRadius;
        bp.SetReferences(anim, rb);
        bp.Initialize(this);
        player = bp;
    }

    private void SpawnEnemy()
    {
        if (player == null || enemyPrefab == null) return;
        var go = Instantiate(enemyPrefab, SpawnParent);
        go.name = "BonkEnemy";
        var angle = Random.Range(0f, Mathf.PI * 2f); var radius = Random.Range(arenaRadius - 1.5f, arenaRadius - 0.2f);
        var pos = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        if (Random.value < 0.4f) { pos = PlayerPosition + new Vector3(Random.Range(-6f, 6f), 0f, Random.Range(-6f, 6f)); pos = Vector3.ClampMagnitude(new Vector3(pos.x, 0f, pos.z), arenaRadius - 1f); pos.y = 0f; }
        go.transform.position = pos;
        Transform visualRoot = null; Animator anim = null;
        if (enemyVisualPrefabs != null && enemyVisualPrefabs.Length > 0)
        {
            var vp = enemyVisualPrefabs[Random.Range(0, enemyVisualPrefabs.Length)];
            if (vp != null)
            {
                var visual = Instantiate(vp, go.transform); visual.name = "Visual"; visual.transform.localPosition = Vector3.zero; visual.transform.localScale = Vector3.one * 1.15f;
                visualRoot = visual.transform;
                anim = visual.GetComponentInChildren<Animator>(true);
                if (anim != null) { anim.applyRootMotion = false; if (enemyAnimatorController != null) anim.runtimeAnimatorController = enemyAnimatorController; anim.SetBool("isWalking", true); }
            }
        }
        var enemy = go.GetComponent<BonkEnemy>();
        if (enemy == null) enemy = go.AddComponent<BonkEnemy>();
        enemy.SetReferences(go.GetComponent<Rigidbody>(), go.GetComponent<Collider>(), visualRoot, anim);
        var progress = Mathf.Clamp01(elapsed / matchDurationSeconds);
        var speed = enemyBaseSpeed + progress * enemySpeedGrowth + Random.Range(-0.3f, 0.5f);
        var scale = Random.Range(3.4f, 4.4f);
        var color = BonkEnemy.EnemyColors[Random.Range(0, BonkEnemy.EnemyColors.Length)];
        enemy.Initialize(this, speed, scale, color); enemies.Add(enemy);
    }

    private void ClearEnemies()
    {
        foreach (var e in enemies) { if (e != null) e.Deactivate(); }
        enemies.Clear();
        if (spawnRoot == null) return;
        for (int i = spawnRoot.childCount - 1; i >= 0; i--)
        {
            var child = spawnRoot.GetChild(i);
            if (child != null && child.name.Contains("BonkEnemy")) Destroy(child.gameObject);
        }
    }

    public BonkEnemy FindClosestEnemy(Vector3 position, float maxDistance)
    {
        BonkEnemy closest = null; var bestSqr = maxDistance * maxDistance;
        foreach (var e in enemies) { if (e == null || e.IsDead) continue; var d = (e.transform.position - position).sqrMagnitude; if (d < bestSqr) { bestSqr = d; closest = e; } }
        return closest;
    }

    public void ForgetEnemy(BonkEnemy enemy)
    {
        if (enemy == null) return;
        enemy.Deactivate(); enemies.Remove(enemy);
        StartCoroutine(DestroyAfter(enemy.gameObject, 0.05f));
    }

    public void Bonk(BonkEnemy enemy, Vector3 hitPosition)
    {
        if (enemy == null || enemy.IsDead) return;
        enemy.TakeBonk((enemy.transform.position - (player != null ? player.transform.position : Vector3.zero)).normalized);
    }

    public void BonkMiss(Vector3 fromPosition) { PlayWhiff(); if (player != null) SpawnFx(fxShockPrefab, fxShockMaterial, fromPosition + Vector3.up * 0.8f + player.transform.forward * 1.2f, 0.22f, 1.4f); }
    public void ReportEnemyBonked(BonkEnemy enemy, Vector3 position) { OnBonked(position); }

    private void OnBonked(Vector3 position)
    {
        bonkedCount++; combo++; maxCombo = Mathf.Max(maxCombo, combo); comboTimer = 2.2f;
        PlayAttackImpact(); SpawnBurst(position); SpawnPopup(position);
        TriggerShake(0.22f, shakeStrength); TriggerHitstop(); TriggerFlash(0.18f, 0.35f); UpdateHud();
    }

    private void PlayAttackImpact()
    {
        if (attackSource == null || attackClips == null || attackClips.Length == 0 || attackSource.isPlaying) return;
        var clip = attackClips[Random.Range(0, attackClips.Length)]; if (clip == null) return;
        attackSource.pitch = Random.Range(0.9f, 1.1f); attackSource.PlayOneShot(clip, 1f);
    }

    public void PlayBau()
    {
        if (sfxSource == null || baubauClips == null || baubauClips.Length == 0) return;
        var clip = baubauClips[Random.Range(0, baubauClips.Length)]; if (clip == null) return;
        sfxSource.pitch = Random.Range(0.94f, 1.18f); sfxSource.PlayOneShot(clip, 1f);
    }

    public void OnPlayerAttack() { PlayBau(); }
    private void PlayWhiff() { }

    private void SpawnBurst(Vector3 position)
    {
        var pos = position + Vector3.up * 0.5f;
        SpawnFx(fxBurstPrefab, fxBurstMaterial, pos, 0.35f, 1.0f);
        SpawnFx(fxShockPrefab, fxShockMaterial, pos, 0.32f, 2.6f);
        if (player != null) SpawnFx(fxSlashPrefab, fxSlashMaterial, position + Vector3.up * 0.7f, 0.14f, 1f, player.transform.rotation * Quaternion.Euler(25f, 0f, 55f), true);
    }

    private void SpawnFx(GameObject prefab, Material mat, Vector3 pos, float duration, float maxScale, Quaternion? rot = null, bool spin = false)
    {
        if (prefab == null || fxRoot == null) return;
        var go = Instantiate(prefab, fxRoot);
        go.transform.position = pos; if (rot.HasValue) go.transform.rotation = rot.Value;
        var rend = go.GetComponentInChildren<Renderer>();
        if (rend != null && mat != null) rend.sharedMaterial = mat;
        if (spin) StartCoroutine(SlashSpin(go, duration)); else StartCoroutine(FxExpandFade(go, rend, duration, maxScale));
    }

    private IEnumerator FxExpandFade(GameObject go, Renderer rend, float duration, float maxScale)
    {
        var t = 0f;
        MaterialPropertyBlock block = null;
        Color baseColor = Color.white;
        int colorId = Shader.PropertyToID("_BaseColor");
        int colorId2 = Shader.PropertyToID("_Color");
        if (rend != null)
        {
            block = new MaterialPropertyBlock();
            var m = rend.sharedMaterial;
            if (m != null) baseColor = m.HasProperty(colorId) ? m.GetColor(colorId) : (m.HasProperty(colorId2) ? m.GetColor(colorId2) : m.color);
        }
        while (t < duration && go != null)
        {
            t += Time.deltaTime; var k = Mathf.Clamp01(t / duration);
            go.transform.localScale = Vector3.one * (0.5f + k * maxScale);
            if (rend != null && block != null)
            {
                var c = baseColor; c.a = baseColor.a * (1f - k);
                rend.GetPropertyBlock(block);
                block.SetColor(colorId, c);
                block.SetColor(colorId2, c);
                rend.SetPropertyBlock(block);
            }
            yield return null;
        }
        if (go != null) Destroy(go);
    }

    private IEnumerator SlashSpin(GameObject go, float duration)
    {
        var t = 0f; while (t < duration && go != null) { t += Time.deltaTime; go.transform.Rotate(0f, 720f * Time.deltaTime, 0f); yield return null; }
        if (go != null) Destroy(go);
    }

    private void SpawnPopup(Vector3 position)
    {
        if (popupPrefab == null || fxRoot == null) return;
        var go = Instantiate(popupPrefab, fxRoot);
        go.transform.position = position + Vector3.up * 1.9f;
        var tm = go.GetComponent<TextMesh>(); if (tm != null) tm.text = BauWords[Random.Range(0, BauWords.Length)];
        StartCoroutine(PopupFloat(go));
    }

    private IEnumerator PopupFloat(GameObject go)
    {
        var tm = go != null ? go.GetComponent<TextMesh>() : null; var t = 0f; var dur = 0.85f; var startY = go != null ? go.transform.position.y : 0f;
        while (t < dur && go != null)
        {
            t += Time.deltaTime; var k = t / dur; go.transform.position = new Vector3(go.transform.position.x, startY + k * 1.4f, go.transform.position.z);
            if (tm != null) { var s = 1f + Mathf.Sin(k * Mathf.PI) * 0.35f; tm.transform.localScale = Vector3.one * s; if (k > 0.6f) { var a = 1f - (k - 0.6f) / 0.4f; tm.color = new Color(1f, 0.95f, 0.3f, a); } }
            yield return null;
        }
        if (go != null) Destroy(go);
    }

    private void TriggerShake(float duration, float strength) { if (!enableScreenShake) return; shakeDuration = duration; shakeTime = duration; shakeAmount = strength; }
    private void TriggerHitstop() { if (!enableHitstop) return; hitstopTimer = hitstopSeconds; Time.timeScale = 0.08f; }
    private void TriggerFlash(float alpha, float duration) { if (flashOverlay == null) return; flashTimer = duration; var c = flashOverlay.color; c.a = alpha; flashOverlay.color = c; }

    private void TickShakeFlash()
    {
        if (hitstopTimer > 0f) { hitstopTimer -= Time.unscaledDeltaTime; if (hitstopTimer <= 0f) Time.timeScale = 1f; }
        TickCameraFollow();
        if (flashTimer > 0f && flashOverlay != null)
        {
            flashTimer -= Time.unscaledDeltaTime; var c = flashOverlay.color; c.a = Mathf.Max(0f, c.a - Time.unscaledDeltaTime * 1.2f); flashOverlay.color = c;
        }
    }

    private void TickCameraFollow()
    {
        if (sceneCamera == null) return;
        Vector3 focus; Vector3 offset = new Vector3(0f, 24f, -16f);
        if (enableCameraFollow && state == GameState.Playing && player != null) { focus = player.transform.position + Vector3.up * 0.5f; offset = cameraFollowOffset; }
        else { focus = new Vector3(0f, 0.3f, 0f); offset = new Vector3(0f, 24f, -16f); }
        var desired = focus + offset; float clampR = arenaRadius + 4f; desired.x = Mathf.Clamp(desired.x, -clampR, clampR); desired.z = Mathf.Clamp(desired.z, -clampR, clampR);
        var smooth = Mathf.Max(0.1f, cameraFollowSmooth); cameraBasePos = Vector3.Lerp(cameraBasePos, desired, Time.unscaledDeltaTime * smooth);
        var shakeOffset = Vector3.zero;
        if (shakeTime > 0f) { shakeTime -= Time.unscaledDeltaTime; var k = shakeDuration > 0f ? Mathf.Max(0f, shakeTime / shakeDuration) : 0f; shakeOffset = Random.insideUnitSphere * (shakeAmount * k); shakeOffset.y = 0f; }
        sceneCamera.transform.position = cameraBasePos + shakeOffset; sceneCamera.transform.LookAt(focus);
    }

    private void TickCombo() { if (combo == 0) return; comboTimer -= Time.deltaTime; if (comboTimer <= 0f) combo = 0; }

    private static void SetBarFill(Image fill, float amount)
    {
        if (fill == null) return;
        float clamped = Mathf.Clamp01(amount);
        var rt = fill.rectTransform; var parent = rt.parent != null ? rt.parent.GetComponent<RectTransform>() : null;
        if (parent == null) { fill.fillAmount = clamped; return; }
        float fullW = Mathf.Max(0f, parent.rect.width - 4f); float fullH = Mathf.Max(0f, parent.rect.height - 4f);
        rt.sizeDelta = new Vector2(fullW * clamped, fullH);
    }

    private void UpdateHud()
    {
        if (timerText != null)
        {
            var remain = Mathf.Max(0f, matchDurationSeconds - elapsed); var urgent = remain <= 10f ? "！" : "";
            timerText.text = $"残り {remain:0.0}s{urgent}"; timerText.color = remain <= 10f ? new Color(1f, 0.25f, 0.3f) : new Color(1f, 0.92f, 0.25f);
        }
        if (countText != null) countText.text = $"殴った: {bonkedCount} 体";
        if (comboText != null) comboText.text = combo >= 2 ? $"BAU! x{combo}" : "";
        SetBarFill(timerFill, 1f - elapsed / matchDurationSeconds);
        if (player != null) SetBarFill(cooldownFill, 1f - player.CooldownNormalized);
        if (player != null) SetBarFill(hpFill, player.CurrentHealth / player.MaxHealth);
        // 「HP」ラベルは別途あるため、ここでは数値のみ表示する（重複表示防止）
        if (hpText != null && player != null) hpText.text = $"{Mathf.CeilToInt(player.CurrentHealth)}";
    }
}


