using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// BonkBattle: 60-second counter game. Fuwawa bonks enemies flashily.
/// WASD/arrows to move, Space/J/mouse to bonk. Each bonk plays a random
/// FuwaMoco "bau bau" voice (Resources/BaubauVoice, from fuwa-moco-trivia.netlify.app).
///
/// Setup: add this component to any GameObject in a scene. It builds the arena,
/// camera, light, HUD, player (Fuwawa model) and enemies at runtime.
/// No scene editing required.
/// </summary>
public sealed class BonkBattleGameController : MonoBehaviour
{
    private enum GameState { Title, Playing, Result }

    [Header("Match")]
    [Tooltip("制限時間（秒）。デフォルト30秒")]
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
    [Tooltip("Inspector override. Empty = Resources/BaubauVoice/Single の一発短尺を使います")]
    public AudioClip[] baubauClipsOverride;

    [Header("Camera Follow")]
    public bool enableCameraFollow = true;
    public Vector3 cameraFollowOffset = new Vector3(0f, 9f, -6.5f);
    [Tooltip("追従の滑らかさ（値が高いほど速く追う）")]
    public float cameraFollowSmooth = 5f;

    [Header("Enemies (Kenney model)")]
    [Tooltip("ON=Kenneyキャラクター+歩行アニメーションを使う。OFF=色球で代用")]
    public bool useKenneyEnemies = true;

    [Header("Stage Style")]
    [Tooltip("ON=Kenney草原アリーナ（草タイル＋木・岩）。OFF=紫円盤アリーナ")]
    public bool useKenneyStage = true;

    [Header("Player (optional override)")]
    [Tooltip("空 = ThirdParty/FuwawaAbyssgard/fuwawa.fbx を自動ロード（なければカプセル）")]
    public GameObject fuwawaPrefabOverride;

    public bool IsPlaying => state == GameState.Playing;
    public Vector3 PlayerPosition => player != null ? player.transform.position : Vector3.zero;
    public float EnemyBaseScale => 1.1f;
    public float ArenaRadius => arenaRadius;
    public int BonkedCount => bonkedCount;

    private GameState state = GameState.Title;
    private Transform worldRoot;
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
    [Tooltip("結果画面が終わった瞬間の誤リスタートを防ぐ待機時間（秒）")]
    public float resultInputLockSeconds = 1.2f;
    private float resultLockTimer;

    private AudioSource sfxSource;
    private AudioSource hurtSource;
    private AudioSource attackSource;
    private AudioSource bgmSource;
    private AudioClip[] baubauClips = new AudioClip[0];
    private AudioClip[] hurtClips = new AudioClip[0];
    private AudioClip[] attackClips = new AudioClip[0];
    private AudioClip bgmClip;

    // HUD
    private GameObject hudRoot;
    private GameObject titlePanel;
    private GameObject resultPanel;
    private Text timerText;
    private Text countText;
    private Text comboText;
    private Text titleBodyText;
    private Text resultHeadText;
    private Text resultBodyText;
    private Image timerFill;
    private Image flashOverlay;
    private Image cooldownFill;
    private Image hpFill;
    private Text hpText;

    private static readonly string[] BauWords = { "BAU!" };

    private void Awake()
    {
        best = PlayerPrefs.GetInt("BonkBattleBest", 0);
        LoadBaubauClips();
        BuildScene();
        BuildAudio();
        BuildHud();
        EnterTitle();
    }

    private void LoadBaubauClips()
    {
        if (baubauClipsOverride != null && baubauClipsOverride.Length > 0)
        {
            baubauClips = baubauClipsOverride;
            return;
        }
        // フワワbaubau: 長尺を切り刻んだ短片(Cut) + 短尺フル(FuwawaFull)
        var allBau = new System.Collections.Generic.List<AudioClip>();
        var folders = new[] { "BaubauVoice/Cut", "BaubauVoice/FuwawaFull" };
        foreach (var folder in folders)
        {
            var clips = Resources.LoadAll<AudioClip>(folder);
            if (clips == null) continue;
            foreach (var c in clips)
            {
                if (c != null && !allBau.Contains(c)) allBau.Add(c);
            }
        }
        if (allBau.Count > 0)
        {
            baubauClips = allBau.ToArray();
            return;
        }
        var all = Resources.LoadAll<AudioClip>("BaubauVoice");
        // フォールバック: 短尺のみ（29s Worlds_Longest_Bau等は重なりすぎるため除外）
        var shortOnes = new System.Collections.Generic.List<AudioClip>();
        foreach (var c in all)
        {
            if (c != null && c.length <= 4f) shortOnes.Add(c);
        }
        baubauClips = shortOnes.Count > 0 ? shortOnes.ToArray() : all;
    }

    // ---------- Scene build ----------

    private void BuildScene()
    {
        worldRoot = new GameObject("WorldRoot").transform;
        worldRoot.SetParent(transform, false);

        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            mainCamera = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
        }
        mainCamera.transform.position = new Vector3(0f, 24f, -16f);
        mainCamera.backgroundColor = new Color(0.07f, 0.09f, 0.14f);
        mainCamera.clearFlags = CameraClearFlags.SolidColor;
        mainCamera.fieldOfView = 62f;
        mainCamera.transform.LookAt(new Vector3(0f, 0.3f, 0f));
        cameraBasePos = mainCamera.transform.position;

        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.35f;
        light.shadows = LightShadows.None; // WebGL: CoreCopy/Shadow 関連エラー回避
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var ambGo = new GameObject("Ambient");
        var amb = ambGo.AddComponent<Light>();
        amb.type = LightType.Directional;
        amb.intensity = 0.35f;
        amb.color = new Color(0.5f, 0.6f, 1f);
        amb.shadows = LightShadows.None;
        ambGo.transform.rotation = Quaternion.Euler(-30f, 140f, 0f);

