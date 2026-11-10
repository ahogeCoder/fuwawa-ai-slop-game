using UnityEditor;
using UnityEngine;
#if UNITY_EDITOR
public static class BonkBattleSetup
{
    [MenuItem("BonkBattle/Create Scene & Play")]
    public static void CreateSceneAndPlay()
    {
        var existing = UnityEngine.Object.FindObjectsByType<BonkBattleGameController>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None);
        if (existing.Length == 0)
        {
            var go = new GameObject("BonkBattleGame");
            go.AddComponent<BonkBattleGameController>();
        }

        EditorApplication.EnterPlaymode();
    }

    [MenuItem("BonkBattle/Create GameObject")]
    public static void CreateGameObject()
    {
        var go = new GameObject("BonkBattleGame");
        go.AddComponent<BonkBattleGameController>();
        Selection.activeObject = go;
    }

    [MenuItem("BonkBattle/Check Audio (BaubauVoice)")]
    public static void CheckAudio()
    {
        var clips = Resources.LoadAll<AudioClip>("BaubauVoice");
        if (clips.Length == 0)
            EditorUtility.DisplayDialog("BonkBattle", "Resources/BaubauVoice が空です。\n\nAssets/Audio/BaubauVoice/*.mp3 を Assets/Resources/BaubauVoice/ へコピーしてください。", "OK");
        else
            EditorUtility.DisplayDialog("BonkBattle", $"BaubauVoice を {clips.Length} 件ロードできました:\n" + string.Join("\n", System.Array.ConvertAll(clips, c => "・" + c.name)), "OK");
    }
}
#endif
