using UnityEngine;

namespace TankCollision
{
    /// <summary>
    /// Строит всю графику сцены из примитивов: танки, снаряд, землю, стрелки, эффекты.
    /// Если в Assets/Resources лежит префаб "TankModel", он используется вместо танка из кубиков.
    /// </summary>
    public static class SceneFactory
    {
        // ---------- Материалы ----------

        static Shader Find(params string[] names)
        {
            foreach (var n in names)
            {
                var s = Shader.Find(n);
                if (s != null) return s;
            }
            return null;
        }

        public static Material Lit(Color c, float smoothness = 0.25f, float metallic = 0f)
        {
            var m = new Material(Find("Universal Render Pipeline/Lit", "Standard"));
            m.color = c;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            return m;
        }

        public static Material Unlit(Color c)
        {
            var m = new Material(Find("Universal Render Pipeline/Unlit", "Unlit/Color"));
            m.color = c;
            return m;
        }

        /// <summary>Аддитивный материал для искр и вспышек.</summary>
        public static Material Particle(Texture2D tex)
        {
            var m = new Material(Find("Legacy Shaders/Particles/Additive", "Particles/Standard Unlit",
                                      "Universal Render Pipeline/Particles/Unlit", "Sprites/Default"));
            m.mainTexture = tex;
            return m;
        }

        /// <summary>Полупрозрачный (alpha blend) материал для дыма и пыли.</summary>
        public static Material Smoke(Texture2D tex)
        {
            var m = new Material(Find("Legacy Shaders/Particles/Alpha Blended", "Sprites/Default",
                                      "Universal Render Pipeline/Particles/Unlit"));
            m.mainTexture = tex;
            return m;
        }

