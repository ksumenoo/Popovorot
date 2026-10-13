using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// Меню Tools → Поповорот → Собрать серый уровень.
/// Строит четыре мира (комната А, коридор, комната Б), все восемь загадок,
/// игрока и WorldManager.
public static class LevelBuilder
{
    const float H = 7f;          // высота комнат: два этажа
    const float G = 3.5f;        // уровень пола второго этажа (галереи и моста)
    const float CH = 3.5f;       // высота коридора и моста
    const float StairRise = 0.25f;   // высота ступени
    const float StairRun = 0.35f;    // глубина ступени
    const int StairSteps = 14;       // 14 ступеней по 0,25 м = 3,5 м
    const float T = 0.2f;        // толщина стен
    const float Door = 2.2f;     // высота проёма
    const float Spacing = 100f;  // расстояние между мирами

    const float W = 7f;          // половина ширины комнат: комнаты 14×14 м
    const float LA = 14f;        // длина комнаты
    const float LC = 8f;         // длина коридора
    const float CW = 1.5f;       // половина ширины коридора
    const float ZA = LA;             // северная стена комнаты А
    const float ZB = LA + LC;        // южная стена комнаты Б
    const float ZN = LA + LC + LA;   // северная стена комнаты Б
    const float ZC0 = ZN + LC;       // южная стена комнаты В
    const float ZCN = ZC0 + LA;      // северная стена комнаты В

    const int Main = 0, Cartoon = 1, Upside = 2, Japanese = 3;

