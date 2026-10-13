using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// Висит на игроке. ЛКМ или F — взаимодействие с объектом под прицелом.
/// Q, E и колесо мыши — выбор предмета из сумки; применить можно только тот, что в руке.
/// Подобранные предметы складываются в сумку: в руках виден последний, а нужный предмет
/// из сумки тратится сам, когда игрок применяет его к объекту.
public class Interactor : MonoBehaviour
{
    public Camera cam;
    public Transform holdPoint;
    public float range = 2.5f;

    readonly System.Collections.Generic.List<Pickup> bag = new System.Collections.Generic.List<Pickup>();

    /// Предмет, который сейчас в руках: последний подобранный.
    public Pickup Held { get { return bag.Count > 0 ? bag[Mathf.Clamp(selected, 0, bag.Count - 1)] : null; } }

    int selected;

    /// Предметы, которые уже потрачены: при загрузке сохранения их убирают из уровня.
    public readonly List<string> UsedItems = new List<string>();
    /// Сумка или потраченные предметы изменились — пора сохраниться.
    public static event System.Action BagChanged;
    /// Сколько секунд было сыграно до загрузки сохранения (для времени на финальном экране).
    public static float TimeOffset;

    public int Selected { get { return selected; } set { selected = value; } }

    public string[] BagIds()
    {
        var ids = new List<string>();
        foreach (var item in bag) if (item != null) ids.Add(item.itemId);
        return ids.ToArray();
    }

    /// Положить предмет в сумку без звука и описания (при загрузке сохранения).
    public void TakeSilently(Pickup item)
    {
        if (bag.Contains(item)) return;
        bag.Add(item);
        item.OnTaken(holdPoint, true);
    }

    IInteractable target;

    /// Объект под прицелом, с которым можно взаимодействовать, или null.
    public IInteractable Target { get { return target; } }
    static string message;
    static float messageUntil;
    float winTime = -1f;

    /// Короткое сообщение в центре экрана.
    public static void Say(string text, float seconds = 2.5f)
    {
        message = text;
        messageUntil = Time.time + seconds;
    }

