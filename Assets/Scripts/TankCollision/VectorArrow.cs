using UnityEngine;

namespace TankCollision
{
    /// <summary>Объёмная стрелка-вектор: стержень (LineRenderer) + конус-наконечник.</summary>
    public class VectorArrow : MonoBehaviour
    {
        LineRenderer shaft;
        Transform head;
        Material mat;
        static Mesh coneMesh;

        public static VectorArrow Create(string name, Color color, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent) go.transform.SetParent(parent, false);
            var a = go.AddComponent<VectorArrow>();
            a.Init(color);
            return a;
        }

        void Init(Color color)
        {
            mat = SceneFactory.Unlit(color);
            shaft = gameObject.AddComponent<LineRenderer>();
            shaft.sharedMaterial = mat;
            shaft.positionCount = 2;
            shaft.useWorldSpace = true;
            shaft.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var h = new GameObject("Head");
            h.transform.SetParent(transform, false);
            h.AddComponent<MeshFilter>().sharedMesh = Cone();
            var mr = h.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            head = h.transform;
        }

        public void SetColor(Color c) => mat.color = c;

        /// <summary>Рисует стрелку от from до to. width — толщина стержня, м.</summary>
        public void Set(Vector3 from, Vector3 to, float width)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            bool visible = len > 1e-4f;
            shaft.enabled = visible;
            head.gameObject.SetActive(visible);
            if (!visible) return;

            float headLen = Mathf.Min(width * 4f, len * 0.6f);
            Vector3 dir = d / len;
            Vector3 headBase = to - dir * headLen;
            shaft.widthMultiplier = width;
            shaft.SetPosition(0, from);
            shaft.SetPosition(1, headBase);

            head.position = headBase;
            head.rotation = Quaternion.LookRotation(dir, Mathf.Abs(dir.y) > 0.99f ? Vector3.forward : Vector3.up);
            float r = width * 1.4f;
            head.localScale = new Vector3(r, r, headLen);
        }

        public void Hide()
        {
            if (shaft) shaft.enabled = false;
            if (head) head.gameObject.SetActive(false);
        }

        /// <summary>Конус единичного радиуса и длины, вершина в +Z.</summary>
        static Mesh Cone()
        {
            if (coneMesh) return coneMesh;
            const int n = 16;
            var v = new Vector3[n + 2];
            var tris = new int[n * 6];
            v[n] = new Vector3(0, 0, 1);  // вершина
            v[n + 1] = Vector3.zero;      // центр основания
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                v[i] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                // порядок вершин — по часовой стрелке снаружи (лицевая сторона в Unity)
                tris[i * 6 + 0] = i; tris[i * 6 + 1] = j; tris[i * 6 + 2] = n;
                tris[i * 6 + 3] = i; tris[i * 6 + 4] = n + 1; tris[i * 6 + 5] = j;
            }
            coneMesh = new Mesh { name = "ArrowCone", vertices = v, triangles = tris };
            coneMesh.RecalculateNormals();
            return coneMesh;
        }
    }
}