    [MenuItem("Tools/Поповорот/1. Собрать серый уровень", false, 1)]
    static void Build()
    {
        var existing = GameObject.Find("Worlds");
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog("Поповорот",
                    "Объект Worlds уже есть в сцене. Удалить его и собрать уровень заново?",
                    "Пересобрать", "Отмена")) return;
            Object.DestroyImmediate(existing);
        }

        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");

        // Цвета: стены и пол для каждого мира.
        Color[] wallColors =
        {
            new Color(0.45f, 0.46f, 0.48f),   // основная: бетон
            new Color(1.00f, 0.80f, 0.25f),   // мультяшная: жёлтый
            new Color(0.30f, 0.32f, 0.38f),   // перевёрнутая: тёмный бетон
            new Color(0.90f, 0.45f, 0.40f),   // японская: тёплое дерево, на экране инвертируется
        };
        Color[] floorColors =
        {
            new Color(0.25f, 0.25f, 0.27f),
            new Color(0.35f, 0.75f, 1.00f),
            new Color(0.55f, 0.55f, 0.58f),
            new Color(0.25f, 0.80f, 0.45f),   // японская: татами, на экране инвертируется
        };
        string[] ids = { "Main", "Cartoon", "Upside", "Japanese" };

        var worlds = new GameObject("Worlds").transform;
        for (int i = 0; i < 4; i++)
        {
            var world = new GameObject("World_" + i + "_" + ids[i]).transform;
            world.SetParent(worlds, false);
            world.localPosition = new Vector3(i * Spacing, 0f, 0f);

            var geometry = new GameObject("Geometry").transform;
            geometry.SetParent(world, false);
            if (i == Upside)
            {
                // Перевёрнутый мир: геометрия отражена по вертикали,
                // поэтому игрок с обычной гравитацией идёт по потолку.
                geometry.localPosition = new Vector3(0f, H, 0f);
                geometry.localScale = new Vector3(1f, -1f, 1f);
            }

            // Текстуры миров. Перевёрнутый мир берёт текстуры основного, но темнее и холоднее.
            string tex = i == Upside ? "Main" : ids[i];
            Color tint = i == Upside ? new Color(0.72f, 0.78f, 0.9f) : Color.white;
            var wall = TexMat(ids[i] + "_Wall", "T_" + tex + "_Wall", tint, WallTile[i]);
            var floor = TexMat(ids[i] + "_Floor", "T_" + tex + "_Floor", tint, 2f);
            var ceiling = TexMat(ids[i] + "_Ceiling", "T_" + tex + "_Ceiling", tint, CeilingTile[i]);
            string[] trims = { "T_Metal", "T_WoodLacquer", "T_MetalDark", "T_Wood" };
            var trim = TexMat(ids[i] + "_Trim", trims[i], Color.white, 2f);
            BuildGeometry(geometry, wall, floor, ceiling, trim, i);

            // Загадки стоят в обычных координатах, без отражения:
            // в перевёрнутом мире y = 0 — это поверхность, по которой идёт игрок.
            var puzzle = new GameObject("Puzzle").transform;
            puzzle.SetParent(world, false);
            BuildPuzzles(puzzle, i, wall);
            BuildDecor(puzzle, i);
        }

        BuildPlayer();

        var oldManager = GameObject.Find("WorldManager");
        if (oldManager != null) Object.DestroyImmediate(oldManager);
        var manager = new GameObject("WorldManager");
        manager.AddComponent<WorldManager>().spacing = Spacing;
        BuildLooks(manager, ids);
        BuildArtRoots(ids);
        int removed = ClearAutoModels();
        if (removed > 0)
            Debug.Log("Поповорот: убрано автоматически расставленных папок с моделями — " + removed +
                      ". Чтобы поставить модели заново, выберите «Расставить модели».");

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("Поповорот: серый уровень собран. Нажмите Play.");
    }

    // ------------------------------------------------------------------
    // Геометрия. Север — это +Z, восток — это +X.
    // Комнаты высотой H = 7 м, у каждой галерея (второй этаж) на высоте G = 3,5 м.
    // Комната А: z от 0 до ZA, галерея над северной половиной, лестница у западной стены.
    // Коридор А–Б: z от ZA до ZB, на уровне первого этажа.
    // Комната Б: z от ZB до ZN, галерея над северной половиной, лестница висит под потолком.
    // Мост Б–В: z от ZN до ZC0, на уровне второго этажа, проёмы во всю высоту.
    // Комната В: z от ZC0 до ZCN, галерея над южной половиной, лестница вниз у западной стены.
    // ------------------------------------------------------------------
    static void BuildGeometry(Transform p, Material wall, Material floor, Material ceiling, Material trim, int world)
    {
        // Полы первого этажа
        Box(p, "Floor_A", new Vector3(-W, -T, 0), new Vector3(W, 0, ZA), floor);
        Box(p, "Floor_Corridor", new Vector3(-CW, -T, ZA), new Vector3(CW, 0, ZB), floor);
        Box(p, "Floor_B", new Vector3(-W, -T, ZB), new Vector3(W, 0, ZN), floor);
        Box(p, "Floor_C", new Vector3(-W, -T, ZC0), new Vector3(W, 0, ZCN), floor);

        // Галереи (пол второго этажа) и пол моста
        Box(p, "Gallery_A", new Vector3(-W, G - T, ZA * 0.5f), new Vector3(W, G, ZA), floor);
        Box(p, "Gallery_B", new Vector3(-W, G - T, ZB + LA * 0.5f), new Vector3(W, G, ZN), floor);
        if (world == Cartoon)
        {
            Box(p, "Gallery_C", new Vector3(-W, G - T, ZC0), new Vector3(W, G, ZC0 + LA * 0.5f), floor);
        }
        else
        {
            // Провал поперёк галереи В: в мультяшном мире пол здесь цел.
            Box(p, "Gallery_C_West", new Vector3(-W, G - T, ZC0), new Vector3(GapX0, G, ZC0 + LA * 0.5f), floor);
            Box(p, "Gallery_C_East", new Vector3(GapX1, G - T, ZC0), new Vector3(W, G, ZC0 + LA * 0.5f), floor);
            RailZ(p, "C_Gap_Rail_W", GapX0, ZC0, ZC0 + LA * 0.5f, trim);
            RailZ(p, "C_Gap_Rail_E", GapX1, ZC0, ZC0 + LA * 0.5f, trim);
        }
        Box(p, "Floor_Bridge", new Vector3(-CW, G - T, ZN), new Vector3(CW, G, ZC0), floor);

        // Потолки
        Box(p, "Ceiling_A", new Vector3(-W, H, 0), new Vector3(W, H + T, ZA), ceiling);
        Box(p, "Ceiling_B", new Vector3(-W, H, ZB), new Vector3(W, H + T, ZN), ceiling);
        Box(p, "Ceiling_C", new Vector3(-W, H, ZC0), new Vector3(W, H + T, ZCN), ceiling);
        // Потолок коридора начинается за толщиной стен: иначе его торец совпадает со стеной
        // над дверью и мерцает полоской на уровне галереи.
        Box(p, "Ceiling_Corridor", new Vector3(-CW, CH, ZA + T), new Vector3(CW, CH + T, ZB - T), ceiling);
        Box(p, "Ceiling_Bridge", new Vector3(-CW, H, ZN), new Vector3(CW, H + T, ZC0), ceiling);

        // Комната А
        Box(p, "A_Wall_S", new Vector3(-W - T, 0, -T), new Vector3(W + T, H, 0), wall);
        Box(p, "A_Wall_W", new Vector3(-W - T, 0, 0), new Vector3(-W, H, ZA), wall);
        Box(p, "A_Wall_E", new Vector3(W, 0, 0), new Vector3(W + T, H, ZA), wall);
        Box(p, "A_Wall_N_Left", new Vector3(-W - T, 0, ZA), new Vector3(-CW, H, ZA + T), wall);
        Box(p, "A_Wall_N_Right", new Vector3(CW, 0, ZA), new Vector3(W + T, H, ZA + T), wall);
        Box(p, "A_Wall_N_AboveDoor", new Vector3(-CW, Door, ZA), new Vector3(CW, H, ZA + T), wall);

        // Коридор А–Б
        Box(p, "Corridor_Wall_W", new Vector3(-CW - T, 0, ZA + T), new Vector3(-CW, CH, ZB - T), wall);
        Box(p, "Corridor_Wall_E", new Vector3(CW, 0, ZA + T), new Vector3(CW + T, CH, ZB - T), wall);

        // Комната Б: внизу на юге проём из коридора, наверху на севере проём на мост
        Box(p, "B_Wall_S_Left", new Vector3(-W - T, 0, ZB - T), new Vector3(-CW, H, ZB), wall);
        Box(p, "B_Wall_S_Right", new Vector3(CW, 0, ZB - T), new Vector3(W + T, H, ZB), wall);
        Box(p, "B_Wall_S_AboveDoor", new Vector3(-CW, Door, ZB - T), new Vector3(CW, H, ZB), wall);
        Box(p, "B_Wall_W", new Vector3(-W - T, 0, ZB), new Vector3(-W, H, ZN), wall);
        Box(p, "B_Wall_E", new Vector3(W, 0, ZB), new Vector3(W + T, H, ZN), wall);
        Box(p, "B_Wall_N_Left", new Vector3(-W - T, 0, ZN), new Vector3(-CW, H, ZN + T), wall);
        Box(p, "B_Wall_N_Right", new Vector3(CW, 0, ZN), new Vector3(W + T, H, ZN + T), wall);
        Box(p, "B_Wall_N_BelowBridge", new Vector3(-CW, 0, ZN), new Vector3(CW, G - T, ZN + T), wall);

        // Мост Б–В: стены во всю его высоту, без притолок
        Box(p, "Bridge_Wall_W", new Vector3(-CW - T, G - T, ZN + T), new Vector3(-CW, H, ZC0 - T), wall);
        Box(p, "Bridge_Wall_E", new Vector3(CW, G - T, ZN + T), new Vector3(CW + T, H, ZC0 - T), wall);

        // Комната В
        Box(p, "C_Wall_S_Left", new Vector3(-W - T, 0, ZC0 - T), new Vector3(-CW, H, ZC0), wall);
        Box(p, "C_Wall_S_Right", new Vector3(CW, 0, ZC0 - T), new Vector3(W + T, H, ZC0), wall);
        Box(p, "C_Wall_S_BelowBridge", new Vector3(-CW, 0, ZC0 - T), new Vector3(CW, G - T, ZC0), wall);
        Box(p, "C_Wall_W", new Vector3(-W - T, 0, ZC0), new Vector3(-W, H, ZCN), wall);
        Box(p, "C_Wall_E", new Vector3(W, 0, ZC0), new Vector3(W + T, H, ZCN), wall);
        Box(p, "C_Wall_N", new Vector3(-W - T, 0, ZCN), new Vector3(W + T, H, ZCN + T), wall);

        // Перила по краю галерей, с проходом к лестнице
        Rail(p, "A_Rail", -W + 2f, W, ZA * 0.5f, trim);
        Rail(p, "B_Rail", -W, W, ZB + LA * 0.5f, trim);
        Rail(p, "C_Rail", -W + 2f, W, ZC0 + LA * 0.5f, trim);

        // Лестница А у западной стены, поднимается на север к галерее.
        // В мультяшном мире она целая, в остальных без середины.
        {
            Transform stairs = Group(p, "Stairs_A");
            float edge = ZA * 0.5f;
            for (int i = 0; i < StairSteps; i++)
            {
                if (world != Cartoon && i >= 5 && i <= 9) continue;
                float z0 = edge - StairRun * (StairSteps - i);
                Box(stairs, "Step_" + i, new Vector3(-W, 0, z0), new Vector3(-W + 2f, StairRise * (i + 1), z0 + StairRun), trim);
            }
        }

        // Комната Б: обычной лестницы нет. Под потолком висит перевёрнутая лестница:
        // в обычных мирах до неё не достать, а в перевёрнутом мире это нормальная лестница
        // от пола до уровня галереи. Ступени идут вдоль края галереи с востока на запад.
        {
            Transform stairs = Group(p, "Stairs_B_Hanging");
            float zEdge = ZB + LA * 0.5f;
            for (int j = 0; j <= StairSteps; j++)
            {
                float x1 = -0.8f - StairRun * j;
                float top = StairRise * (j + 1);   // высота ступени в перевёрнутом мире
                Box(stairs, "Step_" + j, new Vector3(x1 - StairRun, H - top, zEdge - 2.1f), new Vector3(x1, H, zEdge), trim);
            }
        }

        // Лестница В у западной стены: спускается с галереи на север.
        {
            Transform stairs = Group(p, "Stairs_C");
            float edge = ZC0 + LA * 0.5f;
            for (int k = 0; k < StairSteps - 1; k++)
            {
                float z0 = edge + StairRun * k;
                Box(stairs, "Step_" + k, new Vector3(-W, 0, z0), new Vector3(-W + 2f, G - StairRise * (k + 1), z0 + StairRun), trim);
            }
        }

        // Свет: внизу и наверху каждой комнаты, в коридоре и на мосту
        PointLight(p, "Light_A_Low", new Vector3(0, 2.6f, ZA * 0.3f), 14f, 14f);
        PointLight(p, "Light_A_Under", new Vector3(0, 2.6f, ZA * 0.8f), 10f, 8f);
        PointLight(p, "Light_A_Up", new Vector3(0, 6.3f, ZA * 0.6f), 16f, 16f);
        PointLight(p, "Light_Corridor", new Vector3(0, 3f, (ZA + ZB) * 0.5f));
        PointLight(p, "Light_B_Low", new Vector3(0, 2.6f, ZB + LA * 0.3f), 14f, 14f);
        PointLight(p, "Light_B_Under", new Vector3(0, 2.6f, ZB + LA * 0.8f), 10f, 8f);
        PointLight(p, "Light_B_Up", new Vector3(0, 6.3f, ZB + LA * 0.6f), 16f, 16f);
        PointLight(p, "Light_Bridge", new Vector3(0, G + 3f, (ZN + ZC0) * 0.5f));
        PointLight(p, "Light_C_Under", new Vector3(0, 2.6f, ZC0 + LA * 0.2f), 10f, 8f);
        PointLight(p, "Light_C_Low", new Vector3(0, 2.6f, ZC0 + LA * 0.7f), 14f, 14f);
        PointLight(p, "Light_C_Up", new Vector3(0, 6.3f, ZC0 + LA * 0.4f), 16f, 16f);
    }

    const float GapX0 = -4f, GapX1 = -1.6f;   // провал в галерее В

    /// Перила высотой 1 м вдоль линии x, от z0 до z1.
    static void RailZ(Transform p, string name, float x, float z0, float z1, Material material)
    {
        Box(p, name, new Vector3(x - 0.04f, G, z0), new Vector3(x + 0.04f, G + 1f, z1), material);
    }

    /// Плоская стрелка на полу длиной 1,8 м. direction: +1 — остриё на восток, -1 — на запад.
    static void FloorArrow(Transform parent, string name, Vector3 center, int direction, Material material)
    {
        float d = direction;
        Box(parent, name + "_Shaft", center + new Vector3(-0.6f, 0f, -0.11f), center + new Vector3(0.6f, 0.03f, 0.11f), material, false);
        Vector3 tip = center + new Vector3(0.75f * d, 0f, 0f);
        TurnedBox(parent, name + "_HeadA", tip + new Vector3(-0.25f * d, 0.015f, 0.25f), new Vector3(0.75f, 0.03f, 0.22f), 45f * d, material);
        TurnedBox(parent, name + "_HeadB", tip + new Vector3(-0.25f * d, 0.015f, -0.25f), new Vector3(0.75f, 0.03f, 0.22f), -45f * d, material);
    }

    /// Куб без коллайдера, повёрнутый вокруг вертикали на yaw градусов.
    static void TurnedBox(Transform parent, string name, Vector3 center, Vector3 size, float yaw, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = material;
        Object.DestroyImmediate(go.GetComponent<Collider>());
    }

    /// Плоская надпись на полу, читается сверху тем, кто смотрит на юг.
    static void FloorText(Transform parent, string name, string text, Vector3 position, float height, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localRotation = Quaternion.Euler(90f, 180f, 0f);
        var tm = go.AddComponent<TextMesh>();
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        tm.font = font;
        tm.text = text;
        tm.fontSize = 100;
        tm.characterSize = height / 10f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = color;
        go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
    }

    /// Перила высотой 1 м вдоль края галереи на линии z, от x0 до x1.
    static void Rail(Transform p, string name, float x0, float x1, float z, Material material)
    {
        Box(p, name, new Vector3(x0, G, z - 0.04f), new Vector3(x1, G + 1f, z + 0.04f), material);
    }

    // ------------------------------------------------------------------
    // Загадки. Флаги: cage_open, power, gate_timer, gate_fixed, b_timer, locker_open,
    // shutter_open, safe_open, chain_cut, tower_down, tansu_open, door_power, door_open.
    // Предметы: fuse, weight, cutter, key, handle.
    //
    // Координаты внутри якорей записаны для комнаты 10×10: якорь сдвигает
    // свою группу объектов к нужной стене комнаты текущего размера.
    // ------------------------------------------------------------------
    static void BuildPuzzles(Transform p, int world, Material wall)
    {
        Transform logic = Group(p, "Logic");

        Material metal = Mat("C_Metal", new Color(0.35f, 0.37f, 0.40f));
        Material dark = Mat("C_Dark", new Color(0.12f, 0.12f, 0.14f));
        Material red = Mat("C_Red", Color.red);
        Material green = Mat("C_Green", Color.green);
        Material blue = Mat("C_Blue", Color.blue);
        Material yellow = Mat("C_Yellow", Color.yellow);
        Material magenta = Mat("C_Magenta", Color.magenta);
        Material orange = Mat("C_Orange", new Color(1f, 0.5f, 0f));
        Material wood = Mat("C_Wood", new Color(0.55f, 0.38f, 0.2f));
        Material paper = Mat("C_Paper", new Color(0.95f, 0.92f, 0.82f));

        float e = W - 5f;   // на сколько стены отодвинулись в стороны
        Transform aNE = Anchor(p, "A_NorthEast", e, ZA - 10f);
        Transform aN = Anchor(p, "A_North", 0f, ZA - 10f);
        Transform aE = Anchor(p, "A_East", e, 0f);
        Transform aS = Anchor(p, "A_South", 0f, 0f);
        Transform aW = Anchor(p, "A_West_Upper", -e, ZA * 0.8f - 5f, G);   // свиток на галерее
        Transform cN = Anchor(p, "Corridor_North", 0f, ZB - 18f);
        Transform bS = Anchor(p, "B_South", 0f, ZB - 18f);
        Transform bW = Anchor(p, "B_West", -e, ZB - 18f);
        Transform bE = Anchor(p, "B_East", e, ZB - 18f);
        Transform bN = Anchor(p, "B_North", 0f, ZN - 28f);
        Transform bNW = Anchor(p, "B_NorthWest", -W, ZN, G);   // угол галереи Б: x от 0 на восток, z от 0 на юг
        Transform aNU = Anchor(p, "A_North_Upper", 0f, ZA - 10f, G);   // северная стена галереи А
        Transform cNorth = Anchor(p, "C_North", 0f, ZCN - 28f);         // северная стена комнаты В

        // ===== 1. Комната А, северо-восточный угол: клетка и кнопка =====
        {
            if (world == Main)
            {
                Box(aNE, "Cage_Pedestal", new Vector3(4, 0, 9), new Vector3(5, 0.8f, 10), metal);
                Item(aNE, "Fuse", new Vector3(4.5f, 0.95f, 9.5f), new Vector3(0.12f, 0.3f, 0.12f), orange,
                    "fuse", "предохранитель");

                Transform cover = Group(aNE, "Cage_Cover");
                Bars(cover, new Vector3(4, 0.8f, 9), new Vector3(4.05f, 1.8f, 10), false, dark);
                Bars(cover, new Vector3(4, 0.8f, 9), new Vector3(5, 1.8f, 9.05f), true, dark);
                Box(cover, "Top", new Vector3(4, 1.8f, 9), new Vector3(5, 1.85f, 10), dark);
                Blocker(cover, new Vector3(4, 0.8f, 9), new Vector3(5, 1.8f, 10));
                React(logic, "Cage", null, A("cage_open"), new[] { cover.gameObject });
            }
            else if (world == Cartoon)
            {
                Box(aNE, "Chest", new Vector3(4, 0, 9), new Vector3(5, 0.8f, 10), blue);
                GameObject button = Box(aNE, "Chest_Button", new Vector3(4.3f, 0.8f, 9.3f),
                    new Vector3(4.7f, 0.95f, 9.7f), red);
                Switch(button, "cage_open", "ЛКМ — нажать кнопку", null, null, null, "Где-то щёлкнул замок");
            }
            else if (world == Upside)
            {
                Flip(aNE, "Corner_Block", new Vector3(4, 0, 9), new Vector3(5, 0.8f, 10), metal);
            }
            else
            {
                Box(aNE, "Corner_Block", new Vector3(4, 0, 9), new Vector3(5, 1f, 10), wall);
            }
        }

        // ===== 2. Комната А, восточная часть: контейнер со щитком =====
        // Открыт только в мультяшном мире. Игрок входит туда, поворачивается влево
        // и оказывается внутри закрытого контейнера основного мира, где висит щиток.
        // В перевёрнутом мире контейнер висит над головой, как и положено.
        {
            Material body = world == Main ? orange : world == Cartoon ? green : world == Japanese ? wood : metal;
            Transform container = Group(aE, "Container");
            if (world == Upside)
            {
                container.localPosition = new Vector3(0f, H, 0f);
                container.localScale = new Vector3(1f, -1f, 1f);
            }
            Box(container, "Roof", new Vector3(0.8f, 2.3f, 1.2f), new Vector3(4.8f, 2.4f, 3.8f), body);
            Box(container, "Wall_N", new Vector3(0.8f, 0, 3.7f), new Vector3(4.8f, 2.3f, 3.8f), body);
            Box(container, "Wall_S", new Vector3(0.8f, 0, 1.2f), new Vector3(4.8f, 2.3f, 1.3f), body);
            Box(container, "Wall_E", new Vector3(4.7f, 0, 1.3f), new Vector3(4.8f, 2.3f, 3.7f), body);
            if (world == Cartoon)
            {
                // Распахнутые створки, без коллайдеров.
                Box(container, "Door_Open_S", new Vector3(-0.4f, 0, 1.2f), new Vector3(0.8f, 2.3f, 1.3f), yellow, false);
                Box(container, "Door_Open_N", new Vector3(-0.4f, 0, 3.7f), new Vector3(0.8f, 2.3f, 3.8f), yellow, false);
            }
            else
            {
                Box(container, "Door_Closed", new Vector3(0.8f, 0, 1.3f), new Vector3(0.9f, 2.3f, 3.7f), dark);
            }
            PointLight(container, "Light_Inside", new Vector3(2.8f, 2.0f, 2.5f), 5f, 3f);

            if (world == Main)
            {
                GameObject panel = Box(aE, "FusePanel", new Vector3(3.2f, 1.0f, 3.62f),
                    new Vector3(3.8f, 1.8f, 3.7f), metal);
                Switch(panel, "power", "ЛКМ — вставить предохранитель", "fuse", null,
                    "Нужен предохранитель", "Питание включено");
                GameObject lampOff = Box(aE, "FusePanel_LampOff", new Vector3(3.45f, 1.85f, 3.64f),
                    new Vector3(3.55f, 1.95f, 3.7f), red, false);
                GameObject lampOn = Box(aE, "FusePanel_LampOn", new Vector3(3.45f, 1.85f, 3.64f),
                    new Vector3(3.55f, 1.95f, 3.7f), green, false);
                React(logic, "Power_Lamp", A("power"), null, new[] { lampOn }, new[] { lampOff });
            }
        }

        // ===== 3. Комната А, южная стена: кнопка таймера решётки (только с потолка) =====
        {
            if (world == Upside)
            {
                // Механизм решётки: пустой вал, крутится рукояткой из ниши на галерее.
                // Рукоятка не тратится — она понадобится ещё у двери на мост.
                GameObject button = Box(aS, "TimerButton", new Vector3(-0.25f, 0.8f, 0),
                    new Vector3(0.25f, 1.3f, 0.12f), red);
                FlagSwitch sw = Switch(button, "gate_timer", "ЛКМ — провернуть механизм решётки", "lever_handle", "power",
                    "Вал механизма пустой — нужна рукоятка", "Решётка открыта!", "Механизм обесточен");
                sw.duration = 6.5f;
                sw.keepItem = true;
            }
            else if (world != Main)
            {
                Box(aS, "TimerButton_Decor", new Vector3(-0.25f, 2.2f, 0), new Vector3(0.25f, 2.7f, 0.12f), red, false);
            }
        }

        // ===== Комната А, западная стена: свиток с порядком цветов =====
        if (world == Japanese)
        {
            Box(aW, "Scroll", new Vector3(-5, 0.95f, 4.6f), new Vector3(-4.96f, 2.65f, 5.4f), dark, false);
            // Цвета настоящие; цветокоррекция японского мира покажет их инвертированными:
            // жёлтый, голубой, пурпурный, синий.
            Material[] order = { blue, red, green, yellow };
            for (int i = 0; i < 4; i++)
            {
                float y = 2.4f - i * 0.4f;
                Box(aW, "Scroll_Circle_" + i, new Vector3(-4.96f, y - 0.15f, 4.85f),
                    new Vector3(-4.92f, y + 0.15f, 5.15f), order[i], false);
            }
        }

        // ===== Комната А, у проёма: огнетушитель — подсказка об инверсии =====
        if (world != Upside)
        {
            // В японском мире инверсия покажет его голубым.
            Box(aN, "Extinguisher", new Vector3(2.0f, 0.9f, 9.8f), new Vector3(2.25f, 1.5f, 10), red, false);
        }

        // ===== 4–5. Комната А: стрелки на полу и код поворотами на галерее =====
        {
            // Стрелки лежат на полу первого этажа только в мультяшном мире. Мультяшный мир
            // смотрит на восток, поэтому для игрока «влево» — это север. Стрелки идут столбиком
            // слева направо (с севера на юг), у каждой номер точками со стороны игрока.
            // Код набирается здесь же, у стрелок; ниша с рукояткой открывается на галерее.
            if (world == Cartoon)
            {
                int[] north = { 1, -1, -1, 1 };   // влево, вправо, вправо, влево
                for (int i = 0; i < 4; i++)
                {
                    float z = 6.3f - i * 1.7f;
                    Transform arrow = Group(p, "TurnCode_" + i);
                    arrow.localPosition = new Vector3(-1.5f, 0f, z);   // западнее контейнера, чтобы он не заслонял
                    arrow.localRotation = Quaternion.Euler(0f, -90f, 0f);   // локальный +X смотрит на север
                    arrow.localScale = new Vector3(0.75f, 1f, 0.75f);
                    FloorArrow(arrow, "Arrow", Vector3.zero, north[i], red);
                    for (int d = 0; d <= i; d++)   // номер стрелки — столько же точек перед ней
                    {
                        float dz = z + (d - i * 0.5f) * 0.3f;
                        Box(p, "TurnCode_" + i + "_Dot_" + d, new Vector3(-2.75f, 0f, dz - 0.1f), new Vector3(-2.55f, 0.03f, dz + 0.1f), blue, false);
                    }
                }
            }

            if (world == Main)
            {
                var lockObject = new GameObject("TurnCodeLock");
                lockObject.transform.SetParent(logic, false);
                var turnLock = lockObject.AddComponent<TurnSequenceLock>();
                turnLock.combo = "LRRL";
                turnLock.flag = "turn_code";
                // Код срабатывает, если стоять у стрелок на первом этаже.
                turnLock.regionMin = new Vector3(-5f, -0.5f, 0.2f);
                turnLock.regionMax = new Vector3(1f, 2.5f, 6.8f);
                // И на галерее: многие игроки идут набирать код наверх, откуда стрелки видно целиком.
                turnLock.region2Min = new Vector3(-W, G - 0.5f, ZA * 0.5f);
                turnLock.region2Max = new Vector3(W, G + 2.5f, ZA);
                turnLock.doneMessage = "Щёлк! Наверху, на галерее, в стене над проходом во вторую комнату открылась светящаяся ниша";

                // Ниша в северной стене галереи, закрытая заслонкой до решения кода.
                // Ниша: тёмная задняя стенка у самой стены и рамка вокруг, рукоятка лежит
                // на полке между ними (раньше рамка была сплошным кубом и прятала рукоятку).
                Box(aNU, "Niche_Back", new Vector3(2.85f, 0.85f, 9.96f), new Vector3(4.15f, 1.85f, 10), dark, false);
                Box(aNU, "Niche_Frame_L", new Vector3(2.85f, 0.85f, 9.7f), new Vector3(2.95f, 1.85f, 9.96f), dark, false);
                Box(aNU, "Niche_Frame_R", new Vector3(4.05f, 0.85f, 9.7f), new Vector3(4.15f, 1.85f, 9.96f), dark, false);
                Box(aNU, "Niche_Frame_T", new Vector3(2.95f, 1.75f, 9.7f), new Vector3(4.05f, 1.85f, 9.96f), dark, false);
                Box(aNU, "Niche_Shelf", new Vector3(2.95f, 0.85f, 9.7f), new Vector3(4.05f, 0.95f, 9.96f), metal);
                Item(aNU, "LeverHandle", new Vector3(3.5f, 1.05f, 9.83f), new Vector3(0.4f, 0.08f, 0.08f), orange,
                    "lever_handle", "рукоятка");
                GameObject cover = Box(aNU, "Niche_Cover", new Vector3(2.95f, 0.9f, 9.66f), new Vector3(4.05f, 1.8f, 9.7f), metal);
                React(logic, "Niche_Cover", null, A("turn_code"), new[] { cover });
                // Свет в нише, чтобы её было видно издалека после решения кода.
                var nicheLight = new GameObject("Niche_Light");
                nicheLight.transform.SetParent(aNU, false);
                nicheLight.transform.localPosition = new Vector3(3.5f, 1.5f, 9.5f);
                var nl = nicheLight.AddComponent<Light>();
                nl.type = LightType.Point; nl.color = new Color(1f, 0.75f, 0.35f); nl.range = 3.5f; nl.intensity = 3f;
                React(logic, "Niche_Light", A("turn_code"), null, new[] { nicheLight });
            }
        }

        // ===== Коридор, северный конец: решётка (общая для всех миров) =====
        {
            Transform gate = Group(cN, "Gate");
            Bars(gate, new Vector3(-1.5f, 0, 17.4f), new Vector3(1.5f, CH, 17.5f), true, dark);
            Blocker(gate, new Vector3(-1.5f, 0, 17.4f), new Vector3(1.5f, CH, 17.5f));
            React(logic, "Gate", null, A("gate_timer", "gate_fixed"), new[] { gate.gameObject });
        }

        // ===== Комната Б, южная стена: рычаг, фиксирующий решётку =====
        if (world != Main)
        {
            GameObject lever = Box(bS, "GateLever", new Vector3(2.8f, 1.0f, 18), new Vector3(3.2f, 1.4f, 18.12f), orange);
            Switch(lever, "gate_fixed", "ЛКМ — зафиксировать решётку", null, null, null, "Решётка зафиксирована");
        }

        // ===== 4. Комната Б: две кнопки на время =====
        // Первая — низко на северной стене (основной мир), вторая — под потолком
        // на южной (перевёрнутый мир). После первой есть несколько секунд на вторую.
        {
            if (world == Main)
            {
                GameObject first = Box(bN, "PairButton_North", new Vector3(3.3f, 0.9f, 27.88f),
                    new Vector3(3.7f, 1.3f, 28), red);
                FlagSwitch sw = Switch(first, "b_timer", "ЛКМ — нажать кнопку", null, null, null,
                    "Под потолком на другой стене загорелась кнопка");
                sw.duration = 5.5f;
            }
            else if (world != Upside)
            {
                Box(bN, "PairButton_North_Decor", new Vector3(3.3f, 0.9f, 27.88f),
                    new Vector3(3.7f, 1.3f, 28), red, false);
            }

            if (world == Upside)
            {
                GameObject second = Box(bS, "PairButton_South", new Vector3(-3.75f, 0.8f, 18),
                    new Vector3(-3.25f, 1.3f, 18.12f), red);
                Switch(second, "locker_open", "ЛКМ — нажать кнопку", null, "b_timer", null,
                    "Открылся ящик с гирей", "Кнопка не горит. Сначала кнопка на противоположной стене");
            }
            else if (world != Main)
            {
                Box(bS, "PairButton_South_Decor", new Vector3(-3.75f, 2.2f, 18),
                    new Vector3(-3.25f, 2.7f, 18.12f), red, false);
            }
        }

        // ===== 5. Комната Б, у восточной стены: ящик с гирей (x 4..5, z 26..27) =====
        {
            if (world == Cartoon)
            {
                Box(bE, "Locker", new Vector3(4, 0, 26), new Vector3(5, 0.8f, 27), blue);
                Item(bE, "Weight", new Vector3(4.5f, 1.0f, 26.5f), new Vector3(0.35f, 0.4f, 0.35f), dark,
                    "weight", "гиря");

                Transform cover = Group(bE, "Locker_Cover");
                Bars(cover, new Vector3(4, 0.8f, 26), new Vector3(4.05f, 1.8f, 27), false, yellow);
                Bars(cover, new Vector3(4, 0.8f, 26), new Vector3(5, 1.8f, 26.05f), true, yellow);
                Bars(cover, new Vector3(4, 0.8f, 26.95f), new Vector3(5, 1.8f, 27), true, yellow);
                Box(cover, "Top", new Vector3(4, 1.8f, 26), new Vector3(5, 1.85f, 27), yellow);
                Blocker(cover, new Vector3(4, 0.8f, 26), new Vector3(5, 1.8f, 27));
                React(logic, "Locker", null, A("locker_open"), new[] { cover.gameObject });
            }
            else if (world == Upside)
            {
                Flip(bE, "Locker_Block", new Vector3(4, 0, 26), new Vector3(5, 0.8f, 27), metal);
            }
            else
            {
                Box(bE, "Locker_Block", new Vector3(4, 0, 26), new Vector3(5, 1f, 27), wall);
            }
        }

        // ===== 6. Комната Б: плита на потолке. Гиря на плите поднимает ставню сейфа =====
        {
            if (world == Upside)
            {
                GameObject plate = Box(bW, "CeilingPlate", new Vector3(-2.5f, 0, 25), new Vector3(-1.5f, 0.06f, 26), orange);
                Switch(plate, "shutter_open", "ЛКМ — положить гирю", "weight", null,
                    "Плита пружинит. Нужно что-то тяжёлое", "Плита опустилась");
                GameObject placed = Box(bW, "CeilingPlate_Weight", new Vector3(-2.2f, 0.06f, 25.3f),
                    new Vector3(-1.8f, 0.46f, 25.7f), dark, false);
                React(logic, "Plate_Weight", A("shutter_open"), null, new[] { placed });
            }
            else if (world == Main)
            {
                Box(bW, "CeilingPlate_Decor", new Vector3(-2.5f, H - 0.06f, 25), new Vector3(-1.5f, H, 26), orange, false);
                GameObject placed = Box(bW, "CeilingPlate_Weight", new Vector3(-2.2f, H - 0.46f, 25.3f),
                    new Vector3(-1.8f, H - 0.06f, 25.7f), dark, false);
                React(logic, "Plate_Weight", A("shutter_open"), null, new[] { placed });
            }
        }

        // ===== 7. Комната Б, западная стена: сейф и тансу (x -5..-4, z 22..23) =====
        {
            if (world == Main)
            {
                GameObject safe = Box(bW, "Safe", new Vector3(-5, 0, 22), new Vector3(-4, 1.4f, 23), metal);
                SequenceLock safeLock = safe.AddComponent<SequenceLock>();
                safeLock.expected = new[] { 2, 0, 1, 3 };   // синий, красный, зелёный, жёлтый
                safeLock.flag = "safe_open";
                safeLock.doneMessage = "Сейф открыт";

                Material[] colors = { red, green, blue, yellow };
                for (int i = 0; i < 4; i++)
                {
                    float z = 22.1f + i * 0.22f;
                    GameObject b = Box(bW, "Safe_Button_" + i, new Vector3(-4, 0.9f, z),
                        new Vector3(-3.93f, 1.1f, z + 0.16f), colors[i]);
                    SequenceButton sb = b.AddComponent<SequenceButton>();
                    sb.target = safeLock;
                    sb.index = i;
                }

                // Ставня закрывает кнопки, пока на потолочной плите нет гири.
                GameObject shutter = Box(bW, "Safe_Shutter", new Vector3(-3.93f, 0.8f, 22.02f),
                    new Vector3(-3.86f, 1.2f, 22.98f), dark);
                React(logic, "Safe_Shutter", null, A("shutter_open"), new[] { shutter });

                GameObject cutter = Item(bW, "BoltCutter", new Vector3(-4.5f, 1.5f, 22.5f),
                    new Vector3(0.5f, 0.12f, 0.15f), orange, "cutter", "болторез");
                React(logic, "Safe_Reward", A("safe_open"), null, new[] { cutter });
            }
            else if (world == Japanese)
            {
                GameObject tansu = Box(bW, "Tansu", new Vector3(-5, 0, 22), new Vector3(-4, 1.4f, 23), wood);
                Switch(tansu, "tansu_open", "ЛКМ — открыть ящик", "key", null,
                    "Ящик заперт. Нужен ключ", "В ящике лежала дверная ручка");
                GameObject handle = Item(bW, "DoorHandle", new Vector3(-4.5f, 1.5f, 22.5f),
                    new Vector3(0.35f, 0.1f, 0.1f), yellow, "handle", "дверная ручка");
                React(logic, "Tansu_Reward", A("tansu_open"), null, new[] { handle });
            }
            else if (world == Upside)
            {
                Flip(bW, "West_Block", new Vector3(-5, 0, 22), new Vector3(-4, 1.4f, 23), metal);
            }
            else
            {
                Box(bW, "West_Block", new Vector3(-5, 0, 22), new Vector3(-4, 1f, 23), wall);
            }
        }

        // ===== 8–9. Комната Б, восточная стена: штабель, башня, балка (x 4..5, z 22..23) =====
        {
            if (world == Main)
            {
                Transform intact = Group(bE, "Stack_Intact");
                for (int i = 0; i < 3; i++)
                    Box(intact, "Crate_" + i, new Vector3(4, i * 0.9f, 22), new Vector3(5, i * 0.9f + 0.88f, 23), wood);
                Transform fallen = Group(bE, "Stack_Fallen");
                Box(fallen, "Rubble", new Vector3(4, 0, 22), new Vector3(5, 0.5f, 23), wood);
                Box(fallen, "Crate_A", new Vector3(3.0f, 0, 23.3f), new Vector3(3.8f, 0.8f, 24.1f), wood, false);
                Box(fallen, "Crate_B", new Vector3(3.2f, 0, 21.0f), new Vector3(3.9f, 0.7f, 21.7f), wood, false);
                React(logic, "Stack", null, A("tower_down"), new[] { intact.gameObject }, new[] { fallen.gameObject });

                // Ключ: висит на цепи → лежит на штабеле → лежит на полу.
                Transform hanging = Group(bE, "Key_Hanging");
                Box(hanging, "Chain", new Vector3(4.48f, 3.15f, 22.48f), new Vector3(4.52f, H, 22.52f), dark, false);
                Box(hanging, "Key", new Vector3(4.35f, 3.05f, 22.44f), new Vector3(4.65f, 3.15f, 22.56f), yellow, false);
                React(logic, "Key_Hanging", null, A("chain_cut"), new[] { hanging.gameObject });

                GameObject onTop = Box(bE, "Key_OnStack", new Vector3(4.35f, 2.7f, 22.44f),
                    new Vector3(4.65f, 2.78f, 22.56f), yellow, false);
                React(logic, "Key_OnStack", A("chain_cut"), A("tower_down"), new[] { onTop });

                GameObject key = Item(bE, "Key", new Vector3(3.3f, 0.1f, 22.5f), new Vector3(0.3f, 0.08f, 0.12f),
                    yellow, "key", "ключ");
                React(logic, "Key_OnFloor", A("chain_cut", "tower_down"), null, new[] { key });
            }
            else if (world == Cartoon)
            {
                Transform intact = Group(bE, "Tower_Intact");
                Material[] colors = { red, green, magenta };
                for (int i = 0; i < 3; i++)
                    Box(intact, "Cube_" + i, new Vector3(4, i * 0.9f, 22), new Vector3(5, i * 0.9f + 0.88f, 23), colors[i]);
                Switch(intact.gameObject, "tower_down", "ЛКМ — толкнуть башню", null, null, null, "Башня рассыпалась");

                Transform fallen = Group(bE, "Tower_Fallen");
                Box(fallen, "Rubble", new Vector3(4, 0, 22), new Vector3(5, 0.5f, 23), red);
                Box(fallen, "Cube_A", new Vector3(3.0f, 0, 23.3f), new Vector3(3.8f, 0.8f, 24.1f), green, false);
                Box(fallen, "Cube_B", new Vector3(3.2f, 0, 21.0f), new Vector3(3.9f, 0.7f, 21.7f), magenta, false);
                React(logic, "Tower", null, A("tower_down"), new[] { intact.gameObject }, new[] { fallen.gameObject });
            }
            else if (world == Upside)
            {
                Box(bE, "Beam", new Vector3(4, 0, 22), new Vector3(5, 0.5f, 23), metal);
                Transform chained = Group(bE, "Key_Chained");
                Box(chained, "Key", new Vector3(4.35f, 0.5f, 22.44f), new Vector3(4.65f, 0.6f, 22.56f), yellow);
                Box(chained, "Chain", new Vector3(4.3f, 0.5f, 22.4f), new Vector3(4.7f, 0.64f, 22.6f), dark);
                Switch(chained.gameObject, "chain_cut", "ЛКМ — перекусить цепь", "cutter", null,
                    "Цепь руками не порвать", "Ключ улетел вверх");
                React(logic, "Key_Chained", null, A("chain_cut"), new[] { chained.gameObject });

                // Штабель основного мира висит над головой, пока башню не уронили.
                Transform overhead = Group(bE, "Stack_Overhead");
                for (int i = 0; i < 3; i++)
                    Flip(overhead, "Crate_" + i, new Vector3(4, i * 0.9f, 22), new Vector3(5, i * 0.9f + 0.88f, 23), wood, false);
                React(logic, "Stack_Overhead", null, A("tower_down"), new[] { overhead.gameObject });
            }
            else
            {
                Box(bE, "East_Block", new Vector3(4, 0, 22), new Vector3(5, 1f, 23), wall);
            }
        }

        // ===== 16. Комната Б, северо-западный угол галереи: кладовка с рубильником моста =====
        // Закрыта во всех мирах, кроме японского. Игрок входит в японском,
        // поворачивается вправо и оказывается внутри кладовки основного мира.
        {
            Transform closet = Group(bNW, "Closet");
            Box(closet, "Wall_S", new Vector3(0, 0, -3f), new Vector3(2.4f, G - T, -2.9f), wall);
            if (world != Japanese)
                Box(closet, "Wall_E", new Vector3(2.3f, 0, -2.9f), new Vector3(2.4f, G - T, 0), wall);
            else
                Box(closet, "Doorframe", new Vector3(2.3f, 2.3f, -2.9f), new Vector3(2.4f, G - T, 0), wall, false);

            if (world == Main || world == Japanese)
                PointLight(closet, "Light_Inside", new Vector3(1.2f, 2.6f, -1.5f), 5f, 3f);

            if (world == Main)
            {
                GameObject breaker = Box(closet, "DoorBreaker", new Vector3(0.9f, 1.0f, -0.12f),
                    new Vector3(1.5f, 1.6f, 0), orange);
                Switch(breaker, "bridge_power", "ЛКМ — включить рубильник", null, null, null,
                    "Механизм моста под напряжением");
            }
        }

        // ===== 17. Комната Б, галерея: дверь на мост и механизм с рукояткой =====
        {
            // Дверь закрывает проём моста во всех мирах. В перевёрнутом мире мост у пола,
            // поэтому там дверь стоит на высоте от 0 до 3,5 м.
            float y0 = world == Upside ? 0f : G;
            GameObject bridgeDoor = Box(p, "BridgeDoor", new Vector3(-CW, y0, ZN - 0.05f),
                new Vector3(CW, y0 + CH, ZN + 0.15f), dark);
            React(logic, "BridgeDoor", null, A("bridge_open"), new[] { bridgeDoor });

            if (world == Main)
            {
                GameObject crank = Box(p, "BridgeCrank", new Vector3(2.0f, G + 0.9f, ZN - 0.15f),
                    new Vector3(2.5f, G + 1.4f, ZN), metal);
                Switch(crank, "bridge_open", "ЛКМ — вставить рукоятку и открыть мост", "lever_handle", "bridge_power",
                    "Механизм без рукоятки не повернуть", "Дверь на мост открылась", "Механизм моста обесточен");
                GameObject lampOff = Box(p, "BridgeCrank_LampOff", new Vector3(2.6f, G + 1.5f, ZN - 0.1f),
                    new Vector3(2.7f, G + 1.6f, ZN), red, false);
                GameObject lampOn = Box(p, "BridgeCrank_LampOn", new Vector3(2.6f, G + 1.5f, ZN - 0.1f),
                    new Vector3(2.7f, G + 1.6f, ZN), green, false);
                React(logic, "BridgeCrank_Lamp", A("bridge_power"), null, new[] { lampOn }, new[] { lampOff });
            }
        }

        // ===== 19. Комната В: фонарь под потолком; в перевёрнутом мире лежит у ног =====
        {
            if (world == Upside)
            {
                Item(p, "Lantern", new Vector3(3f, 0.25f, 54.5f), new Vector3(0.3f, 0.45f, 0.3f), yellow,
                    "lantern", "фонарь");
            }
            else if (world == Main)
            {
                Box(p, "Lantern_Hanging", new Vector3(2.85f, H - 0.6f, 54.35f), new Vector3(3.15f, H - 0.15f, 54.65f),
                    yellow, false);
                Box(p, "Lantern_Chain", new Vector3(2.98f, H - 0.15f, 54.48f), new Vector3(3.02f, H, 54.52f), dark, false);
            }
        }

        // ===== 20. Комната В, западная стена за лестницей: сёдзи, фонари и тени (японский мир) =====
        if (world == Japanese)
        {
            Box(p, "ShadowScreen", new Vector3(-W, 0.3f, 55.8f), new Vector3(-W + 0.06f, 2.3f, 57.8f), paper, false);
            for (int i = 0; i < 3; i++)
            {
                float z = 56.0f + i * 0.8f;
                Box(p, "LanternStand_" + i, new Vector3(-6.5f, 0, z - 0.15f), new Vector3(-6.2f, 0.9f, z + 0.15f), dark);
                if (i < 2)
                {
                    Box(p, "Lantern_" + i, new Vector3(-6.5f, 0.9f, z - 0.15f), new Vector3(-6.2f, 1.3f, z + 0.15f), yellow, false);
                }
                else
                {
                    GameObject top = Box(p, "LanternStand_Empty", new Vector3(-6.5f, 0.9f, z - 0.15f),
                        new Vector3(-6.2f, 0.95f, z + 0.15f), dark);
                    Switch(top, "lanterns_lit", "ЛКМ — поставить фонарь", "lantern", null,
                        "Подставка пуста. Нужен ещё один фонарь", "На сёдзи проступили тени");
                    GameObject placed = Box(p, "Lantern_Placed", new Vector3(-6.5f, 0.95f, z - 0.15f),
                        new Vector3(-6.2f, 1.35f, z + 0.15f), yellow, false);
                    React(logic, "Lantern_Placed", A("lanterns_lit"), null, new[] { placed });
                }
            }

            // Тени: слева направо для того, кто смотрит на запад, то есть с юга на север:
            // квадрат, широкая полоса, высокая полоса.
            Transform shadows = Group(p, "Shadows");
            Box(shadows, "Square", new Vector3(-W + 0.06f, 1.1f, 55.95f), new Vector3(-W + 0.09f, 1.5f, 56.35f), dark, false);
            Box(shadows, "Wide", new Vector3(-W + 0.06f, 1.2f, 56.6f), new Vector3(-W + 0.09f, 1.4f, 57.2f), dark, false);
            Box(shadows, "Tall", new Vector3(-W + 0.06f, 0.9f, 57.35f), new Vector3(-W + 0.09f, 1.7f, 57.55f), dark, false);
            React(logic, "Shadows", A("lanterns_lit"), null, new[] { shadows.gameObject });
        }

        // ===== 21–24. Комната В, северная стена: выход с замком на четыре мира =====
        {
            if (world == Main)
            {
                GameObject door = Box(cNorth, "ExitDoor", new Vector3(-0.7f, 0, 27.85f), new Vector3(0.7f, 2.3f, 28), dark);
                Switch(door, "door_open", "ЛКМ — открыть дверь", null, "all_locks", null, null, "Горят не все лампы");
                React(logic, "ExitDoor", null, A("door_open"), new[] { door });

                GameObject socket = Box(cNorth, "HandleSocket", new Vector3(0.3f, 0.95f, 27.78f),
                    new Vector3(0.55f, 1.2f, 27.85f), metal);
                Switch(socket, "lock_main", "ЛКМ — вставить дверную ручку", "handle", null,
                    "Здесь не хватает дверной ручки", "Лампа основного мира загорелась");
                GameObject handle = Box(cNorth, "Handle_Placed", new Vector3(0.2f, 1.03f, 27.7f),
                    new Vector3(0.55f, 1.12f, 27.78f), yellow, false);
                React(logic, "Handle_Placed", A("lock_main"), null, new[] { handle });

                string[] locks = { "lock_main", "lock_cartoon", "lock_upside", "lock_japanese" };
                for (int i = 0; i < 4; i++)
                {
                    float x = -0.6f + i * 0.4f;
                    GameObject off = Box(cNorth, "Lock_LampOff_" + i, new Vector3(x - 0.08f, 2.45f, 27.9f),
                        new Vector3(x + 0.08f, 2.6f, 28), red, false);
                    GameObject on = Box(cNorth, "Lock_LampOn_" + i, new Vector3(x - 0.08f, 2.45f, 27.9f),
                        new Vector3(x + 0.08f, 2.6f, 28), green, false);
                    React(logic, "Lock_Lamp_" + i, A(locks[i]), null, new[] { on }, new[] { off });
                }

                var combiner = new GameObject("AllLocks");
                combiner.transform.SetParent(logic, false);
                var fc = combiner.AddComponent<FlagCombiner>();
                fc.allOf = locks;
                fc.flag = "all_locks";
            }
            else if (world == Cartoon)
            {
                Box(cNorth, "ExitDoor_Decor", new Vector3(-0.7f, 0, 27.95f), new Vector3(0.7f, 2.3f, 28), red, false);

                // Панель с фигурами: порядок — как тени на японских сёдзи.
                GameObject panel = Box(cNorth, "ShapePanel", new Vector3(2.9f, 0.9f, 27.9f), new Vector3(4.7f, 1.7f, 28), blue, false);
                SequenceLock shapeLock = panel.AddComponent<SequenceLock>();
                shapeLock.expected = new[] { 2, 1, 0 };   // квадрат, широкая, высокая
                shapeLock.flag = "lock_cartoon";
                shapeLock.doneMessage = "Лампа мультяшного мира загорелась";

                Vector3[] mins = { new Vector3(3.15f, 1.05f, 27.84f), new Vector3(3.6f, 1.2f, 27.84f), new Vector3(4.25f, 1.15f, 27.84f) };
                Vector3[] maxs = { new Vector3(3.27f, 1.45f, 27.9f), new Vector3(4.0f, 1.32f, 27.9f), new Vector3(4.5f, 1.4f, 27.9f) };
                string[] names = { "Tall", "Wide", "Square" };
                for (int i = 0; i < 3; i++)
                {
                    GameObject b = Box(cNorth, "Shape_" + names[i], mins[i], maxs[i], yellow);
                    SequenceButton sb = b.AddComponent<SequenceButton>();
                    sb.target = shapeLock;
                    sb.index = i;
                }
            }
            else if (world == Japanese)
            {
                GameObject shoji = Box(cNorth, "ShojiDoor", new Vector3(-0.7f, 0, 27.9f), new Vector3(0.7f, 2.3f, 28), paper);
                Switch(shoji, "lock_japanese", "ЛКМ — раздвинуть сёдзи", null, "lanterns_lit", null,
                    "Сёдзи раздвинулись. Лампа японского мира загорелась", "Сёдзи не поддаются. Здесь слишком темно");
                // Раздвинутые сёдзи: створки сдвинуты вбок, за ними тёмный проём.
                GameObject shojiOpen = Box(cNorth, "ShojiDoor_Open", new Vector3(-0.7f, 0, 27.9f), new Vector3(0.7f, 2.3f, 28), paper, false);
                React(logic, "ShojiDoor", null, A("lock_japanese"), new[] { shoji }, new[] { shojiOpen });
                // Когда фонари горят, сёдзи подсвечены тёплым светом и выделяются на стене.
                var shojiGlow = new GameObject("ShojiDoor_Glow");
                shojiGlow.transform.SetParent(cNorth, false);
                shojiGlow.transform.localPosition = new Vector3(0f, 1.6f, 27.2f);
                var glow = shojiGlow.AddComponent<Light>();
                glow.type = LightType.Point; glow.color = new Color(1f, 0.8f, 0.5f); glow.range = 3f; glow.intensity = 2.5f;
                React(logic, "ShojiDoor_Glow", A("lanterns_lit"), null, new[] { shojiGlow });
            }
            else
            {
                // Перевёрнутый мир: рычаг под потолком на восточной стене, у ног того, кто ходит по потолку.
                GameObject lever = Box(p, "CeilingLever", new Vector3(W - 0.12f, 0.8f, 55.3f), new Vector3(W, 1.2f, 55.7f), orange);
                Switch(lever, "lock_upside", "ЛКМ — опустить рычаг", null, null, null, "Лампа перевёрнутого мира загорелась");
            }
        }
    }


    // ------------------------------------------------------------------
    // Декор: мебель, номера комнат, светильники. Лежит в Puzzle/Decor.
    // Мебель стоит на одних и тех же местах во всех мирах (меняется только стиль),
    // поэтому поворот рядом с ней не упирается в чужую мебель. В перевёрнутом мире
    // мебель основного мира висит на потолке; места под галереями там пустые,
    // иначе мебель свисала бы на галерею перевёрнутого мира.
    // ------------------------------------------------------------------
    struct DecorSlot
    {
        public string name, kind;
        public float x, z, y, yaw;
        public int zone;   // 0 — пол открытой части комнаты, 1 — под галереей, 2 — на галерее
        public DecorSlot(string name, string kind, float x, float z, float y, float yaw, int zone)
        {
            this.name = name; this.kind = kind; this.x = x; this.z = z; this.y = y; this.yaw = yaw; this.zone = zone;
        }
    }

    // yaw: 0 — спиной к южной стене, 180 — к северной, 90 — к западной, 270 — к восточной.
    static readonly DecorSlot[] DecorSlots =
    {
        new DecorSlot("A_Table_S", "Table", -3f, 0.4f, 0f, 0f, 0),
        new DecorSlot("A_Shelf_S", "Shelf", 4.5f, 0.3f, 0f, 0f, 0),
        new DecorSlot("A_Shelf_E", "Shelf", 6.7f, 6f, 0f, 270f, 0),
        new DecorSlot("A_Small_N", "Small", 4.5f, 13.5f, 0f, 180f, 1),
        new DecorSlot("A_Shelf_W", "Shelf", -6.7f, 12.3f, 0f, 90f, 1),
        new DecorSlot("A_Table_N", "Table", -4f, 13.6f, 0f, 180f, 1),
        new DecorSlot("B_Shelf_S", "Shelf", 5f, 22.3f, 0f, 0f, 0),
        new DecorSlot("B_Table_S", "Table", -5.5f, 22.4f, 0f, 0f, 0),
        new DecorSlot("B_Shelf_W", "Shelf", -6.7f, 31.5f, 0f, 90f, 1),
        new DecorSlot("B_Small_NE", "Small", 5.5f, 35.5f, 0f, 180f, 1),
        new DecorSlot("B_Small_NW", "Small", -5f, 35.5f, 0f, 180f, 1),
        new DecorSlot("B_Shelf_Gallery", "Shelf", 6.7f, 33f, 3.5f, 270f, 2),
        new DecorSlot("C_Shelf_E", "Shelf", 6.7f, 46f, 0f, 270f, 1),
        new DecorSlot("C_Table_N", "Table", -4f, 57.6f, 0f, 180f, 0),
        new DecorSlot("C_Small_N", "Small", 6f, 57.5f, 0f, 180f, 0),
        new DecorSlot("C_Small_S", "Small", 5f, 44.5f, 0f, 0f, 1),
        new DecorSlot("C_Table_Gallery", "Table", 4.5f, 44.4f, 3.5f, 0f, 2),
    };

    // Номера комнат висят на стене, на которую мир смотрит по умолчанию:
    // основной — север, мультяшный — восток, перевёрнутый — юг, японский — запад.
    static DecorSlot[] SignSlots(int world)
    {
        float wx = W - 0.03f;
        switch (world)
        {
            case Main: return new[]
            {
                new DecorSlot("Sign_1", "Sign", 0f, ZA - 0.03f, 2.35f, 180f, 0),
                new DecorSlot("Sign_2", "Sign", 0f, ZN - 0.03f, 1.6f, 180f, 0),
                new DecorSlot("Sign_3", "Sign", 0f, ZCN - 0.03f, 3.0f, 180f, 0),
            };
            case Cartoon: return new[]
            {
                new DecorSlot("Sign_1", "Sign", wx, 10.5f, 1.4f, 270f, 0),
                new DecorSlot("Sign_2", "Sign", wx, 33.5f, 1.4f, 270f, 0),
                new DecorSlot("Sign_3", "Sign", wx, 54f, 1.5f, 270f, 0),
            };
            case Upside: return new[]
            {
                new DecorSlot("Sign_1", "Sign", 3f, 0.03f, 1.6f, 0f, 0),
                new DecorSlot("Sign_2", "Sign", 0f, ZB + 0.03f, 1.6f, 0f, 0),
                new DecorSlot("Sign_3", "Sign", 4f, ZC0 + 0.03f, 1.6f, 0f, 0),
            };
            default: return new[]
            {
                new DecorSlot("Sign_1", "Sign", -wx, 9.5f, 1.4f, 90f, 0),
                new DecorSlot("Sign_2", "Sign", -wx, 23.5f, 1.4f, 90f, 0),
                new DecorSlot("Sign_3", "Sign", -wx, 47f, G + 1f, 90f, 0),
            };
        }
    }

    // Светильники над источниками света: x, высота света, z, высота потолка над ним.
    static readonly Vector4[] LampSpots =
    {
        new Vector4(0f, 2.6f, ZA * 0.3f, H), new Vector4(0f, 2.6f, ZA * 0.8f, G - T), new Vector4(0f, 6.3f, ZA * 0.6f, H),
        new Vector4(0f, 3f, (ZA + ZB) * 0.5f, CH),
        new Vector4(0f, 2.6f, ZB + LA * 0.3f, H), new Vector4(0f, 2.6f, ZB + LA * 0.8f, G - T), new Vector4(0f, 6.3f, ZB + LA * 0.6f, H),
        new Vector4(0f, G + 3f, (ZN + ZC0) * 0.5f, H),
        new Vector4(0f, 2.6f, ZC0 + LA * 0.2f, G - T), new Vector4(0f, 2.6f, ZC0 + LA * 0.7f, H), new Vector4(0f, 6.3f, ZC0 + LA * 0.4f, H),
    };

    static Vector3 DecorSize(string kind, int world)
    {
        switch (kind)
        {
            case "Shelf": return new Vector3(2f, 2.2f, 0.6f);
            case "Table": return new Vector3(1.6f, world == Japanese ? 0.45f : 0.9f, 0.8f);
            case "Small": return new Vector3(1f, 1f, 1f);
            case "Sign": return new Vector3(0.8f, 0.9f, 0.06f);
            default: return new Vector3(0.5f, 0.35f, 0.5f);   // светильник
        }
    }

    static void SlotBounds(DecorSlot slot, int world, out Vector3 min, out Vector3 max)
    {
        Vector3 size = DecorSize(slot.kind, world);
        bool side = Mathf.Abs(Mathf.DeltaAngle(slot.yaw, 90f)) < 1f || Mathf.Abs(Mathf.DeltaAngle(slot.yaw, 270f)) < 1f;
        float sx = side ? size.z : size.x, sz = side ? size.x : size.z;
        min = new Vector3(slot.x - sx * 0.5f, slot.y, slot.z - sz * 0.5f);
        max = new Vector3(slot.x + sx * 0.5f, slot.y + size.y, slot.z + sz * 0.5f);
    }

    static void BuildDecor(Transform p, int world)
    {
        Transform decor = Group(p, "Decor");
        Material furniture = Mat("C_Wood", new Color(0.55f, 0.38f, 0.2f));
        Material dark = Mat("C_Dark", new Color(0.12f, 0.12f, 0.14f));
        Material paper = Mat("C_Paper", new Color(0.95f, 0.92f, 0.82f));

        foreach (DecorSlot slot in DecorSlots)
        {
            Vector3 min, max;
            if (world == Upside)
            {
                if (slot.zone != 0) continue;
                SlotBounds(slot, Main, out min, out max);
                Flip(decor, slot.name, min, max, furniture);
            }
            else
            {
                SlotBounds(slot, world, out min, out max);
                Box(decor, slot.name, min, max, furniture);
            }
        }

        foreach (DecorSlot slot in SignSlots(world))
        {
            Vector3 min, max;
            SlotBounds(slot, world, out min, out max);
            Box(decor, slot.name, min, max, paper, false);
        }

        if (world != Upside)
        {
            for (int i = 0; i < LampSpots.Length; i++)
            {
                Vector4 spot = LampSpots[i];
                float bottom = spot.y - 0.1f, top = bottom + 0.35f;
                Box(decor, "Lamp_" + i, new Vector3(spot.x - 0.25f, bottom, spot.z - 0.25f),
                    new Vector3(spot.x + 0.25f, top, spot.z + 0.25f), paper, false);
                Box(decor, "Lamp_" + i + "_Cable", new Vector3(spot.x - 0.015f, top, spot.z - 0.015f),
                    new Vector3(spot.x + 0.015f, spot.w, spot.z + 0.015f), dark, false);
            }
        }
    }

    static void AddDecorRules(System.Collections.Generic.List<ArtRule> rules)
    {
        string[] style = { "Main", "Cartoon", "Main", "Japanese" };
        for (int w = 0; w < 4; w++)
        {
            foreach (DecorSlot slot in DecorSlots)
            {
                if (w == Upside && slot.zone != 0) continue;
                rules.Add(R(w, "Decor/" + slot.name, slot.kind + "_" + style[w], slot.yaw, flip: w == Upside));
            }
            DecorSlot[] signs = SignSlots(w);
            for (int i = 0; i < signs.Length; i++)
                rules.Add(R(w, "Decor/" + signs[i].name, "Sign_" + style[w] + "_" + (i + 1), signs[i].yaw));
            if (w != Upside)
                for (int i = 0; i < LampSpots.Length; i++)
                    rules.Add(R(w, "Decor/Lamp_" + i, "Lamp_" + style[w]));
        }
    }

    // ------------------------------------------------------------------
    // Помощники
    // ------------------------------------------------------------------
    static string[] A(params string[] values)
    {
        return values;
    }

    static Transform Anchor(Transform parent, string name, float x, float z, float y = 0f)
    {
        Transform t = Group(parent, name);
        t.localPosition = new Vector3(x, y, z);
        return t;
    }

    /// Куб, отражённый по вертикали: то, что стоит на настоящем полу,
    /// в перевёрнутом мире висит над головой.
    static GameObject Flip(Transform parent, string name, Vector3 min, Vector3 max, Material material,
        bool collider = true)
    {
        return Box(parent, name, new Vector3(min.x, H - max.y, min.z), new Vector3(max.x, H - min.y, max.z),
            material, collider);
    }

    static Transform Group(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static GameObject Box(Transform parent, string name, Vector3 min, Vector3 max, Material material,
        bool collider = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = (min + max) * 0.5f;
        go.transform.localScale = max - min;
        go.GetComponent<Renderer>().sharedMaterial = material;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        float tile;
        if (material != null && TileSize.TryGetValue(material, out tile)) MetricUV(go, tile);
        return go;
    }

    // ------------------------------------------------------------------
    // Текстуры геометрии. У каждого куба своя копия сетки, в которой развёртка
    // посчитана в мировых метрах: текстура не растягивается и стыкуется между кубами.
    // ------------------------------------------------------------------
    static readonly float[] WallTile = { 3f, 2f, 3f, 2f };
    static readonly float[] CeilingTile = { 3f, 6f, 3f, 2f };
    static readonly System.Collections.Generic.Dictionary<Material, float> TileSize =
        new System.Collections.Generic.Dictionary<Material, float>();

    static Material TexMat(string name, string texture, Color tint, float tile)
    {
        Material material = Mat(name, tint);
        var map = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Models/Textures/" + texture + ".png");
        if (map == null)
        {
            Debug.LogWarning("Поповорот: нет текстуры " + texture + ".png, стены останутся одноцветными");
            return material;
        }
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", map);
        material.mainTexture = map;
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.15f);
        EditorUtility.SetDirty(material);
        TileSize[material] = tile;
        return material;
    }

    static void MetricUV(GameObject go, float tile)
    {
        var filter = go.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return;
        Mesh mesh = Object.Instantiate(filter.sharedMesh);
        mesh.name = go.name + "_UV";
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        var uv = new Vector2[vertices.Length];
        Transform t = go.transform;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 w = t.TransformPoint(vertices[i]);
            Vector3 n = t.TransformDirection(normals[i]);
            float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
            Vector2 u;
            if (ay >= ax && ay >= az) u = new Vector2(w.x, w.z);
            else if (ax >= az) u = new Vector2(w.z * Mathf.Sign(n.x), w.y);
            else u = new Vector2(-w.x * Mathf.Sign(n.z), w.y);
            uv[i] = u / tile;
        }
        mesh.uv = uv;
        filter.sharedMesh = mesh;
    }

    /// Ряд вертикальных прутьев без коллайдеров. alongX: прутья идут вдоль оси X, иначе вдоль Z.
    static void Bars(Transform parent, Vector3 min, Vector3 max, bool alongX, Material material)
    {
        float length = alongX ? max.x - min.x : max.z - min.z;
        int count = Mathf.Max(Mathf.RoundToInt(length / 0.25f), 1);
        for (int i = 0; i <= count; i++)
        {
            float t = (alongX ? min.x : min.z) + length * i / count;
            Vector3 a = alongX ? new Vector3(t - 0.03f, min.y, min.z) : new Vector3(min.x, min.y, t - 0.03f);
            Vector3 b = alongX ? new Vector3(t + 0.03f, max.y, max.z) : new Vector3(max.x, max.y, t + 0.03f);
            Box(parent, "Bar_" + i, a, b, material, false);
        }
    }

    /// Невидимый коллайдер: не пускает игрока и луч взаимодействия.
    static void Blocker(Transform parent, Vector3 min, Vector3 max)
    {
        var go = new GameObject("Blocker");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = (min + max) * 0.5f;
        go.AddComponent<BoxCollider>().size = max - min;
    }

    static readonly System.Collections.Generic.Dictionary<string, string> ItemDescriptions =
        new System.Collections.Generic.Dictionary<string, string>
        {
            { "fuse", "Предохранитель. Ему нужен щиток." },
            { "lever_handle", "Рукоятка от механизма. Крутит решётку в коридоре, а потом пригодится ещё раз — не теряйте." },
            { "weight", "Тяжёлая гиря. Ею можно что-то продавить." },
            { "cutter", "Болторез. Перекусит цепь." },
            { "key", "Небольшой ключ от ящика." },
            { "handle", "Дверная ручка. Где-то дальше есть дверь без ручки." },
            { "lantern", "Фонарь. Где-то пустует подставка." },
        };

    static GameObject Item(Transform parent, string name, Vector3 center, Vector3 size, Material material,
        string itemId, string displayName)
    {
        var go = Box(parent, name, center - size * 0.5f, center + size * 0.5f, material);
        go.AddComponent<Rigidbody>().isKinematic = true;
        var pickup = go.AddComponent<Pickup>();
        pickup.itemId = itemId;
        pickup.displayName = displayName;
        string description;
        if (ItemDescriptions.TryGetValue(itemId, out description)) pickup.description = description;
        return go;
    }

    static FlagSwitch Switch(GameObject go, string flag, string prompt, string requiredItem, string requiredFlag,
        string failHint, string doneMessage, string flagHint = null)
    {
        var sw = go.AddComponent<FlagSwitch>();
        sw.flag = flag;
        sw.prompt = prompt;
        sw.requiredItem = requiredItem ?? "";
        sw.requiredFlag = requiredFlag ?? "";
        if (failHint != null) sw.failHint = failHint;
        sw.doneMessage = doneMessage ?? "";
        sw.flagHint = flagHint ?? "";
        return sw;
    }

    static void React(Transform logic, string name, string[] allOf, string[] noneOf, GameObject[] on,
        GameObject[] off = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(logic, false);
        var reactor = go.AddComponent<FlagReactor>();
        reactor.allOf = allOf ?? new string[0];
        reactor.noneOf = noneOf ?? new string[0];
        reactor.on = on ?? new GameObject[0];
        reactor.off = off ?? new GameObject[0];
    }

    static void PointLight(Transform parent, string name, Vector3 position, float range = 16f,
        float intensity = 12f)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = range;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
    }

    static Material Mat(string name, Color color)
    {
        string path = "Assets/Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        else material.color = color;
        EditorUtility.SetDirty(material);
        return material;
    }

    // ------------------------------------------------------------------
    // Вид миров: по одному глобальному Volume на мир, WorldLook включает нужный.
    // Профили лежат в Assets/Settings/Worlds, их можно править руками.
    // ------------------------------------------------------------------
    static void BuildLooks(GameObject manager, string[] ids)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Settings"))
            AssetDatabase.CreateFolder("Assets", "Settings");
        if (!AssetDatabase.IsValidFolder("Assets/Settings/Worlds"))
            AssetDatabase.CreateFolder("Assets/Settings", "Worlds");

        var look = manager.AddComponent<WorldLook>();
        look.volumes = new Volume[4];

        for (int i = 0; i < 4; i++)
        {
            string path = "Assets/Settings/Worlds/" + ids[i] + "_Look.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
                FillProfile(profile, i);
                EditorUtility.SetDirty(profile);
            }

            var go = new GameObject("Look_" + ids[i]);
            go.transform.SetParent(manager.transform, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.weight = i == 0 ? 1f : 0f;
            volume.sharedProfile = profile;
            look.volumes[i] = volume;
        }
    }

    static void FillProfile(VolumeProfile profile, int world)
    {
        if (world == Japanese)
        {
            // Инверсия цвета: общая кривая идёт от 1 к 0.
            var curves = VolumeProfileFactory.CreateVolumeComponent<ColorCurves>(profile, false, false);
            var inverted = new TextureCurve(
                new[] { new Keyframe(0f, 1f, -1f, -1f), new Keyframe(1f, 0f, -1f, -1f) },
                0f, false, new Vector2(0f, 1f));
            curves.master.Override(inverted);
            return;
        }

        var color = VolumeProfileFactory.CreateVolumeComponent<ColorAdjustments>(profile, false, false);
        var vignette = VolumeProfileFactory.CreateVolumeComponent<Vignette>(profile, false, false);
        if (world == Main)
        {
            color.saturation.Override(-25f);
            color.contrast.Override(10f);
            vignette.intensity.Override(0.3f);
        }
        else if (world == Cartoon)
        {
            color.saturation.Override(40f);
            color.postExposure.Override(0.3f);
            vignette.intensity.Override(0f);
        }
        else
        {
            color.saturation.Override(-45f);
            color.colorFilter.Override(new Color(0.8f, 0.9f, 1f));
            vignette.intensity.Override(0.42f);
        }
    }

    // ------------------------------------------------------------------
    // Стартовый экран: отдельная сцена Assets/Scenes/MainMenu.unity.
    // ------------------------------------------------------------------
    [MenuItem("Tools/Поповорот/Сервис/Создать стартовый экран", false, 100)]
    static void BuildMainMenu()
    {
        const string menuPath = "Assets/Scenes/MainMenu.unity";

        var gameScene = SceneManager.GetActiveScene();
        string gamePath = gameScene.path;
        string gameName = gameScene.name;
        if (string.IsNullOrEmpty(gamePath) || gamePath == menuPath)
        {
            EditorUtility.DisplayDialog("Поповорот",
                "Сначала откройте сохранённую сцену с игрой, затем выберите этот пункт.", "Понятно");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Четыре стены цветов четырёх миров: север, восток, юг, запад.
        var room = new GameObject("Room").transform;
        Material main = Mat("Menu_Main", new Color(0.45f, 0.46f, 0.48f));
        Material cartoon = Mat("Menu_Cartoon", new Color(1.00f, 0.80f, 0.25f));
        Material upside = Mat("Menu_Upside", new Color(0.30f, 0.32f, 0.38f));
        Material japanese = Mat("Menu_Japanese", new Color(0.10f, 0.55f, 0.60f));
        Material floor = Mat("Menu_Floor", new Color(0.12f, 0.12f, 0.14f));
        Box(room, "Wall_N", new Vector3(-4, 0, 4), new Vector3(4, 4, 4.2f), main);
        Box(room, "Wall_E", new Vector3(4, 0, -4), new Vector3(4.2f, 4, 4), cartoon);
        Box(room, "Wall_S", new Vector3(-4, 0, -4.2f), new Vector3(4, 4, -4), upside);
        Box(room, "Wall_W", new Vector3(-4.2f, 0, -4), new Vector3(-4, 4, 4), japanese);
        Box(room, "Floor", new Vector3(-4, -0.2f, -4), new Vector3(4, 0, 4), floor);
        Box(room, "Ceiling", new Vector3(-4, 4, -4), new Vector3(4, 4.2f, 4), floor);
        PointLight(room, "Light", new Vector3(0, 3.2f, 0));

        var cameraObject = new GameObject("Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 1.6f, 0f);
        var camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 70f;
        cameraObject.AddComponent<AudioListener>();

        var menu = new GameObject("MainMenu").AddComponent<MainMenu>();
        menu.gameSceneName = gameName;
        menu.cameraPivot = cameraObject.transform;

        EditorSceneManager.SaveScene(scene, menuPath);
        AssetDatabase.SaveAssets();

        // Список сборки: сначала меню, потом игра.
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(menuPath, true),
            new EditorBuildSettingsScene(gamePath, true),
        };

        EditorSceneManager.OpenScene(gamePath);
        Debug.Log("Поповорот: стартовый экран создан — " + menuPath);
    }

    // ------------------------------------------------------------------
    // Модели. Корень Art лежит вне Worlds, поэтому пересборка его не трогает.
    // ------------------------------------------------------------------
    static void BuildArtRoots(string[] ids)
    {
        var artObject = GameObject.Find("Art");
        Transform art = artObject != null ? artObject.transform : new GameObject("Art").transform;

        for (int i = 0; i < 4; i++)
        {
            string name = "Art_" + i + "_" + ids[i];
            if (art.Find(name) != null) continue;

            var root = new GameObject(name).transform;
            root.SetParent(art, false);
            root.localPosition = new Vector3(i * Spacing, 0f, 0f);

            if (i == Upside)
            {
                // Всё, что лежит в Mirrored, отражается по вертикали так же, как стены:
                // сюда можно скопировать окружение основного мира, и оно окажется на потолке.
                var mirrored = new GameObject("Mirrored").transform;
                mirrored.SetParent(root, false);
                mirrored.localPosition = new Vector3(0f, H, 0f);
                mirrored.localScale = new Vector3(1f, -1f, 1f);
            }
        }
    }

    /// Удаляет папки Auto в корне Art (их создаёт «Расставить модели»). Свои модели вне Auto не трогает.
    static int ClearAutoModels()
    {
        var art = GameObject.Find("Art");
        if (art == null) return 0;
        int count = 0;
        foreach (Transform root in art.transform)
        {
            foreach (string path in new[] { "Auto", "Mirrored/Auto" })
            {
                Transform auto = root.Find(path);
                if (auto == null) continue;
                Object.DestroyImmediate(auto.gameObject);
                count++;
            }
        }
        return count;
    }

    [MenuItem("Tools/Поповорот/Сервис/Убрать расставленные модели", false, 101)]
    static void ClearAutoModelsMenu()
    {
        int removed = ClearAutoModels();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("Поповорот", "Убрано папок с моделями: " + removed, "Понятно");
    }

    /// Удаляет из корня Art модели, у которых пропал исходный файл: такие объекты
    /// не видны, но их ArtLink прятал серые кубы загадок.
    [MenuItem("Tools/Поповорот/Сервис/Убрать сломанные модели", false, 102)]
    static void RemoveBrokenModels()
    {
        var art = GameObject.Find("Art");
        if (art == null) return;
        var broken = new System.Collections.Generic.List<GameObject>();
        foreach (Transform t in art.GetComponentsInChildren<Transform>(true))
        {
            GameObject go = t.gameObject;
            bool missing = PrefabUtility.IsPrefabAssetMissing(go) && PrefabUtility.IsOutermostPrefabInstanceRoot(go);
            bool empty = go.GetComponent<ArtLink>() != null && go.GetComponentsInChildren<Renderer>(true).Length == 0;
            if (missing || empty) broken.Add(go);
        }
        var names = new System.Collections.Generic.List<string>();
        foreach (var go in broken)
        {
            if (go == null) continue;
            names.Add(go.name);
            Undo.DestroyObjectImmediate(go);
        }
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("Поповорот", names.Count == 0 ? "Сломанных моделей нет."
            : "Убрано: " + names.Count + "\n" + string.Join(", ", names), "Понятно");
    }

    [MenuItem("Tools/Поповорот/Сервис/Показать или скрыть серые стены", false, 103)]
    static void ToggleGreybox()
    {
        var worlds = GameObject.Find("Worlds");
        if (worlds == null) return;

        var renderers = new System.Collections.Generic.List<MeshRenderer>();
        foreach (Transform world in worlds.transform)
        {
            Transform geometry = world.name == "World_4_Secret" ? world : world.Find("Geometry");
            if (geometry != null) renderers.AddRange(geometry.GetComponentsInChildren<MeshRenderer>(true));
        }
        if (renderers.Count == 0) return;

        bool show = !renderers[0].enabled;
        foreach (var r in renderers) r.enabled = show;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("Поповорот: серые стены " + (show ? "показаны" : "скрыты") + ". Коллайдеры остались.");
    }

    [MenuItem("Tools/Поповорот/Сервис/Привязать модель к объекту загадки", false, 104)]
    static void LinkArt()
    {
        var selected = Selection.gameObjects;
        var worlds = GameObject.Find("Worlds");
        if (selected.Length != 2 || worlds == null)
        {
            EditorUtility.DisplayDialog("Поповорот",
                "Выделите два объекта: модель и объект загадки внутри Worlds.", "Понятно");
            return;
        }

        bool firstInWorlds = selected[0].transform.IsChildOf(worlds.transform);
        bool secondInWorlds = selected[1].transform.IsChildOf(worlds.transform);
        if (firstInWorlds == secondInWorlds)
        {
            EditorUtility.DisplayDialog("Поповорот",
                "Один объект должен быть внутри Worlds (объект загадки), второй — вне его (модель).", "Понятно");
            return;
        }

        GameObject target = firstInWorlds ? selected[0] : selected[1];
        GameObject model = firstInWorlds ? selected[1] : selected[0];

        string path = target.name;
        for (Transform t = target.transform.parent; t != null && t != worlds.transform; t = t.parent)
            path = t.name + "/" + path;

        var link = model.GetComponent<ArtLink>();
        if (link == null) link = Undo.AddComponent<ArtLink>(model);
        link.targetPath = path;
        EditorUtility.SetDirty(link);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("Поповорот: модель " + model.name + " привязана к " + path);
    }

    // ------------------------------------------------------------------
    // Автоматическая расстановка моделей из Assets/Models по объектам загадок.
    // Модели кладутся в Art/<мир>/Auto и привязываются через ArtLink.
    // Повторный запуск удаляет папки Auto и расставляет всё заново.
    // ------------------------------------------------------------------
    struct ArtRule
    {
        public int world;
        public string path, model, boundsChild;
        public float yaw;
        public bool center, fit, flip, floor, attach;
    }

    static ArtRule R(int world, string path, string model, float yaw = 0f, bool center = false, bool fit = false,
        bool flip = false, bool floor = false, string boundsChild = null, bool attach = true)
    {
        return new ArtRule
        {
            world = world, path = path, model = model, yaw = yaw, center = center, fit = fit, flip = flip,
            floor = floor, boundsChild = boundsChild, attach = attach
        };
    }

    static bool TryBounds(Transform t, out Bounds bounds)
    {
        var renderers = t.GetComponentsInChildren<Renderer>(true);
        bounds = default;
        if (renderers.Length == 0) return false;
        bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        return true;
    }

    // Модели окружения Env_*.fbx сделаны под одноэтажный уровень джема.
    // Включить, когда будут модели двухэтажного уровня.
    const bool PlaceEnvironment = false;

    [MenuItem("Tools/Поповорот/2. Расставить модели", false, 2)]
    static void PlaceModels()
    {
        var worlds = GameObject.Find("Worlds");
        var art = GameObject.Find("Art");
        if (worlds == null || art == null)
        {
            EditorUtility.DisplayDialog("Поповорот", "Сначала соберите серый уровень.", "Понятно");
            return;
        }

        // yaw: модели смотрят лицом на север; 90 — на восток, 180 — на юг.
        // center: предмет ставится по центру объекта, иначе — низом на его низ.
        // fit: модель растягивается под размер серого куба. flip: вверх дном.
        var rules = new System.Collections.Generic.List<ArtRule>
        {
            // ---- основная ----
            R(Main, "A_NorthEast/Cage_Pedestal", "CagePedestal"),
            R(Main, "A_NorthEast/Cage_Cover", "Cage"),
            R(Main, "A_NorthEast/Fuse", "Item_Fuse", center: true),
            R(Main, "A_East/Container", "Container_Closed", 180f, floor: true, boundsChild: "Roof", attach: false),
            R(Main, "A_East/FusePanel", "FusePanel", 180f),
            R(Main, "Corridor_North/Gate", "Gate"),
            R(Main, "B_North/PairButton_North", "WallButton", 180f),
            R(Main, "B_West/Safe", "Safe", 90f),
            R(Main, "B_West/BoltCutter", "Item_BoltCutter", center: true),
            R(Main, "B_West/CeilingPlate_Weight", "Item_Weight", center: true, flip: true),
            R(Main, "B_East/Stack_Intact/Crate_0", "Crate", fit: true),
            R(Main, "B_East/Stack_Intact/Crate_1", "Crate", fit: true),
            R(Main, "B_East/Stack_Intact/Crate_2", "Crate", fit: true),
            R(Main, "B_East/Stack_Fallen/Rubble", "Crate", fit: true),
            R(Main, "B_East/Stack_Fallen/Crate_A", "Crate", fit: true),
            R(Main, "B_East/Stack_Fallen/Crate_B", "Crate", fit: true),
            R(Main, "B_East/Key", "Item_Key", center: true),
            R(Main, "B_East/Key_OnStack", "Item_Key", center: true),
            R(Main, "B_East/Key_Hanging/Key", "Item_Key", center: true),
            R(Main, "C_North/ExitDoor", "ExitDoor", 180f),
            R(Main, "A_North/Extinguisher", "Extinguisher", 180f),
            R(Main, "B_West/Safe_Shutter", "SafeShutter", 90f),
            R(Main, "B_West/CeilingPlate_Decor", "PressurePlate", flip: true),
            R(Main, "B_East/Key_Hanging/Chain", "Chain", fit: true),
            R(Main, "B_East/Locker_Block", "Crate", fit: true),
            R(Main, "B_NorthWest/Closet/DoorBreaker", "Breaker", 180f),
            R(Main, "A_North_Upper/LeverHandle", "Item_LeverHandle", center: true),
            R(Main, "A_North_Upper/Niche_Cover", "NicheCover", 180f),
            R(Main, "BridgeDoor", "BridgeDoor", fit: true),
            R(Main, "BridgeCrank", "BridgeCrank", 180f),
            R(Main, "BridgeCrank_LampOff", "SignalLamp_Red", 180f),
            R(Main, "BridgeCrank_LampOn", "SignalLamp_Green", 180f),
            R(Main, "Lantern_Hanging", "Item_Lantern", center: true),
            R(Main, "Lantern_Chain", "Chain", fit: true),
            R(Main, "C_North/HandleSocket", "HandleSocket", 180f),
            R(Main, "C_North/Handle_Placed", "Item_DoorHandle", center: true),
            R(Main, "C_North/Lock_LampOff_0", "SignalLamp_Red", 180f),
            R(Main, "C_North/Lock_LampOff_1", "SignalLamp_Red", 180f),
            R(Main, "C_North/Lock_LampOff_2", "SignalLamp_Red", 180f),
            R(Main, "C_North/Lock_LampOff_3", "SignalLamp_Red", 180f),
            R(Main, "C_North/Lock_LampOn_0", "SignalLamp_Green", 180f),
            R(Main, "C_North/Lock_LampOn_1", "SignalLamp_Green", 180f),
            R(Main, "C_North/Lock_LampOn_2", "SignalLamp_Green", 180f),
            R(Main, "C_North/Lock_LampOn_3", "SignalLamp_Green", 180f),

            // ---- мультяшная ----
            R(Cartoon, "A_NorthEast/Chest", "ToyChest"),
            R(Cartoon, "A_NorthEast/Chest_Button", "BigButton"),
            R(Cartoon, "A_East/Container", "Container_Open", 180f, floor: true, boundsChild: "Roof", attach: false),
            R(Cartoon, "A_South/TimerButton_Decor", "GateMechanism"),
            R(Cartoon, "Corridor_North/Gate", "Gate"),
            R(Cartoon, "B_South/GateLever", "Lever"),
            R(Cartoon, "B_North/PairButton_North_Decor", "WallButton", 180f),
            R(Cartoon, "B_South/PairButton_South_Decor", "WallButton"),
            R(Cartoon, "B_East/Locker", "ToyChest"),
            R(Cartoon, "B_East/Locker_Cover", "Cage"),
            R(Cartoon, "B_East/Weight", "Item_Weight", center: true),
            R(Cartoon, "B_East/Tower_Intact/Cube_0", "ToyCube_Red", fit: true),
            R(Cartoon, "B_East/Tower_Intact/Cube_1", "ToyCube_Green", fit: true),
            R(Cartoon, "B_East/Tower_Intact/Cube_2", "ToyCube_Magenta", fit: true),
            R(Cartoon, "B_East/Tower_Fallen/Rubble", "ToyCube_Red", fit: true),
            R(Cartoon, "B_East/Tower_Fallen/Cube_A", "ToyCube_Green", fit: true),
            R(Cartoon, "B_East/Tower_Fallen/Cube_B", "ToyCube_Magenta", fit: true),
            R(Cartoon, "A_North/Extinguisher", "Extinguisher", 180f),
            R(Cartoon, "B_West/West_Block", "ToyCabinet", 90f),
            R(Cartoon, "C_North/ExitDoor_Decor", "ToyDoor", 180f),
            R(Cartoon, "C_North/ShapePanel", "ShapePanel", 180f),
            R(Cartoon, "BridgeDoor", "BridgeDoor", fit: true),

            // ---- перевёрнутая ----
            R(Upside, "A_NorthEast/Corner_Block", "CagePedestal", flip: true),
            R(Upside, "A_East/Container", "Container_Closed", 0f, flip: true, floor: true, boundsChild: "Roof", attach: false),
            R(Upside, "A_South/TimerButton", "GateMechanism"),
            R(Upside, "Corridor_North/Gate", "Gate"),
            R(Upside, "B_South/GateLever", "Lever"),
            R(Upside, "B_South/PairButton_South", "WallButton"),
            R(Upside, "B_West/CeilingPlate_Weight", "Item_Weight", center: true),
            R(Upside, "B_West/West_Block", "Safe", 90f, flip: true),
            R(Upside, "B_East/Key_Chained/Key", "Item_Key", center: true),
            R(Upside, "B_East/Stack_Overhead/Crate_0", "Crate", fit: true, flip: true),
            R(Upside, "B_East/Stack_Overhead/Crate_1", "Crate", fit: true, flip: true),
            R(Upside, "B_East/Stack_Overhead/Crate_2", "Crate", fit: true, flip: true),
            R(Upside, "B_West/CeilingPlate", "PressurePlate"),
            R(Upside, "B_East/Beam", "Beam", fit: true),
            R(Upside, "B_East/Key_Chained/Chain", "ChainWrap"),
            R(Upside, "B_East/Locker_Block", "Crate", fit: true, flip: true),
            R(Upside, "BridgeDoor", "BridgeDoor", fit: true),
            R(Upside, "Lantern", "Item_Lantern", center: true),
            R(Upside, "CeilingLever", "Lever", 270f),

            // ---- японская ----
            R(Japanese, "A_East/Container", "Container_Closed", 180f, floor: true, boundsChild: "Roof", attach: false),
            R(Japanese, "A_South/TimerButton_Decor", "GateMechanism"),
            R(Japanese, "Corridor_North/Gate", "Gate"),
            R(Japanese, "B_South/GateLever", "Lever"),
            R(Japanese, "B_North/PairButton_North_Decor", "WallButton", 180f),
            R(Japanese, "B_South/PairButton_South_Decor", "WallButton"),
            R(Japanese, "B_West/Tansu", "Tansu", 90f),
            R(Japanese, "B_West/DoorHandle", "Item_DoorHandle", center: true),
            R(Japanese, "A_NorthEast/Corner_Block", "LatticeCabinet", 180f),
            R(Japanese, "A_West_Upper/Scroll", "Scroll", 90f),
            R(Japanese, "A_North/Extinguisher", "Extinguisher", 180f),
            R(Japanese, "B_East/East_Block", "LatticeCabinet", 270f),
            R(Japanese, "B_East/Locker_Block", "LatticeCabinet", 270f),
            R(Japanese, "C_North/ShojiDoor", "ShojiDoor", 180f),
            R(Japanese, "C_North/ShojiDoor_Open", "ShojiDoor_Open", 180f),
            R(Japanese, "BridgeDoor", "BridgeDoor", fit: true),
            R(Japanese, "ShadowScreen", "ShadowScreen", 90f),
            R(Japanese, "LanternStand_0", "LanternStand", 90f),
            R(Japanese, "LanternStand_1", "LanternStand", 90f),
            R(Japanese, "LanternStand_2", "LanternStand", 90f),
            R(Japanese, "Lantern_0", "Item_Lantern", center: true),
            R(Japanese, "Lantern_1", "Item_Lantern", center: true),
            R(Japanese, "Lantern_Placed", "Item_Lantern", center: true),
        };

        string[] ids = { "Main", "Cartoon", "Upside", "Japanese" };
        AddDecorRules(rules);
        int placed = 0;
        var problems = new System.Collections.Generic.List<string>();

        for (int w = 0; w < 4; w++)
        {
            Transform worldRoot = worlds.transform.Find("World_" + w + "_" + ids[w]);
            Transform artRoot = art.transform.Find("Art_" + w + "_" + ids[w]);
            if (worldRoot == null || artRoot == null) continue;

            Transform old = artRoot.Find("Auto");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            Transform auto = new GameObject("Auto").transform;
            auto.SetParent(artRoot, false);
            string prefix = w < 4 ? "Puzzle/" : "";

            // Стены, пол и потолок: одна модель на мир. Перевёрнутый мир берёт
            // индустриальную модель и кладёт её в отражённую папку Mirrored.
            if (PlaceEnvironment)
            {
                string[] envModels = { "Env_Main", "Env_Cartoon", "Env_Main", "Env_Japanese" };
                var envPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/" + envModels[w] + ".fbx");
                Transform envParent = auto;
                if (w == Upside)
                {
                    Transform mirrored = artRoot.Find("Mirrored");
                    if (mirrored != null)
                    {
                        Transform oldAuto = mirrored.Find("Auto");
                        if (oldAuto != null) Object.DestroyImmediate(oldAuto.gameObject);
                        envParent = new GameObject("Auto").transform;
                        envParent.SetParent(mirrored, false);
                    }
                }

                if (envPrefab == null)
                {
                    problems.Add("нет модели " + envModels[w] + ".fbx");
                }
                else
                {
                    var envObject = (GameObject)PrefabUtility.InstantiatePrefab(envPrefab);
                    envObject.name = envModels[w] + "__Geometry";
                    envObject.transform.SetParent(envParent, false);
                    var envLink = envObject.AddComponent<ArtLink>();
                    envLink.targetPath = worldRoot.name + "/Geometry";
                    envLink.attach = false;
                    placed++;
                }
            }

            foreach (ArtRule rule in rules)
            {
                if (rule.world != w) continue;

                Transform target = worldRoot.Find(prefix + rule.path);
                if (target == null) { problems.Add(ids[w] + ": нет объекта " + rule.path); continue; }

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/" + rule.model + ".fbx");
                if (prefab == null) { problems.Add("нет модели " + rule.model + ".fbx"); continue; }

                Transform boundsSource = rule.boundsChild != null ? target.Find(rule.boundsChild) : target;
                if (boundsSource == null || !TryBounds(boundsSource, out Bounds b))
                {
                    problems.Add(ids[w] + ": у " + rule.path + " нет видимых частей");
                    continue;
                }

                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.name = rule.model + "__" + target.name;
                go.transform.SetParent(auto, true);
                go.transform.position = Vector3.zero;
                Quaternion baseRotation = go.transform.rotation;
                Vector3 baseScale = go.transform.localScale;

                if (rule.fit && TryBounds(go.transform, out Bounds mb) && mb.size.x > 0.001f && mb.size.y > 0.001f
                    && mb.size.z > 0.001f)
                {
                    go.transform.localScale = Vector3.Scale(baseScale,
                        new Vector3(b.size.x / mb.size.x, b.size.y / mb.size.y, b.size.z / mb.size.z));
                }

                float y;
                if (rule.center) y = b.center.y;
                else if (rule.floor) y = worldRoot.position.y + (rule.flip ? H : 0f);
                else y = rule.flip ? b.max.y : b.min.y;

                go.transform.rotation = Quaternion.Euler(0f, rule.yaw, 0f)
                                        * Quaternion.Euler(0f, 0f, rule.flip ? 180f : 0f) * baseRotation;
                go.transform.position = new Vector3(b.center.x, y, b.center.z);

                var link = go.AddComponent<ArtLink>();
                link.targetPath = worldRoot.name + "/" + prefix + rule.path;
                link.attach = rule.attach;
                placed++;
            }
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        string report = "Поповорот: расставлено моделей — " + placed + ".";
        if (problems.Count > 0) report += "\nНе получилось:\n" + string.Join("\n", problems);
        Debug.Log(report);
        EditorUtility.DisplayDialog("Поповорот", report, "Понятно");
    }

    static void BuildPlayer()
    {
        var old = GameObject.Find("Player");
        if (old != null) Object.DestroyImmediate(old);

        // Выключаем камеру из шаблона сцены, чтобы не было двух камер.
        var mainCamera = Camera.main;
        if (mainCamera != null) mainCamera.gameObject.SetActive(false);

        var player = new GameObject("Player");
        player.transform.position = new Vector3(0f, 0.1f, LA * 0.5f);   // центр комнаты А, основной мир

        var cc = player.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.35f;
        cc.center = new Vector3(0f, 0.9f, 0f);

        var cameraObject = new GameObject("Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.SetParent(player.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        var camera = cameraObject.AddComponent<Camera>();
        camera.nearClipPlane = 0.05f;
        camera.fieldOfView = 70f;
        camera.farClipPlane = 85f;   // соседние миры в 100 м не рисуются
        cameraObject.AddComponent<AudioListener>();
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        // Сглаживание краёв и мелких полосок текстур: дёшево, подходит для браузера.
        camera.GetUniversalAdditionalCameraData().antialiasing = AntialiasingMode.FastApproximateAntialiasing;

        var hold = new GameObject("HoldPoint").transform;
        hold.SetParent(cameraObject.transform, false);
        hold.localPosition = new Vector3(0.35f, -0.3f, 0.7f);

        var controller = player.AddComponent<PlayerController>();
        controller.cameraPivot = cameraObject.transform;

        var interactor = player.AddComponent<Interactor>();
        interactor.cam = camera;
        interactor.holdPoint = hold;

        player.AddComponent<Hints>();
        player.AddComponent<Highlighter>().shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Highlight.shader");
        player.AddComponent<DevCheats>();
    }
}