    void Update()
    {
        GameState.Tick();
        if (Hints.Paused) { target = null; return; }

        target = null;
        if (Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, range, ~0,
                QueryTriggerInteraction.Ignore))
            target = hit.collider.GetComponentInParent<IInteractable>();

        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (GameState.Has("door_open"))
        {
            if (winTime < 0f)
            {
                winTime = Time.timeSinceLevelLoad + TimeOffset;
                SaveSystem.Clear();             // игра пройдена: следующий запуск начнётся заново
                YandexBridge.GameplayStop();
            }
            if (keyboard.rKey.wasPressedThisFrame)
            {
                GameState.ClearFlags();
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
            else if (keyboard.mKey.wasPressedThisFrame && SceneManager.GetActiveScene().buildIndex > 0)
            {
                GameState.ClearFlags();
                SceneManager.LoadScene(0);   // стартовый экран стоит первым в списке сборки
            }
            return;
        }

        var mouse = Mouse.current;
        bool click = mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState == CursorLockMode.Locked;
        if ((click || keyboard.fKey.wasPressedThisFrame) && target != null) target.Interact(this);

        // Выбор предмета: Q — предыдущий, E — следующий, колесо мыши — в обе стороны.
        int step = 0;
        if (keyboard.qKey.wasPressedThisFrame) step = -1;
        if (keyboard.eKey.wasPressedThisFrame) step = 1;
        if (mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll > 0.01f) step = -1;
            else if (scroll < -0.01f) step = 1;
        }
        if (step != 0 && bag.Count > 1)
        {
            selected = ((selected + step) % bag.Count + bag.Count) % bag.Count;
            Sfx.Play("click");
        }
    }

    public void Take(Pickup item)
    {
        if (bag.Contains(item)) return;
        bag.Add(item);
        selected = bag.Count - 1;   // новый предмет сразу в руке
        item.OnTaken(holdPoint);
        BagChanged?.Invoke();
        if (!string.IsNullOrEmpty(item.description)) Say(item.description, 4f);
    }

    void LateUpdate()
    {
        // В руках виден только выбранный предмет, остальные лежат в сумке скрытыми.
        // Проверяем каждый кадр: логика загадок может снова включить объект предмета.
        bag.RemoveAll(i => i == null);
        selected = bag.Count > 0 ? Mathf.Clamp(selected, 0, bag.Count - 1) : 0;
        for (int i = 0; i < bag.Count; i++)
        {
            bool visible = i == selected;
            if (bag[i].gameObject.activeSelf != visible) bag[i].gameObject.SetActive(visible);
        }
    }

    public void Drop()
    {
        if (Held == null) return;
        Vector3 origin = cam.transform.position;
        Vector3 forward = cam.transform.forward;
        float distance = Physics.Raycast(origin, forward, out RaycastHit hit, 1f, ~0, QueryTriggerInteraction.Ignore)
            ? Mathf.Max(hit.distance - 0.3f, 0.1f)
            : 0.8f;
        Pickup item = Held;
        bag.Remove(item);
        item.gameObject.SetActive(true);
        item.OnDropped(origin + forward * distance);
    }

    /// Тратит предмет, если он в руке.
    public bool Consume(string itemId)
    {
        if (!Holds(itemId)) return false;
        Pickup item = Held;
        bag.Remove(item);
        UsedItems.Add(itemId);
        Destroy(item.gameObject);
        BagChanged?.Invoke();
        return true;
    }

    /// Этот ли предмет сейчас в руке.
    public bool Holds(string itemId)
    {
        return Held != null && Held.itemId == itemId;
    }

    /// Есть ли предмет в сумке (в руке или нет).
    public bool InBag(string itemId)
    {
        foreach (var item in bag)
            if (item != null && item.itemId == itemId) return true;
        return false;
    }

    /// Название предмета из сумки, если он там есть.
    public string NameOf(string itemId)
    {
        foreach (var item in bag)
            if (item != null && item.itemId == itemId) return item.displayName;
        return null;
    }

    /// Подсказки по объектам записаны как «E — …»; на экране показываем новую клавишу.
    public static string KeyPrompt(string prompt)
    {
        if (string.IsNullOrEmpty(prompt)) return prompt;
        return prompt.StartsWith("E — ") ? "ЛКМ — " + prompt.Substring(4) : prompt;
    }

    void OnGUI()
    {
        Ui.Begin();
        var center = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
        float w = Ui.W, h = Ui.H;

        if (target != null && !string.IsNullOrEmpty(target.Prompt))
            GUI.Label(new Rect(0, h * 0.5f + 30, w, 30), KeyPrompt(target.Prompt), center);

        if (message != null && Time.time < messageUntil)
        {
            var wrapped = new GUIStyle(center) { wordWrap = true, alignment = TextAnchor.UpperCenter };
            var box = new Rect(w * 0.15f, h * 0.7f, w * 0.7f, h * 0.25f);
            // Тень под текстом, чтобы он читался на любом фоне.
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(box.x + 1, box.y + 1, box.width, box.height), message, wrapped);
            GUI.color = old;
            GUI.Label(box, message, wrapped);
        }

        if (Held != null)
        {
            string line = "В руке: " + Held.displayName;
            if (bag.Count > 1)
            {
                line += "     В сумке: ";
                bool first = true;
                for (int i = 0; i < bag.Count; i++)
                {
                    if (i == selected) continue;
                    line += (first ? "" : ", ") + bag[i].displayName;
                    first = false;
                }
                line += "     (Q / E — сменить)";
            }
            GUI.Label(new Rect(12, 32, w - 24, 48), line, new GUIStyle(GUI.skin.label) { wordWrap = true });
        }

        float timer = GameState.TimerRemaining;
        if (timer > 0f)
        {
            var big = new GUIStyle(center) { fontSize = 36 };
            GUI.Label(new Rect(0, 40, w, 50), timer.ToString("0.0"), big);
        }

        if (GameState.Has("door_open"))
        {
            var big = new GUIStyle(center) { fontSize = 48 };
            GUI.Label(new Rect(0, h * 0.3f, w, 70), "Вы выбрались!", big);
            if (winTime >= 0f)
            {
                int minutes = (int)(winTime / 60f);
                float seconds = winTime - minutes * 60;
                GUI.Label(new Rect(0, h * 0.3f + 80, w, 30),
                    "Время: " + minutes + ":" + seconds.ToString("00.0") + "    R — начать заново    M — в меню", center);
            }
        }
    }
}
