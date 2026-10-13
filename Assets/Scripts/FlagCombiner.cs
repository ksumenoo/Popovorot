using UnityEngine;

/// Включает флаг, когда включены все флаги из allOf. Например, «все четыре замка открыты».
public class FlagCombiner : MonoBehaviour
{
    public string[] allOf = new string[0];
    public string flag;

    void Start()
    {
        GameState.Changed += Check;
        Check();
    }

    void OnDestroy()
    {
        GameState.Changed -= Check;
    }

    void Check()
    {
        if (GameState.Has(flag)) return;
        foreach (string f in allOf)
            if (!GameState.Has(f)) return;
        GameState.Set(flag, true);
    }
}
