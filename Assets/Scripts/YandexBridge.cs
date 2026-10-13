using System;
using System.Runtime.InteropServices;
using UnityEngine;

/// Связь с SDK Яндекс Игр. Сам SDK подключает HTML-шаблон Assets/WebGLTemplates/Yandex,
/// а вызовы идут через Assets/Plugins/WebGL/YandexBridge.jslib.
/// В редакторе и в сборках не для браузера все методы ничего не делают.
public class YandexBridge : MonoBehaviour
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void YaReady();
    [DllImport("__Internal")] static extern void YaGameplayStart();
    [DllImport("__Internal")] static extern void YaGameplayStop();
    [DllImport("__Internal")] static extern void YaSave(string json);
    [DllImport("__Internal")] static extern void YaRequestLoad();
#endif

    static YandexBridge instance;
    static bool readySent;
    static bool gameplayRunning;
    static bool platformPaused;
    static float timeScaleBeforePause = 1f;

    /// Облачное сохранение пришло (или выяснилось, что облака нет).
    public static bool CloudLoaded { get; private set; }
    /// Строка облачного сохранения; пустая, если его нет.
    public static string CloudSave { get; private set; } = "";
    public static event Action CloudChanged;

    /// Площадка поставила игру на паузу (реклама, свёрнутая вкладка).
    public static bool PlatformPaused { get { return platformPaused; } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Create()
    {
        if (instance != null) return;
        var go = new GameObject("YandexBridge");   // имя важно: JS обращается к объекту по нему
        DontDestroyOnLoad(go);
        instance = go.AddComponent<YandexBridge>();
#if UNITY_WEBGL && !UNITY_EDITOR
        YaRequestLoad();
#else
        CloudLoaded = true;   // без облака: только локальное сохранение
#endif
    }

    /// Игра загрузилась и готова к игре (главное меню открыто).
    public static void Ready()
    {
        if (readySent) return;
        readySent = true;
#if UNITY_WEBGL && !UNITY_EDITOR
        YaReady();
#endif
    }

    /// Начался или продолжился геймплей.
    public static void GameplayStart()
    {
        if (gameplayRunning) return;
        gameplayRunning = true;
#if UNITY_WEBGL && !UNITY_EDITOR
        YaGameplayStart();
#endif
    }

    /// Геймплей остановлен: пауза, меню, финал.
    public static void GameplayStop()
    {
        if (!gameplayRunning) return;
        gameplayRunning = false;
#if UNITY_WEBGL && !UNITY_EDITOR
        YaGameplayStop();
#endif
    }

    public static void SaveCloud(string json)
    {
        CloudSave = json ?? "";
#if UNITY_WEBGL && !UNITY_EDITOR
        YaSave(CloudSave);
#endif
    }

    // ----- вызовы из JavaScript (SendMessage) -----

    void OnCloudData(string json)
    {
        CloudSave = json ?? "";
        CloudLoaded = true;
        CloudChanged?.Invoke();
    }

    void OnPlatformPause()
    {
        if (platformPaused) return;
        platformPaused = true;
        timeScaleBeforePause = Time.timeScale;
        Time.timeScale = 0f;
        AudioListener.pause = true;
    }

    void OnPlatformResume()
    {
        if (!platformPaused) return;
        platformPaused = false;
        Time.timeScale = Hints.Paused ? 0f : timeScaleBeforePause;
        AudioListener.pause = false;
    }
}
