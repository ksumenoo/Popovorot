using UnityEngine;
using UnityEngine.Rendering;

/// Включает цветокоррекцию текущего мира: у каждого мира свой Volume.
public class WorldLook : MonoBehaviour
{
    [Tooltip("Четыре Volume в порядке миров: основная, мультяшная, перевёрнутая, японская")]
    public Volume[] volumes = new Volume[0];

    void Start()
    {
        var wm = WorldManager.Instance;
        if (wm == null) return;
        wm.WorldChanged += Apply;
        Apply(wm.Current);
    }

    void OnDestroy()
    {
        if (WorldManager.Instance != null) WorldManager.Instance.WorldChanged -= Apply;
    }

    void Apply(int world)
    {
        for (int i = 0; i < volumes.Length; i++)
            if (volumes[i] != null) volumes[i].weight = i == world ? 1f : 0f;
    }
}
