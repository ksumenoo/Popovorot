using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// Подготовка к релизу: запекание света и настройки сборки WebGL.
/// Меню Tools → Поповорот → Релиз.
public static class ReleaseTools
{
    const float H = 7f, G = 3.5f, W = 7f, CW = 1.5f;
    const float ZA = 14f, ZB = 22f, ZN = 36f, ZC0 = 44f, ZCN = 58f;

    // ------------------------------------------------------------------
    // Свет
    // ------------------------------------------------------------------
    [MenuItem("Tools/Поповорот/3. Релиз/Запечь свет", false, 20)]
    static void BakeLighting()
    {
        var worlds = GameObject.Find("Worlds");
        if (worlds == null)
        {
            EditorUtility.DisplayDialog("Поповорот", "Сначала соберите уровень и расставьте модели.", "Понятно");
            return;
        }
        if (Lightmapping.isRunning)
        {
            EditorUtility.DisplayDialog("Поповорот", "Запекание уже идёт.", "Понятно");
            return;
        }

        EnableLightmapUVs();

        // Что меняется во время игры: объекты, которые включают и выключают загадки,
        // и предметы, которые можно взять. Они не запекаются, их освещают световые зонды.
        var dynamic = new HashSet<Transform>();
        foreach (var reactor in Object.FindObjectsByType<FlagReactor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            foreach (var go in reactor.on) if (go != null) dynamic.Add(go.transform);
            foreach (var go in reactor.off) if (go != null) dynamic.Add(go.transform);
        }
        foreach (var pickup in Object.FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            dynamic.Add(pickup.transform);

        // Серые кубы, вместо которых при запуске показываются модели. В запекании они
        // не участвуют, иначе модель внутри куба окажется в его тени.
        var art = GameObject.Find("Art");
        var replaced = new HashSet<MeshRenderer>();
        if (art != null)
        {
            foreach (var link in art.GetComponentsInChildren<ArtLink>(true))
            {
                if (!link.hideGreybox) continue;
                Transform target = worlds.transform.Find(link.targetPath);
                if (target == null) continue;
                foreach (var r in target.GetComponentsInChildren<MeshRenderer>(true)) replaced.Add(r);
            }
        }

        int staticCount = 0, probeCount = 0;
        foreach (var renderer in worlds.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (replaced.Contains(renderer))
            {
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, 0);
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveGI = ReceiveGI.LightProbes;
                EditorUtility.SetDirty(renderer);
                continue;
            }
            bool moving = IsUnder(renderer.transform, dynamic);
            if (SetupRenderer(renderer, moving, IsLamp(renderer.transform))) staticCount++; else probeCount++;
        }

        // Модели из корня Art: при запуске ArtLink цепляет их к объектам загадок.
        if (art != null)
        {
            foreach (var link in art.GetComponentsInChildren<ArtLink>(true))
            {
                Transform target = worlds.transform.Find(link.targetPath);
                bool moving = target == null || IsUnder(target, dynamic);
                foreach (var renderer in link.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (SetupRenderer(renderer, moving, IsLamp(target))) staticCount++; else probeCount++;
                }
            }
        }

        // Лампы: свет, который включается по ходу игры, остаётся живым, остальной запекается.
        int baked = 0, realtime = 0;
        foreach (var light in worlds.GetComponentsInChildren<Light>(true))
        {
            if (IsUnder(light.transform, dynamic))
            {
                light.lightmapBakeType = LightmapBakeType.Realtime;
                light.shadows = LightShadows.None;
                realtime++;
            }
            else
            {
                light.lightmapBakeType = LightmapBakeType.Baked;
                light.shadows = LightShadows.Soft;
                light.shadowRadius = 0.15f;
                baked++;
            }
            EditorUtility.SetDirty(light);
        }

        BuildProbes(worlds.transform);
        ApplyLightingSettings();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("Поповорот: запекаемых объектов " + staticCount + ", на зондах " + probeCount +
                  ", ламп запекается " + baked + ", живых " + realtime + ". Запекание началось.");
        Lightmapping.bakeCompleted -= OnBakeCompleted;
        Lightmapping.bakeCompleted += OnBakeCompleted;
        Lightmapping.BakeAsync();
    }

    static void OnBakeCompleted()
    {
        Lightmapping.bakeCompleted -= OnBakeCompleted;
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Debug.Log("Поповорот: свет запечён, сцена сохранена.");
    }

