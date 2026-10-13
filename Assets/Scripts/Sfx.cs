using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// Звуки игры. Клипы лежат в Assets/Resources/Audio и находятся по имени файла,
/// поэтому звук можно заменить, просто положив свой файл с тем же именем.
/// Объект создаётся сам при загрузке сцены, в сцену его добавлять не нужно.
public class Sfx : MonoBehaviour
{
    static Sfx instance;
    static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    static readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();

    // Флаги загадок, смена которых сопровождается звуком.
    static readonly string[] Watched =
    {
        "cage_open", "power", "turn_code", "gate_timer", "gate_fixed", "locker_open", "shutter_open",
        "safe_open", "chain_cut", "tower_down", "tansu_open", "bridge_power", "bridge_open", "lanterns_lit",
        "lock_main", "lock_cartoon", "lock_upside", "lock_japanese", "door_open"
    };

    // Высота звука перехода и фонового гула для каждого мира.
    static readonly float[] SwitchPitch = { 1.0f, 1.25f, 0.8f, 1.1f };
    static readonly float[] AmbiencePitch = { 1.0f, 1.5f, 0.75f, 1.2f };

    [Range(0f, 1f)] public float ambienceVolume = 0.25f;
    [Range(0f, 1f)] public float musicVolume = 0.7f;

    AudioSource oneShot;
    AudioSource loop;
    readonly Dictionary<string, bool> state = new Dictionary<string, bool>();
    float nextTick;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Ensure();
    }

    static void Ensure()
    {
        if (instance == null) instance = new GameObject("Sfx").AddComponent<Sfx>();
    }

    /// Проиграть звук по имени файла из Resources/Audio.
    public static void Play(string name, float volume = 1f, float pitch = 1f)
    {
        Ensure();

        // Один и тот же звук не чаще, чем раз в 0,08 секунды.
        if (lastPlayed.TryGetValue(name, out float last) && Time.unscaledTime - last < 0.08f) return;
        lastPlayed[name] = Time.unscaledTime;

        if (!clips.TryGetValue(name, out AudioClip clip))
        {
            clip = Resources.Load<AudioClip>("Audio/" + name);
            clips[name] = clip;
            if (clip == null) Debug.LogWarning("Sfx: нет звука Resources/Audio/" + name);
        }
        if (clip == null) return;

        instance.oneShot.pitch = pitch;
        instance.oneShot.PlayOneShot(clip, volume);
    }

    /// Включить музыку вместо фонового гула. Пустое имя возвращает гул.
    public static void Music(string name)
    {
        Ensure();
        instance.SetMusic(name);
    }

    void SetMusic(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            var wm = WorldManager.Instance;
            loop.clip = Resources.Load<AudioClip>("Audio/ambience");
            loop.volume = ambienceVolume;
            loop.pitch = wm != null ? AmbiencePitch[wm.Current] : 1f;
        }
        else
        {
            var clip = Resources.Load<AudioClip>("Audio/" + name);
            if (clip == null)
            {
                Debug.LogWarning("Sfx: нет музыки Resources/Audio/" + name);
                return;
            }
            loop.clip = clip;
            loop.volume = musicVolume;
            loop.pitch = 1f;
        }
        if (loop.clip != null) loop.Play();
    }

    void Awake()
    {
        oneShot = gameObject.AddComponent<AudioSource>();
        oneShot.playOnAwake = false;
        loop = gameObject.AddComponent<AudioSource>();
        loop.playOnAwake = false;
        loop.loop = true;
        loop.volume = ambienceVolume;
    }

    void Start()
    {
        foreach (string flag in Watched) state[flag] = GameState.Has(flag);
        GameState.Changed += OnFlags;

        var wm = WorldManager.Instance;
        if (wm != null)
        {
            wm.WorldChanged += OnWorld;
            loop.clip = Resources.Load<AudioClip>("Audio/ambience");
            if (loop.clip != null) loop.Play();
        }
    }

    void OnDestroy()
    {
        GameState.Changed -= OnFlags;
        if (WorldManager.Instance != null) WorldManager.Instance.WorldChanged -= OnWorld;
    }

    void OnWorld(int world)
    {
        Play("switch", 0.8f, SwitchPitch[world]);
        loop.pitch = AmbiencePitch[world];
    }

    void OnFlags()
    {
        foreach (string flag in Watched)
        {
            bool now = GameState.Has(flag);
            bool was = state[flag];
            state[flag] = now;
            if (now == was) continue;

            if (now)
            {
                if (flag == "door_open") Play("win");
                else if (flag == "gate_timer" || flag == "gate_fixed") Play("gate");
                else Play("solve");
            }
            else if (flag == "gate_timer" && !GameState.Has("gate_fixed"))
            {
                Play("gate", 1f, 0.8f);   // решётка закрылась
            }
        }
    }

    void Update()
    {
        // Тиканье, пока идёт таймер; в последние две секунды вдвое чаще.
        float remaining = GameState.TimerRemaining;
        if (remaining <= 0f)
        {
            nextTick = 0f;
            return;
        }
        nextTick -= Time.deltaTime;
        if (nextTick <= 0f)
        {
            bool hurry = remaining < 2f;
            Play("tick", 0.7f, hurry ? 1.3f : 1f);
            nextTick = hurry ? 0.25f : 0.5f;
        }
    }
}