        BuildArena();
    }

    private void BuildArena()
    {
        if (useKenneyStage && BuildArenaKenney())
        {
            BuildArenaParticles();
            return;
        }
        BuildArenaLegacy();
        BuildArenaParticles();
    }

    private void BuildArenaLegacy()
    {
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        floor.name = "ArenaFloor";
        floor.transform.SetParent(worldRoot, true);
        floor.transform.localPosition = Vector3.zero;
        floor.transform.localScale = new Vector3(arenaRadius * 2f, 0.24f, arenaRadius * 2f);
        floor.GetComponent<Renderer>().material.color = new Color(0.09f, 0.11f, 0.15f);
        Destroy(floor.GetComponent<Collider>());

        var inner = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        inner.name = "ArenaInner";
        inner.transform.SetParent(worldRoot, true);
        inner.transform.localPosition = new Vector3(0f, 0.12f, 0f);
        inner.transform.localScale = new Vector3((arenaRadius - 2f) * 2f, 0.06f, (arenaRadius - 2f) * 2f);
        inner.GetComponent<Renderer>().material.color = new Color(0.14f, 0.22f, 0.32f);
        Destroy(inner.GetComponent<Collider>());

        var center = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        center.name = "ArenaCenter";
        center.transform.SetParent(worldRoot, true);
        center.transform.localPosition = new Vector3(0f, 0.16f, 0f);
        center.transform.localScale = new Vector3(5f, 0.08f, 5f);
        center.GetComponent<Renderer>().material.color = new Color(0.25f, 0.45f, 0.65f);
        Destroy(center.GetComponent<Collider>());

        var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        rim.name = "ArenaRim";
        rim.transform.SetParent(worldRoot, true);
        rim.transform.localPosition = new Vector3(0f, 0.35f, 0f);
        rim.transform.localScale = new Vector3((arenaRadius + 0.6f) * 2f, 0.32f, (arenaRadius + 0.6f) * 2f);
        rim.GetComponent<Renderer>().material.color = new Color(0.45f, 0.15f, 0.35f);
        Destroy(rim.GetComponent<Collider>());

        for (var i = 0; i < 12; i++)
        {
            var a = i * Mathf.PI * 2f / 12f;
            var pos = new Vector3(Mathf.Cos(a) * (arenaRadius - 1f), 1f, Mathf.Sin(a) * (arenaRadius - 1f));
            var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pillar.name = "Pillar";
            pillar.transform.SetParent(worldRoot, true);
            pillar.transform.localPosition = pos;
            pillar.transform.localScale = new Vector3(0.7f, 1.8f, 0.7f);
            pillar.GetComponent<Renderer>().material.color = i % 2 == 0
                ? new Color(0.3f, 0.35f, 0.5f)
                : new Color(0.2f, 0.25f, 0.35f);
            Destroy(pillar.GetComponent<Collider>());

            var beacon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            beacon.name = "Beacon";
            beacon.transform.SetParent(worldRoot, true);
            beacon.transform.localPosition = pos + Vector3.up * 1.6f;
            beacon.transform.localScale = Vector3.one * 0.32f;
            beacon.GetComponent<Renderer>().material.color = new Color(1f, 0.6f, 0.95f);
            Destroy(beacon.GetComponent<Collider>());
        }
    }

    private bool BuildArenaKenney()
    {
        var grass = LoadKenneyModel("Assets/ThirdParty/Kenney/MiniForest/patch-grass.glb");
        var dirt = LoadKenneyModel("Assets/ThirdParty/Kenney/MiniForest/patch-dirt.glb");
        var treeHigh = LoadKenneyModel("Assets/ThirdParty/Kenney/MiniForest/tree-high.glb");
        var tree = LoadKenneyModel("Assets/ThirdParty/Kenney/MiniForest/tree.glb");
        var rockHigh = LoadKenneyModel("Assets/ThirdParty/Kenney/MiniForest/rocks-high.glb");
        var rockLow = LoadKenneyModel("Assets/ThirdParty/Kenney/MiniForest/rocks-low.glb");
        if (grass == null) return false;

        // 隙間防止の下地
        var baseFloor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        baseFloor.name = "ArenaBase";
        baseFloor.transform.SetParent(worldRoot, true);
        baseFloor.transform.localPosition = new Vector3(0f, -0.12f, 0f);
        baseFloor.transform.localScale = new Vector3(arenaRadius * 2f, 0.24f, arenaRadius * 2f);
        baseFloor.GetComponent<Renderer>().material.color = new Color(0.25f, 0.45f, 0.25f);
        Destroy(baseFloor.GetComponent<Collider>());

        // 草タイル（2m刻み）。落下は半径 arenaRadius で判定
        const float step = 2f;
        int extent = Mathf.CeilToInt(arenaRadius / step) + 1;
        for (int gx = -extent; gx <= extent; gx++)
        {
            for (int gz = -extent; gz <= extent; gz++)
            {
                Vector3 pos = new Vector3(gx * step, 0f, gz * step);
                if (new Vector2(pos.x, pos.z).magnitude > arenaRadius) continue;
                var tile = (GameObject)Instantiate(grass, worldRoot);
                tile.name = "ArenaTile";
                tile.transform.position = pos;
                tile.transform.rotation = Quaternion.identity;
                tile.transform.localScale = Vector3.one * 2f;
            }
        }
        if (dirt != null)
        {
            var c = (GameObject)Instantiate(dirt, worldRoot);
            c.name = "ArenaCenter";
            c.transform.position = Vector3.zero;
            c.transform.localScale = Vector3.one * 3f;
        }

        // 周囲の木・岩
        var place = new System.Action<GameObject, Vector3, float>((prefab, pos, s) =>
        {
            if (prefab == null) return;
            var inst = (GameObject)Instantiate(prefab, worldRoot);
            inst.name = prefab.name;
            inst.transform.position = pos;
            inst.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            inst.transform.localScale = Vector3.one * s;
        });
        for (int i = 0; i < 12; i++)
        {
            float a = i * Mathf.PI * 2f / 12f;
            Vector3 pos = new Vector3(Mathf.Cos(a) * (arenaRadius - 1.2f), 0f, Mathf.Sin(a) * (arenaRadius - 1.2f));
            place(i % 2 == 0 ? treeHigh : tree, pos, Random.Range(1.4f, 2.0f));
        }
        for (int i = 0; i < 6; i++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            float r = Random.Range(arenaRadius - 4f, arenaRadius - 2f);
            Vector3 pos = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            place(i % 2 == 0 ? rockHigh : rockLow, pos, Random.Range(1.2f, 1.8f));
        }
        return true;
    }

    private void BuildArenaParticles()
    {
        // particle system template for bonk bursts
        var psGo = new GameObject("BonkParticles");
        psGo.transform.SetParent(worldRoot, true);
        var ps = psGo.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.Clear(true);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.6f;
        main.startLifetime = 0.55f;
        main.startSpeed = 5.5f;
        main.startSize = 0.22f;
        main.gravityModifier = 0.6f;
        main.maxParticles = 64;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.3f;
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.x = new ParticleSystem.MinMaxCurve(-3f, 3f);
        vel.y = new ParticleSystem.MinMaxCurve(2f, 6f);
        vel.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
        particles.Add(ps);
    }

    private void BuildAudio()
    {
        var root = new GameObject("AudioRoot");
        root.transform.SetParent(transform, false);
        bgmSource = root.AddComponent<AudioSource>();
        bgmSource.loop = true;
        bgmSource.playOnAwake = false;
        bgmSource.volume = 0.35f;
        // メインはバトル用 Monkeys Spinning Monkeys(Kevin MacLeod, CC BY)。無ければ SpacyLoop に退避
        bgmClip = Resources.Load<AudioClip>("BGM/BGM_Battle");
        if (bgmClip == null) bgmClip = Resources.Load<AudioClip>("BGM/BGM_SpacyLoop");
        if (bgmClip != null) bgmSource.clip = bgmClip;

        sfxSource = root.AddComponent<AudioSource>();
        sfxSource.loop = false;
        sfxSource.playOnAwake = false;
        sfxSource.volume = 1f;

        hurtSource = root.AddComponent<AudioSource>();
        hurtSource.loop = false;
        hurtSource.playOnAwake = false;
        hurtSource.volume = 1f;
        hurtClips = Resources.LoadAll<AudioClip>("Hurt");

        attackSource = root.AddComponent<AudioSource>();
        attackSource.loop = false;
        attackSource.playOnAwake = false;
        attackSource.volume = 1f;
        attackClips = Resources.LoadAll<AudioClip>("Attack");

        // rescue: if Resources empty, try loading from Assets via direct path (editor only hint)
        if (baubauClips == null || baubauClips.Length == 0)
        {
            Debug.LogWarning("[BonkBattle] Resources/BaubauVoice が空です。mp3 を Assets/Resources/BaubauVoice に置いてください。");
        }
    }

    private void StartBgm()
    {
        if (bgmSource == null || bgmSource.clip == null) return;
        if (!bgmSource.isPlaying) bgmSource.Play();
    }

    private void StopBgm()
    {
        if (bgmSource != null && bgmSource.isPlaying) bgmSource.Stop();
    }

    // ---------- HUD ----------

    private void BuildHud()
    {
        var canvasGo = new GameObject("HUD");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var canvasScaler = canvasGo.AddComponent<CanvasScaler>();
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = new Vector2(1280f, 720f);
        canvasScaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        // flash overlay (full-screen, transparent)
        var flashGo = new GameObject("Flash");
        flashGo.transform.SetParent(canvasGo.transform, false);
        var flashRect = flashGo.AddComponent<RectTransform>();
        flashRect.anchorMin = Vector2.zero;
        flashRect.anchorMax = Vector2.one;
        flashRect.offsetMin = Vector2.zero;
        flashRect.offsetMax = Vector2.zero;
        flashOverlay = flashGo.AddComponent<Image>();
        flashOverlay.color = new Color(1f, 1f, 1f, 0f);
        flashOverlay.raycastTarget = false;

        hudRoot = new GameObject("HUDRoot");
        hudRoot.transform.SetParent(canvasGo.transform, false);
        var hudRect = hudRoot.AddComponent<RectTransform>();
        hudRect.anchorMin = Vector2.zero;
        hudRect.anchorMax = Vector2.one;
        hudRect.offsetMin = Vector2.zero;
        hudRect.offsetMax = Vector2.zero;

        timerText = CreateText("Timer", hudRoot.transform, font, new Vector2(0f, -18f), 44, TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.25f));
        timerText.rectTransform.sizeDelta = new Vector2(400f, 70f);
        countText = CreateText("Count", hudRoot.transform, font, new Vector2(-260f, -18f), 34, TextAnchor.MiddleCenter, Color.white);
        countText.rectTransform.sizeDelta = new Vector2(380f, 70f);
        comboText = CreateText("Combo", hudRoot.transform, font, new Vector2(260f, -18f), 34, TextAnchor.MiddleCenter, new Color(1f, 0.6f, 0.85f));
        comboText.rectTransform.sizeDelta = new Vector2(380f, 70f);

        timerFill = CreateBar("TimerBar", hudRoot.transform, new Vector2(0f, -70f), new Color(1f, 0.45f, 0.55f), 520f, 16f);
        CreateBarLabel("TimerLabel", hudRoot.transform, font, new Vector2(-370f, -65f), "TIME", new Color(1f, 0.55f, 0.6f));
        cooldownFill = CreateBar("CooldownBar", hudRoot.transform, new Vector2(0f, -95f), new Color(0.55f, 0.95f, 1f), 520f, 10f);
        CreateBarLabel("CooldownLabel", hudRoot.transform, font, new Vector2(-370f, -87f), "ATTACK", new Color(0.6f, 0.95f, 1f));
        hpFill = CreateBar("HPBar", hudRoot.transform, new Vector2(0f, -120f), new Color(0.35f, 0.95f, 0.45f), 520f, 20f);
        CreateBarLabel("HPLabel", hudRoot.transform, font, new Vector2(-370f, -117f), "HP", new Color(0.5f, 1f, 0.6f));
        hpText = CreateText("HPText", hudRoot.transform, font, new Vector2(310f, -120f), 24, TextAnchor.MiddleLeft, new Color(0.6f, 1f, 0.7f));
        hpText.rectTransform.sizeDelta = new Vector2(180f, 30f);

        titlePanel = CreatePanel("TitlePanel", canvasGo.transform, new Color(0f, 0f, 0f, 0.45f));
        var titleHead = CreatePanelText("TitleHead", titlePanel.transform, font, new Vector2(0f, 150f), 54, TextAnchor.MiddleCenter, new Color(1f, 0.55f, 0.85f));
        titleHead.text = "BAUBAU BONK BATTLE";
        titleHead.rectTransform.sizeDelta = new Vector2(900f, 90f);
        titleBodyText = CreatePanelText("TitleBody", titlePanel.transform, font, new Vector2(0f, -60f), 24, TextAnchor.MiddleCenter, Color.white);
        titleBodyText.rectTransform.sizeDelta = new Vector2(900f, 200f);
        titleBodyText.text = "フワワが30秒で何匹殴れるか！\nWASD / 矢印キー：移動　Space / J / クリック：殴る（BAU!）\n敵に触れるとダメージ！\n\nSPACE / ENTER / クリック：スタート";

        resultPanel = CreatePanel("ResultPanel", canvasGo.transform, new Color(0f, 0f, 0f, 0.55f));
        resultHeadText = CreatePanelText("ResultHead", resultPanel.transform, font, new Vector2(0f, 130f), 54, TextAnchor.MiddleCenter, Color.white);
        resultHeadText.rectTransform.sizeDelta = new Vector2(900f, 90f);
        resultHeadText.text = "TIME UP!";
        resultBodyText = CreatePanelText("ResultBody", resultPanel.transform, font, new Vector2(0f, -20f), 34, TextAnchor.MiddleCenter, new Color(0.95f, 0.95f, 0.95f));
    }

    private Text CreateText(string objName, Transform parent, Font font, Vector2 anchoredPos, int fontSize, TextAnchor anchor, Color color)
    {
        var go = new GameObject(objName);
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = anchor == TextAnchor.MiddleCenter ? new Vector2(0.5f, 1f) : anchor == TextAnchor.MiddleLeft ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
        rect.anchorMax = rect.anchorMin;
        rect.pivot = anchor == TextAnchor.MiddleCenter ? new Vector2(0.5f, 1f) : anchor == TextAnchor.MiddleLeft ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = new Vector2(500f, 80f);
        var t = go.AddComponent<Text>();
        t.font = font;
        t.fontSize = fontSize;
        t.alignment = anchor;
        t.color = color;
        return t;
    }

    private Image CreateBar(string objName, Transform parent, Vector2 anchorOffset, Color fillColor, float width, float height)
    {
        var root = new GameObject(objName);
        root.transform.SetParent(parent, false);
        var rootRect = root.AddComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0.5f, 1f);
        rootRect.anchorMax = new Vector2(0.5f, 1f);
        rootRect.pivot = new Vector2(0.5f, 1f);
        rootRect.anchoredPosition = anchorOffset;
        rootRect.sizeDelta = new Vector2(width, height);
        var bg = root.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.5f);

        var fillGo = new GameObject(objName + "Fill");
        fillGo.transform.SetParent(root.transform, false);
        var fillRect = fillGo.AddComponent<RectTransform>();
        // 左寄せアンカーで横幅を直接決める。Filledの描画不具合にも依存しない最も確実な方法
        fillRect.anchorMin = new Vector2(0f, 0.5f);
        fillRect.anchorMax = new Vector2(0f, 0.5f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.anchoredPosition = new Vector2(2f, 0f);
        fillRect.sizeDelta = new Vector2(width - 4f, height - 4f);
        var fill = fillGo.AddComponent<Image>();
        fill.type = Image.Type.Simple;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = 0;
        fill.fillAmount = 1f;
        fill.color = fillColor;
        return fill;
    }

    private Text CreateBarLabel(string objName, Transform parent, Font font, Vector2 anchoredPos, string label, Color color)
    {
        var t = CreateText(objName, parent, font, anchoredPos, 20, TextAnchor.MiddleCenter, color);
        t.text = label;
        t.rectTransform.sizeDelta = new Vector2(190f, 26f);
        return t;
    }

    private static void SetBarFill(Image fill, float amount)
    {
        if (fill == null) return;
        float clamped = Mathf.Clamp01(amount);
        fill.fillAmount = clamped;
        var rt = fill.rectTransform;
        var parent = rt.parent != null ? rt.parent.GetComponent<RectTransform>() : null;
        if (parent == null) return;
        float fullW = Mathf.Max(0f, parent.rect.width - 4f);
        float fullH = Mathf.Max(0f, parent.rect.height - 4f);
        rt.sizeDelta = new Vector2(fullW * clamped, fullH);
    }

    private GameObject CreatePanel(string panelName, Transform parent, Color color)
    {
        var go = new GameObject(panelName);
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.color = color;
        return go;
    }

    private Text CreatePanelText(string objName, Transform parent, Font font, Vector2 anchoredPos, int fontSize, TextAnchor anchor, Color color)
    {
        var go = new GameObject(objName);
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = new Vector2(900f, 260f);
        var t = go.AddComponent<Text>();
        t.font = font;
        t.fontSize = fontSize;
        t.alignment = anchor;
        t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.resizeTextForBestFit = true;
        t.resizeTextMinSize = 14;
        t.resizeTextMaxSize = 54;
        return t;
    }

    // ---------- Flow ----------

    private void EnterTitle()
    {
        state = GameState.Title;
        bonkedCount = 0;
        combo = 0;
        elapsed = 0f;
        ClearEnemies();
        SpawnPlayer();
        hudRoot.SetActive(false);
        titlePanel.SetActive(true);
        resultPanel.SetActive(false);
        UpdateHud();
    }

    private void StartRun()
    {
        ClearEnemies();
        SpawnPlayer();
        state = GameState.Playing;
        bonkedCount = 0;
        combo = 0;
        comboTimer = 0f;
        elapsed = 0f;
        spawnTimer = 0.15f;
        hudRoot.SetActive(true);
        titlePanel.SetActive(false);
        resultPanel.SetActive(false);
        StartBgm();
        UpdateHud();
    }

    private void EnterResult(bool isGameOver = false)
    {
        state = GameState.Result;
        resultLockTimer = resultInputLockSeconds;
        StopBgm();
        ClearEnemies();
        if (bonkedCount > best)
        {
            best = bonkedCount;
            PlayerPrefs.SetInt("BonkBattleBest", best);
            PlayerPrefs.Save();
        }
        hudRoot.SetActive(false);
        titlePanel.SetActive(false);
        resultPanel.SetActive(true);
        if (resultHeadText != null)
        {
            resultHeadText.text = isGameOver ? "GAME OVER" : "TIME UP!";
            resultHeadText.color = isGameOver ? new Color(1f, 0.35f, 0.35f) : Color.white;
        }
        float lasted = Mathf.Max(0f, Mathf.Min(elapsed, matchDurationSeconds));
        if (isGameOver)
        {
            resultBodyText.text = $"{lasted:0.0}秒で死んだ… {bonkedCount} 体 殴った！\nベスト：{best} 体\n最大コンボ： BAU! x{MaxCombo}\n\nSPACE / ENTER / クリック：もう一回";
        }
        else
        {
            resultBodyText.text = $"{lasted:0.0}秒で {bonkedCount} 体 殴った！\nベスト：{best} 体\n最大コンボ： BAU! x{MaxCombo}\n\nSPACE / ENTER / クリック：もう一回";
        }
    }

    public void ReportPlayerDied()
    {
        if (state != GameState.Playing) return;
        EnterResult(true);
    }

    public void DamagePlayer(float damage)
    {
        if (state != GameState.Playing || player == null) return;
        float before = player.CurrentHealth;
        bool died = player.TakeDamage(damage);
        bool applied = player.CurrentHealth < before - 0.01f;
        if (applied) PlayHurt();
        // 被ダメでもコンボ維持、赤フラッシュ＋シェイクで分かる
        TriggerFlash(0.35f, 0.3f);
        TriggerShake(0.25f, shakeStrength * 1.4f);
        UpdateHud();
        if (died) ReportPlayerDied();
    }

    private void PlayHurt()
    {
        if (hurtSource == null) return;
        if (hurtClips == null || hurtClips.Length == 0) return;
        if (hurtSource.isPlaying) return;
        var clip = hurtClips[Random.Range(0, hurtClips.Length)];
        if (clip == null) return;
        hurtSource.pitch = Random.Range(0.85f, 1.05f);
        hurtSource.PlayOneShot(clip, 1f);
    }

    private int MaxCombo { get; set; }

    private void Update()
    {
        TickShakeFlash();
        TickCombo();

        if (state == GameState.Title)
        {
            if (GameInput.IsStartPressed()) StartRun();
            return;
        }
        if (state == GameState.Result)
        {
            if (resultLockTimer > 0f)
            {
                resultLockTimer -= Time.unscaledDeltaTime;
                return;
            }
            if (GameInput.IsRetryPressed()) StartRun();
            return;
        }

        // Playing
        elapsed += Time.deltaTime;
        if (elapsed >= matchDurationSeconds)
        {
            EnterResult();
            return;
        }

        spawnTimer -= Time.deltaTime;
        if (spawnTimer <= 0f && enemies.Count < maxEnemiesAlive)
        {
            SpawnEnemy();
            spawnTimer = Mathf.Max(minSpawnInterval, initialSpawnInterval - elapsed * spawnIntervalDecayPerSecond);
        }

        // cleanup dead enemies list
        for (var i = enemies.Count - 1; i >= 0; i--)
        {
            var e = enemies[i];
            if (e == null || e.IsDead)
            {
                enemies.RemoveAt(i);
                if (e != null && e.gameObject != null)
                {
                    StartCoroutine(DestroyAfter(e.gameObject, 0.4f));
                }
            }
        }

        UpdateHud();
    }

    private IEnumerator DestroyAfter(GameObject go, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (go != null) Destroy(go);
    }

    // ---------- Player / enemies ----------

    private void SpawnPlayer()
    {
        if (player != null)
        {
            Destroy(player.gameObject);
            player = null;
        }

        GameObject go = null;
        if (fuwawaPrefabOverride != null)
        {
            go = Instantiate(fuwawaPrefabOverride, worldRoot);
        }
        else
        {
            go = LoadFuwawaPrefab();
            if (go != null) go = Instantiate(go, worldRoot);
        }

        if (go == null)
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.transform.SetParent(worldRoot, true);
            go.GetComponent<Renderer>().material.color = new Color(0.55f, 0.85f, 1f);
        }

        go.name = "FuwawaPlayer";
        go.transform.position = Vector3.zero;
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one * 1.6f;

        // ensure collider for bonk arc math fallback
        var col = go.GetComponent<Collider>();
        if (col == null) go.AddComponent<SphereCollider>();

        var rb = go.GetComponent<Rigidbody>();
        if (rb == null) rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.useGravity = false;

        // Animator を BonkPlayer より先に用意する。経路 Bone/... なので fuwawa_armature に付ける
        playerAnimator = go.GetComponentInChildren<Animator>();
        if (playerAnimator == null)
        {
            var armature = FindChildByName(go.transform, "fuwawa_armature");
            if (armature != null)
            {
                playerAnimator = armature.gameObject.AddComponent<Animator>();
            }
        }
        if (playerAnimator != null)
        {
            playerAnimator.applyRootMotion = false;
            playerAnimator.enabled = true;
            var ctl = LoadFuwawaAnimator();
            if (ctl != null) playerAnimator.runtimeAnimatorController = ctl;
        }

        var bp = go.AddComponent<BonkPlayer>();
        bp.arenaRadius = arenaRadius;
        bp.floorRadius = arenaRadius;
        bp.Initialize(this);
        player = bp;
    }

    private static Transform FindChildByName(Transform root, string name)
    {
        if (root == null) return null;
        foreach (Transform ch in root)
        {
            if (ch.name == name) return ch;
            var found = FindChildByName(ch, name);
            if (found != null) return found;
        }
        return null;
    }

    private UnityEngine.RuntimeAnimatorController LoadFuwawaAnimator()
    {
#if UNITY_EDITOR
        var ctl = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.RuntimeAnimatorController>(
            "Assets/ThirdParty/FuwawaAbyssgard/FuwawaAnimator.controller");
        if (ctl != null) return ctl;
#endif
        return Resources.Load<UnityEngine.RuntimeAnimatorController>("FuwawaAnimator");
    }

    private GameObject LoadFuwawaPrefab()
    {
#if UNITY_EDITOR
        // Editor-only fallback: pull directly from Assets so no prefab copy is needed.
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ThirdParty/FuwawaAbyssgard/fuwawa.fbx");
        if (prefab != null) return prefab;
#endif
        // Runtime fallback: expected at Resources/Fuwawa/fuwawa (optional copy)
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

        var go = new GameObject("BonkEnemy");
        go.transform.SetParent(worldRoot, true);
        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
        rb.useGravity = false;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        var col = go.AddComponent<SphereCollider>();
        col.radius = 0.7f;
        col.center = new Vector3(0f, 0.8f, 0f);

        var angle = Random.Range(0f, Mathf.PI * 2f);
        var radius = Random.Range(arenaRadius - 1.5f, arenaRadius - 0.2f);
        var pos = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

        // bias spawn closer to player sometimes so action never stalls
        if (Random.value < 0.4f)
        {
            pos = PlayerPosition + new Vector3(Random.Range(-6f, 6f), 0f, Random.Range(-6f, 6f));
            pos = Vector3.ClampMagnitude(new Vector3(pos.x, 0f, pos.z), arenaRadius - 1f);
            pos.y = 0f;
        }
        go.transform.position = pos;

        // Kenneyモデル＋歩行アニメ（失敗時は色球にフォールバック）
        bool modeled = false;
        if (useKenneyEnemies)
        {
            for (int tries = 0; tries < KenneyEnemyPaths.Length && !modeled; tries++)
            {
                var path = KenneyEnemyPaths[Random.Range(0, KenneyEnemyPaths.Length)];
                var modelPrefab = LoadKenneyModel(path);
                if (modelPrefab == null) continue;
                var visual = (GameObject)Instantiate(modelPrefab, go.transform);
                visual.name = "Visual";
                visual.transform.localPosition = Vector3.zero;
                // CharacterAnimator（isWalking）を割り当て。無くてもプロシージャル歩行で動く
                var anim = visual.GetComponentInChildren<Animator>(true);
                if (anim != null)
                {
                    anim.applyRootMotion = false;
                    var ctl = LoadCharacterAnimator();
                    if (ctl != null) anim.runtimeAnimatorController = ctl;
                    anim.SetBool("isWalking", true);
                }
                // 見た目を大きく
                visual.transform.localScale = Vector3.one * 1.15f;
                modeled = true;
            }
        }
        if (!modeled)
        {
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Visual";
            ball.transform.SetParent(go.transform, true);
            ball.transform.localPosition = Vector3.zero;
            ball.transform.localScale = Vector3.one * 0.9f;
            Destroy(ball.GetComponent<Collider>());
            var ballColor = BonkEnemy.EnemyColors[Random.Range(0, BonkEnemy.EnemyColors.Length)];
            ball.GetComponent<Renderer>().material.color = ballColor;
        }

        go.AddComponent<BonkEnemy>();
        var enemy = go.GetComponent<BonkEnemy>();
        var progress = Mathf.Clamp01(elapsed / matchDurationSeconds);
        var speed = enemyBaseSpeed + progress * enemySpeedGrowth + Random.Range(-0.3f, 0.5f);
        // Kenney MiniCharactersは約0.4mと小さいため、Fuwawa(約3m)に対抗できるよう約4倍に拡大
        var scale = Random.Range(3.4f, 4.4f);
        var color = BonkEnemy.EnemyColors[Random.Range(0, BonkEnemy.EnemyColors.Length)];
        enemy.Initialize(this, speed, scale, color);
        enemies.Add(enemy);
    }

    private GameObject LoadKenneyModel(string path)
    {
#if UNITY_EDITOR
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab != null) return prefab;
#endif
        // Runtime: Resources に無い場合はフォールバック（球）
        var rel = System.IO.Path.GetFileNameWithoutExtension(path);
        return Resources.Load<GameObject>("Kenney/" + rel);
    }

    private UnityEngine.RuntimeAnimatorController LoadCharacterAnimator()
    {
#if UNITY_EDITOR
        var ctl = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>("Assets/Animators/CharacterAnimator.controller");
        if (ctl != null) return ctl;
#endif
        return Resources.Load<UnityEngine.RuntimeAnimatorController>("Animators/CharacterAnimator");
    }

    private void ClearEnemies()
    {
        foreach (var e in enemies)
        {
            if (e != null) e.Deactivate();
        }
        enemies.Clear();
        // sweep any stragglers
        var all = FindObjectsOfType<BonkEnemy>();
        foreach (var e in all) Destroy(e.gameObject);
    }

    public BonkEnemy FindClosestEnemy(Vector3 position, float maxDistance)
    {
        BonkEnemy closest = null;
        var bestSqr = maxDistance * maxDistance;
        foreach (var e in enemies)
        {
            if (e == null || e.IsDead) continue;
            var d = (e.transform.position - position).sqrMagnitude;
            if (d < bestSqr)
            {
                bestSqr = d;
                closest = e;
            }
        }
        return closest;
    }

    public void ForgetEnemy(BonkEnemy enemy)
    {
        if (enemy == null) return;
        enemy.Deactivate();
        enemies.Remove(enemy);
        StartCoroutine(DestroyAfter(enemy.gameObject, 0.05f));
    }

    // ---------- Bonk ----------

    public void Bonk(BonkEnemy enemy, Vector3 hitPosition)
    {
        if (enemy == null || enemy.IsDead) return;
        enemy.TakeBonk((enemy.transform.position - transform_pos()).normalized);
        // OnBonked is triggered via ReportEnemyBonked inside TakeBonk
    }

    // helper to avoid unused signature noise
    private Vector3 transform_pos() => player != null ? player.transform.position : Vector3.zero;

    public void BonkMiss(Vector3 fromPosition)
    {
        PlayWhiff();
        if (player != null) SpawnRing(fromPosition);
    }

    public void ReportEnemyBonked(BonkEnemy enemy, Vector3 position)
    {
        OnBonked(position);
    }

    private void OnBonked(Vector3 position)
    {
        bonkedCount++;
        combo++;
        MaxCombo = Mathf.Max(MaxCombo, combo);
        comboTimer = 2.2f;

        // 「baubau」は攻撃時に鳴らす（OnAttack）。ヒット時は打撃音のみ
        PlayAttackImpact();
        SpawnBurst(position);
        SpawnPopup(position);
        TriggerShake(0.22f, shakeStrength);
        TriggerHitstop();
        TriggerFlash(0.18f, 0.35f);
        UpdateHud();
    }

    private void PlayAttackImpact()
    {
        if (attackSource == null) return;
        if (attackClips == null || attackClips.Length == 0) return;
        if (attackSource.isPlaying) return;
        var clip = attackClips[Random.Range(0, attackClips.Length)];
        if (clip == null) return;
        attackSource.pitch = Random.Range(0.9f, 1.1f);
        attackSource.PlayOneShot(clip, 1f);
    }

    public void PlayBau()
    {
        if (sfxSource == null) return;
        if (baubauClips == null || baubauClips.Length == 0) return;
        // 重ねて毎回鳴らす（連打でも各攻撃で新しいbaubau）
        var clip = baubauClips[Random.Range(0, baubauClips.Length)];
        if (clip == null) return;
        sfxSource.pitch = Random.Range(0.94f, 1.18f);
        sfxSource.PlayOneShot(clip, 1f);
    }

    public void OnPlayerAttack()
    {
        PlayBau();
    }

    private void PlayWhiff()
    {
        if (sfxSource == null) return;
        // no bau on miss - keep distinct
    }

    private void SpawnBurst(Vector3 position)
    {
        var ps = particles.Count > 0 ? particles[0] : null;
        if (ps != null)
        {
            ps.transform.position = position + Vector3.up * 0.4f;
            ps.Play();
        }

        var burst = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        burst.transform.SetParent(worldRoot, true);
        burst.transform.position = position + Vector3.up * 0.5f;
        burst.transform.localScale = Vector3.one * 0.5f;
        burst.GetComponent<Renderer>().material.color = new Color(1f, 0.85f, 0.3f);
        Destroy(burst.GetComponent<Collider>());
        effectObjects.Add(burst);
        StartCoroutine(EffectFade(burst, 0.35f));

        // 衝撃波: 円柱でなく膨張する透明球シェル＋残像スラッシュ
        SpawnShockSphere(position + Vector3.up * 0.5f, 2.6f, 0.32f);
        SpawnSlashAt(position);
    }

    private void SpawnRing(Vector3 fromPosition)
    {
        Vector3 ahead = fromPosition;
        if (player != null) ahead = fromPosition + Vector3.up * 0.8f + player.transform.forward * 1.2f;
        SpawnShockSphere(ahead, 1.4f, 0.22f);
    }

    private void SpawnShockSphere(Vector3 position, float maxScale, float duration)
    {
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "ShockSphere";
        sphere.transform.SetParent(worldRoot, true);
        sphere.transform.position = position;
        sphere.transform.localScale = Vector3.one * 0.5f;
        var rend = sphere.GetComponent<Renderer>();
        // Unlitにして明るく、フェードで消える透明感を出す（Litのalphaだけでは透明にならないためUnlitへ）
        rend.material.shader = Shader.Find("Universal Render Pipeline/Unlit");
        rend.material.color = new Color(1f, 0.65f, 0.25f, 0.55f);
        Destroy(sphere.GetComponent<Collider>());
        StartCoroutine(SphereExpandFade(sphere, maxScale, duration));
    }

    private void SpawnSlashAt(Vector3 position)
    {
        // 斬撃スラッシュ: 前方向に斜めの板を一瞬表示して消す
        var slash = GameObject.CreatePrimitive(PrimitiveType.Cube);
        slash.name = "Slash";
        slash.transform.SetParent(worldRoot, true);
        slash.transform.position = position + Vector3.up * 0.7f;
        if (player != null)
        {
            slash.transform.rotation = player.transform.rotation * Quaternion.Euler(25f, 0f, 55f);
        }
        slash.transform.localScale = new Vector3(0.08f, 1.4f, 0.18f);
        var rend = slash.GetComponent<Renderer>();
        rend.material.shader = Shader.Find("Universal Render Pipeline/Unlit");
        rend.material.color = new Color(1f, 0.98f, 0.55f, 0.9f);
        Destroy(slash.GetComponent<Collider>());
        StartCoroutine(SlashSpin(slash));
    }

    private IEnumerator SphereExpandFade(GameObject sphere, float maxScale, float duration)
    {
        var rend = sphere.GetComponent<Renderer>();
        var t = 0f;
        while (t < duration && sphere != null)
        {
            t += Time.deltaTime;
            var k = t / duration;
            sphere.transform.localScale = Vector3.one * (0.5f + k * maxScale);
            var c = rend.material.color;
            c.a = 0.55f * (1f - k);
            rend.material.color = c;
            yield return null;
        }
        if (sphere != null) Destroy(sphere);
    }

    private IEnumerator SlashSpin(GameObject slash)
    {
        var t = 0f;
        var dur = 0.14f;
        while (t < dur && slash != null)
        {
            t += Time.deltaTime;
            slash.transform.Rotate(0f, 720f * Time.deltaTime, 0f);
            yield return null;
        }
        if (slash != null) Destroy(slash);
    }

    private IEnumerator EffectFade(GameObject go, float duration)
    {
        var r = go.GetComponent<Renderer>();
        var start = r.material.color;
        var t = 0f;
        while (t < duration && go != null)
        {
            t += Time.deltaTime;
            var k = 1f - t / duration;
            r.material.color = new Color(start.r, start.g, start.b, k);
            go.transform.localScale = Vector3.one * (0.5f * (0.5f + k));
            yield return null;
        }
        if (go != null) Destroy(go);
    }

    private IEnumerator RingExpand(GameObject ring)
    {
        var t = 0f;
        var dur = 0.28f;
        while (t < dur && ring != null)
        {
            t += Time.deltaTime;
            var k = t / dur;
            ring.transform.localScale = Vector3.one * (0.6f + k * 2.4f);
            ring.transform.Rotate(0f, 180f * Time.deltaTime, 0f);
            yield return null;
        }
        if (ring != null) Destroy(ring);
    }

    private void SpawnPopup(Vector3 position)
    {
        var go = new GameObject("Popup");
        go.transform.SetParent(worldRoot, true);
        go.transform.position = position + Vector3.up * 1.9f;
        var tm = go.AddComponent<TextMesh>();
        tm.text = BauWords[Random.Range(0, BauWords.Length)];
        tm.fontSize = 48;
        tm.alignment = TextAlignment.Center;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.color = new Color(1f, 0.95f, 0.3f);
        StartCoroutine(PopupFloat(go));
    }

    private IEnumerator PopupFloat(GameObject go)
    {
        var tm = go.GetComponent<TextMesh>();
        var t = 0f;
        var dur = 0.85f;
        var startY = go.transform.position.y;
        while (t < dur && go != null)
        {
            t += Time.deltaTime;
            var k = t / dur;
            go.transform.position = new Vector3(go.transform.position.x, startY + k * 1.4f, go.transform.position.z);
            var s = 1f + Mathf.Sin(k * Mathf.PI) * 0.35f;
            tm.transform.localScale = Vector3.one * s;
            // fade out last 40%
            if (k > 0.6f)
            {
                var a = 1f - (k - 0.6f) / 0.4f;
                tm.color = new Color(1f, 0.95f, 0.3f, a);
            }
            yield return null;
        }
        if (go != null) Destroy(go);
    }

    // ---------- FX timers ----------

    private void TriggerShake(float duration, float strength)
    {
        if (!enableScreenShake) return;
        shakeDuration = duration;
        shakeTime = duration;
        shakeStrength = strength;
    }

    private void TriggerHitstop()
    {
        if (!enableHitstop) return;
        hitstopTimer = hitstopSeconds;
        Time.timeScale = 0.08f;
    }

    private void TriggerFlash(float alpha, float duration)
    {
        if (flashOverlay == null) return;
        flashTimer = duration;
        var c = flashOverlay.color;
        c.a = alpha;
        flashOverlay.color = c;
    }

    private void TickShakeFlash()
    {
        if (hitstopTimer > 0f)
        {
            hitstopTimer -= Time.unscaledDeltaTime;
            if (hitstopTimer <= 0f) Time.timeScale = 1f;
        }

        TickCameraFollow();

        if (flashTimer > 0f && flashOverlay != null)
        {
            flashTimer -= Time.unscaledDeltaTime;
            var c = flashOverlay.color;
            c.a = Mathf.Max(0f, c.a - Time.unscaledDeltaTime * 1.2f);
            flashOverlay.color = c;
        }
    }

    private void TickCameraFollow()
    {
        if (mainCamera == null) return;

        // 追従対象: Playing中はプレイヤー、他はアリーナ中心
        Vector3 focus;
        Vector3 offset = new Vector3(0f, 24f, -16f);
        if (enableCameraFollow && state == GameState.Playing && player != null)
        {
            focus = player.transform.position + Vector3.up * 0.5f;
            offset = cameraFollowOffset;
        }
        else
        {
            focus = new Vector3(0f, 0.3f, 0f);
            offset = new Vector3(0f, 24f, -16f);
        }

        var desired = focus + offset;
        // アリーナ外へ飛び出さないようXZをクランプ（追従しすぎて床が切れないように）
        float clampR = arenaRadius + 4f;
        desired.x = Mathf.Clamp(desired.x, -clampR, clampR);
        desired.z = Mathf.Clamp(desired.z, -clampR, clampR);

        var smooth = Mathf.Max(0.1f, cameraFollowSmooth);
        cameraBasePos = Vector3.Lerp(cameraBasePos, desired, Time.unscaledDeltaTime * smooth);

        var shakeOffset = Vector3.zero;
        if (shakeTime > 0f)
        {
            shakeTime -= Time.unscaledDeltaTime;
            var k = Mathf.Max(0f, shakeTime / shakeDuration);
            shakeOffset = Random.insideUnitSphere * (shakeStrength * k);
            shakeOffset.y = 0f;
        }
        mainCamera.transform.position = cameraBasePos + shakeOffset;
        // focus側を見る（シェイクでも視線はブレない）
        mainCamera.transform.LookAt(focus);
    }

    private void TickCombo()
    {
        if (combo == 0) return;
        comboTimer -= Time.deltaTime;
        if (comboTimer <= 0f) combo = 0;
    }

    // ---------- HUD update ----------

    private void UpdateHud()
    {
        if (timerText != null)
        {
            var remain = Mathf.Max(0f, matchDurationSeconds - elapsed);
            var urgent = remain <= 10f ? "！" : "";
            timerText.text = $"残り {remain:0.0}s{urgent}";
            timerText.color = remain <= 10f ? new Color(1f, 0.25f, 0.3f) : new Color(1f, 0.92f, 0.25f);
        }
        if (countText != null) countText.text = $"殴った {bonkedCount} 体";
        if (comboText != null) comboText.text = combo >= 2 ? $"BAU! x{combo}" : "";
        SetBarFill(timerFill, 1f - elapsed / matchDurationSeconds);
        if (player != null) SetBarFill(cooldownFill, 1f - player.CooldownNormalized);
        if (player != null) SetBarFill(hpFill, (float)player.CurrentHealth / player.MaxHealth);
        if (hpText != null && player != null) hpText.text = $"HP {Mathf.CeilToInt(player.CurrentHealth)}";
    }
}