        public static Texture2D SoftDot(int size = 64)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float h = (size - 1) / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - h) * (x - h) + (y - h) * (y - h)) / h;
                float a = Mathf.Clamp01(1f - d);
                a *= a;
                t.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            t.Apply();
            t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        /// <summary>Текстура земли: клетка 10×10 м с линиями сетки (чтобы было видно движение).</summary>
        public static Texture2D GroundTexture()
        {
            const int n = 256;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true);
            var baseA = new Color(0.36f, 0.40f, 0.27f);
            var baseB = new Color(0.33f, 0.37f, 0.25f);
            var line = new Color(0.25f, 0.28f, 0.19f);
            var rnd = new System.Random(7);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                bool grid = x < 3 || y < 3;
                bool half = (x < n / 2) ^ (y < n / 2);
                var c = grid ? line : (half ? baseA : baseB);
                float noise = (float)rnd.NextDouble() * 0.04f - 0.02f;
                t.SetPixel(x, y, new Color(c.r + noise, c.g + noise, c.b + noise, 1));
            }
            t.Apply(true);
            t.wrapMode = TextureWrapMode.Repeat;
            t.filterMode = FilterMode.Trilinear;
            t.anisoLevel = 8;
            return t;
        }

        // ---------- Примитивы ----------

        static GameObject Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material mat,
                               Vector3 euler = default, bool keepCollider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!keepCollider) Object.Destroy(go.GetComponent<Collider>());
            return go;
        }

        public const float BarrelHeight = 2.05f;   // высота оси ствола над землёй, м
        public const float MuzzleForward = 5.8f;   // вынос дульного среза от центра танка, м

        /// <summary>
        /// Танк из примитивов. Локальная ось +Z — вперёд. Возвращает корень; дульный срез — дочерний объект "Muzzle".
        /// </summary>
        public static Transform BuildTank(string name, Color body, Color dark)
        {
            var root = new GameObject(name).transform;

            var custom = Resources.Load<GameObject>("TankModel");
            if (custom != null)
            {
                var model = Object.Instantiate(custom, root);
                model.name = "Model";
                // коллайдеры нужны для определения точки попадания
                foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
                    if (mf.GetComponent<Collider>() == null) mf.gameObject.AddComponent<MeshCollider>();
            }
            else
            {
                var mBody = Lit(body, 0.2f);
                var mDark = Lit(dark, 0.1f);
                var mMetal = Lit(dark * 0.8f, 0.5f, 0.6f);

                Part(PrimitiveType.Cube, root, new Vector3(0, 1.15f, 0), new Vector3(3.0f, 1.0f, 6.2f), mBody, default, true);
                Part(PrimitiveType.Cube, root, new Vector3(0, 1.30f, 3.15f), new Vector3(3.0f, 0.55f, 1.1f), mBody, new Vector3(-28, 0, 0));
                Part(PrimitiveType.Cube, root, new Vector3(-1.75f, 0.6f, 0), new Vector3(0.7f, 1.0f, 6.7f), mDark);
                Part(PrimitiveType.Cube, root, new Vector3(1.75f, 0.6f, 0), new Vector3(0.7f, 1.0f, 6.7f), mDark);
                for (int i = 0; i < 5; i++)
                {
                    float z = -2.4f + i * 1.2f;
                    Part(PrimitiveType.Cylinder, root, new Vector3(-2.13f, 0.55f, z), new Vector3(0.85f, 0.06f, 0.85f), mMetal, new Vector3(0, 0, 90));
                    Part(PrimitiveType.Cylinder, root, new Vector3(2.13f, 0.55f, z), new Vector3(0.85f, 0.06f, 0.85f), mMetal, new Vector3(0, 0, 90));
                }
                // башня, маска, ствол
                Part(PrimitiveType.Cube, root, new Vector3(0, 2.05f, -0.3f), new Vector3(2.3f, 0.85f, 2.7f), mBody, default, true);
                Part(PrimitiveType.Cylinder, root, new Vector3(0, 2.05f, 1.15f), new Vector3(0.7f, 0.35f, 0.7f), mBody, new Vector3(90, 0, 0));
                Part(PrimitiveType.Cylinder, root, new Vector3(0, BarrelHeight, 3.45f), new Vector3(0.2f, 2.35f, 0.2f), mMetal, new Vector3(90, 0, 0));
                Part(PrimitiveType.Cylinder, root, new Vector3(0, BarrelHeight, 5.6f), new Vector3(0.3f, 0.2f, 0.3f), mMetal, new Vector3(90, 0, 0));
                // люк командира
                Part(PrimitiveType.Cylinder, root, new Vector3(-0.5f, 2.55f, -0.8f), new Vector3(0.7f, 0.08f, 0.7f), mDark);
            }

            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(root, false);
            muzzle.localPosition = new Vector3(0, BarrelHeight, MuzzleForward);
            return root;
        }

        /// <summary>Снаряд: локальная ось +Z — направление полёта. Размер слегка увеличен, чтобы был виден.</summary>
        public static Transform BuildShell()
        {
            var root = new GameObject("Shell").transform;
            var brass = Lit(new Color(0.75f, 0.55f, 0.2f), 0.7f, 0.8f);
            var steel = Lit(new Color(0.25f, 0.25f, 0.27f), 0.6f, 0.8f);
            Part(PrimitiveType.Cylinder, root, new Vector3(0, 0, -0.1f), new Vector3(0.16f, 0.28f, 0.16f), brass, new Vector3(90, 0, 0));
            Part(PrimitiveType.Sphere, root, new Vector3(0, 0, 0.22f), new Vector3(0.16f, 0.16f, 0.36f), steel);

            var trail = root.gameObject.AddComponent<TrailRenderer>();
            trail.time = 0.35f;
            trail.minVertexDistance = 0.2f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0, 0.12f), new Keyframe(1, 0f));
            trail.sharedMaterial = Particle(SoftDot());
            trail.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(new Color(1f, 0.85f, 0.4f), 0), new GradientColorKey(new Color(1f, 0.3f, 0.05f), 1) },
                alphaKeys = new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) }
            };
            return root;
        }

        public static GameObject BuildGround()
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Plane);
            g.name = "Ground";
            g.transform.localScale = new Vector3(400, 1, 400);   // 4 × 4 км
            var m = Lit(Color.white, 0.05f);
            m.mainTexture = GroundTexture();
            m.mainTextureScale = new Vector2(400, 400);          // клетка 10 м
            g.GetComponent<Renderer>().sharedMaterial = m;
            return g;
        }

        public static LineRenderer Line(string name, Color c, float width, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent) go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = Unlit(c);
            lr.widthMultiplier = width;
            lr.positionCount = 0;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        // ---------- Эффекты ----------

        public static ParticleSystem Burst(string name, Material mat, Color c, float speed, float size, float life,
                                           int count, float gravity, float radius = 0.2f)
        {
            var go = new GameObject(name);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.3f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.4f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.25f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
            main.startColor = c;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1000;

            var em = ps.emission;
            em.rateOverTime = 0;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = radius;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                alphaKeys = new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) }
            });

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            return ps;
        }
    }
}
