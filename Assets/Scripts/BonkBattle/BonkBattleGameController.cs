using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Orthodox scene-driven controller: binds to scene objects (Camera, Lights, World/Arena/FX roots,
/// Canvas HUD, AudioRoot) and material/prefab assets instead of constructing the whole scene at runtime.
/// Dynamic gameplay spawning (player/enemies/effects) remains, but uses prefab/material references.
/// </summary>
public sealed class BonkBattleGameController : MonoBehaviour
{
    private enum GameState { Title, Playing, Result }

    [Header("Scene References (orthodox)")]
    [System.NonSerialized] public Camera sceneCamera;
    [System.NonSerialized] public Transform worldRootRef;
    [System.NonSerialized] public Transform arenaRootRef;
    [System.NonSerialized] public Transform fxRootRef;
    [System.NonSerialized] public Transform spawnRootRef;
    [System.NonSerialized] public Transform audioRootRef;

    [Header("UI References")]
    [System.NonSerialized] public GameObject hudRoot;
    [System.NonSerialized] public GameObject titlePanel;
    [System.NonSerialized] public GameObject resultPanel;
    [System.NonSerialized] public Text timerText;
    [System.NonSerialized] public Text countText;
    [System.NonSerialized] public Text comboText;
    [System.NonSerialized] public Text titleBodyText;
    [System.NonSerialized] public Text resultHeadText;
    [System.NonSerialized] public Text resultBodyText;
    [System.NonSerialized] public Image timerFill;
    [System.NonSerialized] public Image flashOverlay;
    [System.NonSerialized] public Image cooldownFill;
    [System.NonSerialized] public Image hpFill;
    [System.NonSerialized] public Text hpText;

    [Header("Audio Sources (optional, auto-found/created)")]
    [System.NonSerialized] public AudioSource bgmSource;
    [System.NonSerialized] public AudioSource sfxSource;
    [System.NonSerialized] public AudioSource hurtSource;
    [System.NonSerialized] public AudioSource attackSource;

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

    [Header("Flashy FX")]
    public bool enableScreenShake = true;
    public bool enableHitstop = true;
    public float hitstopSeconds = 0.045f;
    public float shakeStrength = 0.28f;

    [Header("Audio (optional override)")]
    public AudioClip[] baubauClipsOverride;

    [Header("FX Materials (assets preferred)")]
    [System.NonSerialized] public Material shockFxMaterial;
    [System.NonSerialized] public Material slashFxMaterial;
    [System.NonSerialized] public Material burstFxMaterial;

    [Header("Camera Follow")]
    public bool enableCameraFollow = true;
    public Vector3 cameraFollowOffset = new Vector3(0f, 9f, -6.5f);
    public float cameraFollowSmooth = 5f;

    [Header("Enemies (Kenney model)")]
    public bool useKenneyEnemies = true;

    [Header("Stage Style")]
    public bool useKenneyStage = true;

    [Header("Player (optional override)")]
    [System.NonSerialized] public GameObject fuwawaPrefabOverride;

    [Header("Prefabs (optional)")]
    [System.NonSerialized] public GameObject playerPrefab;
    [System.NonSerialized] public GameObject enemyPrefab;

    public bool IsPlaying => state == GameState.Playing;
    public Vector3 PlayerPosition => player != null ? player.transform.position : Vector3.zero;
    public float EnemyBaseScale => 1.1f;
    public float ArenaRadius => arenaRadius;
    public int BonkedCount => bonkedCount;

    private GameState state = GameState.Title;
    private Transform worldRoot;
    private Transform arenaRoot;
    private Transform fxRoot;
    private Transform spawnRoot;
    private Camera mainCamera;
    private Vector3 cameraBasePos;
    private BonkPlayer player;
    private Animator playerAnimator;
    private readonly List<BonkEnemy> enemies = new();
    private readonly List<GameObject> effectObjects = new();
    private readonly List<ParticleSystem> particles = new();

    private float elapsed;
    private float spawnTimer;
    private int bonkedCount;
    private int combo;
    private float comboTimer;
    private int best;
    private float shakeTime;
    private float shakeDuration;
    private float hitstopTimer;
    private float flashTimer;
    public float resultInputLockSeconds = 1.2f;
    private float resultLockTimer;

    private AudioClip[] baubauClips = new AudioClip[0];
    private AudioClip[] hurtClips = new AudioClip[0];
    private AudioClip[] attackClips = new AudioClip[0];
    private AudioClip bgmClip;
    private int MaxCombo { get; set; }

    private static readonly string[] BauWords = { "BAU!" };

    private void Awake()
    {
        best = PlayerPrefs.GetInt("BonkBattleBest", 0);
        LoadBaubauClips();
        BindScene();
        EnsureFxMaterials();
        EnsureAudio();
        EnsureArena();
        EnsureParticles();
        EnterTitle();
    }

    private void BindScene()
    {
        mainCamera = sceneCamera != null ? sceneCamera : Camera.main;
        if (mainCamera == null)
        {
            var camGo = GameObject.Find("Main Camera");
            if (camGo != null) mainCamera = camGo.GetComponent<Camera>();
        }
        if (mainCamera == null)
        {
            var camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera"; mainCamera = camGo.AddComponent<Camera>(); camGo.AddComponent<AudioListener>();
        }
        mainCamera.transform.position = new Vector3(0f, 24f, -16f);
        mainCamera.backgroundColor = new Color(0.07f, 0.09f, 0.14f);
        mainCamera.clearFlags = CameraClearFlags.SolidColor;
        mainCamera.fieldOfView = 62f;
        mainCamera.transform.LookAt(new Vector3(0f, 0.3f, 0f));
        cameraBasePos = mainCamera.transform.position;

        worldRoot = worldRootRef != null ? worldRootRef : (GameObject.Find("WorldRoot")?.transform ?? new GameObject("WorldRoot").transform);
        arenaRoot = arenaRootRef != null ? arenaRootRef : (GameObject.Find("ArenaRoot")?.transform ?? new GameObject("ArenaRoot").transform);
        if (arenaRoot.parent != worldRoot) arenaRoot.SetParent(worldRoot, true);
        fxRoot = fxRootRef != null ? fxRootRef : (GameObject.Find("FXRoot")?.transform ?? new GameObject("FXRoot").transform);
        if (fxRoot.parent != worldRoot) fxRoot.SetParent(worldRoot, true);
        spawnRoot = spawnRootRef != null ? spawnRootRef : (GameObject.Find("SpawnRoot")?.transform ?? new GameObject("SpawnRoot").transform);
        if (spawnRoot.parent != worldRoot) spawnRoot.SetParent(worldRoot, true);

        BindUI();
        EnsureLights();
    }

    private T FindComp<T>(string name) where T : Component
    {
        var go = GameObject.Find(name);
        return go != null ? go.GetComponent<T>() : null;
    }

    private void BindUI()
    {
        if (hudRoot == null) hudRoot = GameObject.Find("HUDRoot");
        if (titlePanel == null) titlePanel = GameObject.Find("TitlePanel");
        if (resultPanel == null) resultPanel = GameObject.Find("ResultPanel");
        if (flashOverlay == null) flashOverlay = FindComp<Image>("Flash");
        if (timerText == null) timerText = FindComp<Text>("Timer");
        if (countText == null) countText = FindComp<Text>("Count");
        if (comboText == null) comboText = FindComp<Text>("Combo");
        if (titleBodyText == null) titleBodyText = FindComp<Text>("TitleBody");
        if (resultHeadText == null) resultHeadText = FindComp<Text>("ResultHead");
        if (resultBodyText == null) resultBodyText = FindComp<Text>("ResultBody");
        if (timerFill == null) timerFill = FindComp<Image>("TimerBarFill");
        if (cooldownFill == null) cooldownFill = FindComp<Image>("CooldownBarFill");
        if (hpFill == null) hpFill = FindComp<Image>("HPBarFill");
        if (hpText == null) hpText = FindComp<Text>("HPText");

        // Fallback: if scene UI missing, create minimal (keeps playable)
        if (hudRoot == null || titlePanel == null || resultPanel == null) BuildHudFallback();
    }

    private void EnsureLights()
    {
        var dl = GameObject.Find("Directional Light");
        if (dl != null) { var l = dl.GetComponent<Light>(); if (l != null) { l.shadows = LightShadows.None; l.type = LightType.Directional; } }
        var al = GameObject.Find("Ambient Light");
        if (al != null) { var l = al.GetComponent<Light>(); if (l != null) { l.shadows = LightShadows.None; l.type = LightType.Directional; } }
        if (dl == null && al == null)
        {
            var lightGo = new GameObject("Directional Light"); var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.35f; light.shadows = LightShadows.None; lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var ambGo = new GameObject("Ambient Light"); var amb = ambGo.AddComponent<Light>(); amb.type = LightType.Directional; amb.intensity = 0.35f; amb.color = new Color(0.5f, 0.6f, 1f); amb.shadows = LightShadows.None; ambGo.transform.rotation = Quaternion.Euler(-30f, 140f, 0f);
        }
    }

    private void EnsureAudio()
    {
        var rootGo = audioRootRef != null ? audioRootRef.gameObject : GameObject.Find("AudioRoot");
        if (rootGo == null) rootGo = new GameObject("AudioRoot");
        audioRootRef = rootGo.transform;
        AudioSource EnsureSrc(ref AudioSource src, string name, bool loop, float vol)
        {
            if (src != null) return src;
            var child = rootGo.transform.Find(name);
            GameObject go = child != null ? child.gameObject : new GameObject(name);
            if (go.transform.parent != rootGo.transform) go.transform.SetParent(rootGo.transform, false);
            src = go.GetComponent<AudioSource>(); if (src == null) src = go.AddComponent<AudioSource>();
            src.playOnAwake = false; src.loop = loop; src.volume = vol; return src;
        }
        EnsureSrc(ref bgmSource, "BGM", true, 0.35f);
        EnsureSrc(ref sfxSource, "SFX", false, 1f);
        EnsureSrc(ref hurtSource, "Hurt", false, 1f);
        EnsureSrc(ref attackSource, "Attack", false, 1f);
        bgmClip = Resources.Load<AudioClip>("BGM/BGM_Battle");
        if (bgmClip == null) bgmClip = Resources.Load<AudioClip>("BGM/BGM_SpacyLoop");
        if (bgmClip != null) bgmSource.clip = bgmClip;
        hurtClips = Resources.LoadAll<AudioClip>("Hurt");
        attackClips = Resources.LoadAll<AudioClip>("Attack");
    }

    private void EnsureFxMaterials()
    {
        if (shockFxMaterial == null) shockFxMaterial = Resources.Load<Material>("Materials/FX_Shock_Unlit");
        if (slashFxMaterial == null) slashFxMaterial = Resources.Load<Material>("Materials/FX_Slash_Unlit");
        if (burstFxMaterial == null) burstFxMaterial = Resources.Load<Material>("Materials/FX_Burst_Unlit");
    }

    private void EnsureArena()
    {
        if (arenaRoot == null) return;
        // Orthodox: if arena built in scene, keep it. Otherwise build legacy fallback.
        if (arenaRoot.childCount > 0) return;
        BuildArenaLegacyFallback();
    }

    private void EnsureParticles()
    {
        if (particles.Count > 0) return;
        var existing = fxRoot != null ? fxRoot.Find("BonkParticles") : null;
        GameObject psGo = existing != null ? existing.gameObject : new GameObject("BonkParticles");
        if (fxRoot != null && psGo.transform.parent != fxRoot) psGo.transform.SetParent(fxRoot, true);
        var ps = psGo.GetComponent<ParticleSystem>(); if (ps == null) ps = psGo.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); ps.Clear(true);
        var main = ps.main; main.playOnAwake = false; main.loop = false; main.duration = 0.6f; main.startLifetime = 0.55f; main.startSpeed = 5.5f; main.startSize = 0.22f; main.gravityModifier = 0.6f; main.maxParticles = 64;
        var emission = ps.emission; emission.rateOverTime = 0f;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.3f;
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.x = new ParticleSystem.MinMaxCurve(-3f, 3f); vel.y = new ParticleSystem.MinMaxCurve(2f, 6f); vel.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
        particles.Add(ps);
    }

    private void LoadBaubauClips()
    {
        if (baubauClipsOverride != null && baubauClipsOverride.Length > 0) { baubauClips = baubauClipsOverride; return; }
        var allBau = new List<AudioClip>();
        var folders = new[] { "BaubauVoice/Cut", "BaubauVoice/FuwawaFull" };
        foreach (var folder in folders)
        {
            var clips = Resources.LoadAll<AudioClip>(folder);
            if (clips == null) continue;
            foreach (var c in clips) { if (c != null && !allBau.Contains(c)) allBau.Add(c); }
        }
        if (allBau.Count > 0) { baubauClips = allBau.ToArray(); return; }
        var all = Resources.LoadAll<AudioClip>("BaubauVoice");
        var shortOnes = new List<AudioClip>();
        foreach (var c in all) { if (c != null && c.length <= 4f) shortOnes.Add(c); }
        baubauClips = shortOnes.Count > 0 ? shortOnes.ToArray() : all;
    }

    private void BuildArenaLegacyFallback()
    {
        Material LoadMat(string n) { var m = Resources.Load<Material>("Materials/" + n); return m; }
        void Prim(string name, PrimitiveType t, Vector3 pos, Vector3 sc, Color c)
        {
            var go = GameObject.CreatePrimitive(t); go.name = name; go.transform.SetParent(arenaRoot, true);
            go.transform.localPosition = pos; go.transform.localScale = sc;
            var r = go.GetComponent<Renderer>(); var mat = r != null ? r.material : null; if (mat != null) { if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c); else mat.color = c; }
            var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
        }
        Prim("ArenaFloor", PrimitiveType.Cylinder, Vector3.zero, new Vector3(arenaRadius * 2f, 0.24f, arenaRadius * 2f), new Color(0.09f, 0.11f, 0.15f));
        Prim("ArenaInner", PrimitiveType.Cylinder, new Vector3(0f, 0.12f, 0f), new Vector3((arenaRadius - 2f) * 2f, 0.06f, (arenaRadius - 2f) * 2f), new Color(0.14f, 0.22f, 0.32f));
        Prim("ArenaCenter", PrimitiveType.Cylinder, new Vector3(0f, 0.16f, 0f), new Vector3(5f, 0.08f, 5f), new Color(0.25f, 0.45f, 0.65f));
        Prim("ArenaRim", PrimitiveType.Cylinder, new Vector3(0f, 0.35f, 0f), new Vector3((arenaRadius + 0.6f) * 2f, 0.32f, (arenaRadius + 0.6f) * 2f), new Color(0.45f, 0.15f, 0.35f));
        for (var i = 0; i < 12; i++)
        {
            var a = i * Mathf.PI * 2f / 12f; var pos = new Vector3(Mathf.Cos(a) * (arenaRadius - 1f), 1f, Mathf.Sin(a) * (arenaRadius - 1f));
            Prim("Pillar", PrimitiveType.Cylinder, pos, new Vector3(0.7f, 1.8f, 0.7f), i % 2 == 0 ? new Color(0.3f, 0.35f, 0.5f) : new Color(0.2f, 0.25f, 0.35f));
            Prim("Beacon", PrimitiveType.Sphere, pos + Vector3.up * 1.6f, Vector3.one * 0.32f, new Color(1f, 0.6f, 0.95f));
        }
    }

    private void BuildHudFallback()
    {
        // minimal fallback if scene canvas missing
        var canvasGo = GameObject.Find("Canvas"); if (canvasGo == null) canvasGo = new GameObject("Canvas");
        var canvas = canvasGo.GetComponent<Canvas>(); if (canvas == null) { canvas = canvasGo.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; var cs = canvasGo.AddComponent<CanvasScaler>(); cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; cs.referenceResolution = new Vector2(1280, 720); canvasGo.AddComponent<GraphicRaycaster>(); }
        if (hudRoot == null) { hudRoot = new GameObject("HUDRoot"); hudRoot.transform.SetParent(canvasGo.transform, false); var rt=hudRoot.AddComponent<RectTransform>(); rt.anchorMin=Vector2.zero; rt.anchorMax=Vector2.one; rt.offsetMin=Vector2.zero; rt.offsetMax=Vector2.zero; }
        if (titlePanel == null) { titlePanel = new GameObject("TitlePanel"); titlePanel.transform.SetParent(canvasGo.transform, false); }
        if (resultPanel == null) { resultPanel = new GameObject("ResultPanel"); resultPanel.transform.SetParent(canvasGo.transform, false); }
    }

    private void StartBgm() { if (bgmSource == null || bgmSource.clip == null) return; if (!bgmSource.isPlaying) bgmSource.Play(); }
    private void StopBgm() { if (bgmSource != null && bgmSource.isPlaying) bgmSource.Stop(); }

    private void EnterTitle()
    {
        state = GameState.Title; bonkedCount = 0; combo = 0; elapsed = 0f;
        ClearEnemies(); SpawnPlayer();
        if (hudRoot != null) hudRoot.SetActive(false);
        if (titlePanel != null) titlePanel.SetActive(true);
        if (resultPanel != null) resultPanel.SetActive(false);
        UpdateHud();
    }

    private void StartRun()
    {
        ClearEnemies(); SpawnPlayer();
        state = GameState.Playing; bonkedCount = 0; combo = 0; comboTimer = 0f; elapsed = 0f; spawnTimer = 0.15f;
        if (hudRoot != null) hudRoot.SetActive(true);
        if (titlePanel != null) titlePanel.SetActive(false);
        if (resultPanel != null) resultPanel.SetActive(false);
        StartBgm(); UpdateHud();
    }

    private void EnterResult(bool isGameOver = false)
    {
        state = GameState.Result; resultLockTimer = resultInputLockSeconds; StopBgm(); ClearEnemies();
        if (bonkedCount > best) { best = bonkedCount; PlayerPrefs.SetInt("BonkBattleBest", best); PlayerPrefs.Save(); }
        if (hudRoot != null) hudRoot.SetActive(false);
        if (titlePanel != null) titlePanel.SetActive(false);
        if (resultPanel != null) resultPanel.SetActive(true);
        if (resultHeadText != null) { resultHeadText.text = isGameOver ? "GAME OVER" : "TIME UP!"; resultHeadText.color = isGameOver ? new Color(1f, 0.35f, 0.35f) : Color.white; }
        float lasted = Mathf.Max(0f, Mathf.Min(elapsed, matchDurationSeconds));
        if (resultBodyText != null)
        {
            resultBodyText.text = isGameOver
                ? $"{lasted:0.0}秒で死んだ… {bonkedCount} 体 殴った！\nベスト：{best} 体\n最大コンボ： BAU! x{MaxCombo}\n\nSPACE / ENTER / クリック：もう一回"
                : $"{lasted:0.0}秒で {bonkedCount} 体 殴った！\nベスト：{best} 体\n最大コンボ： BAU! x{MaxCombo}\n\nSPACE / ENTER / クリック：もう一回";
        }
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
        TickShakeFlash(); TickCombo();
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
            if (e == null || e.IsDead) { enemies.RemoveAt(i); if (e != null && e.gameObject != null) StartCoroutine(DestroyAfter(e.gameObject, 0.4f)); }
        }
        UpdateHud();
    }

    private IEnumerator DestroyAfter(GameObject go, float delay) { yield return new WaitForSeconds(delay); if (go != null) Destroy(go); }

    private void SpawnPlayer()
    {
        if (player != null) { Destroy(player.gameObject); player = null; }
        GameObject go = null;
        if (fuwawaPrefabOverride != null) go = Instantiate(fuwawaPrefabOverride, spawnRoot != null ? spawnRoot : worldRoot);
        else if (playerPrefab != null) go = Instantiate(playerPrefab, spawnRoot != null ? spawnRoot : worldRoot);
        else
        {
            go = LoadFuwawaPrefab(); if (go != null) go = Instantiate(go, spawnRoot != null ? spawnRoot : worldRoot);
        }
        if (go == null) { go = GameObject.CreatePrimitive(PrimitiveType.Capsule); go.transform.SetParent(spawnRoot != null ? spawnRoot : worldRoot, true); var r = go.GetComponent<Renderer>(); if (r!=null){ if(r.material.HasProperty("_BaseColor")) r.material.SetColor("_BaseColor", new Color(0.55f,0.85f,1f)); else r.material.color=new Color(0.55f,0.85f,1f);} }
        go.name = "FuwawaPlayer"; go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.identity; go.transform.localScale = Vector3.one * 1.6f;
        var col = go.GetComponent<Collider>(); if (col == null) go.AddComponent<SphereCollider>();
        var rb = go.GetComponent<Rigidbody>(); if (rb == null) rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true; rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; rb.useGravity = false;
        playerAnimator = go.GetComponentInChildren<Animator>();
        if (playerAnimator == null)
        {
            var armature = FindChildByName(go.transform, "fuwawa_armature");
            if (armature != null) playerAnimator = armature.gameObject.AddComponent<Animator>();
        }
        if (playerAnimator != null)
        {
            playerAnimator.applyRootMotion = false; playerAnimator.enabled = true;
            var ctl = LoadFuwawaAnimator(); if (ctl != null) playerAnimator.runtimeAnimatorController = ctl;
        }
        var bp = go.GetComponent<BonkPlayer>(); if (bp == null) bp = go.AddComponent<BonkPlayer>();
        bp.arenaRadius = arenaRadius; bp.floorRadius = arenaRadius; bp.Initialize(this); player = bp;
    }

    private static Transform FindChildByName(Transform root, string name)
    {
        if (root == null) return null;
        foreach (Transform ch in root) { if (ch.name == name) return ch; var found = FindChildByName(ch, name); if (found != null) return found; }
        return null;
    }

    private RuntimeAnimatorController LoadFuwawaAnimator()
    {
#if UNITY_EDITOR
        var ctl = UnityEditor.AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/ThirdParty/FuwawaAbyssgard/FuwawaAnimator.controller");
        if (ctl != null) return ctl;
#endif
        return Resources.Load<RuntimeAnimatorController>("FuwawaAnimator");
    }

    private GameObject LoadFuwawaPrefab()
    {
#if UNITY_EDITOR
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ThirdParty/FuwawaAbyssgard/fuwawa.fbx");
        if (prefab != null) return prefab;
#endif
        var res = Resources.Load<GameObject>("Fuwawa/fuwawa");
        return res;
    }

    private static readonly string[] KenneyEnemyPaths =
    {
        "Assets/ThirdParty/Kenney/MiniCharacters/character-female-a.glb",
        "Assets/ThirdParty/Kenney/MiniCharacters/character-female-b.glb",
        "Assets/ThirdParty/Kenney/MiniCharacters/character-female-c.glb",
        "Assets/ThirdParty/Kenney/MiniCharacters/character-female-d.glb",
        "Assets/ThirdParty/Kenney/MiniCharacters/character-male-a.glb",
        "Assets/ThirdParty/Kenney/MiniCharacters/character-male-b.glb",
        "Assets/ThirdParty/Kenney/MiniCharacters/character-male-c.glb",
        "Assets/ThirdParty/Kenney/MiniCharacters/character-male-d.glb",
        "Assets/ThirdParty/Kenney/MiniCharacters/character-male-e.glb",
        "Assets/ThirdParty/Kenney/MiniCharacters/character-female-e.glb",
    };

    private void SpawnEnemy()
    {
        if (player == null) return;
        var parent = spawnRoot != null ? spawnRoot : worldRoot;
        var go = new GameObject("BonkEnemy"); go.transform.SetParent(parent, true);
        var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY; rb.useGravity = false; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        var col = go.AddComponent<SphereCollider>(); col.radius = 0.7f; col.center = new Vector3(0f, 0.8f, 0f);
        var angle = Random.Range(0f, Mathf.PI * 2f); var radius = Random.Range(arenaRadius - 1.5f, arenaRadius - 0.2f);
        var pos = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        if (Random.value < 0.4f) { pos = PlayerPosition + new Vector3(Random.Range(-6f, 6f), 0f, Random.Range(-6f, 6f)); pos = Vector3.ClampMagnitude(new Vector3(pos.x, 0f, pos.z), arenaRadius - 1f); pos.y = 0f; }
        go.transform.position = pos;
        bool modeled = false;
        if (useKenneyEnemies)
        {
            for (int tries = 0; tries < KenneyEnemyPaths.Length && !modeled; tries++)
            {
                var path = KenneyEnemyPaths[Random.Range(0, KenneyEnemyPaths.Length)];
                var modelPrefab = LoadKenneyModel(path);
                if (modelPrefab == null) continue;
                var visual = Instantiate(modelPrefab, go.transform); visual.name = "Visual"; visual.transform.localPosition = Vector3.zero;
                var anim = visual.GetComponentInChildren<Animator>(true);
                if (anim != null) { anim.applyRootMotion = false; var ctl = LoadCharacterAnimator(); if (ctl != null) anim.runtimeAnimatorController = ctl; anim.SetBool("isWalking", true); }
                visual.transform.localScale = Vector3.one * 1.15f; modeled = true;
            }
        }
        if (!modeled)
        {
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere); ball.name = "Visual"; ball.transform.SetParent(go.transform, true); ball.transform.localPosition = Vector3.zero; ball.transform.localScale = Vector3.one * 0.9f;
            var bc = ball.GetComponent<Collider>(); if (bc != null) Destroy(bc);
            var ballColor = BonkEnemy.EnemyColors[Random.Range(0, BonkEnemy.EnemyColors.Length)];
            var br = ball.GetComponent<Renderer>(); if (br != null) { if (br.material.HasProperty("_BaseColor")) br.material.SetColor("_BaseColor", ballColor); else br.material.color = ballColor; }
        }
        var enemy = go.GetComponent<BonkEnemy>(); if (enemy == null) enemy = go.AddComponent<BonkEnemy>();
        var progress = Mathf.Clamp01(elapsed / matchDurationSeconds);
        var speed = enemyBaseSpeed + progress * enemySpeedGrowth + Random.Range(-0.3f, 0.5f);
        var scale = Random.Range(3.4f, 4.4f);
        var color = BonkEnemy.EnemyColors[Random.Range(0, BonkEnemy.EnemyColors.Length)];
        enemy.Initialize(this, speed, scale, color); enemies.Add(enemy);
    }

    private GameObject LoadKenneyModel(string path)
    {
#if UNITY_EDITOR
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab != null) return prefab;
#endif
        var rel = System.IO.Path.GetFileNameWithoutExtension(path);
        return Resources.Load<GameObject>("Kenney/" + rel);
    }

    private RuntimeAnimatorController LoadCharacterAnimator()
    {
#if UNITY_EDITOR
        var ctl = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>("Assets/Animators/CharacterAnimator.controller");
        if (ctl != null) return ctl;
#endif
        return Resources.Load<RuntimeAnimatorController>("Animators/CharacterAnimator");
    }

    private void ClearEnemies()
    {
        foreach (var e in enemies) { if (e != null) e.Deactivate(); }
        enemies.Clear();
        var all = FindObjectsOfType<BonkEnemy>();
        foreach (var e in all) Destroy(e.gameObject);
    }

    public BonkEnemy FindClosestEnemy(Vector3 position, float maxDistance)
    {
        BonkEnemy closest = null; var bestSqr = maxDistance * maxDistance;
        foreach (var e in enemies)
        {
            if (e == null || e.IsDead) continue;
            var d = (e.transform.position - position).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; closest = e; }
        }
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

    public void BonkMiss(Vector3 fromPosition) { PlayWhiff(); if (player != null) SpawnRing(fromPosition); }
    public void ReportEnemyBonked(BonkEnemy enemy, Vector3 position) { OnBonked(position); }

    private void OnBonked(Vector3 position)
    {
        bonkedCount++; combo++; MaxCombo = Mathf.Max(MaxCombo, combo); comboTimer = 2.2f;
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

    private Transform FxParent => fxRoot != null ? fxRoot : worldRoot;

    private void SpawnBurst(Vector3 position)
    {
        var ps = particles.Count > 0 ? particles[0] : null;
        if (ps != null) { ps.transform.position = position + Vector3.up * 0.4f; ps.Play(); }
        var burst = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        burst.transform.SetParent(FxParent, true); burst.transform.position = position + Vector3.up * 0.5f; burst.transform.localScale = Vector3.one * 0.5f;
        var burstR = burst.GetComponent<Renderer>();
        if (burstFxMaterial == null) EnsureFxMaterials();
        if (burstFxMaterial != null) burstR.material = new Material(burstFxMaterial);
        var bc = new Color(1f, 0.85f, 0.3f, 1f);
        if (burstR.material.HasProperty("_BaseColor")) burstR.material.SetColor("_BaseColor", bc); else burstR.material.color = bc;
        Destroy(burst.GetComponent<Collider>()); effectObjects.Add(burst); StartCoroutine(EffectFade(burst, 0.35f));
        SpawnShockSphere(position + Vector3.up * 0.5f, 2.6f, 0.32f); SpawnSlashAt(position);
    }

    private void SpawnRing(Vector3 fromPosition)
    {
        Vector3 ahead = fromPosition; if (player != null) ahead = fromPosition + Vector3.up * 0.8f + player.transform.forward * 1.2f;
        SpawnShockSphere(ahead, 1.4f, 0.22f);
    }

    private void SpawnShockSphere(Vector3 position, float maxScale, float duration)
    {
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere); sphere.name = "ShockSphere";
        sphere.transform.SetParent(FxParent, true); sphere.transform.position = position; sphere.transform.localScale = Vector3.one * 0.5f;
        var rend = sphere.GetComponent<Renderer>();
        if (shockFxMaterial == null) EnsureFxMaterials();
        if (shockFxMaterial != null) rend.material = new Material(shockFxMaterial);
        else { var shader = Shader.Find("Universal Render Pipeline/Unlit"); if (shader != null) rend.material.shader = shader; }
        var col = new Color(1f, 0.65f, 0.25f, 0.55f);
        if (rend.material.HasProperty("_BaseColor")) rend.material.SetColor("_BaseColor", col); else rend.material.color = col;
        Destroy(sphere.GetComponent<Collider>()); StartCoroutine(SphereExpandFade(sphere, maxScale, duration));
    }

    private void SpawnSlashAt(Vector3 position)
    {
        var slash = GameObject.CreatePrimitive(PrimitiveType.Cube); slash.name = "Slash";
        slash.transform.SetParent(FxParent, true); slash.transform.position = position + Vector3.up * 0.7f;
        if (player != null) slash.transform.rotation = player.transform.rotation * Quaternion.Euler(25f, 0f, 55f);
        slash.transform.localScale = new Vector3(0.08f, 1.4f, 0.18f);
        var rend = slash.GetComponent<Renderer>();
        if (slashFxMaterial == null) EnsureFxMaterials();
        if (slashFxMaterial != null) rend.material = new Material(slashFxMaterial);
        else { var shader = Shader.Find("Universal Render Pipeline/Unlit"); if (shader != null) rend.material.shader = shader; }
        var col = new Color(1f, 0.98f, 0.55f, 0.9f);
        if (rend.material.HasProperty("_BaseColor")) rend.material.SetColor("_BaseColor", col); else rend.material.color = col;
        Destroy(slash.GetComponent<Collider>()); StartCoroutine(SlashSpin(slash));
    }

    private IEnumerator SphereExpandFade(GameObject sphere, float maxScale, float duration)
    {
        var rend = sphere != null ? sphere.GetComponent<Renderer>() : null; var t = 0f;
        while (t < duration && sphere != null)
        {
            t += Time.deltaTime; var k = t / duration; sphere.transform.localScale = Vector3.one * (0.5f + k * maxScale);
            if (rend != null) { var mat = rend.material; var c = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : mat.color; c.a = 0.55f * (1f - k); if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c); else mat.color = c; }
            yield return null;
        }
        if (sphere != null) Destroy(sphere);
    }

    private IEnumerator SlashSpin(GameObject slash)
    {
        var t = 0f; var dur = 0.14f;
        while (t < dur && slash != null) { t += Time.deltaTime; slash.transform.Rotate(0f, 720f * Time.deltaTime, 0f); yield return null; }
        if (slash != null) Destroy(slash);
    }

    private IEnumerator EffectFade(GameObject go, float duration)
    {
        var r = go != null ? go.GetComponent<Renderer>() : null; var mat = r != null ? r.material : null;
        var start = mat != null ? (mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : mat.color) : Color.white; var t = 0f;
        while (t < duration && go != null)
        {
            t += Time.deltaTime; var k = 1f - t / duration;
            if (mat != null) { var c = new Color(start.r, start.g, start.b, start.a * k); if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c); else mat.color = c; }
            go.transform.localScale = Vector3.one * (0.5f * (0.5f + k)); yield return null;
        }
        if (go != null) Destroy(go);
    }

    private void SpawnPopup(Vector3 position)
    {
        var go = new GameObject("Popup"); go.transform.SetParent(FxParent, true); go.transform.position = position + Vector3.up * 1.9f;
        var tm = go.AddComponent<TextMesh>(); tm.text = BauWords[Random.Range(0, BauWords.Length)]; tm.fontSize = 48; tm.alignment = TextAlignment.Center; tm.anchor = TextAnchor.MiddleCenter; tm.color = new Color(1f, 0.95f, 0.3f);
        StartCoroutine(PopupFloat(go));
    }

    private IEnumerator PopupFloat(GameObject go)
    {
        var tm = go.GetComponent<TextMesh>(); var t = 0f; var dur = 0.85f; var startY = go.transform.position.y;
        while (t < dur && go != null)
        {
            t += Time.deltaTime; var k = t / dur; go.transform.position = new Vector3(go.transform.position.x, startY + k * 1.4f, go.transform.position.z);
            var s = 1f + Mathf.Sin(k * Mathf.PI) * 0.35f; tm.transform.localScale = Vector3.one * s;
            if (k > 0.6f) { var a = 1f - (k - 0.6f) / 0.4f; tm.color = new Color(1f, 0.95f, 0.3f, a); }
            yield return null;
        }
        if (go != null) Destroy(go);
    }

    private void TriggerShake(float duration, float strength) { if (!enableScreenShake) return; shakeDuration = duration; shakeTime = duration; shakeStrength = strength; }
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
        if (mainCamera == null) return;
        Vector3 focus; Vector3 offset = new Vector3(0f, 24f, -16f);
        if (enableCameraFollow && state == GameState.Playing && player != null) { focus = player.transform.position + Vector3.up * 0.5f; offset = cameraFollowOffset; }
        else { focus = new Vector3(0f, 0.3f, 0f); offset = new Vector3(0f, 24f, -16f); }
        var desired = focus + offset; float clampR = arenaRadius + 4f; desired.x = Mathf.Clamp(desired.x, -clampR, clampR); desired.z = Mathf.Clamp(desired.z, -clampR, clampR);
        var smooth = Mathf.Max(0.1f, cameraFollowSmooth); cameraBasePos = Vector3.Lerp(cameraBasePos, desired, Time.unscaledDeltaTime * smooth);
        var shakeOffset = Vector3.zero;
        if (shakeTime > 0f) { shakeTime -= Time.unscaledDeltaTime; var k = Mathf.Max(0f, shakeTime / shakeDuration); shakeOffset = Random.insideUnitSphere * (shakeStrength * k); shakeOffset.y = 0f; }
        mainCamera.transform.position = cameraBasePos + shakeOffset; mainCamera.transform.LookAt(focus);
    }

    private void TickCombo() { if (combo == 0) return; comboTimer -= Time.deltaTime; if (comboTimer <= 0f) combo = 0; }

    private static void SetBarFill(Image fill, float amount)
    {
        if (fill == null) return;
        float clamped = Mathf.Clamp01(amount); fill.fillAmount = clamped;
        var rt = fill.rectTransform; var parent = rt.parent != null ? rt.parent.GetComponent<RectTransform>() : null;
        if (parent == null) return; float fullW = Mathf.Max(0f, parent.rect.width - 4f); float fullH = Mathf.Max(0f, parent.rect.height - 4f);
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
        if (hpText != null && player != null) hpText.text = $"HP {Mathf.CeilToInt(player.CurrentHealth)}";
    }
}
