using UnityEngine;

[CreateAssetMenu(menuName = "EndlessHunter/Enemy Type", fileName = "EnemyType")]
public sealed class EnemyTypeDefinition : ScriptableObject
{
    [Header("Spawn Weight")]
    [Min(0.01f)] public float baseWeight = 1f;
    [Min(0f)] public float weightGrowthByProgress = 0.5f;

    [Header("Stats")]
    public float baseHealth = 18f;
    public float healthGrowthPerSecond = 0.34f;
    public float baseSpeed = 2.6f;
    public float speedGrowthByProgress = 0.8f;
    public float baseContactDamage = 10f;
    public float damageGrowthByProgress = 8f;
    public float scale = 1.2f;

    [Header("Rewards")]
    public int scoreReward = 10;
    public float expReward = 10f;

    [Header("Visual")]
    public Color color = Color.red;
    public bool randomizeColorPulse;
    public bool isBoss;
}
