using System;
using UnityEngine;

[CreateAssetMenu(menuName = "EndlessHunter/Game Balance Settings", fileName = "GameBalanceSettings")]
public sealed class GameBalanceSettings : ScriptableObject
{
    [Serializable]
    public sealed class PlayerBalance
    {
        public float baseMaxHealth = 100f;
        public float baseMoveSpeed = 8.5f;
        public float baseShotInterval = 0.2f;
        public float baseProjectileSpeed = 24f;
        public float baseProjectileDamage = 20f;
        public float maxShotInterval = 0.08f;
    }

    [Serializable]
    public sealed class MatchBalance
    {
        public float arenaRadius = 18f;
        public float matchDurationSeconds = 180f;
        public float initialSpawnDelay = 0.2f;
        public float bossFirstSpawnDelay = 30f;
        public float bossSpawnInterval = 45f;
        public float minSpawnInterval = 0.3f;
        public float initialSpawnInterval = 1.2f;
        public float spawnIntervalDecayPerSecond = 0.02f;
    }

    [Serializable]
    public sealed class ProgressionBalance
    {
        public float baseExpToNextLevel = 40f;
        public float expIncrementPerLevel = 25f;
        public float levelUpHealAmount = 18f;
        public float levelUpHealthBonus = 8f;
        public float levelUpMoveSpeedBonus = 0.22f;
        public float levelUpProjectileDamageBonus = 2f;
        public float levelUpProjectileDamagePerLevel = 0.12f;
        public float levelUpProjectileSpeedBonus = 0.32f;
        public float levelUpShotIntervalReduction = 0.008f;
    }

    [Serializable]
    public sealed class PoolingBalance
    {
        public int projectilePoolSize = 240;
        public int enemyPoolSize = 120;
        public int effectPoolSize = 40;
    }

    public PlayerBalance player = new();
    public MatchBalance match = new();
    public ProgressionBalance progression = new();
    public PoolingBalance pooling = new();

    public EnemyTypeDefinition[] enemyTypes;
    public EnemyTypeDefinition bossType;
}
