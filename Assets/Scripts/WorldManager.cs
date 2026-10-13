using System;
using UnityEngine;

/// Четыре мира стоят в сцене рядом, со сдвигом spacing по оси X.
/// Индексы: 0 — основная, 1 — мультяшная, 2 — перевёрнутая, 3 — японская.
public class WorldManager : MonoBehaviour
{
    public const int WorldCount = 4;
    public static readonly string[] Names = { "Основная", "Мультяшная", "Перевёрнутая", "Японская" };

    public static WorldManager Instance { get; private set; }

    [Tooltip("Расстояние между мирами по оси X")]
    public float spacing = 100f;

    public int Current { get; private set; }

    /// Вызывается после смены мира, аргумент — индекс нового мира.
    public event Action<int> WorldChanged;

    void Awake()
    {
        Instance = this;
    }

    public Vector3 Origin(int world)
    {
        return new Vector3(world * spacing, 0f, 0f);
    }

    /// В каком мире находится точка сцены.
    public int WorldOf(Vector3 position)
    {
        return Mathf.Clamp(Mathf.RoundToInt(position.x / spacing), 0, WorldCount - 1);
    }

    public void SetWorld(int world)
    {
        Current = world;
        WorldChanged?.Invoke(world);
    }
}
