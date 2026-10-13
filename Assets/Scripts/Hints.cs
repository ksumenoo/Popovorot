using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// Меню паузы по Esc. В нём же лежит подсказка: она показывает следующий шаг
/// в зависимости от того, какие загадки уже решены.
public class Hints : MonoBehaviour
{
    /// Игра на паузе: управление игроком и взаимодействие отключены.
    public static bool Paused { get; private set; }

    bool showHint;
    bool needRelock;

    void Start()
    {
        Time.timeScale = 1f;
        Paused = false;
        Interactor.Say("Поворот на 90° уводит в другой мир", 8f);
        YandexBridge.GameplayStart();
    }

    void OnDestroy()
    {
        Time.timeScale = 1f;
        Paused = false;
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        // Редактор Unity сам освобождает курсор по Esc, причём в тот же кадр, когда меню
        // закрывается. Поэтому захватываем курсор ещё раз, как только Esc отпущена.
        if (needRelock && !Paused && !keyboard.escapeKey.isPressed)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            needRelock = false;
        }

        if (!keyboard.escapeKey.wasPressedThisFrame) return;
        if (GameState.Has("door_open")) return;   // на финальном экране свои клавиши
        SetPaused(!Paused);
    }

    void SetPaused(bool paused)
    {
        Paused = paused;
        showHint = false;
        Time.timeScale = paused || YandexBridge.PlatformPaused ? 0f : 1f;   // останавливает и таймеры загадок
        if (paused) YandexBridge.GameplayStop(); else YandexBridge.GameplayStart();
        Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = paused;
        needRelock = !paused;
        Sfx.Play("click");
    }

    static void Fill(Rect rect, Color color)
    {
        Color old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = old;
    }

    static GUIStyle Text(float size, Color color, FontStyle style, TextAnchor anchor)
    {
        var s = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(size), fontStyle = style, alignment = anchor, wordWrap = true };
        s.normal.textColor = color;
        return s;
    }

    static bool Button(Rect rect, string text, string key, float k)
    {
        var accent = new Color(1f, 0.80f, 0.25f);
        bool hover = rect.Contains(Event.current.mousePosition);
        Fill(rect, hover ? accent : new Color(1f, 1f, 1f, 0.10f));
        Fill(new Rect(rect.x, rect.y, 4f * k, rect.height), accent);
        Color c = hover ? new Color(0.05f, 0.05f, 0.07f) : Color.white;
        GUI.Label(new Rect(rect.x + 20f * k, rect.y, rect.width, rect.height), text,
            Text(22f * k, c, FontStyle.Bold, TextAnchor.MiddleLeft));
        if (!string.IsNullOrEmpty(key))
            GUI.Label(new Rect(rect.x, rect.y, rect.width - 16f * k, rect.height), key,
                Text(14f * k, new Color(c.r, c.g, c.b, 0.6f), FontStyle.Normal, TextAnchor.MiddleRight));
        return GUI.Button(rect, GUIContent.none, GUIStyle.none);
    }

    void OnGUI()
    {
        if (!Paused) return;

        GUI.depth = -100;   // поверх остального интерфейса
        GUI.matrix = Matrix4x4.identity;
        float w = Screen.width, h = Screen.height;
        float k = Ui.MenuScale;

        Fill(new Rect(0, 0, w, h), new Color(0.02f, 0.02f, 0.04f, 0.82f));

        float bw = 360f * k, bh = 52f * k, gap = 10f * k;
        float x = (w - bw) * 0.5f;
        float y = 120f * k;

        GUI.Label(new Rect(0, y, w, 70f * k), "ПАУЗА", Text(56f * k, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter));
        y += 96f * k;

        if (Button(new Rect(x, y, bw, bh), "Продолжить", "Esc", k)) SetPaused(false);
        y += bh + gap;
        if (Button(new Rect(x, y, bw, bh), showHint ? "Скрыть подсказку" : "Подсказка", "", k))
        {
            showHint = !showHint;
            Sfx.Play("click");
        }
        y += bh + gap;
        if (Button(new Rect(x, y, bw, bh), "Начать заново", "", k))
        {
            SaveSystem.PrepareNewGame();
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            return;
        }
        y += bh + gap;
        if (Button(new Rect(x, y, bw, bh), "В главное меню", "", k) && SceneManager.GetActiveScene().buildIndex > 0)
        {
            GameState.ClearFlags();
            Time.timeScale = 1f;
            SceneManager.LoadScene(0);
            return;
        }
        y += bh + gap;
#if !UNITY_WEBGL
        if (Button(new Rect(x, y, bw, bh), "Выйти из игры", "", k))
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
#endif
        y += bh + 26f * k;

        if (showHint)
        {
            float tw = Mathf.Min(w * 0.8f, 760f * k);
            var box = new Rect((w - tw) * 0.5f, y, tw, h - y - 20f * k);
            Fill(new Rect(box.x - 16f * k, box.y - 10f * k, box.width + 32f * k, box.height), new Color(1f, 1f, 1f, 0.07f));
            GUI.Label(box, HintText(), Text(18f * k, Color.white, FontStyle.Normal, TextAnchor.UpperLeft));

            // Следующая ступень открывается только по кнопке, когда пройдёт время.
            if (currentStep < Steps.Length && revealed < 3)
            {
                float wait = StepDelay - (Time.time - stepStart);
                var next = new Rect((w - bw) * 0.5f, h - 20f * k - bh - 16f * k, bw, bh);
                if (wait > 0f)
                    GUI.Label(next, "Следующая подсказка через " + Mathf.CeilToInt(wait) + " с",
                        Text(16f * k, new Color(1f, 1f, 1f, 0.6f), FontStyle.Normal, TextAnchor.MiddleCenter));
                else if (Button(next, revealed == 1 ? "Подсказать точнее" : "Показать ответ", "", k))
                {
                    revealed++;
                    stepStart = Time.time;   // отсчёт до следующей ступени идёт заново
                    Sfx.Play("click");
                }
            }
        }
        else
        {
            GUI.Label(new Rect(0, y, w, 60f * k),
                "WASD — движение     Shift — бег     Мышь — обзор и смена мира\nЛКМ или F — взять или нажать     Q / E / колесо — сменить предмет",
                Text(16f * k, new Color(1f, 1f, 1f, 0.6f), FontStyle.Normal, TextAnchor.UpperCenter));
        }
    }

    // ------------------------------------------------------------------
    // Подсказки ступенями. Для каждого шага: флаг, который он включает, и три подсказки —
    // намёк, направление, ответ. На экране видна одна ступень; следующую игрок открывает
    // кнопкой, и она становится доступна через StepDelay секунд после предыдущей.
    // ------------------------------------------------------------------
    const float StepDelay = 60f;

    public static readonly string[][] Steps =
    {
        new[] { "cage_open",
            "Предохранитель лежит в запертой клетке в углу под галереей.",
            "Посмотри, что стоит на месте клетки в соседнем мире: повернись вправо.",
            "В мультяшном мире на месте клетки сундук с кнопкой. Нажми её — клетка в основном мире откроется." },
        new[] { "power",
            "Предохранитель надо куда-то вставить, но щитка в комнате не видно.",
            "Щиток внутри закрытого контейнера. Найди мир, где контейнер открыт.",
            "В мультяшном мире контейнер открыт. Зайди в него с предохранителем и повернись влево — окажешься у щитка." },
        new[] { "turn_code",
            "В мультяшном мире на полу первой комнаты нарисованы стрелки с номерами.",
            "Встань у стрелок и повторяй их поворотами по порядку номеров: стрелка влево — поворот влево. Без пауз.",
            "Стоя у стрелок: влево, вправо, вправо, влево. Наверху откроется ниша с рукояткой; целая лестница на галерею есть только в мультяшном мире." },
        new[] { "gate_fixed",
            "Решётка в коридоре открывается механизмом, но ненадолго. Для механизма нужна рукоятка из ниши.",
            "Механизм на южной стене под самым потолком. Дотянуться можно только с потолка.",
            "В перевёрнутом мире возьми рукоятку в руку, проверни механизм на южной стене, дважды повернись и беги в коридор. За решёткой закрепи её рычагом на южной стене — из основного мира его не видно." },
        new[] { "locker_open",
            "Во второй комнате две красные кнопки на противоположных стенах.",
            "Одна низко на северной стене, другая под потолком на южной. Времени между ними мало.",
            "Нажми кнопку на северной стене в основном мире, дважды повернись и добеги по потолку до кнопки на южной стене." },
        new[] { "shutter_open",
            "Открылся ящик. Что в нём и где это пригодится?",
            "Гиря нужна на плите, которая в основном мире висит на потолке.",
            "Возьми гирю в мультяшном мире и положи её на оранжевую плиту в перевёрнутом мире." },
        new[] { "safe_open",
            "У сейфа четыре цветные кнопки. Порядок уже встречался в первой комнате.",
            "Свиток с цветными кругами висит на галерее первой комнаты, в японском мире. Цвета там вывернуты.",
            "Посмотри, каким стал красный огнетушитель в японском мире. Настоящий порядок: синий, красный, зелёный, жёлтый." },
        new[] { "chain_cut",
            "Болторезом можно что-то перекусить.",
            "Над штабелем ящиков висит ключ на цепи.",
            "В перевёрнутом мире цепь с ключом лежит на балке у тебя под ногами — перекуси её." },
        new[] { "tower_down",
            "Ключ упал, но не на пол.",
            "Ключ лежит на штабеле. На месте штабеля в другом мире стоит что-то лёгкое.",
            "В мультяшном мире на месте штабеля башня из кубиков — толкни её, и ключ в основном мире окажется на полу." },
        new[] { "tansu_open",
            "Ключ подходит к чему-то запертому в этой комнате.",
            "На месте сейфа в одном из миров стоит комод.",
            "В японском мире на месте сейфа стоит тансу — открой его ключом. Ручку изнутри держи до последней комнаты." },
        new[] { "bridge_power",
            "Выход дальше — на мост с галереи. Но лестницы на галерею нет.",
            "Лестница висит под потолком. В каком мире потолок — это пол?",
            "В перевёрнутом мире поднимись по лестнице к краю галереи и повернись вправо. В японском мире кладовка в углу галереи открыта: зайди в неё и повернись вправо — там рубильник." },
        new[] { "bridge_open",
            "Механизм двери на мост под напряжением, но сам не крутится.",
            "Нужна рукоятка. Её дала ниша в первой комнате.",
            "Принеси рукоятку из ниши на галерее первой комнаты и вставь её в механизм у двери на мост в основном мире." },
        new[] { "lanterns_lit",
            "В последней комнате нужно спуститься вниз и найти свет. Часть галереи провалилась.",
            "В мультяшном мире галерея цела. Одна подставка для фонаря у сёдзи пуста, недостающий фонарь висит под потолком.",
            "В перевёрнутом мире фонарь лежит у ног в северной части комнаты. Возьми его и поставь на пустую подставку у сёдзи в японском мире." },
        new[] { "lock_cartoon",
            "У двери четыре лампы — по одной на мир. Тени на сёдзи показывают фигуры.",
            "Такие же фигуры есть на панели у двери в мультяшном мире.",
            "Нажми фигуры на панели в том порядке, в каком тени стоят на сёдзи слева направо: квадрат, широкая, высокая." },
        new[] { "lock_upside",
            "Лампа перевёрнутого мира включается рычагом, до которого не достать с пола.",
            "Рычаг висит под потолком на восточной стене у двери.",
            "В перевёрнутом мире рычаг на восточной стене у тебя под ногами — опусти его." },
        new[] { "lock_japanese",
            "Осталась лампа японского мира.",
            "В японском мире на месте двери сёдзи.",
            "Когда фонари у сёдзи горят, раздвинь сёдзи на месте двери в японском мире." },
        new[] { "lock_main",
            "Последняя лампа — основного мира.",
            "У двери в основном мире нет ручки.",
            "Вставь дверную ручку из тансу в дверь в основном мире." },
        new[] { "door_open",
            "Все лампы горят?",
            "Дверь открывается в основном мире.",
            "Открой дверь в основном мире." },
    };

    int currentStep = -1;
    float stepStart;
    int revealed = 1;   // сколько ступеней подсказки игрок уже открыл: 1 — намёк, 2 — направление, 3 — ответ

    public static int FindStep()
    {
        for (int i = 0; i < Steps.Length; i++)
            if (!GameState.Has(Steps[i][0])) return i;
        return Steps.Length;
    }

    void LateUpdate()
    {
        int step = FindStep();
        if (step != currentStep)
        {
            currentStep = step;
            revealed = 1;
            stepStart = Time.time;   // на паузе время стоит, поэтому пауза не ускоряет подсказки
        }
    }

    string HintText()
    {
        if (currentStep >= Steps.Length) return "Вы уже выбрались.";
        // Показываем только последнюю открытую ступень, а не всё сразу.
        string[] titles = { "Намёк", "Направление", "Ответ" };
        int i = Mathf.Clamp(revealed, 1, 3) - 1;
        return titles[i] + ": " + Steps[currentStep][i + 1];
    }
}
