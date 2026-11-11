#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public static class OrthodoxSetup
{
    public static string Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/BonkBattle.unity", OpenSceneMode.Single);
        EnsureFolders();
        EnsureMaterials();
        EnsureCoreObjects();
        EnsureCanvas();
        EnsureArena();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return "orthodox scene setup done";
    }

    static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
    }

    static Material Mat(string name, Color c, string shaderName = "Universal Render Pipeline/Lit", bool transparent = false)
    {
        var path = "Assets/Materials/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        var sh = Shader.Find(shaderName);
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Standard");
        m = new Material(sh);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
        if (transparent)
        {
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_SrcBlend")) m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);
            m.renderQueue = 3000;
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static void EnsureMaterials()
    {
        Mat("ArenaFloor", new Color(0.09f, 0.11f, 0.15f));
        Mat("ArenaInner", new Color(0.14f, 0.22f, 0.32f));
        Mat("ArenaCenterMat", new Color(0.25f, 0.45f, 0.65f));
        Mat("ArenaRim", new Color(0.45f, 0.15f, 0.35f));
        Mat("PillarA", new Color(0.3f, 0.35f, 0.5f));
        Mat("PillarB", new Color(0.2f, 0.25f, 0.35f));
        Mat("BeaconMat", new Color(1f, 0.6f, 0.95f));
        Mat("ArenaBaseMat", new Color(0.25f, 0.45f, 0.25f));
        // FX mats may already exist
        Mat("FX_Shock_Unlit", new Color(1f,0.65f,0.25f,0.55f), "Universal Render Pipeline/Unlit", true);
        Mat("FX_Slash_Unlit", new Color(1f,0.98f,0.55f,0.9f), "Universal Render Pipeline/Unlit", true);
        Mat("FX_Burst_Unlit", new Color(1f,0.85f,0.3f,1f), "Universal Render Pipeline/Unlit", true);
    }

    static GameObject Ensure(string name, Transform parent = null)
    {
        var go = GameObject.Find(name);
        if (go == null) go = new GameObject(name);
        if (parent != null && go.transform.parent != parent) go.transform.SetParent(parent, false);
        return go;
    }

    static void EnsureCoreObjects()
    {
        var camGo = GameObject.Find("Main Camera");
        if (camGo == null) { camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera"; camGo.AddComponent<Camera>(); camGo.AddComponent<AudioListener>(); }
        var cam = camGo.GetComponent<Camera>();
        if (cam == null) cam = camGo.AddComponent<Camera>();
        camGo.transform.position = new Vector3(0f, 24f, -16f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.07f, 0.09f, 0.14f);
        cam.fieldOfView = 62f;
        camGo.transform.LookAt(new Vector3(0f, 0.3f, 0f));

        var lightGo = GameObject.Find("Directional Light");
        if (lightGo == null) { lightGo = new GameObject("Directional Light"); lightGo.AddComponent<Light>(); }
        var light = lightGo.GetComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.35f; light.shadows = LightShadows.None;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var ambGo = GameObject.Find("Ambient Light");
        if (ambGo == null) { ambGo = new GameObject("Ambient Light"); ambGo.AddComponent<Light>(); }
        var amb = ambGo.GetComponent<Light>();
        amb.type = LightType.Directional; amb.intensity = 0.35f; amb.color = new Color(0.5f, 0.6f, 1f); amb.shadows = LightShadows.None;
        ambGo.transform.rotation = Quaternion.Euler(-30f, 140f, 0f);

        if (Object.FindObjectOfType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        var world = Ensure("WorldRoot");
        var arena = Ensure("ArenaRoot", world.transform);
        var fx = Ensure("FXRoot", world.transform);
        var spawn = Ensure("SpawnRoot", world.transform);
        var audio = Ensure("AudioRoot");
        if (audio.GetComponent<AudioSource>() == null) { }
    }

    static void FullRect(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; }

    static Text MakeText(string name, Transform parent, Vector2 anchoredPos, int fontSize, TextAnchor anchor, Color color, Vector2 size)
    {
        var go = GameObject.Find(name);
        if (go == null) { go = new GameObject(name); go.transform.SetParent(parent, false); }
        else if (go.transform.parent != parent) go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>(); if (rt == null) rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f); rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = anchoredPos; rt.sizeDelta = size;
        var t = go.GetComponent<Text>(); if (t == null) t = go.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        t.fontSize = fontSize; t.alignment = anchor; t.color = color;
        return t;
    }

    static Image MakeBar(string name, Transform parent, Vector2 pos, Color col, float w, float h)
    {
        var root = GameObject.Find(name);
        if (root == null) { root = new GameObject(name); root.transform.SetParent(parent, false); }
        else if (root.transform.parent != parent) root.transform.SetParent(parent, false);
        var rrt = root.GetComponent<RectTransform>(); if (rrt == null) rrt = root.AddComponent<RectTransform>();
        rrt.anchorMin = new Vector2(0.5f,1f); rrt.anchorMax = new Vector2(0.5f,1f); rrt.pivot = new Vector2(0.5f,1f);
        rrt.anchoredPosition = pos; rrt.sizeDelta = new Vector2(w,h);
        var bg = root.GetComponent<Image>(); if (bg == null) bg = root.AddComponent<Image>(); bg.color = new Color(0,0,0,0.5f);
        var fillName = name + "Fill";
        var fillGo = root.transform.Find(fillName)?.gameObject;
        if (fillGo == null) { fillGo = new GameObject(fillName); fillGo.transform.SetParent(root.transform, false); }
        var frt = fillGo.GetComponent<RectTransform>(); if (frt == null) frt = fillGo.AddComponent<RectTransform>();
        frt.anchorMin = new Vector2(0,0.5f); frt.anchorMax = new Vector2(0,0.5f); frt.pivot = new Vector2(0,0.5f);
        frt.anchoredPosition = new Vector2(2,0); frt.sizeDelta = new Vector2(w-4,h-4);
        var fi = fillGo.GetComponent<Image>(); if (fi == null) fi = fillGo.AddComponent<Image>(); fi.color = col; fi.type = Image.Type.Simple;
        return fi;
    }

    static void EnsureCanvas()
    {
        var canvasGo = GameObject.Find("Canvas");
        if (canvasGo == null) canvasGo = new GameObject("Canvas");
        var canvas = canvasGo.GetComponent<Canvas>(); if (canvas == null) canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>(); if (scaler == null) scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1280,720); scaler.matchWidthOrHeight = 0.5f;
        if (canvasGo.GetComponent<GraphicRaycaster>() == null) canvasGo.AddComponent<GraphicRaycaster>();

        var flash = GameObject.Find("Flash") ?? new GameObject("Flash");
        flash.transform.SetParent(canvasGo.transform, false);
        var frt = flash.GetComponent<RectTransform>() ?? flash.AddComponent<RectTransform>(); FullRect(frt);
        var fimg = flash.GetComponent<Image>() ?? flash.AddComponent<Image>(); fimg.color = new Color(1,1,1,0); fimg.raycastTarget = false;

        var hudRoot = GameObject.Find("HUDRoot") ?? new GameObject("HUDRoot");
        hudRoot.transform.SetParent(canvasGo.transform, false);
        var hrt = hudRoot.GetComponent<RectTransform>() ?? hudRoot.AddComponent<RectTransform>(); FullRect(hrt);

        MakeText("Timer", hudRoot.transform, new Vector2(0,-18), 44, TextAnchor.MiddleCenter, new Color(1f,0.92f,0.25f), new Vector2(400,70));
        MakeText("Count", hudRoot.transform, new Vector2(-260,-18), 34, TextAnchor.MiddleCenter, Color.white, new Vector2(380,70));
        MakeText("Combo", hudRoot.transform, new Vector2(260,-18), 34, TextAnchor.MiddleCenter, new Color(1f,0.6f,0.85f), new Vector2(380,70));
        MakeBar("TimerBar", hudRoot.transform, new Vector2(0,-70), new Color(1f,0.45f,0.55f), 520,16);
        var tl = MakeText("TimerLabel", hudRoot.transform, new Vector2(-370,-65), 20, TextAnchor.MiddleCenter, new Color(1f,0.55f,0.6f), new Vector2(190,26)); tl.text = "TIME";
        MakeBar("CooldownBar", hudRoot.transform, new Vector2(0,-95), new Color(0.55f,0.95f,1f), 520,10);
        var cl = MakeText("CooldownLabel", hudRoot.transform, new Vector2(-370,-87), 20, TextAnchor.MiddleCenter, new Color(0.6f,0.95f,1f), new Vector2(190,26)); cl.text = "ATTACK";
        MakeBar("HPBar", hudRoot.transform, new Vector2(0,-120), new Color(0.35f,0.95f,0.45f), 520,20);
        var hl = MakeText("HPLabel", hudRoot.transform, new Vector2(-370,-117), 20, TextAnchor.MiddleCenter, new Color(0.5f,1f,0.6f), new Vector2(190,26)); hl.text = "HP";
        MakeText("HPText", hudRoot.transform, new Vector2(310,-120), 24, TextAnchor.MiddleLeft, new Color(0.6f,1f,0.7f), new Vector2(180,30));

        GameObject Panel(string name, Color c) {
            var go = GameObject.Find(name) ?? new GameObject(name);
            go.transform.SetParent(canvasGo.transform, false);
            var rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>(); FullRect(rt);
            var img = go.GetComponent<Image>() ?? go.AddComponent<Image>(); img.color = c; return go;
        }
        Text CenterText(string name, Transform p, Vector2 pos, int size, Color col, string txt, Vector2 sz) {
            var t = MakeText(name, p, pos, size, TextAnchor.MiddleCenter, col, sz);
            var rt = t.rectTransform; rt.anchorMin=new Vector2(0.5f,0.5f); rt.anchorMax=new Vector2(0.5f,0.5f); rt.pivot=new Vector2(0.5f,0.5f);
            rt.anchoredPosition = pos; t.text = txt; t.horizontalOverflow=HorizontalWrapMode.Wrap; t.verticalOverflow=VerticalWrapMode.Overflow; return t;
        }
        var title = Panel("TitlePanel", new Color(0,0,0,0.45f));
        CenterText("TitleHead", title.transform, new Vector2(0,150), 54, new Color(1f,0.55f,0.85f), "BAUBAU BONK BATTLE", new Vector2(900,90));
        var tb = CenterText("TitleBody", title.transform, new Vector2(0,-60), 24, Color.white, "Fuwawa Bonk Battle\nMove: WASD/Arrows  Bonk: Space/J/Click\nSPACE/ENTER/Click: Start", new Vector2(900,200));
        var result = Panel("ResultPanel", new Color(0,0,0,0.55f));
        CenterText("ResultHead", result.transform, new Vector2(0,130), 54, Color.white, "TIME UP!", new Vector2(900,90));
        CenterText("ResultBody", result.transform, new Vector2(0,-20), 34, new Color(0.95f,0.95f,0.95f), "", new Vector2(900,260));
    }

    static void SetMat(GameObject go, string matName)
    {
        var r = go.GetComponent<Renderer>();
        var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/" + matName + ".mat");
        if (r != null && m != null) r.sharedMaterial = m;
        var col = go.GetComponent<Collider>(); if (col != null) Object.DestroyImmediate(col);
    }

    static void EnsureArena()
    {
        var arena = GameObject.Find("ArenaRoot");
        if (arena == null) return;
        // keep simple: if already has children assume built
        if (arena.transform.childCount > 5) return;
        // clear
        for (int i = arena.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(arena.transform.GetChild(i).gameObject);
        float arenaRadius = 12f;
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder); floor.name="ArenaFloor"; floor.transform.SetParent(arena.transform,false);
        floor.transform.localPosition=Vector3.zero; floor.transform.localScale=new Vector3(arenaRadius*2f,0.24f,arenaRadius*2f); SetMat(floor,"ArenaFloor");
        var inner = GameObject.CreatePrimitive(PrimitiveType.Cylinder); inner.name="ArenaInner"; inner.transform.SetParent(arena.transform,false);
        inner.transform.localPosition=new Vector3(0f,0.12f,0f); inner.transform.localScale=new Vector3((arenaRadius-2f)*2f,0.06f,(arenaRadius-2f)*2f); SetMat(inner,"ArenaInner");
        var center = GameObject.CreatePrimitive(PrimitiveType.Cylinder); center.name="ArenaCenter"; center.transform.SetParent(arena.transform,false);
        center.transform.localPosition=new Vector3(0f,0.16f,0f); center.transform.localScale=new Vector3(5f,0.08f,5f); SetMat(center,"ArenaCenterMat");
        var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder); rim.name="ArenaRim"; rim.transform.SetParent(arena.transform,false);
        rim.transform.localPosition=new Vector3(0f,0.35f,0f); rim.transform.localScale=new Vector3((arenaRadius+0.6f)*2f,0.32f,(arenaRadius+0.6f)*2f); SetMat(rim,"ArenaRim");
        for (int i=0;i<12;i++){
            float a=i*Mathf.PI*2f/12f; var pos=new Vector3(Mathf.Cos(a)*(arenaRadius-1f),1f,Mathf.Sin(a)*(arenaRadius-1f));
            var pillar=GameObject.CreatePrimitive(PrimitiveType.Cylinder); pillar.name="Pillar"; pillar.transform.SetParent(arena.transform,false);
            pillar.transform.localPosition=pos; pillar.transform.localScale=new Vector3(0.7f,1.8f,0.7f); SetMat(pillar, i%2==0?"PillarA":"PillarB");
            var beacon=GameObject.CreatePrimitive(PrimitiveType.Sphere); beacon.name="Beacon"; beacon.transform.SetParent(arena.transform,false);
            beacon.transform.localPosition=pos+Vector3.up*1.6f; beacon.transform.localScale=Vector3.one*0.32f; SetMat(beacon,"BeaconMat");
        }
    }
}
#endif
