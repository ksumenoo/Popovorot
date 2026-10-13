using UnityEngine;

/// Включает и выключает объекты по флагам. Условие выполнено, когда
/// включены все флаги из allOf и не включён ни один из noneOf.
/// Сам компонент должен висеть на объекте, который всегда активен.
public class FlagReactor : MonoBehaviour
{
    public string[] allOf = new string[0];
    public string[] noneOf = new string[0];
    [Tooltip("Активны, когда условие выполнено")]
    public GameObject[] on = new GameObject[0];
    [Tooltip("Активны, когда условие не выполнено")]
    public GameObject[] off = new GameObject[0];

    void Start()
    {
        GameState.Changed += Apply;
        Apply();
    }

    void OnDestroy()
    {
        GameState.Changed -= Apply;
    }

    void Apply()
    {
        bool ok = true;
        foreach (var f in allOf) if (!GameState.Has(f)) ok = false;
        foreach (var f in noneOf) if (GameState.Has(f)) ok = false;

        foreach (var go in on) if (go != null) go.SetActive(ok);
        foreach (var go in off) if (go != null) go.SetActive(!ok);
    }
}
