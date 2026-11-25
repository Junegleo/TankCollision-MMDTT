using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TankCollision
{
    /// <summary>
    /// Окружение сцены: советская деревня (избы, сарай, колодец-журавль, заборы, стога),
    /// грунтовая дорога с телеграфными столбами, лес за спиной стреляющего танка и полосы леса на горизонте.
    /// Всё собирается из примитивов и объединяется в несколько больших мешей (по одному на материал),
    /// чтобы тысячи деревьев не тормозили сцену. На физику модели окружение не влияет.
    /// </summary>
    public static class EnvironmentBuilder
    {
        // Дорога проходит через точку удара (начало координат) под углом 60° к оси OX —
        // по умолчанию танк-цель едет прямо по ней в сторону деревни.
        const float RoadAngleDeg = 60f;
        const float RoadFrom = -320f, RoadTo = 640f, RoadWidth = 6f;

        static Vector3 roadDir, roadNormal;
        static Mesh cube, cylinder, sphere, cone;
        static readonly Dictionary<Material, List<CombineInstance>> batches = new Dictionary<Material, List<CombineInstance>>();
        static System.Random rnd;

        // материалы
        static Material wood1, wood2, woodDark, roofGrey, roofGreen, roofRust, glass, frameBlue, frameWhite, brick;
        static Material road, rut, pine1, pine2, trunk, birchTrunk, birchCrown, bush, hay, pole;

        public static Transform Build()
        {
            var root = new GameObject("Окружение: деревня, лес, дорога").transform;
            batches.Clear();
            rnd = new System.Random(1945);
            float a = RoadAngleDeg * Mathf.Deg2Rad;
            roadDir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            roadNormal = new Vector3(-roadDir.z, 0f, roadDir.x);

            LoadMeshes();
            CreateMaterials();

            BuildRoad();
            BuildVillage();
            BuildForests();

            Flush(root);
            return root;
        }

        // ------------------------------------------------------------------ базовые вещи

        static void LoadMeshes()
        {
            // В проекте Play запускается без перезагрузки домена: статические поля переживают перезапуск,
            // а меши, созданные кодом, Unity уничтожает при выходе из Play. Поэтому проверяем каждый меш
            // через «живость» объекта Unity (!mesh) и пересоздаём уничтоженные — иначе пропадают кроны деревьев.
            if (!cube) cube = PrimitiveMesh(PrimitiveType.Cube);
            if (!cylinder) cylinder = PrimitiveMesh(PrimitiveType.Cylinder);
            if (!sphere) sphere = BuildSphere(8, 6);
            if (!cone) cone = BuildCone(10);
        }

        static Mesh PrimitiveMesh(PrimitiveType t)
        {
            var go = GameObject.CreatePrimitive(t);
            var m = go.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(go);
            return m;
        }

        /// <summary>Лёгкая UV-сфера диаметром 1 (как стандартная сфера Unity, но в 8 раз меньше вершин).</summary>
        static Mesh BuildSphere(int seg, int rings)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float phi = Mathf.PI * r / rings;
                for (int k = 0; k <= seg; k++)
                {
                    float th = Mathf.PI * 2f * k / seg;
                    v.Add(new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th)) * 0.5f);
                }
            }
            for (int r = 0; r < rings; r++)
            for (int k = 0; k < seg; k++)
            {
                int a = r * (seg + 1) + k, b = a + seg + 1, c = b + 1, d = a + 1;
                t.Add(a); t.Add(c); t.Add(b);
                t.Add(a); t.Add(d); t.Add(c);
            }
            var m = new Mesh { name = "LowSphere" };
            m.SetVertices(v);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Конус: основание радиуса 1 на y = 0, вершина на y = 1.</summary>
        static Mesh BuildCone(int n)
        {
            var v = new Vector3[n + 2];
            var t = new int[n * 6];
            v[n] = Vector3.up;
            v[n + 1] = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                float ang = i * Mathf.PI * 2f / n;
                v[i] = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                t[i * 6 + 0] = i; t[i * 6 + 1] = n; t[i * 6 + 2] = j;        // боковая грань (наружу)
                t[i * 6 + 3] = i; t[i * 6 + 4] = j; t[i * 6 + 5] = n + 1;    // дно (вниз)
            }
            var m = new Mesh { name = "Cone", vertices = v, triangles = t };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static void CreateMaterials()
        {
            Material L(string name, float r, float g, float b, float smooth = 0.1f)
            {
                var m = SceneFactory.Lit(new Color(r, g, b), smooth);
                m.name = name;
                return m;
            }
            wood1 = L("Брёвна светлые", 0.47f, 0.33f, 0.20f);
            wood2 = L("Брёвна тёмные", 0.36f, 0.26f, 0.17f);
            woodDark = L("Пазы между брёвнами", 0.22f, 0.16f, 0.11f);
            roofGrey = L("Крыша серая (тёс)", 0.38f, 0.37f, 0.35f);
            roofGreen = L("Крыша зелёная (жесть)", 0.24f, 0.37f, 0.30f, 0.3f);
            roofRust = L("Крыша рыжая (жесть)", 0.48f, 0.26f, 0.17f, 0.3f);
            glass = L("Стекло", 0.07f, 0.09f, 0.11f, 0.85f);
            frameBlue = L("Наличники синие", 0.26f, 0.46f, 0.74f, 0.3f);
            frameWhite = L("Наличники белые", 0.88f, 0.88f, 0.84f, 0.3f);
            brick = L("Кирпич", 0.55f, 0.25f, 0.18f);
            road = L("Грунтовая дорога", 0.42f, 0.35f, 0.25f, 0.05f);
            rut = L("Колея", 0.31f, 0.25f, 0.18f, 0.15f);
            pine1 = L("Хвоя", 0.12f, 0.24f, 0.13f);
            pine2 = L("Хвоя тёмная", 0.09f, 0.19f, 0.11f);
            trunk = L("Ствол сосны", 0.33f, 0.22f, 0.14f);
            birchTrunk = L("Ствол берёзы", 0.86f, 0.86f, 0.81f);
            birchCrown = L("Листва берёзы", 0.36f, 0.50f, 0.22f);
            bush = L("Кусты", 0.17f, 0.30f, 0.14f);
            hay = L("Сено", 0.70f, 0.60f, 0.33f);
            pole = L("Столбы", 0.30f, 0.24f, 0.18f);
        }

        static void Add(Mesh mesh, Material mat, Matrix4x4 m)
        {
            if (!batches.TryGetValue(mat, out var list)) batches[mat] = list = new List<CombineInstance>();
            list.Add(new CombineInstance { mesh = mesh, transform = m });
        }

        static Matrix4x4 T(Vector3 p, Vector3 s) => Matrix4x4.TRS(p, Quaternion.identity, s);
        static Matrix4x4 T(Vector3 p, Quaternion r, Vector3 s) => Matrix4x4.TRS(p, r, s);

        static float R(float min, float max) => min + (float)rnd.NextDouble() * (max - min);
        static T Pick<T>(params T[] items) => items[rnd.Next(items.Length)];

        static Vector3 OnRoad(float s, float offset) => roadDir * s + roadNormal * offset;

        static void Flush(Transform root)
        {
            foreach (var kv in batches)
            {
                var mesh = new Mesh { name = kv.Key.name, indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                mesh.RecalculateBounds();
                var go = new GameObject(kv.Key.name);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = kv.Key;
                mr.shadowCastingMode = ShadowCastingMode.On;
            }
            batches.Clear();
        }

        // ------------------------------------------------------------------ дорога

        static void BuildRoad()
        {
            float len = RoadTo - RoadFrom;
            float mid = (RoadFrom + RoadTo) * 0.5f;
            var rot = Quaternion.LookRotation(roadDir);
            // полотно (верх на 2,5 см над землёй, ниже линий осей и следов)
            Add(cube, road, T(OnRoad(mid, 0) + Vector3.up * 0.0125f, rot, new Vector3(RoadWidth, 0.025f, len)));
            // две колеи
            Add(cube, rut, T(OnRoad(mid, -1.3f) + Vector3.up * 0.015f, rot, new Vector3(0.7f, 0.03f, len)));
            Add(cube, rut, T(OnRoad(mid, 1.3f) + Vector3.up * 0.015f, rot, new Vector3(0.7f, 0.03f, len)));

            // телеграфные столбы вдоль дороги
            for (float s = RoadFrom + 20f; s < RoadTo; s += 45f)
            {
                Vector3 p = OnRoad(s, 6.5f);
                Add(cylinder, pole, T(p + Vector3.up * 3.6f, new Vector3(0.24f, 3.6f, 0.24f)));
                Add(cube, pole, T(p + Vector3.up * 6.6f, rot * Quaternion.Euler(0, 90, 0), new Vector3(1.8f, 0.14f, 0.14f)));
            }
        }

        // ------------------------------------------------------------------ деревня

        static void BuildVillage()
        {
            // избы по обе стороны дороги
            for (float s = 235f; s <= 400f; s += R(21f, 26f))
            {
                House(s + R(-2f, 2f), +1, 17f + R(-1.5f, 1.5f));
                House(s + R(-2f, 2f) + 11f, -1, 17f + R(-1.5f, 1.5f));
            }
            // второй ряд, подальше от дороги
            for (float s = 250f; s <= 380f; s += R(30f, 40f))
                House(s, rnd.Next(2) == 0 ? 1 : -1, 46f + R(-3f, 3f));

            // колхозный сарай и колодец-журавль
            Barn(OnRoad(215f, -32f), Quaternion.LookRotation(roadDir));
            Barn(OnRoad(420f, 34f), Quaternion.LookRotation(roadNormal));
            Well(OnRoad(312f, 7.5f));

            // стога сена в полях перед деревней
            for (int i = 0; i < 26; i++)
            {
                float s = R(120f, 330f);
                float off = R(30f, 110f) * (rnd.Next(2) == 0 ? 1 : -1);
                Vector3 p = OnRoad(s, off);
                if (p.magnitude < 130f || (s > 222f && Mathf.Abs(off) < 62f)) continue;
                float k = R(0.8f, 1.2f);
                Add(sphere, hay, T(p + Vector3.up * 1.2f * k, new Vector3(4.2f, 3.8f, 4.2f) * k));
                Add(cone, hay, T(p + Vector3.up * 2.4f * k, new Vector3(1.6f, 1.6f, 1.6f) * k));
            }
        }

        /// <summary>Изба. side = +1/−1 — сторона дороги, offset — расстояние от оси дороги до центра избы.</summary>
        static void House(float s, int side, float offset)
        {
            Vector3 pos = OnRoad(s, side * offset);
            // локальная ось −Z (фасад с окнами) смотрит на дорогу
            var rot = Quaternion.LookRotation(roadNormal * side) * Quaternion.Euler(0, R(-4f, 4f), 0);
            var H = Matrix4x4.TRS(pos, rot, Vector3.one);

            float w = R(5.6f, 6.8f), l = R(7f, 8.5f), h = R(2.9f, 3.4f), rise = R(2.1f, 2.7f);
            var wood = Pick(wood1, wood2);
            var roof = Pick(roofGrey, roofGrey, roofGreen, roofRust);
            var frame = Pick(frameBlue, frameBlue, frameWhite);

            // сруб и пазы между брёвнами
            Add(cube, wood, H * T(new Vector3(0, h / 2, 0), new Vector3(w, h, l)));
            for (float y = 0.35f; y < h; y += 0.42f)
            {
                Add(cube, woodDark, H * T(new Vector3(w / 2 + 0.02f, y, 0), new Vector3(0.05f, 0.06f, l + 0.3f)));
                Add(cube, woodDark, H * T(new Vector3(-w / 2 - 0.02f, y, 0), new Vector3(0.05f, 0.06f, l + 0.3f)));
                Add(cube, woodDark, H * T(new Vector3(0, y, -l / 2 - 0.02f), new Vector3(w + 0.3f, 0.06f, 0.05f)));
            }
            // фронтон из брёвен (треугольник под крышей)
            Gable(H, wood, h, w, rise, -l / 2);
            Gable(H, wood, h, w, rise, l / 2);

            // двускатная крыша со свесами
            Roof(H, roof, h, w, l, rise, 0.6f, 0.16f);

            // окна с наличниками: три на фасаде, по два на боковых стенах, слуховое на фронтоне
            for (int i = -1; i <= 1; i++)
                Window(H, frame, new Vector3(i * w / 3.2f, h * 0.52f, -l / 2), Quaternion.identity);
            for (int i = -1; i <= 1; i += 2)
            {
                Window(H, frame, new Vector3(w / 2, h * 0.52f, i * l / 4), Quaternion.Euler(0, -90, 0));
                Window(H, frame, new Vector3(-w / 2, h * 0.52f, i * l / 4), Quaternion.Euler(0, 90, 0));
            }
            Window(H, frame, new Vector3(0, h + rise * 0.35f, -l / 2 - 0.13f), Quaternion.identity, 0.6f);

            // печная труба
            Add(cube, brick, H * T(new Vector3(w * 0.2f, h + rise * 0.75f, l * 0.15f), new Vector3(0.6f, rise * 0.9f, 0.6f)));

            // крыльцо сбоку
            Add(cube, woodDark, H * T(new Vector3(w / 2 + 0.8f, 0.3f, l * 0.3f), new Vector3(1.6f, 0.6f, 1.8f)));

            // забор-палисадник перед фасадом
            float fz = -l / 2 - 4f;
            float fw = w + 5f;
            for (float x = -fw / 2; x <= fw / 2 + 0.01f; x += 2.2f)
                Add(cube, pole, H * T(new Vector3(x, 0.65f, fz), new Vector3(0.12f, 1.3f, 0.12f)));
            Add(cube, wood2, H * T(new Vector3(0, 0.45f, fz), new Vector3(fw, 0.09f, 0.05f)));
            Add(cube, wood2, H * T(new Vector3(0, 1.0f, fz), new Vector3(fw, 0.09f, 0.05f)));
            for (float z = fz; z < -l / 2; z += 2.2f)
            {
                Add(cube, pole, H * T(new Vector3(-fw / 2, 0.65f, z), new Vector3(0.12f, 1.3f, 0.12f)));
                Add(cube, pole, H * T(new Vector3(fw / 2, 0.65f, z), new Vector3(0.12f, 1.3f, 0.12f)));
            }

            // черёмуха или берёза у дома
            if (rnd.NextDouble() < 0.6)
            {
                Vector3 tp = H.MultiplyPoint3x4(new Vector3(R(-w, w), 0, l / 2 + R(2f, 5f)));
                Birch(tp, R(9f, 14f));
            }
        }

        /// <summary>
        /// Двускатная крыша: конёк на высоте h + rise, скат проходит точно через верх стены (x = ±w/2, y = h),
        /// свес overhang выходит за стену и опускается ниже её верха.
        /// </summary>
        static void Roof(Matrix4x4 H, Material mat, float h, float w, float l, float rise, float overhang, float thick)
        {
            float hw = w / 2 + overhang;
            float riseT = rise * hw / (w / 2);
            float slope = Mathf.Sqrt(hw * hw + riseT * riseT);
            float ang = Mathf.Atan2(riseT, hw) * Mathf.Rad2Deg;
            float cy = h + rise - riseT / 2 + thick / 2;
            var size = new Vector3(slope + 0.1f, thick, l + 2f * overhang + 0.1f);
            Add(cube, mat, H * T(new Vector3(-hw / 2, cy, 0), Quaternion.Euler(0, 0, ang), size));
            Add(cube, mat, H * T(new Vector3(hw / 2, cy, 0), Quaternion.Euler(0, 0, -ang), size));
        }

        /// <summary>Треугольный фронтон из брёвен: набран из горизонтальных брусков убывающей ширины.</summary>
        static void Gable(Matrix4x4 H, Material wood, float h, float w, float rise, float z)
        {
            const int steps = 6;
            for (int i = 0; i < steps; i++)
            {
                float y0 = rise * i / steps;
                float width = w * (1f - (i + 0.5f) / steps);
                Add(cube, wood, H * T(new Vector3(0, h + y0 + rise / steps / 2f, z), new Vector3(width, rise / steps, 0.25f)));
            }
        }

        static void Window(Matrix4x4 H, Material frame, Vector3 wallPoint, Quaternion faceRot, float size = 1f)
        {
            // faceRot поворачивает «фасадное» окно (смотрит в −Z) на нужную стену
            var M = H * Matrix4x4.TRS(wallPoint, faceRot, Vector3.one * size);
            Add(cube, frame, M * T(new Vector3(0, 0, -0.05f), new Vector3(1.15f, 1.45f, 0.08f)));
            Add(cube, glass, M * T(new Vector3(0, 0, -0.10f), new Vector3(0.8f, 1.05f, 0.05f)));
            Add(cube, frame, M * T(new Vector3(0, 0, -0.13f), new Vector3(0.08f, 1.05f, 0.03f)));  // переплёт
            Add(cube, frame, M * T(new Vector3(0, 0.82f, -0.12f), new Vector3(1.4f, 0.22f, 0.1f))); // резной верх
            Add(cube, frame, M * T(new Vector3(0, -0.8f, -0.12f), new Vector3(1.3f, 0.12f, 0.14f))); // подоконник
        }

        static void Barn(Vector3 pos, Quaternion rot)
        {
            var H = Matrix4x4.TRS(pos, rot, Vector3.one);
            float w = 11f, l = 26f, h = 5f, rise = 3.6f;
            Add(cube, wood2, H * T(new Vector3(0, h / 2, 0), new Vector3(w, h, l)));
            for (float y = 0.5f; y < h; y += 0.6f)
            {
                Add(cube, woodDark, H * T(new Vector3(w / 2 + 0.02f, y, 0), new Vector3(0.05f, 0.08f, l + 0.3f)));
                Add(cube, woodDark, H * T(new Vector3(-w / 2 - 0.02f, y, 0), new Vector3(0.05f, 0.08f, l + 0.3f)));
            }
            Gable(H, wood2, h, w, rise, -l / 2);
            Gable(H, wood2, h, w, rise, l / 2);
            Roof(H, roofRust, h, w, l, rise, 0.8f, 0.2f);
            // широкие ворота в торце
            Add(cube, woodDark, H * T(new Vector3(0, 1.9f, -l / 2 - 0.06f), new Vector3(4.2f, 3.8f, 0.1f)));
        }

        static void Well(Vector3 pos)
        {
            // сруб колодца
            Add(cube, wood2, T(pos + Vector3.up * 0.45f, new Vector3(1.4f, 0.9f, 1.4f)));
            // столб-опора и длинный шест журавля
            Vector3 post = pos + roadDir * 2.5f;
            Add(cylinder, pole, T(post + Vector3.up * 2.2f, new Vector3(0.28f, 2.2f, 0.28f)));
            var tilt = Quaternion.LookRotation(-roadDir) * Quaternion.Euler(-22f, 0, 0);
            Add(cube, pole, T(post + Vector3.up * 4.3f, tilt, new Vector3(0.14f, 0.14f, 9f)));
            Add(cube, woodDark, T(post + Vector3.up * 2.6f + roadDir * 3.9f, new Vector3(0.5f, 0.5f, 0.5f))); // противовес
        }

        // ------------------------------------------------------------------ лес

        static void BuildForests()
        {
            // лес за спиной стреляющего танка (он стоит у кромки, x ≈ −156)
            Forest(new Rect(-340f, -230f, 168f, 460f), 8.5f, false);
            // кустарник и берёзки у самой кромки — танк «в засаде»
            for (int i = 0; i < 14; i++)
            {
                float z = R(-28f, 28f);
                if (z > -13f && z < 5f) continue;         // не загораживаем ствол, линию выстрела и камеру «Кино»
                Vector3 p = new Vector3(R(-170f, -160f), 0, z);
                if (rnd.NextDouble() < 0.5) Birch(p, R(8f, 13f));
                else Add(sphere, bush, T(p + Vector3.up * 0.9f, new Vector3(R(2.5f, 4f), R(1.8f, 2.6f), R(2.5f, 4f))));
            }

            // полосы леса на горизонте
            Forest(new Rect(-420f, 470f, 1100f, 140f), 13f, true);   // за деревней
            Forest(new Rect(-420f, -500f, 980f, 110f), 13f, true);   // на юге
            Forest(new Rect(560f, -390f, 120f, 860f), 13f, true);    // на востоке

            // отдельные рощицы в поле
            Grove(new Vector3(-60f, 0, 210f), 30f, 14);
            Grove(new Vector3(260f, 0, -170f), 35f, 18);
            Grove(new Vector3(-110f, 0, -260f), 25f, 10);
        }

        static void Forest(Rect area, float spacing, bool simple)
        {
            for (float x = area.xMin; x < area.xMax; x += spacing)
            for (float z = area.yMin; z < area.yMax; z += spacing)
            {
                var p = new Vector3(x + R(-0.45f, 0.45f) * spacing, 0, z + R(-0.45f, 0.45f) * spacing);
                if (NearRoad(p, 8f)) continue;
                if (rnd.NextDouble() < 0.12) continue;     // прогалины
                if (rnd.NextDouble() < 0.2) Birch(p, R(11f, 17f));
                else Pine(p, R(14f, 23f), simple);
            }
        }

        static void Grove(Vector3 center, float radius, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float a = R(0f, Mathf.PI * 2f), d = Mathf.Sqrt(R(0f, 1f)) * radius;
                var p = center + new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d);
                if (NearRoad(p, 8f)) continue;
                if (rnd.NextDouble() < 0.6) Birch(p, R(9f, 15f));
                else Pine(p, R(12f, 18f), true);
            }
        }

        static bool NearRoad(Vector3 p, float margin)
        {
            float along = Vector3.Dot(p, roadDir);
            float across = Mathf.Abs(Vector3.Dot(p, roadNormal));
            return along > RoadFrom - margin && along < RoadTo + margin && across < RoadWidth / 2 + margin;
        }

        static void Pine(Vector3 p, float H, bool simple)
        {
            Add(cylinder, trunk, T(p + Vector3.up * H * 0.25f, new Vector3(0.22f + H * 0.012f, H * 0.25f, 0.22f + H * 0.012f)));
            var foliage = rnd.Next(2) == 0 ? pine1 : pine2;
            int n = simple ? 2 : 3;
            for (int i = 0; i < n; i++)
            {
                float k = simple ? i * 1.5f : i;
                float y0 = H * (0.25f + 0.22f * k);
                float r = H * (0.17f - 0.04f * k);
                float hh = H * (0.42f - 0.06f * k);
                Add(cone, foliage, T(p + Vector3.up * y0, Quaternion.Euler(0, R(0f, 360f), 0), new Vector3(r, hh, r)));
            }
        }

        static void Birch(Vector3 p, float H)
        {
            Add(cylinder, birchTrunk, T(p + Vector3.up * H * 0.35f, new Vector3(0.28f, H * 0.35f, 0.28f)));
            Add(sphere, birchCrown, T(p + Vector3.up * H * 0.72f, new Vector3(H * 0.36f, H * 0.46f, H * 0.36f)));
            Add(sphere, birchCrown, T(p + Vector3.up * H * 0.6f + new Vector3(R(-1f, 1f), 0, R(-1f, 1f)),
                                      new Vector3(H * 0.26f, H * 0.3f, H * 0.26f)));
        }
    }
}
