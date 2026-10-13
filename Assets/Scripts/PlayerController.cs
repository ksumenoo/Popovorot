using UnityEngine;
using UnityEngine.InputSystem;

/// Игрок от первого лица. WASD — движение, мышь — обзор.
/// Поворот на switchAngle градусов от момента последнего перехода
/// переносит игрока в соседний мир с сохранением позиции.
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    public Transform cameraPivot;
    public float moveSpeed = 5f;
    [Tooltip("Скорость бега с зажатым Shift")]
    public float runSpeed = 8.5f;
    [Tooltip("На сколько градусов расширяется поле зрения при беге")]
    public float runFovKick = 6f;
    public float mouseSensitivity = 0.12f;
    public float gravity = -20f;
    public float maxPitch = 85f;
    [Tooltip("Угол поворота, при котором меняется мир")]
    public float switchAngle = 90f;
    [Tooltip("Слои, которые мешают появиться в другом мире")]
    public LayerMask obstacleMask = ~0;
    [Tooltip("На сколько градусов расширяется поле зрения в момент перехода")]
    public float switchFovKick = 14f;
    public float fovReturnSpeed = 60f;

    CharacterController cc;
    float localYaw;          // поворот от момента последнего перехода
    float pitch;
    float verticalVelocity;
    Camera cam;
    float baseFov;
    float freeYaw;
    int pitchZone;           // 1 — смотрит вверх, -1 — вниз, 0 — прямо
    bool running;

    /// Свободный обзор: поворот не меняет мир. Включается в пятой комнате.
    public bool FreeLook { get; private set; }

    /// Движения для комбинации: 'L' и 'R' — смена мира влево и вправо, 'U' и 'D' — взгляд вверх и вниз.
    public event System.Action<char> ComboInput;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
    }

    void Start()
    {
        LockCursor(true);
        ApplyRotation();
        if (cameraPivot != null) cam = cameraPivot.GetComponent<Camera>();
        if (cam != null) baseFov = cam.fieldOfView;
    }

    void Update()
    {
        Look();
        Move();
        if (cam != null)
            cam.fieldOfView = Mathf.MoveTowards(cam.fieldOfView, baseFov + (running ? runFovKick : 0f),
                fovReturnSpeed * Time.deltaTime);
    }

    void Look()
    {
        var mouse = Mouse.current;
        var keyboard = Keyboard.current;
        if (mouse == null || keyboard == null) return;

        if (Hints.Paused) return;   // меню паузы: камера не двигается

        if (Cursor.lockState != CursorLockMode.Locked)
        {
            if (mouse.leftButton.wasPressedThisFrame) LockCursor(true);
            return;
        }

        Vector2 delta = mouse.delta.ReadValue() * mouseSensitivity;
        pitch = Mathf.Clamp(pitch - delta.y, -maxPitch, maxPitch);
        TrackPitch();

        if (FreeLook)
        {
            freeYaw += delta.x;
            ApplyRotation();
            return;
        }

        localYaw += delta.x;

        while (localYaw >= switchAngle)
        {
            if (!TrySwitchWorld(+1)) { localYaw = switchAngle - 0.01f; SayBlocked(); break; }
            localYaw -= switchAngle;
            ComboInput?.Invoke('R');
        }
        while (localYaw <= -switchAngle)
        {
            if (!TrySwitchWorld(-1)) { localYaw = -switchAngle + 0.01f; SayBlocked(); break; }
            localYaw += switchAngle;
            ComboInput?.Invoke('L');
        }

        ApplyRotation();
    }

    void ApplyRotation()
    {
        int world = WorldManager.Instance != null ? WorldManager.Instance.Current : 0;
        transform.rotation = Quaternion.Euler(0f, FreeLook ? freeYaw : world * switchAngle + localYaw, 0f);
        if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    // pitch меньше нуля — взгляд вверх, больше нуля — вниз.
    void TrackPitch()
    {
        if (pitch < -55f && pitchZone != 1) { pitchZone = 1; ComboInput?.Invoke('U'); }
        else if (pitch > 55f && pitchZone != -1) { pitchZone = -1; ComboInput?.Invoke('D'); }
        else if (Mathf.Abs(pitch) < 25f) pitchZone = 0;
    }

    public void SetFreeLook(bool on)
    {
        FreeLook = on;
        if (on) freeYaw = transform.eulerAngles.y;
        ApplyRotation();
    }

    public float LocalYaw { get { return localYaw; } }
    public float Pitch { get { return pitch; } }

    /// Восстановить игрока из сохранения: место (с учётом мира), поворот и наклон головы.
    public void RestoreState(Vector3 position, float savedLocalYaw, float savedPitch)
    {
        var wm = WorldManager.Instance;
        Teleport(position);
        if (wm != null) wm.SetWorld(wm.WorldOf(position));
        localYaw = Mathf.Clamp(savedLocalYaw, -switchAngle + 1f, switchAngle - 1f);
        pitch = savedPitch;
        ApplyRotation();
    }

    public void Teleport(Vector3 position)
    {
        cc.enabled = false;
        transform.position = position;
        cc.enabled = true;
        verticalVelocity = 0f;
    }

    /// direction: +1 — поворот вправо, -1 — влево. false, если в новом мире место занято.
    bool TrySwitchWorld(int direction)
    {
        var wm = WorldManager.Instance;
        if (wm == null) return false;

        int target = (wm.Current + direction + WorldManager.WorldCount) % WorldManager.WorldCount;
        Vector3 newPosition = transform.position + wm.Origin(target) - wm.Origin(wm.Current);

        // В другом мире пол может оказаться чуть выше (например, плита галереи в
        // перевёрнутом мире). Тогда игрок встаёт на неё, как на ступеньку.
        bool found = false;
        bool sawSpace = false;
        foreach (float lift in new[] { 0f, 0.25f, 0.5f })
        {
            Vector3 candidate = newPosition + Vector3.up * lift;
            if (IsBlocked(candidate)) continue;
            sawSpace = true;
            // Под ногами в другом мире должен быть пол: иначе игрок окажется в пустоте
            // за стенами (например, в коридоре между комнатами).
            if (!HasFloor(candidate)) continue;
            newPosition = candidate;
            found = true;
            break;
        }
        if (!found)
        {
            lastBlockNoFloor = sawSpace;
            return false;
        }

        cc.enabled = false;
        transform.position = newPosition;
        cc.enabled = true;
        wm.SetWorld(target);
        if (cam != null) cam.fieldOfView = baseFov + switchFovKick;
        return true;
    }

    bool lastBlockNoFloor;

    bool HasFloor(Vector3 position)
    {
        return Physics.Raycast(position + Vector3.up * 0.3f, Vector3.down, 0.9f, obstacleMask, QueryTriggerInteraction.Ignore);
    }

    bool IsBlocked(Vector3 position)
    {
        float radius = cc.radius * 0.9f;
        Vector3 center = position + cc.center;
        float half = Mathf.Max(cc.height * 0.5f - cc.radius, 0f);
        Vector3 bottom = center - Vector3.up * (half - 0.15f);   // чуть выше пола
        Vector3 top = center + Vector3.up * half;
        return Physics.CheckCapsule(bottom, top, radius, obstacleMask, QueryTriggerInteraction.Ignore);
    }

    void Move()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
        float z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
        Vector3 direction = Vector3.ClampMagnitude(transform.right * x + transform.forward * z, 1f);
        // Бег: Shift и движение вперёд или вбок; в паузе не бежим.
        running = !Hints.Paused && keyboard.shiftKey.isPressed && direction.sqrMagnitude > 0.01f && z >= 0f;

        if (cc.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
        verticalVelocity += gravity * Time.deltaTime;

        cc.Move((direction * (running ? runSpeed : moveSpeed) + Vector3.up * verticalVelocity) * Time.deltaTime);
    }

    static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    static float lastBlockedSound = -10f;

    void SayBlocked()
    {
        Interactor.Say(lastBlockNoFloor
            ? "Дальше не повернуться: в соседнем мире здесь нет пола"
            : "Дальше не повернуться: в соседнем мире это место занято", 1.5f);
        // Звук не чаще раза в 0,6 секунды, иначе при упоре он трещит каждый кадр.
        if (Time.unscaledTime - lastBlockedSound > 0.6f)
        {
            lastBlockedSound = Time.unscaledTime;
            Sfx.Play("blocked", 0.8f);
        }
    }

    // Цвета миров для индикатора у края экрана.
    static readonly Color[] WorldColors =
    {
        new Color(0.62f, 0.63f, 0.66f),   // основная
        new Color(1.00f, 0.80f, 0.25f),   // мультяшная
        new Color(0.35f, 0.45f, 0.75f),   // перевёрнутая
        new Color(0.10f, 0.72f, 0.78f),   // японская
    };

    void OnGUI()
    {
        Ui.Begin();
        var wm = WorldManager.Instance;
        if (wm != null) GUI.Label(new Rect(12, 8, 400, 24), "Мир: " + WorldManager.Names[wm.Current]);
        // Прицел: над интерактивным объектом он становится крупным жёлтым кружком.
        var interactor = GetComponent<Interactor>();
        if (interactor != null && interactor.Target != null && !string.IsNullOrEmpty(interactor.Target.Prompt))
        {
            var aimColor = GUI.color;
            GUI.color = new Color(1f, 0.85f, 0.3f);
            GUI.DrawTexture(new Rect(Ui.W / 2f - 10f, Ui.H / 2f - 10f, 20f, 20f), AimRing());
            GUI.color = aimColor;
        }
        else
        {
            GUI.Label(new Rect(Ui.W / 2f - 4f, Ui.H / 2f - 11f, 20f, 22f), "+");
        }
        GUI.Label(new Rect(12, Ui.H - 28, 300, 24), "Esc — пауза и подсказки");

        if (wm == null || FreeLook) return;

        // Индикатор порога: с той стороны, куда поворачивается игрок, проступает
        // цвет соседнего мира — тем сильнее, чем ближе поворот к 90°.
        float t = Mathf.Abs(localYaw) / switchAngle;
        if (t < 0.45f) return;
        float strength = Mathf.InverseLerp(0.45f, 1f, t);

        bool right = localYaw > 0f;
        int neighbour = (wm.Current + (right ? 1 : -1) + WorldManager.WorldCount) % WorldManager.WorldCount;
        Color color = WorldColors[neighbour];

        const int strips = 14;
        float bandWidth = Ui.W * (0.06f + 0.16f * strength);
        float stripWidth = bandWidth / strips;
        Color old = GUI.color;
        for (int i = 0; i < strips; i++)
        {
            // i = 0 — у самого края экрана, там полоса плотнее всего.
            float fade = 1f - (float)i / strips;
            GUI.color = new Color(color.r, color.g, color.b, 0.75f * strength * fade * fade);
            float x = right ? Ui.W - (i + 1) * stripWidth : i * stripWidth;
            GUI.DrawTexture(new Rect(x, 0, stripWidth + 1f, Ui.H), Texture2D.whiteTexture);
        }
        GUI.color = old;

        if (strength > 0.25f)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = right ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft, fontSize = 18
            };
            string label = right ? WorldManager.Names[neighbour] + "  →" : "←  " + WorldManager.Names[neighbour];
            GUI.Label(new Rect(16, Ui.H * 0.5f - 60, Ui.W - 32, 30), label, style);
        }
    }

    static Texture2D aimRing;

    /// Кольцо прицела, рисуется один раз.
    static Texture2D AimRing()
    {
        if (aimRing != null) return aimRing;
        const int size = 64;
        aimRing = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                float a = Mathf.Clamp01(1f - Mathf.Abs(d - 26f) / 3.5f);
                aimRing.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        aimRing.Apply();
        return aimRing;
    }
}
