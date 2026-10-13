using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// Стартовый экран. Камера плавно поворачивается на 90° к четырём цветным стенам,
/// а акцентный цвет меню меняется вместе с «миром», на который она смотрит.
public class MainMenu : MonoBehaviour
{
    [Tooltip("Имя сцены с игрой")]
    public string gameSceneName = "SampleScene";
    public string title = "Вот это поповорот";
    public Transform cameraPivot;
    [Tooltip("Сколько секунд камера смотрит на одну стену")]
    public float turnEvery = 2.5f;
    [Tooltip("Не используется, оставлено для совместимости со сценой")]
    public float turnSpeed = 220f;

    static readonly string[] WorldNames = { "Основная", "Мультяшная", "Перевёрнутая", "Японская" };
    static readonly Color[] WorldColors =
    {
        new Color(0.80f, 0.82f, 0.86f),
        new Color(1.00f, 0.80f, 0.25f),
        new Color(0.45f, 0.58f, 0.95f),
        new Color(0.15f, 0.80f, 0.85f),
    };

    float yaw, targetYaw, yawVelocity, nextTurn;
    int world;
    Color accent = WorldColors[0];
    float fade = 1f;          // 1 — чёрный экран, 0 — меню видно полностью
    bool starting;
    Camera cam;
    float baseFov;

