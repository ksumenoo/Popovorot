using System.Collections.Generic;
using UnityEngine;

/// Подсвечивает всё, с чем можно взаимодействовать: вблизи предметы и механизмы
/// слегка мерцают светящимся контуром, а тот, на который наведён прицел, светится ярко.
/// Мебель и прочий интерьер не подсвечиваются. Висит на игроке рядом с Interactor.
public class Highlighter : MonoBehaviour
{
    public Shader shader;
    [Tooltip("С какого расстояния объекты начинают слегка мерцать")]
    public float nearDistance = 6f;
    [Range(0f, 1f)] public float idleStrength = 0.3f;
    [Range(0f, 1f)] public float hoverStrength = 0.85f;
    public Color color = new Color(1f, 0.85f, 0.4f);
    [Tooltip("Цвет для японского мира: экран там инвертирован, поэтому тёмно-синий выглядит тёплым светом")]
    public Color invertedColor = new Color(0.05f, 0.2f, 0.65f);

    Interactor interactor;
    Material material;
    MaterialPropertyBlock block;
    readonly List<MonoBehaviour> interactables = new List<MonoBehaviour>();
    readonly List<MeshRenderer> renderers = new List<MeshRenderer>();
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int StrengthId = Shader.PropertyToID("_Strength");

    void Start()
    {
        interactor = GetComponent<Interactor>();
        if (shader == null) shader = Shader.Find("Popovorot/Highlight");
        if (shader == null) { enabled = false; return; }
        material = new Material(shader);
        block = new MaterialPropertyBlock();

        foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (behaviour is IInteractable) interactables.Add(behaviour);
    }

    void LateUpdate()
    {
        if (Hints.Paused || GameState.Has("door_open")) return;

        var wm = WorldManager.Instance;
        int world = wm != null ? wm.Current : 0;
        Vector3 eye = interactor != null && interactor.cam != null ? interactor.cam.transform.position : transform.position;
        Component hovered = interactor != null ? interactor.Target as Component : null;
        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 3f);

        block.SetColor(ColorId, world == 3 ? invertedColor : color);
        var rp = new RenderParams(material) { matProps = block, layer = gameObject.layer };

        foreach (var behaviour in interactables)
        {
            if (behaviour == null || !behaviour.isActiveAndEnabled) continue;
            if (behaviour.transform.IsChildOf(transform)) continue;   // предмет в руках
            if (string.IsNullOrEmpty(((IInteractable)behaviour).Prompt)) continue;   // уже сделано
            if (wm != null && wm.WorldOf(behaviour.transform.position) != world) continue;

            bool isHovered = hovered != null && hovered.transform == behaviour.transform;
            float distance = Vector3.Distance(eye, behaviour.transform.position);
            if (!isHovered && distance > nearDistance) continue;

            float fade = isHovered ? 1f : Mathf.Clamp01((nearDistance - distance) / 2f);
            block.SetFloat(StrengthId, isHovered ? hoverStrength : idleStrength * pulse * fade);

            behaviour.GetComponentsInChildren(false, renderers);
            foreach (var r in renderers)
            {
                if (!r.enabled) continue;
                var filter = r.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                Mesh mesh = filter.sharedMesh;
                for (int i = 0; i < mesh.subMeshCount; i++)
                    Graphics.RenderMesh(rp, mesh, i, r.localToWorldMatrix);
            }
        }
    }

    void OnDestroy()
    {
        if (material != null) Destroy(material);
    }
}
