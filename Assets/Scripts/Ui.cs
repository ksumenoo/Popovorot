using UnityEngine;

/// Общий масштаб всего игрового текста: подсказки, сообщения, таймер, индикатор мира.
/// Масштаб считается от размера окна, поэтому интерфейс одинаково выглядит
/// в 720p, 1080p, 4K и в узком окне браузера. Чтобы сделать текст крупнее, меняйте Size.
public static class Ui
{
    /// Размер текста относительно эталонного окна 1920×1080 (там масштаб равен 2).
    public const float Size = 1f;

    /// Множитель для экрана: по высоте от 540 пикселей, по ширине от 960, берётся меньший,
    /// чтобы в узком окне строки не вылезали за край.
    public static float Scale
    {
        get { return Mathf.Max(0.75f, Size * Mathf.Min(Screen.height / 540f, Screen.width / 960f)); }
    }

    /// Множитель для меню и паузы, которые размечены под окно 1280×720.
    public static float MenuScale
    {
        get { return Mathf.Min(Screen.height / 720f, Screen.width / 1100f); }
    }

    /// Размеры экрана в единицах интерфейса. В OnGUI используйте их вместо Screen.width и Screen.height.
    public static float W { get { return Screen.width / Scale; } }
    public static float H { get { return Screen.height / Scale; } }

    /// Вызывается первой строкой в каждом OnGUI.
    public static void Begin()
    {
        GUI.matrix = Matrix4x4.Scale(new Vector3(Scale, Scale, 1f));
    }
}
