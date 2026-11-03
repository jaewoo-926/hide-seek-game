using UnityEngine;

namespace Chase.Online
{
    // 임시 스윙 이펙트: 공격 범위를 따라 호가 휙 지나가고 사라진다. 애니메이션이 생기면 교체.
    public class SwingEffect : MonoBehaviour
    {
        const int Segments = 24;
        const float Duration = 0.28f;
        const float SweepTime = 0.14f;
        const float TailLength = 0.7f; // 호 길이 대비 잔상 길이
        const float Height = 1.1f;

        static Shader shader;

        Mesh mesh;
        Material material;
        Color color;
        Color[] colors;
        float elapsed;

        public static void Play(Transform attacker, float radius, float halfAngle, Color color)
        {
            if (shader == null) shader = Resources.Load<Shader>("SwingEffect");
            if (shader == null)
            {
                Debug.LogError("[Chase] SwingEffect 셰이더를 찾을 수 없음");
                return;
            }
            var go = new GameObject("Swing Effect");
            go.transform.SetParent(attacker, false);
            go.transform.localPosition = Vector3.up * Height;
            go.AddComponent<SwingEffect>().Build(radius, halfAngle, color);
        }

        void Build(float radius, float halfAngle, Color swingColor)
        {
            color = swingColor;
            int count = (Segments + 1) * 2;
            var vertices = new Vector3[count];
            var triangles = new int[Segments * 6];
            colors = new Color[count];
            for (int i = 0; i <= Segments; i++)
            {
                float angle = Mathf.Lerp(halfAngle, -halfAngle, (float)i / Segments);
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                vertices[i * 2] = dir * (radius * 0.55f);
                vertices[i * 2 + 1] = dir * radius;
            }
            for (int i = 0; i < Segments; i++)
            {
                int v = i * 2, t = i * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 2;
                triangles[t + 4] = v + 1;
                triangles[t + 5] = v + 3;
            }

            mesh = new Mesh { name = "Swing Effect" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.colors = colors;
            mesh.RecalculateBounds();

            material = new Material(shader);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        void Update()
        {
            elapsed += Time.deltaTime;
            if (elapsed >= Duration)
            {
                Destroy(gameObject);
                return;
            }

            float head = elapsed / SweepTime;
            float fade = 1f - Mathf.InverseLerp(SweepTime, Duration, elapsed);
            for (int i = 0; i <= Segments; i++)
            {
                float u = (float)i / Segments;
                float behind = head - u;
                float a = behind < 0f ? 0f : Mathf.Clamp01(1f - behind / TailLength) * fade;
                var inner = color;
                inner.a = a * 0.15f; // 안쪽은 흐리게, 바깥쪽 날을 진하게
                var outer = color;
                outer.a = a;
                colors[i * 2] = inner;
                colors[i * 2 + 1] = outer;
            }
            mesh.colors = colors;
        }

        void OnDestroy()
        {
            if (mesh) Destroy(mesh);
            if (material) Destroy(material);
        }
    }
}
