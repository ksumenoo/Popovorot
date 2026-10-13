using UnityEngine;

/// Привязывает модель к объекту загадки из серого уровня.
/// При запуске модель становится дочерней к этому объекту: появляется, исчезает
/// и двигается вместе с ним, а его серый куб скрывается.
/// Модель лежит в корне Art, поэтому пересборка уровня её не стирает.
public class ArtLink : MonoBehaviour
{
    [Tooltip("Путь к объекту внутри Worlds, например World_0_Main/Puzzle/Cage_Cover")]
    public string targetPath;
    [Tooltip("Скрыть серые кубы самого объекта загадки")]
    public bool hideGreybox = true;
    [Tooltip("Прикрепить модель к объекту. Выключите для неподвижных объектов, которые всегда на месте")]
    public bool attach = true;

    void Start()
    {
        var worlds = GameObject.Find("Worlds");
        Transform target = worlds != null ? worlds.transform.Find(targetPath) : null;
        if (target == null)
        {
            Debug.LogWarning("ArtLink: не найден объект " + targetPath, this);
            return;
        }

        // Если у модели нет ни одной видимой части (например, её файл удалён из проекта),
        // серый куб не прячем, иначе объект загадки станет невидимым.
        if (GetComponentsInChildren<Renderer>(true).Length == 0)
        {
            Debug.LogWarning("ArtLink: у модели " + name + " нет видимых частей, серый куб оставлен", this);
            hideGreybox = false;
        }

        if (hideGreybox)
            foreach (var r in target.GetComponentsInChildren<MeshRenderer>(true))
                r.enabled = false;

        // Модель окружения уже содержит стенки кладовки. Серые стенки кладовки лежат
        // не в Geometry, а среди загадок, поэтому их нужно скрыть отдельно,
        // иначе две стены в одном месте мерцают друг сквозь друга.
        if (hideGreybox && target.name == "Geometry" && target.parent != null)
        {
            Transform closet = target.parent.Find("Puzzle/B_NorthWest/Closet");
            if (closet != null)
            {
                foreach (Transform part in closet)
                {
                    if (!part.name.StartsWith("Wall") && part.name != "Doorframe") continue;
                    var r = part.GetComponent<MeshRenderer>();
                    if (r != null) r.enabled = false;
                }
            }
        }

        if (attach) transform.SetParent(target, true);
    }
}
