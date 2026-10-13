using UnityEngine;

/// Замок на серию поворотов. Буквы: L — поворот влево со сменой мира, R — вправо,
/// U — взгляд вверх, D — вниз. Серию нужно выполнить без пауз и закончить внутри
/// заданной области (в координатах уровня, без сдвига между мирами).
public class TurnSequenceLock : MonoBehaviour
{
    public string combo = "LRRL";
    [Tooltip("Наибольшая пауза между движениями, в секундах")]
    public float maxGap = 1.5f;
    public Vector3 regionMin;
    public Vector3 regionMax;
    [Tooltip("Вторая область, где код тоже работает. Пустая (min = max) — не используется")]
    public Vector3 region2Min;
    public Vector3 region2Max;
    public string flag = "turn_code";
    public string doneMessage = "Где-то щёлкнул замок";

    PlayerController player;
    string buffer = "";
    float lastInputTime;

    void Start()
    {
        player = FindFirstObjectByType<PlayerController>();
        if (player != null) player.ComboInput += OnInput;
    }

    void OnDestroy()
    {
        if (player != null) player.ComboInput -= OnInput;
    }

    void OnInput(char input)
    {
        if (GameState.Has(flag)) return;

        if (Time.time - lastInputTime > maxGap) buffer = "";
        lastInputTime = Time.time;

        buffer += input;
        if (buffer.Length > combo.Length) buffer = buffer.Substring(buffer.Length - combo.Length);
        if (buffer != combo) return;

        buffer = "";
        var wm = WorldManager.Instance;
        Vector3 local = player.transform.position - (wm != null ? wm.Origin(wm.Current) : Vector3.zero);
        bool inside = Inside(local, regionMin, regionMax) ||
                      (region2Min != region2Max && Inside(local, region2Min, region2Max));
        if (!inside) return;

        GameState.Set(flag, true);
        Interactor.Say(doneMessage);
    }

    static bool Inside(Vector3 p, Vector3 min, Vector3 max)
    {
        return p.x >= min.x && p.x <= max.x && p.y >= min.y && p.y <= max.y && p.z >= min.z && p.z <= max.z;
    }
}
