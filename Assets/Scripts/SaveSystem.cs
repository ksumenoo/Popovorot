using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// Сохранение прогресса: решённые загадки, сумка, потраченные предметы, место игрока.
/// Пишется в PlayerPrefs (в браузере это хранилище страницы) и в облако Яндекс Игр,
/// поэтому прогресс переживает обновление страницы и переход на другое устройство.
public static class SaveSystem
{
    const string Key = "popovorot_save";

    [Serializable]
    public class Data
    {
        public int version = 1;
        public long savedAt;          // время записи, чтобы выбрать более свежую из двух копий
        public string[] flags;
        public string[] bag;
        public int selected;
        public string[] used;
        public float x, y, z;
        public float yaw, pitch;
        public float playTime;
    }

    /// Сохранение, которое нужно применить при загрузке сцены с игрой.
    public static Data Pending { get; private set; }

    public static bool HasSave { get { return Current() != null; } }

    static Data Parse(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var data = JsonUtility.FromJson<Data>(json);
            return data != null && data.flags != null ? data : null;
        }
        catch (Exception) { return null; }
    }

    /// Самая свежая копия: локальная или облачная.
    static Data Current()
    {
        Data local = Parse(PlayerPrefs.GetString(Key, ""));
        if (!YandexBridge.CloudLoaded) return local;
        Data cloud = Parse(YandexBridge.CloudSave);
        if (cloud == null) return local;
        if (local == null) return cloud;
        return cloud.savedAt > local.savedAt ? cloud : local;
    }

    public static void Write(Data data)
    {
        data.savedAt = DateTime.UtcNow.Ticks;
        string json = JsonUtility.ToJson(data);
        PlayerPrefs.SetString(Key, json);
        PlayerPrefs.Save();
        YandexBridge.SaveCloud(json);
    }

    /// Прогресс сброшен (новая игра или победа): уходящая сцена не должна записать его обратно.
    public static bool Discarded { get; set; }

    public static void Clear()
    {
        Pending = null;
        Discarded = true;
        PlayerPrefs.DeleteKey(Key);
        PlayerPrefs.Save();
        YandexBridge.SaveCloud("");
    }

    /// «Продолжить» в меню: флаги ставятся сразу, остальное — когда загрузится сцена.
    public static void PrepareContinue()
    {
        Pending = Current();
        GameState.RestoreSilently(Pending != null ? Pending.flags : null);
    }

    public static void PrepareNewGame()
    {
        Clear();
        GameState.ClearFlags();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // Сцена с игрой узнаётся по игроку: на него вешается автосохранение.
    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        if (player != null && player.GetComponent<ProgressSaver>() == null)
            player.gameObject.AddComponent<ProgressSaver>();
    }

    public static void ConsumePending()
    {
        Pending = null;
    }
}

/// Висит на игроке: применяет сохранение при старте и пишет прогресс по ходу игры.
public class ProgressSaver : MonoBehaviour
{
    const float MinInterval = 5f;        // не чаще раза в 5 с: у облака лимит 100 записей за 5 минут
    const float PositionInterval = 20f;  // место игрока сохраняется раз в 20 с

    Interactor interactor;
    PlayerController controller;
    bool dirty;
    bool finished;
    float lastSave;
    float startTime;

    IEnumerator Start()
    {
        interactor = GetComponent<Interactor>();
        controller = GetComponent<PlayerController>();
        Interactor.TimeOffset = 0f;
        startTime = Time.time;

        // Ждём кадр, чтобы все объекты загадок успели включиться по флагам.
        yield return null;
        Apply(SaveSystem.Pending);
        SaveSystem.ConsumePending();
        SaveSystem.Discarded = false;

        GameState.Changed += MarkDirty;
        Interactor.BagChanged += MarkDirty;
        lastSave = Time.unscaledTime;
    }

    void OnDestroy()
    {
        GameState.Changed -= MarkDirty;
        Interactor.BagChanged -= MarkDirty;
        if (!finished && !SaveSystem.Discarded && !GameState.Has("door_open") && Time.time - startTime > 3f) Save();
    }

    void MarkDirty() { dirty = true; }

    void Update()
    {
        if (GameState.Has("door_open") || SaveSystem.Discarded) { finished = GameState.Has("door_open"); return; }
        float since = Time.unscaledTime - lastSave;
        if ((dirty && since >= MinInterval) || since >= PositionInterval) Save();
    }

    void Save()
    {
        if (interactor == null || controller == null) return;
        Vector3 p = transform.position;
        SaveSystem.Write(new SaveSystem.Data
        {
            flags = GameState.Snapshot(),
            bag = interactor.BagIds(),
            selected = interactor.Selected,
            used = interactor.UsedItems.ToArray(),
            x = p.x, y = p.y, z = p.z,
            yaw = controller.LocalYaw,
            pitch = controller.Pitch,
            playTime = Interactor.TimeOffset + Time.timeSinceLevelLoad,
        });
        dirty = false;
        lastSave = Time.unscaledTime;
    }

    void Apply(SaveSystem.Data data)
    {
        if (data == null || interactor == null) return;

        var pickups = FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Pickup Find(string id)
        {
            foreach (var p in pickups) if (p != null && p.itemId == id) return p;
            return null;
        }

        if (data.used != null)
            foreach (string id in data.used)
            {
                var item = Find(id);
                if (item != null) { interactor.UsedItems.Add(id); Destroy(item.gameObject); }
            }
        if (data.bag != null)
            foreach (string id in data.bag)
            {
                var item = Find(id);
                if (item != null) interactor.TakeSilently(item);
            }
        interactor.Selected = data.selected;

        if (controller != null) controller.RestoreState(new Vector3(data.x, data.y, data.z), data.yaw, data.pitch);
        Interactor.TimeOffset = data.playTime;
        Interactor.Say("Прогресс загружен", 2.5f);
    }
}