    [MenuItem("Tools/Поповорот/3. Релиз/Убрать запечённый свет", false, 22)]
    static void ClearLighting()
    {
        Lightmapping.Clear();
        Lightmapping.ClearLightingDataAsset();
        var worlds = GameObject.Find("Worlds");
        if (worlds != null)
            foreach (var light in worlds.GetComponentsInChildren<Light>(true))
            {
                light.lightmapBakeType = LightmapBakeType.Realtime;
                light.shadows = LightShadows.None;
            }
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    static bool IsUnder(Transform t, HashSet<Transform> set)
    {
        for (; t != null; t = t.parent) if (set.Contains(t)) return true;
        return false;
    }

    static bool IsLamp(Transform t)
    {
        return t != null && t.name.StartsWith("Lamp_");
    }

    /// Возвращает true, если объект запекается в карту освещения.
    static bool SetupRenderer(MeshRenderer renderer, bool moving, bool lamp)
    {
        var go = renderer.gameObject;
        StaticEditorFlags flags = 0;
        if (!moving) flags |= StaticEditorFlags.BatchingStatic;
        bool gi = !moving && !lamp && HasLightmapUV(go);
        if (gi) flags |= StaticEditorFlags.ContributeGI;
        GameObjectUtility.SetStaticEditorFlags(go, flags);

        renderer.receiveGI = gi ? ReceiveGI.Lightmaps : ReceiveGI.LightProbes;
        renderer.lightProbeUsage = gi ? LightProbeUsage.Off : LightProbeUsage.BlendProbes;
        // Абажуры не должны закрывать свет собственной лампы.
        if (lamp) renderer.shadowCastingMode = ShadowCastingMode.Off;
        EditorUtility.SetDirty(renderer);
        return gi;
    }

    static bool HasLightmapUV(GameObject go)
    {
        var filter = go.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return false;
        Mesh mesh = filter.sharedMesh;
        if (mesh.uv2 != null && mesh.uv2.Length > 0) return true;
        // Сетки, созданные генератором уровня, хранятся в сцене: развёртку можно добавить прямо в них.
        if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mesh)))
        {
            Unwrapping.GenerateSecondaryUVSet(mesh);
            return mesh.uv2 != null && mesh.uv2.Length > 0;
        }
        return false;
    }

    /// Для моделей из Assets/Models включает генерацию второй развёртки (для карт освещения).
    static void EnableLightmapUVs()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Models" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null || importer.generateSecondaryUV) continue;
            importer.generateSecondaryUV = true;
            importer.SaveAndReimport();
        }
    }

    /// Световые зонды: сетка с шагом 2 м по каждой комнате, коридору и мосту, в каждом мире.
    static void BuildProbes(Transform worlds)
    {
        var points = new List<Vector3>();
        float[] roomHeights = { 0.6f, 2.2f, 4.4f, 6.3f };
        AddGrid(points, -W + 1f, W - 1f, 1f, ZA - 1f, roomHeights);
        AddGrid(points, -W + 1f, W - 1f, ZB + 1f, ZN - 1f, roomHeights);
        AddGrid(points, -W + 1f, W - 1f, ZC0 + 1f, ZCN - 1f, roomHeights);
        AddGrid(points, -CW + 0.5f, CW - 0.5f, ZA + 0.5f, ZB - 0.5f, new[] { 0.6f, 2.2f, 3.2f });
        AddGrid(points, -CW + 0.5f, CW - 0.5f, ZN + 0.5f, ZC0 - 0.5f, new[] { 0.6f, 2.2f, 4.4f, 6.3f });

        foreach (Transform world in worlds)
        {
            Transform old = world.Find("LightProbes");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var go = new GameObject("LightProbes");
            go.transform.SetParent(world, false);
            go.AddComponent<LightProbeGroup>().probePositions = points.ToArray();
        }
    }

    static void AddGrid(List<Vector3> points, float x0, float x1, float z0, float z1, float[] heights)
    {
        int nx = Mathf.Max(1, Mathf.RoundToInt((x1 - x0) / 2f));
        int nz = Mathf.Max(1, Mathf.RoundToInt((z1 - z0) / 2f));
        foreach (float y in heights)
            for (int i = 0; i <= nx; i++)
                for (int j = 0; j <= nz; j++)
                    points.Add(new Vector3(Mathf.Lerp(x0, x1, (float)i / nx), y, Mathf.Lerp(z0, z1, (float)j / nz)));
    }

    static void ApplyLightingSettings()
    {
        const string path = "Assets/Settings/Popovorot_Lighting.lighting";
        var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
        if (settings == null)
        {
            settings = new LightingSettings { name = "Popovorot_Lighting" };
            AssetDatabase.CreateAsset(settings, path);
        }
        settings.bakedGI = true;
        settings.realtimeGI = false;
        settings.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
        settings.lightmapResolution = 6f;       // текселей на метр: комнаты большие, свет мягкий
        settings.lightmapPadding = 2;
        settings.lightmapMaxSize = 1024;
        settings.lightmapCompression = LightmapCompression.NormalQuality;
        settings.directionalityMode = LightmapsMode.NonDirectional;   // вдвое меньше текстур
        settings.ao = true;
        settings.aoMaxDistance = 1f;
        settings.maxBounces = 2;
        settings.directSampleCount = 32;
        settings.indirectSampleCount = 256;
        settings.environmentSampleCount = 128;
        settings.filteringMode = LightingSettings.FilterMode.Auto;
        EditorUtility.SetDirty(settings);
        Lightmapping.lightingSettings = settings;
    }

    // ------------------------------------------------------------------
    // Сборка WebGL
    // ------------------------------------------------------------------
    [MenuItem("Tools/Поповорот/3. Релиз/Настроить сборку WebGL", false, 21)]
    static void SetupWebGL()
    {
        var target = NamedBuildTarget.WebGL;

        // Сжатие с запасным распаковщиком: игра запустится, даже если сервер
        // не отдаёт нужные заголовки для сжатых файлов.
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
        PlayerSettings.WebGL.nameFilesAsHashes = true;
        // Шаблон страницы с SDK Яндекс Игр: игра на всё окно, пауза и звук по событиям площадки.
        PlayerSettings.WebGL.template = "PROJECT:Yandex";
        PlayerSettings.productName = "Вот это поповорот";

        PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetManagedStrippingLevel(target, ManagedStrippingLevel.Medium);
        PlayerSettings.SetIl2CppCodeGeneration(target, Il2CppCodeGeneration.OptimizeSize);
        PlayerSettings.stripEngineCode = true;
        PlayerSettings.gcIncremental = true;
        // Во вкладке, которая не видна, игра и звук должны вставать на паузу (требование площадок).
        PlayerSettings.runInBackground = false;

        // Тени в реальном времени не нужны: тени запечены, у живых ламп теней нет.
        foreach (string guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset", new[] { "Assets/Settings" }))
        {
            var asset = AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(guid));
            var so = new SerializedObject(asset);
            SetBool(so, "m_MainLightShadowsSupported", false);
            SetBool(so, "m_AdditionalLightShadowsSupported", false);
            SetBool(so, "m_SoftShadowsSupported", false);
            SetBool(so, "m_SupportsHDR", false);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // Звук: короткие эффекты распаковываются при загрузке, длинный фон читается сжатым.
        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Resources/Audio" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) continue;
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            var sample = importer.defaultSampleSettings;
            sample.compressionFormat = AudioCompressionFormat.Vorbis;
            sample.quality = 0.5f;
            sample.loadType = clip != null && clip.length > 5f
                ? AudioClipLoadType.CompressedInMemory
                : AudioClipLoadType.DecompressOnLoad;
            importer.defaultSampleSettings = sample;
            importer.forceToMono = true;
            importer.SaveAndReimport();
        }

        // Камера не дотягивается до соседних миров (они в 100 м), поэтому их не рисует.
        foreach (var camera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            if (camera.CompareTag("MainCamera")) { camera.farClipPlane = 85f; EditorUtility.SetDirty(camera); }
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Поповорот",
            "Настройки WebGL применены.\nПлатформу переключите сами: File → Build Profiles → Web → Switch Platform " +
            "(переключение долгое, поэтому не делаю его автоматически).", "Понятно");
    }

    // ------------------------------------------------------------------
    // Сборка и архив для загрузки в консоль Яндекс Игр
    // ------------------------------------------------------------------
    const string BuildFolder = "Builds/popovorot_web";   // латиница без пробелов — требование площадки

    [MenuItem("Tools/Поповорот/3. Релиз/Собрать архив для Яндекс Игр", false, 23)]
    static void BuildForYandex()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL &&
            !EditorUtility.DisplayDialog("Поповорот",
                "Сейчас выбрана не платформа Web. Unity переключится на неё перед сборкой — это может занять несколько минут.",
                "Собрать", "Отмена"))
            return;

        var scenes = new List<string>();
        foreach (var scene in EditorBuildSettings.scenes) if (scene.enabled) scenes.Add(scene.path);
        if (scenes.Count == 0)
        {
            EditorUtility.DisplayDialog("Поповорот", "В списке сборки нет сцен.", "Понятно");
            return;
        }

        string projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
        string output = System.IO.Path.Combine(projectRoot, BuildFolder);
        if (System.IO.Directory.Exists(output)) System.IO.Directory.Delete(output, true);

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes.ToArray(),
            locationPathName = output,
            target = BuildTarget.WebGL,
            options = BuildOptions.None,
        });
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            EditorUtility.DisplayDialog("Поповорот", "Сборка не удалась: " + report.summary.result +
                ". Подробности — в консоли.", "Понятно");
            return;
        }

        // Архив: index.html лежит в корне архива, как требует площадка.
        string zip = output + ".zip";
        if (System.IO.File.Exists(zip)) System.IO.File.Delete(zip);
        System.IO.Compression.ZipFile.CreateFromDirectory(output, zip);

        long bytes = 0;
        foreach (var file in System.IO.Directory.GetFiles(output, "*", System.IO.SearchOption.AllDirectories))
            bytes += new System.IO.FileInfo(file).Length;
        EditorUtility.RevealInFinder(zip);
        EditorUtility.DisplayDialog("Поповорот",
            "Готово: " + BuildFolder + ".zip\nРазмер файлов игры: " + (bytes / 1048576f).ToString("0.0") +
            " МБ (лимит площадки — 100 МБ).\nЗагрузите архив в черновик игры в консоли Яндекс Игр.", "Понятно");
    }

    static void SetBool(SerializedObject so, string name, bool value)
    {
        var p = so.FindProperty(name);
        if (p != null) p.boolValue = value;
    }
}