    void Start()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 1f;
        GameState.ClearFlags();
        YandexBridge.GameplayStop();
        YandexBridge.Ready();   // меню открыто и готово: площадка убирает свой экран загрузки
        nextTurn = Time.time + turnEvery;
        if (cameraPivot != null) cam = cameraPivot.GetComponent<Camera>();
        if (cam != null) baseFov = cam.fieldOfView;
    }

    void Update()
    {
        if (Time.time >= nextTurn)
        {
            targetYaw += 90f;
            world = (world + 1) % 4;
            nextTurn = Time.time + turnEvery;
            if (cam != null) cam.fieldOfView = baseFov + 10f;
        }
        yaw = Mathf.SmoothDampAngle(yaw, targetYaw, ref yawVelocity, 0.28f);
        if (cameraPivot != null) cameraPivot.rotation = Quaternion.Euler(0f, yaw, 0f);
        if (cam != null) cam.fieldOfView = Mathf.MoveTowards(cam.fieldOfView, baseFov, 30f * Time.deltaTime);

        accent = Color.Lerp(accent, WorldColors[world], Time.deltaTime * 5f);

        // Плавное появление меню и затемнение перед запуском игры.
        fade = Mathf.MoveTowards(fade, starting ? 1f : 0f, Time.deltaTime / 0.4f);
        if (starting && fade >= 1f) SceneManager.LoadScene(gameSceneName);

        var keyboard = Keyboard.current;
        if (keyboard == null || starting) return;
        if (keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)
        {
            if (SaveSystem.HasSave) ContinueGame(); else NewGame();
        }
#if !UNITY_WEBGL
        if (keyboard.escapeKey.wasPressedThisFrame) Quit();
#endif
    }

    void StartGame()
    {
        if (starting) return;
        starting = true;
        Sfx.Play("click");
    }

    void NewGame()
    {
        if (starting) return;
        SaveSystem.PrepareNewGame();
        StartGame();
    }

    void ContinueGame()
    {
        if (starting) return;
        SaveSystem.PrepareContinue();
        StartGame();
    }

    static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ------------------------------------------------------------------
    // Отрисовка
    // ------------------------------------------------------------------
    static void Fill(Rect rect, Color color)
    {
        Color old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = old;
    }

    static GUIStyle Text(float size, Color color, FontStyle style = FontStyle.Normal,
        TextAnchor anchor = TextAnchor.UpperLeft)
    {
        var s = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(size), fontStyle = style, alignment = anchor, wordWrap = true,
            richText = false
        };
        s.normal.textColor = color;
        s.padding = new RectOffset(0, 0, 0, 0);
        return s;
    }

    static void Label(Rect rect, string text, GUIStyle style, float shadow)
    {
        // Тень под текстом, чтобы он читался на светлых стенах.
        Color c = style.normal.textColor;
        style.normal.textColor = new Color(0f, 0f, 0f, 0.6f * c.a);
        GUI.Label(new Rect(rect.x + shadow, rect.y + shadow, rect.width, rect.height), text, style);
        style.normal.textColor = c;
        GUI.Label(rect, text, style);
    }

    bool Button(Rect rect, string text, string key, float k)
    {
        bool hover = rect.Contains(Event.current.mousePosition);
        Fill(rect, hover ? accent : new Color(1f, 1f, 1f, 0.10f));
        Fill(new Rect(rect.x, rect.y, 4f * k, rect.height), accent);
        Color textColor = hover ? new Color(0.05f, 0.05f, 0.07f) : Color.white;
        GUI.Label(new Rect(rect.x + 22f * k, rect.y, rect.width, rect.height), text,
            Text(24f * k, textColor, FontStyle.Bold, TextAnchor.MiddleLeft));
        GUI.Label(new Rect(rect.x, rect.y, rect.width - 18f * k, rect.height), key,
            Text(14f * k, new Color(textColor.r, textColor.g, textColor.b, 0.6f), FontStyle.Normal,
                TextAnchor.MiddleRight));
        return GUI.Button(rect, GUIContent.none, GUIStyle.none);
    }

    float Keys(float x, float y, string[] keys, string description, float k)
    {
        float cx = x;
        var keyStyle = Text(15f * k, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
        keyStyle.wordWrap = false;
        foreach (string key in keys)
        {
            float w = Mathf.Max(28f * k, keyStyle.CalcSize(new GUIContent(key)).x + 16f * k);
            var r = new Rect(cx, y, w, 28f * k);
            Fill(r, new Color(1f, 1f, 1f, 0.14f));
            Fill(new Rect(r.x, r.yMax - 3f * k, r.width, 3f * k), new Color(1f, 1f, 1f, 0.25f));
            GUI.Label(r, key, keyStyle);
            cx += w + 5f * k;
        }
        GUI.Label(new Rect(x + 170f * k, y, 400f * k, 28f * k), description,
            Text(17f * k, new Color(1f, 1f, 1f, 0.85f), FontStyle.Normal, TextAnchor.MiddleLeft));
        return y + 34f * k;
    }

    void OnGUI()
    {
        float w = Screen.width, h = Screen.height;
        float k = Ui.MenuScale;   // всё меню масштабируется под размер окна

        // Затемнение слева: плотное у края, прозрачное к середине экрана.
        const int strips = 160;
        float panel = Mathf.Min(w * 0.62f, 820f * k);
        for (int i = 0; i < strips; i++)
        {
            float t = (float)i / strips;
            float x0 = Mathf.Floor(panel * i / strips);
            float x1 = Mathf.Floor(panel * (i + 1) / strips);
            Fill(new Rect(x0, 0, x1 - x0, h), new Color(0.02f, 0.02f, 0.04f, 0.86f * (1f - t * t)));
        }

        float x = 72f * k;
        float y = 52f * k;

        Label(new Rect(x, y, 600f * k, 22f * k), "ESCAPE ROOM  ·  ОТ ПЕРВОГО ЛИЦА",
            Text(14f * k, accent, FontStyle.Bold), k);
        y += 34f * k;

        // Название: последнее слово крупно и акцентным цветом.
        int split = title.LastIndexOf(' ');
        string first = split > 0 ? title.Substring(0, split) : "";
        string last = (split > 0 ? title.Substring(split + 1) : title).ToUpperInvariant();
        if (first.Length > 0)
        {
            Label(new Rect(x, y, 700f * k, 62f * k), first, Text(50f * k, Color.white), 2f * k);
            y += 58f * k;
        }
        Label(new Rect(x - 3f * k, y, 800f * k, 92f * k), last, Text(76f * k, accent, FontStyle.Bold), 3f * k);
        y += 96f * k;

        Fill(new Rect(x, y, 96f * k, 5f * k), accent);
        y += 24f * k;

        Label(new Rect(x, y, 520f * k, 60f * k), "Повернись на 90° — и окажешься в другой версии той же комнаты.",
            Text(19f * k, new Color(1f, 1f, 1f, 0.9f)), k);
        y += 66f * k;

        if (SaveSystem.HasSave)
        {
            if (Button(new Rect(x, y, 320f * k, 56f * k), "Продолжить", "Enter", k)) ContinueGame();
            y += 66f * k;
            if (Button(new Rect(x, y, 320f * k, 56f * k), "Новая игра", "", k)) NewGame();
            y += 66f * k;
        }
        else
        {
            if (Button(new Rect(x, y, 320f * k, 56f * k), "Начать", "Enter", k)) NewGame();
            y += 66f * k;
        }
#if !UNITY_WEBGL
        // В браузере выйти из игры нельзя: вкладку закрывает сам игрок.
        if (Button(new Rect(x, y, 320f * k, 56f * k), "Выход", "Esc", k)) Quit();
        y += 66f * k;
#endif
        y += 6f * k;

        y = Keys(x, y, new[] { "W", "A", "S", "D" }, "движение", k);
        y = Keys(x, y, new[] { "Мышь" }, "обзор и смена мира", k);
        y = Keys(x, y, new[] { "ЛКМ", "F" }, "взять или нажать", k);
        y = Keys(x, y, new[] { "Shift" }, "бег", k);
        y = Keys(x, y, new[] { "Q", "E" }, "сменить предмет (или колесо)", k);
        Keys(x, y, new[] { "Esc" }, "пауза и подсказки", k);

        // Четыре мира внизу справа: подсвечен тот, на который сейчас смотрит камера.
        float dotY = h - 54f * k;
        float dotX = w - 40f * k;
        for (int i = 3; i >= 0; i--)
        {
            bool current = i == world;
            var style = Text(16f * k, current ? WorldColors[i] : new Color(1f, 1f, 1f, 0.4f),
                current ? FontStyle.Bold : FontStyle.Normal, TextAnchor.MiddleRight);
            style.wordWrap = false;
            float tw = style.CalcSize(new GUIContent(WorldNames[i])).x;
            Label(new Rect(dotX - tw, dotY, tw, 24f * k), WorldNames[i], style, k);
            Fill(new Rect(dotX - tw, dotY + 27f * k, tw, 3f * k),
                current ? WorldColors[i] : new Color(1f, 1f, 1f, 0.15f));
            dotX -= tw + 26f * k;
        }

        if (fade > 0f) Fill(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, fade));
    }
}
