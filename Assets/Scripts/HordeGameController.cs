using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public sealed class HordeGameController : MonoBehaviour
{
    private enum GameState
    {
        Title,
        Playing,
        GameOver,
        Clear
    }

    private struct EnemySpawnProfile
    {
        public float Health;
        public float Speed;
        public float ContactDamage;
        public float Scale;
        public int ScoreReward;
        public float ExpReward;
        public Color Color;
        public bool IsBoss;
    }

    private struct ActiveEffect
    {
        public GameObject Object;
        public float Remaining;
    }

    [SerializeField] private GameBalanceSettings balanceSettings;
    [Header("Optional Stage Prefabs")]
    [SerializeField] private GameObject stageFloorPrefab;
    [SerializeField] private GameObject stageFloorDetailPrefab;
    [SerializeField] private GameObject stageWallPrefab;
    [SerializeField] private GameObject stageWallCornerPrefab;
    [SerializeField] private GameObject stageColumnPrefab;
    [SerializeField] private GameObject stageStatuePrefab;
    [SerializeField] private GameObject stageBannerPrefab;
    [Header("Character Prefabs")]
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private GameObject enemyGruntPrefab;
    [SerializeField] private GameObject enemyRunnerPrefab;
    [SerializeField] private GameObject enemyTankPrefab;
    [SerializeField] private GameObject enemyBossPrefab;
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private GameObject killEffectPrefab;
    [Header("Optional Audio Clips")]
    [SerializeField] private AudioClip bgmClip;
    [SerializeField] private AudioClip shotClip;
    [SerializeField] private AudioClip hitClip;
    [SerializeField] private AudioClip levelUpClip;
    [SerializeField] private AudioClip gameOverClip;
    [SerializeField] private AudioClip clearClip;

    private readonly List<EnemyController> enemies = new();
    private readonly List<ActiveEffect> activeEffects = new();

    private Transform worldRoot;
    private Camera mainCamera;
    private PlayerController player;
    private GameObject hudRoot;
    private GameObject titlePanel;
    private GameObject resultPanel;
    private Text scoreText;
    private Text statusText;
    private Text centerMessageText;
    private Text titleHeadlineText;
    private Text titleBodyText;
    private Text resultHeadlineText;
    private Text resultBodyText;
    private Image hpFill;
    private Image expFill;
    private GameObjectPool enemyPool;
    private GameObjectPool projectilePool;
    private GameObjectPool effectPool;
    private AudioSource bgmSource;
    private AudioSource sfxSource;
    private int score;
    private int level;
    private float currentExp;
    private float expToNextLevel;
    private float spawnTimer;
    private float bossSpawnTimer;
    private float elapsedTime;
    private float centerMessageTimer;
    private float shotSfxCooldown;
    private GameState gameState;

    public float GetArenaRadius() => balanceSettings.match.arenaRadius;

    public bool IsPlaying => gameState == GameState.Playing;

    public Transform PlayerTransform => player != null && player.gameObject.activeInHierarchy ? player.transform : null;

    public float GetPlayerBaseMaxHealth() => balanceSettings.player.baseMaxHealth;
    public float GetPlayerBaseMoveSpeed() => balanceSettings.player.baseMoveSpeed;
    public float GetPlayerBaseShotInterval() => balanceSettings.player.baseShotInterval;
    public float GetPlayerBaseProjectileSpeed() => balanceSettings.player.baseProjectileSpeed;
    public float GetPlayerBaseProjectileDamage() => balanceSettings.player.baseProjectileDamage;
    public float GetPlayerMaxShotInterval() => balanceSettings.player.maxShotInterval;
    public float GetLevelUpHealthBonus() => balanceSettings.progression.levelUpHealthBonus;
    public float GetLevelUpHealAmount() => balanceSettings.progression.levelUpHealAmount;
    public float GetLevelUpMoveSpeedBonus() => balanceSettings.progression.levelUpMoveSpeedBonus;
    public float GetLevelUpProjectileDamageBonus() => balanceSettings.progression.levelUpProjectileDamageBonus;
    public float GetLevelUpProjectileDamagePerLevel() => balanceSettings.progression.levelUpProjectileDamagePerLevel;
    public float GetLevelUpProjectileSpeedBonus() => balanceSettings.progression.levelUpProjectileSpeedBonus;
    public float GetLevelUpShotIntervalReduction() => balanceSettings.progression.levelUpShotIntervalReduction;

    private void Awake()
    {
        EnsureBalanceSettings();
        SetupScene();
        SetupPools();
        SetupAudio();
        CreateHud();
        EnterTitle();
    }

    private void Update()
    {
        TickEffects();
        shotSfxCooldown -= Time.deltaTime;

        if (gameState == GameState.Title)
        {
            if (GameInput.IsStartPressed())
            {
                StartRun();
            }

            return;
        }

        if (gameState == GameState.GameOver || gameState == GameState.Clear)
        {
            if (GameInput.IsRetryPressed())
            {
                StartRun();
            }

            return;
        }

        elapsedTime += Time.deltaTime;
        spawnTimer -= Time.deltaTime;
        bossSpawnTimer -= Time.deltaTime;

        if (spawnTimer <= 0f)
        {
            SpawnEnemyWave();
            spawnTimer = Mathf.Max(
                balanceSettings.match.minSpawnInterval,
                balanceSettings.match.initialSpawnInterval - elapsedTime * balanceSettings.match.spawnIntervalDecayPerSecond);
        }

        if (bossSpawnTimer <= 0f)
        {
            SpawnBoss();
            bossSpawnTimer = balanceSettings.match.bossSpawnInterval;
        }

        if (elapsedTime >= balanceSettings.match.matchDurationSeconds)
        {
            EnterResult(GameState.Clear);
        }

        TickCenterMessage();
        UpdateHud();
    }

    public EnemyController FindClosestEnemy(Vector3 position, float maxDistance)
    {
        EnemyController closest = null;
        var closestDistanceSqr = maxDistance * maxDistance;

        for (var i = enemies.Count - 1; i >= 0; i--)
        {
            var enemy = enemies[i];
            if (enemy == null || !enemy.gameObject.activeSelf)
            {
                enemies.RemoveAt(i);
                continue;
            }

            var distanceSqr = (enemy.transform.position - position).sqrMagnitude;
            if (distanceSqr < closestDistanceSqr)
            {
                closestDistanceSqr = distanceSqr;
                closest = enemy;
            }
        }

        return closest;
    }

    public void SpawnProjectile(Vector3 origin, Vector3 direction, float speed, float damage)
    {
        var projectileObject = projectilePool.Rent();
        projectileObject.name = "Projectile";
        projectileObject.transform.SetParent(worldRoot, true);
        var projectile = projectileObject.GetComponent<Projectile>();
        projectile.Activate(this, origin, direction, speed, damage);
    }

    public void NotifyPlayerShot()
    {
        if (shotSfxCooldown > 0f)
        {
            return;
        }

        PlaySfx(shotClip, 0.22f, Random.Range(0.96f, 1.04f));
        shotSfxCooldown = 0.04f;
    }

    public void ReleaseProjectile(Projectile projectile)
    {
        if (projectile == null)
        {
            return;
        }

        projectile.Deactivate();
        projectilePool.Return(projectile.gameObject);
    }

    public void ReportEnemyKilled(EnemyController enemy, Vector3 position, int scoreReward, float expReward)
    {
        score += Mathf.Max(1, scoreReward);
        AddExperience(expReward);
        ReleaseEnemy(enemy);
        SpawnKillEffect(position);
        PlaySfx(hitClip, 0.28f, Random.Range(0.94f, 1.05f));
    }

    public void ReportPlayerDied()
    {
        EnterResult(GameState.GameOver);
    }

    private void EnsureBalanceSettings()
    {
        if (balanceSettings != null && balanceSettings.enemyTypes != null && balanceSettings.enemyTypes.Length > 0 && balanceSettings.bossType != null)
        {
            return;
        }

        balanceSettings = ScriptableObject.CreateInstance<GameBalanceSettings>();
        balanceSettings.enemyTypes = new[]
        {
            CreateEnemyType("Grunt", 1f, 0.2f, 18f, 0.34f, 2.6f, 0.8f, 10f, 8f, 1.2f, 10, 10f, new Color(1f, 0.35f, 0.25f)),
            CreateEnemyType("Runner", 0.55f, 0.65f, 14f, 0.28f, 4.6f, 1.1f, 8f, 5f, 0.9f, 11, 8f, new Color(1f, 0.95f, 0.25f)),
            CreateEnemyType("Tank", 0.35f, 0.2f, 42f, 0.5f, 2.1f, 0.7f, 13f, 10f, 1.85f, 20, 16f, new Color(0.8f, 0.28f, 1f))
        };
        balanceSettings.bossType = CreateEnemyType("Boss", 1f, 0f, 220f, 2.8f, 1.8f, 1.2f, 22f, 16f, 2.8f, 120, 70f, new Color(0.95f, 0.12f, 0.12f));
        balanceSettings.bossType.isBoss = true;
    }

    private EnemyTypeDefinition CreateEnemyType(
        string name,
        float baseWeight,
        float weightGrowthByProgress,
        float baseHealth,
        float healthGrowthPerSecond,
        float baseSpeed,
        float speedGrowthByProgress,
        float baseDamage,
        float damageGrowthByProgress,
        float scale,
        int scoreReward,
        float expReward,
        Color color)
    {
        var def = ScriptableObject.CreateInstance<EnemyTypeDefinition>();
        def.name = name;
        def.baseWeight = baseWeight;
        def.weightGrowthByProgress = weightGrowthByProgress;
        def.baseHealth = baseHealth;
        def.healthGrowthPerSecond = healthGrowthPerSecond;
        def.baseSpeed = baseSpeed;
        def.speedGrowthByProgress = speedGrowthByProgress;
        def.baseContactDamage = baseDamage;
        def.damageGrowthByProgress = damageGrowthByProgress;
        def.scale = scale;
        def.scoreReward = scoreReward;
        def.expReward = expReward;
        def.color = color;
        return def;
    }

    private void SetupScene()
    {
        worldRoot = new GameObject("WorldRoot").transform;
        worldRoot.SetParent(transform, false);

        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            mainCamera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
        }

        mainCamera.transform.position = new Vector3(0f, 18f, -10f);
        mainCamera.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
        mainCamera.backgroundColor = new Color(0.06f, 0.08f, 0.12f);
        mainCamera.clearFlags = CameraClearFlags.SolidColor;

        var light = FindAnyObjectByType<Light>();
        if (light == null)
        {
            var lightObject = new GameObject("Directional Light");
            light = lightObject.AddComponent<Light>();
        }

        light.type = LightType.Directional;
        light.intensity = 1.25f;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        BuildArenaVisuals(balanceSettings.match.arenaRadius);
    }

    private void BuildArenaVisuals(float arenaRadius)
    {
        if (stageFloorPrefab != null)
        {
            BuildArenaFromPrefabs(arenaRadius);
            return;
        }

        var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        floor.name = "ArenaFloor";
        floor.transform.position = Vector3.zero;
        floor.transform.localScale = new Vector3(arenaRadius * 2f, 0.24f, arenaRadius * 2f);
        floor.transform.SetParent(worldRoot, true);
        floor.GetComponent<Renderer>().material.color = new Color(0.07f, 0.09f, 0.12f);

        var innerGrid = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        innerGrid.name = "ArenaInnerGrid";
        innerGrid.transform.position = new Vector3(0f, 0.12f, 0f);
        innerGrid.transform.localScale = new Vector3((arenaRadius - 2.4f) * 2f, 0.06f, (arenaRadius - 2.4f) * 2f);
        innerGrid.transform.SetParent(worldRoot, true);
        innerGrid.GetComponent<Renderer>().material.color = new Color(0.12f, 0.17f, 0.24f);

        var centerDisk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        centerDisk.name = "ArenaCenter";
        centerDisk.transform.position = new Vector3(0f, 0.16f, 0f);
        centerDisk.transform.localScale = new Vector3(4.5f, 0.08f, 4.5f);
        centerDisk.transform.SetParent(worldRoot, true);
        centerDisk.GetComponent<Renderer>().material.color = new Color(0.18f, 0.31f, 0.44f);

        var outerWall = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        outerWall.name = "ArenaOuterWall";
        outerWall.transform.position = new Vector3(0f, 0.45f, 0f);
        outerWall.transform.localScale = new Vector3((arenaRadius + 0.8f) * 2f, 0.32f, (arenaRadius + 0.8f) * 2f);
        outerWall.transform.SetParent(worldRoot, true);
        outerWall.GetComponent<Renderer>().material.color = new Color(0.35f, 0.12f, 0.16f);

        for (var i = 0; i < 16; i++)
        {
            var angle = i * Mathf.PI * 2f / 16f;
            var pos = new Vector3(Mathf.Cos(angle) * (arenaRadius - 1.2f), 0.9f, Mathf.Sin(angle) * (arenaRadius - 1.2f));

            var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pillar.name = "ArenaPillar";
            pillar.transform.position = pos;
            pillar.transform.localScale = new Vector3(0.8f, 1.6f, 0.8f);
            pillar.transform.SetParent(worldRoot, true);
            pillar.GetComponent<Renderer>().material.color = i % 2 == 0
                ? new Color(0.3f, 0.35f, 0.45f)
                : new Color(0.2f, 0.25f, 0.33f);

            var beacon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            beacon.name = "ArenaBeacon";
            beacon.transform.position = pos + Vector3.up * 1.9f;
            beacon.transform.localScale = Vector3.one * 0.35f;
            beacon.transform.SetParent(worldRoot, true);
            beacon.GetComponent<Renderer>().material.color = new Color(0.2f, 0.9f, 1f);
        }
    }

    private void BuildArenaFromPrefabs(float arenaRadius)
    {
        var tileStep = 2.2f;
        var tileScale = new Vector3(2.25f, 1f, 2.25f);
        var gridExtent = Mathf.CeilToInt(arenaRadius / tileStep) + 1;
        for (var x = -gridExtent; x <= gridExtent; x++)
        {
            for (var z = -gridExtent; z <= gridExtent; z++)
            {
                var pos = new Vector3(x * tileStep, 0f, z * tileStep);
                if (new Vector2(pos.x, pos.z).magnitude > arenaRadius - 0.4f)
                {
                    continue;
                }

                var prefab = ((x + z) & 1) == 0 && stageFloorDetailPrefab != null ? stageFloorDetailPrefab : stageFloorPrefab;
                PlaceStagePrefab(prefab, "ArenaTile", pos, Vector3.zero, tileScale);
            }
        }

        PlaceStagePrefab(stageFloorDetailPrefab, "ArenaCenter", new Vector3(0f, 0.02f, 0f), Vector3.zero, new Vector3(3.2f, 1f, 3.2f));

        var ringRadius = arenaRadius - 1.5f;
        for (var i = 0; i < 8; i++)
        {
            var angleDeg = i * 45f;
            var angleRad = angleDeg * Mathf.Deg2Rad;
            var pos = new Vector3(Mathf.Cos(angleRad) * ringRadius, 0f, Mathf.Sin(angleRad) * ringRadius);
            PlaceStagePrefab(stageWallPrefab, "ArenaWall", pos, new Vector3(0f, angleDeg + 90f, 0f), new Vector3(2.8f, 1.45f, 2.8f));
        }

        var cornerRadius = arenaRadius - 0.9f;
        for (var i = 0; i < 4; i++)
        {
            var angleDeg = 45f + i * 90f;
            var angleRad = angleDeg * Mathf.Deg2Rad;
            var pos = new Vector3(Mathf.Cos(angleRad) * cornerRadius, 0f, Mathf.Sin(angleRad) * cornerRadius);
            PlaceStagePrefab(stageWallCornerPrefab, "ArenaWallCorner", pos, new Vector3(0f, angleDeg + 180f, 0f), new Vector3(2.5f, 1.35f, 2.5f));
        }

        for (var i = 0; i < 12; i++)
        {
            var angleDeg = i * 30f;
            var angleRad = angleDeg * Mathf.Deg2Rad;
            var pos = new Vector3(Mathf.Cos(angleRad) * (arenaRadius - 2.3f), 0f, Mathf.Sin(angleRad) * (arenaRadius - 2.3f));
            PlaceStagePrefab(stageColumnPrefab, "ArenaColumn", pos, new Vector3(0f, angleDeg, 0f), new Vector3(1.45f, 1.7f, 1.45f));
        }

        PlaceStagePrefab(stageStatuePrefab, "ArenaStatueA", new Vector3(6.5f, 0f, 6.5f), new Vector3(0f, 225f, 0f), new Vector3(2.2f, 2.2f, 2.2f));
        PlaceStagePrefab(stageStatuePrefab, "ArenaStatueB", new Vector3(-6.5f, 0f, -6.5f), new Vector3(0f, 45f, 0f), new Vector3(2.2f, 2.2f, 2.2f));
        PlaceStagePrefab(stageStatuePrefab, "ArenaStatueC", new Vector3(-6.5f, 0f, 6.5f), new Vector3(0f, -45f, 0f), new Vector3(2.2f, 2.2f, 2.2f));
        PlaceStagePrefab(stageStatuePrefab, "ArenaStatueD", new Vector3(6.5f, 0f, -6.5f), new Vector3(0f, 135f, 0f), new Vector3(2.2f, 2.2f, 2.2f));

        PlaceStagePrefab(stageBannerPrefab, "ArenaBannerA", new Vector3(0f, 0f, arenaRadius - 2f), new Vector3(0f, 180f, 0f), new Vector3(1.7f, 1.7f, 1.7f));
        PlaceStagePrefab(stageBannerPrefab, "ArenaBannerB", new Vector3(0f, 0f, -arenaRadius + 2f), new Vector3(0f, 0f, 0f), new Vector3(1.7f, 1.7f, 1.7f));
    }

    private void PlaceStagePrefab(GameObject prefab, string objectName, Vector3 position, Vector3 eulerAngles, Vector3 scale)
    {
        if (prefab == null)
        {
            return;
        }

        var instance = Instantiate(prefab, worldRoot);
        instance.name = objectName;
        instance.transform.localPosition = position;
        instance.transform.localRotation = Quaternion.Euler(eulerAngles);
        instance.transform.localScale = scale;
        instance.layer = 9; // Environment
        foreach (Transform child in instance.transform) child.gameObject.layer = 9;
    }

    private void SetupAudio()
    {
        var audioRoot = new GameObject("AudioRoot");
        audioRoot.transform.SetParent(transform, false);

        bgmSource = audioRoot.AddComponent<AudioSource>();
        bgmSource.loop = true;
        bgmSource.playOnAwake = false;
        bgmSource.volume = 0.42f;

        sfxSource = audioRoot.AddComponent<AudioSource>();
        sfxSource.loop = false;
        sfxSource.playOnAwake = false;
        sfxSource.volume = 0.8f;

        EnsureRuntimeAudioClips();
    }

    private void EnsureRuntimeAudioClips()
    {
        if (bgmClip == null)
        {
            bgmClip = CreateMusicClip("BGM_Main", 58f, 122f, 0.18f);
        }

        if (shotClip == null)
        {
            shotClip = CreateSfxClip("SFX_Shot", 0.11f, 880f, 0.08f, 0.004f);
        }

        if (hitClip == null)
        {
            hitClip = CreateSfxClip("SFX_Hit", 0.16f, 220f, 0.16f, 0.018f);
        }

        if (levelUpClip == null)
        {
            levelUpClip = CreateChordClip("SFX_LevelUp", 0.42f, new[] { 523.25f, 659.25f, 783.99f }, 0.14f);
        }

        if (gameOverClip == null)
        {
            gameOverClip = CreateSfxClip("SFX_GameOver", 0.55f, 220f, 0.15f, 0.05f);
        }

        if (clearClip == null)
        {
            clearClip = CreateChordClip("SFX_Clear", 0.8f, new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.13f);
        }
    }

    private void SetupPools()
    {
        enemyPool = new GameObjectPool(CreateEnemyObject, balanceSettings.pooling.enemyPoolSize, worldRoot);
        projectilePool = new GameObjectPool(CreateProjectileObject, balanceSettings.pooling.projectilePoolSize, worldRoot);
        effectPool = new GameObjectPool(CreateEffectObject, balanceSettings.pooling.effectPoolSize, worldRoot);
    }

    private GameObject CreateEnemyObject()
    {
        GameObject enemyObject;
        if (enemyGruntPrefab != null)
        {
            enemyObject = Instantiate(enemyGruntPrefab, worldRoot);
        }
        else
        {
            enemyObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var body = enemyObject.AddComponent<Rigidbody>();
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            enemyObject.AddComponent<EnemyController>();
        }
        
        enemyObject.name = "Enemy";
        enemyObject.layer = 7; // Enemy
        var enemy = enemyObject.GetComponent<EnemyController>();
        if (enemy == null) enemy = enemyObject.AddComponent<EnemyController>();
        enemy.SetController(this);
        return enemyObject;
    }

    private GameObject CreateProjectileObject()
    {
        GameObject projectileObject;
        if (projectilePrefab != null)
        {
            projectileObject = Instantiate(projectilePrefab, worldRoot);
        }
        else
        {
            projectileObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(projectileObject.GetComponent<Collider>());
            projectileObject.AddComponent<Projectile>();
        }
        
        projectileObject.name = "Projectile";
        projectileObject.layer = 8; // Projectile
        var projectile = projectileObject.GetComponent<Projectile>();
        if (projectile == null) projectile = projectileObject.AddComponent<Projectile>();
        return projectileObject;
    }

    private GameObject CreateEffectObject()
    {
        GameObject effectObject;
        if (killEffectPrefab != null)
        {
            effectObject = Instantiate(killEffectPrefab, worldRoot);
        }
        else
        {
            effectObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(effectObject.GetComponent<Collider>());
        }
        
        effectObject.name = "KillBurst";
        return effectObject;
    }

    private void SpawnPlayer()
    {
        GameObject playerObject;
        if (playerPrefab != null)
        {
            playerObject = Instantiate(playerPrefab, worldRoot);
        }
        else
        {
            playerObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            playerObject.GetComponent<Renderer>().material.color = new Color(0.2f, 0.85f, 1f);
            var body = playerObject.AddComponent<Rigidbody>();
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            playerObject.AddComponent<PlayerController>();
        }

        playerObject.name = "Player";
        playerObject.transform.position = new Vector3(0f, 1f, 0f);
        playerObject.transform.SetParent(worldRoot, true);

        player = playerObject.GetComponent<PlayerController>();
        if (player == null) player = playerObject.AddComponent<PlayerController>();
        player.Initialize(this);
    }

    private void SpawnEnemyWave()
    {
        var waveSize = Mathf.Clamp(1 + Mathf.FloorToInt(elapsedTime / 12f), 1, 5);
        for (var i = 0; i < waveSize; i++)
        {
            SpawnEnemy(PickEnemyProfile());
        }
    }

    private EnemySpawnProfile PickEnemyProfile()
    {
        var progress = Mathf.Clamp01(elapsedTime / balanceSettings.match.matchDurationSeconds);
        var definitions = balanceSettings.enemyTypes;

        var totalWeight = 0f;
        for (var i = 0; i < definitions.Length; i++)
        {
            var weight = definitions[i].baseWeight + definitions[i].weightGrowthByProgress * progress;
            totalWeight += Mathf.Max(0.001f, weight);
        }

        var roll = Random.Range(0f, totalWeight);
        var cursor = 0f;
        for (var i = 0; i < definitions.Length; i++)
        {
            var def = definitions[i];
            var weight = Mathf.Max(0.001f, def.baseWeight + def.weightGrowthByProgress * progress);
            cursor += weight;
            if (roll <= cursor)
            {
                return BuildProfile(def, progress);
            }
        }

        return BuildProfile(definitions[0], progress);
    }

    private EnemySpawnProfile BuildProfile(EnemyTypeDefinition def, float progress)
    {
        var color = def.randomizeColorPulse
            ? Color.Lerp(def.color, Color.white, Mathf.PingPong(elapsedTime * 0.15f, 0.35f))
            : def.color;

        return new EnemySpawnProfile
        {
            Health = def.baseHealth + elapsedTime * def.healthGrowthPerSecond,
            Speed = def.baseSpeed + progress * def.speedGrowthByProgress,
            ContactDamage = def.baseContactDamage + progress * def.damageGrowthByProgress,
            Scale = def.scale,
            ScoreReward = def.scoreReward,
            ExpReward = def.expReward,
            Color = color,
            IsBoss = def.isBoss
        };
    }

    private void SpawnBoss()
    {
        if (!IsPlaying)
        {
            return;
        }

        var progress = Mathf.Clamp01(elapsedTime / balanceSettings.match.matchDurationSeconds);
        var profile = BuildProfile(balanceSettings.bossType, progress);
        profile.IsBoss = true;
        SpawnEnemy(profile);
        ShowCenterMessage("WARNING: ELITE INBOUND", 2.2f);
    }

    private void SpawnEnemy(EnemySpawnProfile profile)
    {
        if (player == null || !IsPlaying)
        {
            return;
        }

        var arenaRadius = balanceSettings.match.arenaRadius;
        var angle = Random.Range(0f, Mathf.PI * 2f);
        var spawnRadius = Random.Range(arenaRadius - 2f, arenaRadius - 0.5f);
        var spawnPosition = new Vector3(Mathf.Cos(angle) * spawnRadius, 1f, Mathf.Sin(angle) * spawnRadius);

        var enemyObject = enemyPool.Rent();
        enemyObject.name = profile.IsBoss ? "BossEnemy" : "Enemy";
        enemyObject.transform.SetParent(worldRoot, true);
        var enemy = enemyObject.GetComponent<EnemyController>();
        enemy.Activate(
            spawnPosition,
            profile.Health,
            profile.Speed,
            profile.ContactDamage,
            profile.ScoreReward,
            profile.ExpReward,
            profile.Scale,
            profile.Color);
        RegisterEnemy(enemy);
    }

    private void RegisterEnemy(EnemyController enemy)
    {
        if (enemy != null && !enemies.Contains(enemy))
        {
            enemies.Add(enemy);
        }
    }

    private void ReleaseEnemy(EnemyController enemy)
    {
        if (enemy == null)
        {
            return;
        }

        enemy.Deactivate();
        enemies.Remove(enemy);
        enemyPool.Return(enemy.gameObject);
    }

    private void SpawnKillEffect(Vector3 position)
    {
        var effectObject = effectPool.Rent();
        effectObject.transform.SetParent(worldRoot, true);
        effectObject.transform.position = position + Vector3.up * 0.6f;
        effectObject.transform.localScale = Vector3.one * 0.6f;
        effectObject.GetComponent<Renderer>().material.color = new Color(1f, 0.65f, 0.2f);

        activeEffects.Add(new ActiveEffect
        {
            Object = effectObject,
            Remaining = 0.15f
        });
    }

    private void TickEffects()
    {
        for (var i = activeEffects.Count - 1; i >= 0; i--)
        {
            var effect = activeEffects[i];
            effect.Remaining -= Time.deltaTime;
            if (effect.Remaining <= 0f || effect.Object == null)
            {
                if (effect.Object != null)
                {
                    effectPool.Return(effect.Object);
                }

                activeEffects.RemoveAt(i);
                continue;
            }

            activeEffects[i] = effect;
        }
    }

    private void CreateHud()
    {
        var canvasObject = new GameObject("HUD");
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasObject.AddComponent<GraphicRaycaster>();

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
        {
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        hudRoot = new GameObject("HUDRoot");
        hudRoot.transform.SetParent(canvas.transform, false);

        scoreText = CreateText("ScoreText", hudRoot.transform, font, new Vector2(20f, -20f), 28, TextAnchor.UpperLeft);
        statusText = CreateText("StatusText", hudRoot.transform, font, new Vector2(0f, 40f), 26, TextAnchor.UpperCenter);
        centerMessageText = CreateText("CenterMessage", hudRoot.transform, font, new Vector2(0f, -40f), 36, TextAnchor.MiddleCenter);
        statusText.alignment = TextAnchor.UpperCenter;
        centerMessageText.alignment = TextAnchor.MiddleCenter;

        hpFill = CreateBar("HPBar", hudRoot.transform, new Vector2(0f, 30f), new Color(0.2f, 0.85f, 1f));
        expFill = CreateBar("EXPBar", hudRoot.transform, new Vector2(0f, 10f), new Color(1f, 0.8f, 0.18f));

        titlePanel = CreatePanel("TitlePanel", canvas.transform, new Color(0f, 0f, 0f, 0.6f));
        titleHeadlineText = CreatePanelText("TitleHeadline", titlePanel.transform, font, new Vector2(0f, 110f), 68, TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.2f));
        titleBodyText = CreatePanelText("TitleBody", titlePanel.transform, font, new Vector2(0f, -20f), 30, TextAnchor.MiddleCenter, Color.white);

        resultPanel = CreatePanel("ResultPanel", canvas.transform, new Color(0f, 0f, 0f, 0.62f));
        resultHeadlineText = CreatePanelText("ResultHeadline", resultPanel.transform, font, new Vector2(0f, 100f), 64, TextAnchor.MiddleCenter, Color.white);
        resultBodyText = CreatePanelText("ResultBody", resultPanel.transform, font, new Vector2(0f, -25f), 30, TextAnchor.MiddleCenter, new Color(0.95f, 0.95f, 0.95f));

        UpdateHud();
        centerMessageText.text = string.Empty;
    }

    private GameObject CreatePanel(string panelName, Transform parent, Color color)
    {
        var panel = new GameObject(panelName);
        panel.transform.SetParent(parent, false);
        var rect = panel.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var image = panel.AddComponent<Image>();
        image.color = color;
        return panel;
    }

    private Text CreatePanelText(string objectName, Transform parent, Font font, Vector2 anchoredPosition, int fontSize, TextAnchor anchor, Color color)
    {
        var textObject = new GameObject(objectName);
        textObject.transform.SetParent(parent, false);

        var rect = textObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(1100f, 280f);

        var text = textObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.alignment = anchor;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private Image CreateBar(string objectName, Transform parent, Vector2 anchorOffset, Color fillColor)
    {
        var rootObject = new GameObject(objectName);
        rootObject.transform.SetParent(parent, false);
        var rootRect = rootObject.AddComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0.5f, 0f);
        rootRect.anchorMax = new Vector2(0.5f, 0f);
        rootRect.pivot = new Vector2(0.5f, 0f);
        rootRect.anchoredPosition = anchorOffset;
        rootRect.sizeDelta = new Vector2(400f, 14f);

        var background = rootObject.AddComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.45f);

        var fillObject = new GameObject(objectName + "Fill");
        fillObject.transform.SetParent(rootObject.transform, false);
        var fillRect = fillObject.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(2f, 2f);
        fillRect.offsetMax = new Vector2(-2f, -2f);

        var fillImage = fillObject.AddComponent<Image>();
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = 0;
        fillImage.fillAmount = 1f;
        fillImage.color = fillColor;
        return fillImage;
    }

    private Text CreateText(string objectName, Transform parent, Font font, Vector2 anchoredPosition, int fontSize, TextAnchor anchor)
    {
        var textObject = new GameObject(objectName);
        textObject.transform.SetParent(parent, false);

        var rect = textObject.AddComponent<RectTransform>();
        rect.anchorMin = anchor == TextAnchor.UpperCenter ? new Vector2(0.5f, 1f) : new Vector2(0f, 1f);
        rect.anchorMax = rect.anchorMin;
        rect.pivot = anchor == TextAnchor.UpperCenter ? new Vector2(0.5f, 1f) : new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(700f, 120f);

        var text = textObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.alignment = anchor;
        text.color = Color.white;
        return text;
    }

    private void UpdateHud()
    {
        if (scoreText == null || player == null)
        {
            return;
        }

        scoreText.text = $"SCORE {score}\nLEVEL {level}\nENEMIES {enemies.Count}";
        statusText.text = $"Time {elapsedTime:0.0}/{balanceSettings.match.matchDurationSeconds:0}s\nWASD / Arrow move | Auto fire";
        hpFill.fillAmount = Mathf.Clamp01(player.CurrentHealth / player.MaxHealth);
        expFill.fillAmount = Mathf.Clamp01(currentExp / expToNextLevel);
    }

    private void EnterTitle()
    {
        gameState = GameState.Title;
        score = 0;
        elapsedTime = 0f;
        level = 1;
        currentExp = 0f;
        expToNextLevel = balanceSettings.progression.baseExpToNextLevel;
        titleHeadlineText.text = "ENDLESS HUNTER";
        titleBodyText.text = "Defeat enemies, level up, survive to the end\n\nSPACE / ENTER : Start";
        statusText.text = "Defeat enemies, level up, survive until stage clear";
        scoreText.text = "SCORE 0\nLEVEL 1\nENEMIES 0";
        hpFill.fillAmount = 1f;
        expFill.fillAmount = 0f;
        centerMessageText.text = string.Empty;
        centerMessageTimer = 0f;
        SetScreenVisibility(showHud: false, showTitle: true, showResult: false);
    }

    private void StartRun()
    {
        ClearRunObjects();
        score = 0;
        elapsedTime = 0f;
        level = 1;
        currentExp = 0f;
        expToNextLevel = balanceSettings.progression.baseExpToNextLevel;
        spawnTimer = balanceSettings.match.initialSpawnDelay;
        bossSpawnTimer = balanceSettings.match.bossFirstSpawnDelay;
        gameState = GameState.Playing;
        centerMessageText.text = string.Empty;
        centerMessageTimer = 0f;
        SpawnPlayer();
        StartBgm();
        SetScreenVisibility(showHud: true, showTitle: false, showResult: false);
        UpdateHud();
    }

    private void AddExperience(float gained)
    {
        currentExp += gained;
        while (currentExp >= expToNextLevel)
        {
            currentExp -= expToNextLevel;
            LevelUp();
        }
    }

    private void LevelUp()
    {
        level++;
        expToNextLevel = balanceSettings.progression.baseExpToNextLevel + level * balanceSettings.progression.expIncrementPerLevel;
        player.ApplyLevelUp(level);
        score += 25;
        PlaySfx(levelUpClip, 0.45f, 1f);
        ShowCenterMessage($"LEVEL UP {level}", 1.8f);
    }

    private void ClearRunObjects()
    {
        if (player != null)
        {
            Destroy(player.gameObject);
            player = null;
        }

        for (var i = enemies.Count - 1; i >= 0; i--)
        {
            if (enemies[i] != null)
            {
                ReleaseEnemy(enemies[i]);
            }
        }

        enemies.Clear();
        projectilePool.ReturnAll();
        effectPool.ReturnAll();
        activeEffects.Clear();
    }

    private void EnterResult(GameState resultState)
    {
        if (resultState != GameState.GameOver && resultState != GameState.Clear)
        {
            return;
        }

        gameState = resultState;
        SetScreenVisibility(showHud: false, showTitle: false, showResult: true);

        if (resultState == GameState.Clear)
        {
            resultHeadlineText.text = "STAGE CLEAR";
            resultHeadlineText.color = new Color(0.3f, 1f, 0.55f);
            PlaySfx(clearClip, 0.62f, 1f);
        }
        else
        {
            resultHeadlineText.text = "GAME OVER";
            resultHeadlineText.color = new Color(1f, 0.35f, 0.35f);
            PlaySfx(gameOverClip, 0.66f, 1f);
        }

        StopBgm();
        var surviveTime = Mathf.Min(elapsedTime, balanceSettings.match.matchDurationSeconds);
        resultBodyText.text = $"Score: {score}\nLevel: {level}\nSurvive: {surviveTime:0.0}s\n\nR / SPACE : Retry";
    }

    private void SetScreenVisibility(bool showHud, bool showTitle, bool showResult)
    {
        if (hudRoot != null)
        {
            hudRoot.SetActive(showHud);
        }

        if (titlePanel != null)
        {
            titlePanel.SetActive(showTitle);
        }

        if (resultPanel != null)
        {
            resultPanel.SetActive(showResult);
        }
    }

    private void ShowCenterMessage(string message, float duration)
    {
        centerMessageText.text = message;
        centerMessageTimer = duration;
    }

    private void TickCenterMessage()
    {
        if (centerMessageTimer < 0f)
        {
            return;
        }

        if (centerMessageTimer <= 0f)
        {
            centerMessageText.text = string.Empty;
            return;
        }

        centerMessageTimer -= Time.deltaTime;
    }

    private void StartBgm()
    {
        if (bgmSource == null || bgmClip == null)
        {
            return;
        }

        bgmSource.clip = bgmClip;
        bgmSource.pitch = 1f;
        if (!bgmSource.isPlaying)
        {
            bgmSource.Play();
        }
    }

    private void StopBgm()
    {
        if (bgmSource != null && bgmSource.isPlaying)
        {
            bgmSource.Stop();
        }
    }

    private void PlaySfx(AudioClip clip, float volume, float pitch)
    {
        if (sfxSource == null || clip == null)
        {
            return;
        }

        sfxSource.pitch = pitch;
        sfxSource.PlayOneShot(clip, volume);
    }

    private AudioClip CreateSfxClip(string clipName, float seconds, float frequency, float amplitude, float decay)
    {
        const int sampleRate = 44100;
        var sampleCount = Mathf.Max(1, Mathf.RoundToInt(seconds * sampleRate));
        var data = new float[sampleCount];

        for (var i = 0; i < sampleCount; i++)
        {
            var t = i / (float)sampleRate;
            var envelope = Mathf.Exp(-t / Mathf.Max(0.0001f, decay));
            var wave = Mathf.Sin(2f * Mathf.PI * frequency * t);
            var overtone = Mathf.Sin(2f * Mathf.PI * frequency * 2.01f * t) * 0.35f;
            data[i] = (wave + overtone) * amplitude * envelope;
        }

        var clip = AudioClip.Create(clipName, sampleCount, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private AudioClip CreateChordClip(string clipName, float seconds, float[] frequencies, float amplitude)
    {
        const int sampleRate = 44100;
        var sampleCount = Mathf.Max(1, Mathf.RoundToInt(seconds * sampleRate));
        var data = new float[sampleCount];
        var invCount = frequencies.Length > 0 ? 1f / frequencies.Length : 1f;

        for (var i = 0; i < sampleCount; i++)
        {
            var t = i / (float)sampleRate;
            var envelope = Mathf.Clamp01(1f - t / seconds);
            var sample = 0f;

            for (var j = 0; j < frequencies.Length; j++)
            {
                sample += Mathf.Sin(2f * Mathf.PI * frequencies[j] * t);
            }

            data[i] = sample * invCount * amplitude * envelope;
        }

        var clip = AudioClip.Create(clipName, sampleCount, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private AudioClip CreateMusicClip(string clipName, float seconds, float bpm, float amplitude)
    {
        const int sampleRate = 44100;
        var sampleCount = Mathf.Max(1, Mathf.RoundToInt(seconds * sampleRate));
        var data = new float[sampleCount];
        var beatSeconds = 60f / bpm;
        var bassSequence = new[] { 55f, 55f, 65.41f, 73.42f, 82.41f, 65.41f, 73.42f, 55f };
        var leadSequence = new[] { 220f, 246.94f, 261.63f, 293.66f, 329.63f, 293.66f, 261.63f, 246.94f };

        for (var i = 0; i < sampleCount; i++)
        {
            var t = i / (float)sampleRate;
            var beatIndex = Mathf.FloorToInt(t / beatSeconds);
            var noteProgress = (t % beatSeconds) / beatSeconds;
            var bassFreq = bassSequence[beatIndex % bassSequence.Length];
            var leadFreq = leadSequence[(beatIndex / 2) % leadSequence.Length];

            var bass = Mathf.Sin(2f * Mathf.PI * bassFreq * t) * 0.45f;
            var lead = Mathf.Sin(2f * Mathf.PI * leadFreq * t) * 0.25f;
            var arp = Mathf.Sin(2f * Mathf.PI * leadFreq * 2f * t) * 0.12f;
            var kick = Mathf.Exp(-noteProgress * 12f) * (beatIndex % 2 == 0 ? 0.18f : 0.06f);

            data[i] = (bass + lead + arp + kick) * amplitude;
        }

        var clip = AudioClip.Create(clipName, sampleCount, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
