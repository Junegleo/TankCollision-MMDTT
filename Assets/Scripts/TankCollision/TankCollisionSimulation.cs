using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TankCollision
{
    /// <summary>
    /// Математическая модель: абсолютно неупругий удар снаряда (m1, V1 вдоль OX) о едущий танк (m2, V2 под углом α).
    /// Ось OX модели = ось X Unity, ось OY модели = ось Z Unity (вид сверху: X вправо, Y вверх).
    /// Все параметры можно менять ползунками прямо во время анимации — расчёт и движение обновляются каждый кадр.
    /// </summary>
    public class TankCollisionSimulation : MonoBehaviour
    {
        [Header("Параметры задачи")]
        public CollisionParams prm = new CollisionParams();

        [Header("Воспроизведение")]
        [Range(0.1f, 2f)] public float playSpeed = 1f;
        public bool autoRepeat = true;
        public bool showVectors = true;
        public bool showUI = true;
        public int cameraMode = 1; // 1 — обзор, 2 — сверху, 3 — кино

        // ---- сценарий ----
        const float THit = 4f;            // момент удара (время модели), с
        const float TAfter = 6f;          // сколько показываем после удара, с
        const float ShooterDist = 150f;   // от дульного среза до точки удара, м
        const float FlightRealTime = 2.5f;// сколько реальных секунд длится полёт снаряда на экране

        enum Phase { Approach, Flight, After, Finished }
        Phase phase;
        float t;                 // время модели
        bool fired, hit;
        float realSinceHit;
        float flightScale = 1f;
        float timeScaleNow = 1f;

        // ---- объекты ----
        Transform target, shooter, shell;
        TrailRenderer shellTrail;
        Vector3 tankPos, tankOffset, lastV2vec;
        float tankYaw;
        Vector3 shellPrevPos;
        bool shellPrevValid;
        Vector3 hitPointWorld, tankPosAtHit;

        Transform glow;
        Material glowMat;
        Light glowLight, flashLight;
        ParticleSystem sparks, sparksHot, hitSmoke, muzzleFire, muzzleSmoke;
        LineRenderer trail, ghost, shellPath, axisX, axisY;
        readonly List<Vector3> trailPts = new List<Vector3>();
        VectorArrow arV1, arV2, arP1, arP2, arP;
        OrbitCamera cam;
        CollisionResult res;

        // ---- интерфейс ----
        static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
        readonly List<(Vector3 pos, string text, Color color)> labels = new List<(Vector3, string, Color)>();
        Rect leftRect, rightRect;
        float uiScale = 1f;
        GUIStyle sLabel, sSmall, sTitle, sBox, sBanner, sWorld, sButton, sToggle;
        Texture2D texPanel;

        static readonly Color CP1 = new Color(0.95f, 0.25f, 0.2f);
        static readonly Color CP2 = new Color(0.25f, 0.55f, 1f);
        static readonly Color CP = new Color(0.25f, 0.9f, 0.35f);

        // =====================================================================
        void Start()
        {
            BuildScene();
            Restart();
        }

        void OnDisable() => Time.timeScale = 1f;

        void BuildScene()
        {
            // камера
            var camGo = Camera.main != null ? Camera.main.gameObject : new GameObject("Main Camera", typeof(Camera));
            camGo.tag = "MainCamera";
            var c = camGo.GetComponent<Camera>();
            c.farClipPlane = 6000f;
            c.nearClipPlane = 0.1f;
            cam = camGo.GetComponent<OrbitCamera>();
            if (cam == null) cam = camGo.AddComponent<OrbitCamera>();
            cam.pointerOverUI = PointerOverUI;

            // свет и атмосфера
            Light sun = null;
            foreach (var l in FindObjectsByType<Light>())
                if (l.type == LightType.Directional) { sun = l; break; }
            if (sun == null) sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
            sun.intensity = 1.3f;
            sun.color = new Color(1f, 0.93f, 0.82f);
            sun.shadows = LightShadows.Soft;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0022f;
            RenderSettings.fogColor = new Color(0.62f, 0.66f, 0.68f);

            SceneFactory.BuildGround();
            EnvironmentBuilder.Build();   // деревня, лес, дорога (на физику не влияет)

            // танки
            target = SceneFactory.BuildTank("Танк-цель (m2)", new Color(0.42f, 0.44f, 0.40f), new Color(0.16f, 0.16f, 0.15f));
            shooter = SceneFactory.BuildTank("Стреляющий танк", new Color(0.30f, 0.36f, 0.22f), new Color(0.14f, 0.15f, 0.11f));
            shooter.SetPositionAndRotation(new Vector3(-ShooterDist - SceneFactory.MuzzleForward, 0, 0), Quaternion.Euler(0, 90, 0));

            shell = SceneFactory.BuildShell();
            shellTrail = shell.GetComponent<TrailRenderer>();

            // эффекты
            var dot = SceneFactory.SoftDot();
            var add = SceneFactory.Particle(dot);
            var smokeMat = SceneFactory.Smoke(dot);
            sparks = SceneFactory.Burst("Искры", add, new Color(1f, 0.75f, 0.3f), 30f, 0.25f, 1.2f, 200, 1.5f);
            sparksHot = SceneFactory.Burst("Вспышка удара", add, new Color(1f, 0.95f, 0.8f), 4f, 2.5f, 0.35f, 12, 0f);
            hitSmoke = SceneFactory.Burst("Дым удара", smokeMat, new Color(0.25f, 0.24f, 0.22f, 0.6f), 3f, 3.5f, 3.5f, 25, -0.05f, 0.6f);
            muzzleFire = SceneFactory.Burst("Дульное пламя", add, new Color(1f, 0.7f, 0.3f), 10f, 2.2f, 0.25f, 30, 0f, 0.3f);
            muzzleSmoke = SceneFactory.Burst("Дым выстрела", smokeMat, new Color(0.7f, 0.68f, 0.64f, 0.5f), 5f, 4f, 3f, 30, -0.05f, 0.5f);

            flashLight = new GameObject("Вспышка выстрела").AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.range = 25f;
            flashLight.color = new Color(1f, 0.75f, 0.4f);
            flashLight.intensity = 0f;

            // раскалённая броня
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(g.GetComponent<Collider>());
            g.name = "Раскалённая броня";
            glow = g.transform;
            glowMat = SceneFactory.Unlit(Color.black);
            g.GetComponent<Renderer>().sharedMaterial = glowMat;
            g.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            glowLight = new GameObject("Свет от брони").AddComponent<Light>();
            glowLight.type = LightType.Point;
            glowLight.range = 8f;
            glowLight.transform.SetParent(glow, false);

            // линии
            axisX = SceneFactory.Line("Ось OX", new Color(0.55f, 0.15f, 0.12f), 0.25f);
            axisX.positionCount = 2;
            axisX.SetPosition(0, new Vector3(-400, 0.04f, 0));
            axisX.SetPosition(1, new Vector3(400, 0.04f, 0));
            axisY = SceneFactory.Line("Ось OY", new Color(0.15f, 0.45f, 0.18f), 0.25f);
            axisY.positionCount = 2;
            axisY.SetPosition(0, new Vector3(0, 0.04f, -400));
            axisY.SetPosition(1, new Vector3(0, 0.04f, 400));
            shellPath = SceneFactory.Line("Траектория снаряда", new Color(1f, 0.55f, 0.15f), 0.35f);
            trail = SceneFactory.Line("След танка", new Color(0.1f, 0.1f, 0.1f), 0.6f);
            ghost = SceneFactory.Line("Путь без удара", new Color(0.85f, 0.85f, 0.85f), 0.3f);

            // векторы
            arV1 = VectorArrow.Create("V1", CP1);
            arV2 = VectorArrow.Create("V2 / u", CP2);
            arP1 = VectorArrow.Create("p1", CP1);
            arP2 = VectorArrow.Create("p2", CP2);
            arP = VectorArrow.Create("p", CP);
        }

        // =====================================================================
        public void Restart()
        {
            t = 0f;
            phase = Phase.Approach;
            fired = hit = false;
            realSinceHit = 0f;
            shellPrevValid = false;

            shell.SetParent(null, true);
            shell.gameObject.SetActive(false);
            shellTrail.Clear();
            glow.gameObject.SetActive(false);
            glow.SetParent(null, true);
            flashLight.intensity = 0f;
            foreach (var ps in new[] { sparks, sparksHot, hitSmoke, muzzleFire, muzzleSmoke })
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            Vector3 v2vec = Dir(prm.alphaDeg) * prm.v2;
            lastV2vec = v2vec;
            tankOffset = Vector3.zero;
            tankPos = -v2vec * THit;
            tankYaw = 90f - prm.alphaDeg;
            target.SetPositionAndRotation(tankPos, Quaternion.Euler(0, tankYaw, 0));

            trailPts.Clear();
            trail.positionCount = 0;
            ghost.positionCount = 0;
            shellPath.positionCount = 0;
            cam.Snap(tankPos + Vector3.up * 1.5f);
        }

        static Vector3 Dir(float deg)
        {
            float a = deg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        }

        static float YawOf(Vector3 v) => 90f - Mathf.Atan2(v.z, v.x) * Mathf.Rad2Deg;

        // =====================================================================
        void Update()
        {
            HandleKeys();
            res = InelasticCollisionModel.Solve(prm);

            float flightDur = ShooterDist / Mathf.Max(prm.v1, 1f);
            float tFire = THit - flightDur;
            UpdateTimeScale(flightDur, tFire);

            float dt = Time.deltaTime;               // время модели (с учётом замедления)
            float rdt = Time.unscaledDeltaTime;      // реальное время
            if (phase != Phase.Finished) t += dt;

            Vector3 v2vec = Dir(prm.alphaDeg) * prm.v2;
            float desiredYaw = tankYaw;

            if (!hit)
            {
                // если параметр поменяли на ходу — танк плавно «переезжает» на новую траекторию
                tankOffset += (v2vec - lastV2vec) * (THit - t);
                tankOffset *= Mathf.Exp(-rdt * 4f);
                tankPos = -v2vec * (THit - t) + tankOffset;
                desiredYaw = 90f - prm.alphaDeg;
                target.SetPositionAndRotation(tankPos, Quaternion.Euler(0, Mathf.MoveTowardsAngle(tankYaw, desiredYaw, 240f * rdt), 0));

                if (!fired && t >= tFire) Fire();
                if (fired && !hit) UpdateShellFlight();
                if (!hit && t >= THit) Impact(tankPos + Vector3.up * SceneFactory.BarrelHeight - Vector3.right * 1.1f);
            }
            else if (phase != Phase.Finished)
            {
                realSinceHit += rdt * (timeScaleNow > 0 ? 1 : 0);
                var u = new Vector3((float)res.ux, 0f, (float)res.uy);
                tankPos += u * dt;
                if (u.sqrMagnitude > 0.0025f) desiredYaw = YawOf(u);
                UpdateGlow();
                if (t >= THit + TAfter)
                {
                    if (autoRepeat) { Restart(); return; }
                    phase = Phase.Finished;
                }
            }
            lastV2vec = v2vec;

            tankYaw = Mathf.MoveTowardsAngle(tankYaw, desiredYaw, 240f * rdt);
            target.SetPositionAndRotation(tankPos, Quaternion.Euler(0, tankYaw, 0));

            flashLight.intensity = Mathf.MoveTowards(flashLight.intensity, 0f, 60f * Time.deltaTime);

            UpdateLines(v2vec);
            UpdateVectors(v2vec);
        }

        void LateUpdate()
        {
            Vector3 tankFocus = tankPos + Vector3.up * 1.5f;
            switch (cameraMode)
            {
                case 3:
                    if (phase == Phase.Flight && shell.gameObject.activeSelf)
                        cam.TickCinema(shell.position + new Vector3(-6f, 1.6f, -4.5f), shell.position + Vector3.right * 6f, 30f);
                    else if (!hit)
                        cam.TickCinema(shooter.position + new Vector3(-9f, 4.5f, -7f), tankFocus, 3f);
                    else
                    {
                        float a = (Time.unscaledTime * 12f) * Mathf.Deg2Rad;
                        cam.TickCinema(tankFocus + new Vector3(Mathf.Cos(a) * 20f, 7f, Mathf.Sin(a) * 20f), tankFocus, 4f);
                    }
                    break;
                default:
                    cam.TickOrbit(tankFocus, true);
                    break;
            }
        }

        void UpdateTimeScale(float flightDur, float tFire)
        {
            flightScale = Mathf.Clamp(flightDur / FlightRealTime, 0.003f, 1f);
            float s;
            switch (phase)
            {
                case Phase.Flight: s = flightScale; break;
                case Phase.After: s = Mathf.Lerp(flightScale, 1f, Mathf.SmoothStep(0f, 1f, realSinceHit / 2f)); break;
                case Phase.Approach: s = Mathf.Lerp(flightScale, 1f, Mathf.Clamp01((tFire - t) / 0.8f)); break;
                default: s = 1f; break;
            }
            timeScaleNow = s * playSpeed;
            Time.timeScale = Mathf.Clamp(timeScaleNow, 0f, 100f);
        }

        // ---------------------------------------------------------------------
        void Fire()
        {
            fired = true;
            phase = Phase.Flight;
            Vector3 muzzle = new Vector3(-ShooterDist, SceneFactory.BarrelHeight, 0f);
            shell.gameObject.SetActive(true);
            shell.SetPositionAndRotation(muzzle, Quaternion.Euler(0, 90, 0));
            shellTrail.Clear();
            shellTrail.emitting = true;
            shellPrevValid = false;

            muzzleFire.transform.position = muzzle;
            muzzleSmoke.transform.position = muzzle;
            muzzleFire.Play();
            muzzleSmoke.Play();
            flashLight.transform.position = muzzle + Vector3.up * 0.5f;
            flashLight.intensity = 25f;
        }

        void UpdateShellFlight()
        {
            float remain = (THit - t) * prm.v1;      // расстояние от снаряда до точки встречи
            var newPos = new Vector3(-remain, SceneFactory.BarrelHeight, 0f);
            if (remain > ShooterDist + 0.01f)
            {
                // скорость уменьшили на ходу — снаряд «ещё в стволе»
                shell.gameObject.SetActive(false);
                shellPrevValid = false;
                return;
            }
            shell.gameObject.SetActive(true);

            Vector3 from = shellPrevValid ? shellPrevPos : newPos - Vector3.right * 0.5f;
            float len = newPos.x - from.x + 0.45f;
            if (len > 0f && RaycastTarget(from, len, out var point))
            {
                Impact(point);
                return;
            }
            shell.position = newPos;
            shellPrevPos = newPos;
            shellPrevValid = true;
        }

        bool RaycastTarget(Vector3 from, float len, out Vector3 point)
        {
            Physics.SyncTransforms();
            point = default;
            float best = float.MaxValue;
            foreach (var h in Physics.RaycastAll(from, Vector3.right, len))
            {
                if (!h.collider.transform.IsChildOf(target)) continue;
                if (h.distance < best) { best = h.distance; point = h.point; }
            }
            return best < float.MaxValue;
        }

        void Impact(Vector3 point)
        {
            hit = true;
            phase = Phase.After;
            realSinceHit = 0f;
            hitPointWorld = point;
            tankPosAtHit = tankPos;

            // снаряд застревает в броне
            shell.gameObject.SetActive(true);
            shellTrail.emitting = false;
            shell.SetPositionAndRotation(point + Vector3.right * 0.25f, Quaternion.Euler(0, 90, 0));
            shell.SetParent(target, true);

            glow.SetParent(target, false);
            glow.position = point;
            glow.gameObject.SetActive(true);

            float heat = Heat();
            var em = sparks.emission;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.RoundToInt(10 + 400 * heat)) });
            foreach (var ps in new[] { sparks, sparksHot, hitSmoke })
            {
                ps.transform.position = point - Vector3.right * 0.3f;
                if (heat > 0.02f) ps.Play();
            }
            UpdateGlow();
        }

        /// <summary>Нагрев 0..1 по выделившейся теплоте (логарифмическая шкала: 1 кДж → 0.35, 2 МДж → 1).</summary>
        float Heat() => Mathf.Clamp01((float)Math.Log10(Math.Max(res.q, 1.0)) / 6.3f);

        void UpdateGlow()
        {
            if (!glow.gameObject.activeSelf) return;
            float heat = Heat();
            float cool = Mathf.Exp(-Mathf.Max(0f, t - THit) / 3f);     // остывание
            float k = heat * (0.35f + 0.65f * cool);
            var hot = Color.Lerp(new Color(0.5f, 0.05f, 0f), new Color(1f, 0.9f, 0.6f), cool * heat);
            glowMat.color = Color.Lerp(new Color(0.12f, 0.1f, 0.09f), hot, k);
            glow.localScale = Vector3.one * Mathf.Lerp(0.2f, 1.2f, heat);
            glowLight.color = hot;
            glowLight.intensity = 12f * k * cool;
        }

        // ---------------------------------------------------------------------
        void UpdateLines(Vector3 v2vec)
        {
            // траектория снаряда вдоль OX
            if (fired)
            {
                shellPath.positionCount = 2;
                shellPath.SetPosition(0, new Vector3(-ShooterDist, 0.07f, 0f));
                float endX = hit ? hitPointWorld.x : shell.position.x;
                shellPath.SetPosition(1, new Vector3(endX, 0.07f, 0f));
            }

            // след танка
            Vector3 p = new Vector3(tankPos.x, 0.06f, tankPos.z);
            if (trailPts.Count == 0 || (trailPts[trailPts.Count - 1] - p).sqrMagnitude > 0.25f)
            {
                if (trailPts.Count < 6000) trailPts.Add(p);
                trail.positionCount = trailPts.Count;
                trail.SetPositions(trailPts.ToArray());
            }

            // где был бы танк без удара
            if (hit)
            {
                ghost.positionCount = 2;
                Vector3 a = new Vector3(tankPosAtHit.x, 0.08f, tankPosAtHit.z);
                ghost.SetPosition(0, a);
                ghost.SetPosition(1, a + v2vec * Mathf.Max(0f, t - THit));
            }
        }

        void UpdateVectors(Vector3 v2vec)
        {
            labels.Clear();
            if (!showVectors)
            {
                foreach (var a in new[] { arV1, arV2, arP1, arP2, arP }) a.Hide();
                return;
            }

            // скорость снаряда
            if (fired && !hit && shell.gameObject.activeSelf)
            {
                Vector3 s0 = shell.position + Vector3.up * 1.2f;
                arV1.Set(s0, s0 + Vector3.right * 8f, 0.18f);
                labels.Add((s0 + Vector3.right * 8.5f, $"V1 = {N(prm.v1)} м/с", CP1));
            }
            else arV1.Hide();

            // скорость танка (до удара V2, после — u)
            Vector3 top = tankPos + Vector3.up * 3.6f;
            if (!hit)
            {
                Vector3 v = Vector3.ClampMagnitude(v2vec, 40f);
                arV2.SetColor(CP2);
                arV2.Set(top, top + v, 0.25f);
                labels.Add((top + v + Vector3.up * 0.8f, $"V2 = {N(prm.v2, 1)} м/с", CP2));
            }
            else
            {
                Vector3 u = Vector3.ClampMagnitude(new Vector3((float)res.ux, 0, (float)res.uy), 40f);
                arV2.SetColor(CP);
                arV2.Set(top, top + u, 0.25f);
                labels.Add((top + u + Vector3.up * 0.8f, $"u = {N(res.u, 2)} м/с", CP));
            }

            // треугольник импульсов на земле в начале координат: p = p1 + p2
            double maxP = Math.Max(res.p, Math.Max(res.p1, res.p2));
            float k = maxP > 0 ? (float)(35.0 / maxP) : 0f;
            Vector3 o = new Vector3(0, 0.15f, 0);
            Vector3 p1End = o + Vector3.right * (float)res.p1 * k;
            Vector3 p2End = p1End + new Vector3((float)res.p2x, 0, (float)res.p2y) * k;
            Vector3 pEnd = o + new Vector3((float)res.px, 0, (float)res.py) * k;
            arP1.Set(o, p1End, 0.35f);
            arP2.Set(p1End, p2End, 0.35f);
            arP.Set(o, pEnd, 0.35f);
            labels.Add(((o + p1End) * 0.5f - Vector3.forward * 2.5f, "p1 = m1·V1", CP1));
            labels.Add(((p1End + p2End) * 0.5f + Vector3.right * 2.5f, "p2 = m2·V2", CP2));
            labels.Add(((o + pEnd) * 0.5f - Vector3.right * 3f, "p = (m1+m2)·u", CP));
            labels.Add((new Vector3(60, 0.1f, -3), "X", new Color(1f, 0.5f, 0.45f)));
            labels.Add((new Vector3(-3, 0.1f, 60), "Y", new Color(0.5f, 1f, 0.55f)));
        }

        // =====================================================================
        void HandleKeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.spaceKey.wasPressedThisFrame) TogglePause();
            if (kb.rKey.wasPressedThisFrame) Restart();
            if (kb.digit1Key.wasPressedThisFrame) SetCamera(1);
            if (kb.digit2Key.wasPressedThisFrame) SetCamera(2);
            if (kb.digit3Key.wasPressedThisFrame) SetCamera(3);
            if (kb.vKey.wasPressedThisFrame) showVectors = !showVectors;
            if (kb.hKey.wasPressedThisFrame) showUI = !showUI;
        }

        float savedSpeed = 1f;
        bool Paused => playSpeed <= 0f;

        void TogglePause()
        {
            if (Paused) playSpeed = savedSpeed;
            else { savedSpeed = playSpeed; playSpeed = 0f; }
        }

        void SetCamera(int m)
        {
            cameraMode = m;
            if (m == 1) { cam.yaw = -135f; cam.pitch = 22f; cam.distance = 38f; }
            if (m == 2) { cam.yaw = 0f; cam.pitch = 89.5f; cam.distance = 110f; }
        }

        void ApplyPreset(float m1, float v1, float m2, float v2, float alpha)
        {
            prm.m1 = m1; prm.v1 = v1; prm.m2 = m2; prm.v2 = v2; prm.alphaDeg = alpha;
            Restart();
        }

        bool PointerOverUI()
        {
            if (!showUI || Mouse.current == null) return false;
            Vector2 m = Mouse.current.position.ReadValue();
            var g = new Vector2(m.x, Screen.height - m.y) / uiScale;
            return leftRect.Contains(g) || rightRect.Contains(g);
        }

        // =====================================================================
        static string N(double v, int dec = 0) => v.ToString("N" + dec, Ru);

        static string E(double j)
        {
            double a = Math.Abs(j);
            if (a >= 1e6) return N(j / 1e6, 3) + " МДж";
            if (a >= 1e3) return N(j / 1e3, 2) + " кДж";
            return N(j, 1) + " Дж";
        }

        void InitStyles()
        {
            if (sLabel != null) return;
            texPanel = new Texture2D(1, 1);
            texPanel.SetPixel(0, 0, new Color(0.06f, 0.07f, 0.08f, 0.82f));
            texPanel.Apply();

            sBox = new GUIStyle(GUI.skin.box) { padding = new RectOffset(14, 14, 10, 10) };
            sBox.normal.background = texPanel;
            sLabel = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true, wordWrap = true };
            sLabel.normal.textColor = new Color(0.92f, 0.92f, 0.92f);
            sSmall = new GUIStyle(sLabel) { fontSize = 13 };
            sSmall.normal.textColor = new Color(0.7f, 0.72f, 0.75f);
            sTitle = new GUIStyle(sLabel) { fontSize = 18, fontStyle = FontStyle.Bold };
            sTitle.normal.textColor = new Color(1f, 0.85f, 0.5f);
            sBanner = new GUIStyle(sLabel) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            sBanner.normal.textColor = Color.white;
            sWorld = new GUIStyle(sLabel) { fontSize = 15, fontStyle = FontStyle.Bold, wordWrap = false };
            sButton = new GUIStyle(GUI.skin.button) { fontSize = 14 };
            sToggle = new GUIStyle(GUI.skin.toggle) { fontSize = 14 };
            sToggle.normal.textColor = sToggle.onNormal.textColor = new Color(0.9f, 0.9f, 0.9f);
        }

        float Slider(string label, float value, float min, float max, string valueText, bool log = false)
        {
            GUILayout.Label($"{label}: <b><color=#ffd27a>{valueText}</color></b>", sLabel);
            float v;
            if (log)
                v = Mathf.Pow(10f, GUILayout.HorizontalSlider(Mathf.Log10(Mathf.Max(value, min)), Mathf.Log10(min), Mathf.Log10(max)));
            else
                v = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.Space(4);
            return v;
        }

        void OnGUI()
        {
            InitStyles();
            uiScale = Mathf.Clamp(Screen.height / 1000f, 0.75f, 2.5f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));
            float W = Screen.width / uiScale, H = Screen.height / uiScale;

            // подписи векторов в 3D
            var c = Camera.main;
            if (c != null)
            {
                foreach (var (pos, text, color) in labels)
                {
                    Vector3 sp = c.WorldToScreenPoint(pos);
                    if (sp.z <= 0) continue;
                    var gp = new Vector2(sp.x, Screen.height - sp.y) / uiScale;
                    sWorld.normal.textColor = Color.black;
                    GUI.Label(new Rect(gp.x - 99, gp.y - 11, 300, 24), text, sWorld);
                    sWorld.normal.textColor = color;
                    GUI.Label(new Rect(gp.x - 100, gp.y - 12, 300, 24), text, sWorld);
                }
            }

            // баннер фазы
            string banner = phase switch
            {
                Phase.Approach => "Танк m2 едет к точке встречи",
                Phase.Flight => $"ВЫСТРЕЛ!  замедление ×{N(1f / Mathf.Max(flightScale, 1e-4f))}",
                _ => $"ПОПАДАНИЕ: снаряд застрял, выделилось Q = {E(res.q)}"
            };
            if (Paused) banner = "ПАУЗА — " + banner;
            GUI.Label(new Rect(W * 0.5f - 400, 8, 800, 36), banner, sBanner);

            if (!showUI)
            {
                GUI.Label(new Rect(10, H - 28, 500, 24), "H — показать панели", sSmall);
                leftRect = rightRect = Rect.zero;
                return;
            }

            DrawLeftPanel(H);
            DrawRightPanel(W, H);

            GUI.Label(new Rect(410, H - 28, W - 830, 24),
                "Мышь — вращать камеру, колесо — зум · 1/2/3 — камеры · Пробел — пауза · R — заново · V — векторы · H — скрыть панели",
                sSmall);
        }

        void DrawLeftPanel(float H)
        {
            leftRect = new Rect(10, 10, 390, Mathf.Min(H - 20, 860));
            GUILayout.BeginArea(leftRect, sBox);
            GUILayout.Label("Параметры (меняются на ходу)", sTitle);
            GUILayout.Space(6);

            GUILayout.Label("<b>Снаряд</b> — летит вдоль OX", sSmall);
            prm.m1 = Slider("Масса m1", prm.m1, 0.5f, 5000f, N(prm.m1, prm.m1 < 10 ? 1 : 0) + " кг", true);
            prm.v1 = Slider("Скорость V1", prm.v1, 50f, 1500f, N(prm.v1) + " м/с");

            GUILayout.Label("<b>Танк</b> — едет под углом α к OX", sSmall);
            prm.m2 = Slider("Масса m2", prm.m2, 500f, 70000f, N(prm.m2) + " кг", true);
            prm.v2 = Slider("Скорость V2", prm.v2, 0f, 25f, $"{N(prm.v2, 1)} м/с ({N(prm.v2 * 3.6f)} км/ч)");
            prm.alphaDeg = Slider("Угол α", prm.alphaDeg, 0f, 180f, N(prm.alphaDeg) + "°");

            GUILayout.Space(4);
            GUILayout.Label("<b>Воспроизведение</b>", sSmall);
            if (!Paused) playSpeed = Slider("Скорость анимации", playSpeed, 0.1f, 2f, "×" + N(playSpeed, 2));
            else GUILayout.Label("Скорость анимации: <b>пауза</b>", sLabel);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Paused ? "Продолжить" : "Пауза", sButton, GUILayout.Height(28))) TogglePause();
            if (GUILayout.Button("Заново", sButton, GUILayout.Height(28))) Restart();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            autoRepeat = GUILayout.Toggle(autoRepeat, " автоповтор", sToggle);
            showVectors = GUILayout.Toggle(showVectors, " векторы", sToggle);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("<b>Камера</b>", sSmall);
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(cameraMode == 1, "Обзор", sButton) && cameraMode != 1) SetCamera(1);
            if (GUILayout.Toggle(cameraMode == 2, "Сверху", sButton) && cameraMode != 2) SetCamera(2);
            if (GUILayout.Toggle(cameraMode == 3, "Кино", sButton) && cameraMode != 3) SetCamera(3);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("<b>Готовые случаи</b>", sSmall);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Реальный бой", sButton)) ApplyPreset(7f, 800f, 30000f, 10f, 60f);
            if (GUILayout.Button("Тяжёлый снаряд", sButton)) ApplyPreset(3000f, 400f, 30000f, 10f, 90f);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Вдогонку α=0°", sButton)) { prm.alphaDeg = 0f; Restart(); }
            if (GUILayout.Button("Сбоку 90°", sButton)) { prm.alphaDeg = 90f; Restart(); }
            if (GUILayout.Button("Лоб 180°", sButton)) { prm.alphaDeg = 180f; Restart(); }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void DrawRightPanel(float W, float H)
        {
            rightRect = new Rect(W - 420, 10, 410, Mathf.Min(H - 20, 760));
            GUILayout.BeginArea(rightRect, sBox);
            GUILayout.Label("Расчёт (закон сохранения импульса)", sTitle);
            GUILayout.Space(4);

            GUILayout.Label("<b>До удара</b>", sSmall);
            GUILayout.Label($"p1 = m1·V1 = <b>{N(res.p1)}</b> кг·м/с", sLabel);
            GUILayout.Label($"p2 = m2·V2 = <b>{N(res.p2)}</b> кг·м/с", sLabel);

            GUILayout.Space(4);
            GUILayout.Label("<b>Импульс системы после удара</b>", sSmall);
            GUILayout.Label($"px = m1V1 + m2V2·cosα = <b>{N(res.px)}</b>", sLabel);
            GUILayout.Label($"py = m2V2·sinα = <b>{N(res.py)}</b>", sLabel);
            GUILayout.Label($"p = √(px² + py²) = <b><color=#7dff8e>{N(res.p)}</color></b> кг·м/с", sLabel);
            GUILayout.Label($"β = arctg(py/px) = <b>{N(res.betaDeg, 2)}°</b>", sLabel);

            GUILayout.Space(4);
            GUILayout.Label("<b>Скорость тела после удара</b>", sSmall);
            GUILayout.Label($"u = p / (m1 + m2) = <b>{N(res.u, 3)}</b> м/с", sLabel);
            GUILayout.Label($"Скорость танка изменилась на <b>{(res.dvTank >= 0 ? "+" : "")}{N(res.dvTank, 3)}</b> м/с, " +
                            $"направление — на <b>{N(res.turnDeg, 2)}°</b>", sLabel);

            GUILayout.Space(4);
            GUILayout.Label("<b>Энергия и теплота</b>", sSmall);
            GUILayout.Label($"E до = m1V1²/2 + m2V2²/2 = <b>{E(res.eBefore)}</b>", sLabel);
            GUILayout.Label($"E после = p²/(2(m1+m2)) = <b>{E(res.eAfter)}</b>", sLabel);
            GUILayout.Label($"Q = E до − E после = <b><color=#ff9a6a>{E(res.q)}</color></b>", sLabel);
            GUILayout.Label($"Q = m1m2(V1²+V2²−2V1V2cosα) / (2(m1+m2)) = <b>{E(res.qFormula)}</b>", sSmall);
            GUILayout.Label($"Q составляет <b>{N(res.qShare * 100, 1)} %</b> энергии снаряда", sLabel);

            GUILayout.Space(6);
            GUILayout.Label($"Время модели t = {N(t, 3)} с (удар при t = {N(THit)} с) · темп ×{N(timeScaleNow, 3)}", sSmall);
            GUILayout.Label("Белая линия — где был бы танк без попадания, чёрная — реальный след.", sSmall);
            GUILayout.EndArea();
        }
    }
}
