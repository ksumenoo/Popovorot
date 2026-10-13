using UnityEngine;
using UnityEngine.InputSystem;

/// Отладочные клавиши для проверки уровня. Работают только в редакторе Unity,
/// в собранной игре ничего не делают.
///   F1 — комната А, F2 — комната Б, F3 — галерея Б у моста, F4 — галерея В, F5 — первый этаж В.
///   F6 — засчитать текущий шаг цепочки (тот, по которому сейчас подсказка).
public class DevCheats : MonoBehaviour
{
#if UNITY_EDITOR
    static readonly Vector3[] Points =
    {
        new Vector3(0f, 0.1f, 7f),       // А, первый этаж
        new Vector3(3f, 0.1f, 26f),      // Б, первый этаж
        new Vector3(0f, 3.6f, 34f),      // Б, галерея у моста
        new Vector3(3f, 3.6f, 46f),      // В, галерея
        new Vector3(0f, 0.1f, 54f),      // В, первый этаж
    };

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        Key[] keys = { Key.F1, Key.F2, Key.F3, Key.F4, Key.F5 };
        for (int i = 0; i < keys.Length; i++)
        {
            if (!keyboard[keys[i]].wasPressedThisFrame) continue;
            var player = GetComponent<PlayerController>();
            var wm = WorldManager.Instance;
            if (player == null || wm == null) return;
            player.Teleport(wm.Origin(wm.Current) + Points[i]);
            Interactor.Say("Отладка: перенос в точку " + (i + 1), 1.5f);
        }

        if (keyboard.f6Key.wasPressedThisFrame)
        {
            int step = Hints.FindStep();
            if (step < Hints.Steps.Length)
            {
                string flag = Hints.Steps[step][0];
                GameState.Set(flag, true);
                Interactor.Say("Отладка: засчитан шаг " + flag, 2f);
            }
        }
    }
#endif
}
