using UnityEngine;
using UnityEngine.AI;

namespace Chase
{
    // 게임 시작/진행 중 랜덤 생성되는 오브젝트(금고, 단서, 아이템, 감옥, 탈출구)를 로우폴리 프리미티브로 조립
    public class WorldSpawner : MonoBehaviour
    {
        public Material safeBody;
        public Material safeFull;
        public Material safeEmpty;
        public Material clue;
        public Material drink;
        public Material prisonFrame;
        public Material prisonFloor;
        public Material alarm;
        public Material exitFrame;
        public Material exitBeam;

        public Safe SpawnSafe(Vector3 pos, int money)
        {
            var root = new GameObject("Safe");
            root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, Random.Range(0, 4) * 90f, 0f));
            Prim.Make(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.6f, 0f), new Vector3(1.1f, 1.2f, 1f), safeBody, true);
            var door = Prim.Make(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.65f, 0.5f), new Vector3(0.75f, 0.75f, 0.06f), safeFull, false);
            Prim.Make(PrimitiveType.Cylinder, root.transform, new Vector3(0.18f, 0.65f, 0.55f), new Vector3(0.18f, 0.03f, 0.18f), safeBody, false)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var obstacle = root.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = new Vector3(0f, 0.6f, 0f);
            obstacle.size = new Vector3(1.1f, 1.2f, 1f);
            obstacle.carving = true;

            var safe = root.AddComponent<Safe>();
            safe.Init(money, door.GetComponent<Renderer>(), safeFull, safeEmpty);
            return safe;
        }

        public void SpawnClue(Vector3 pos, int digitIndex)
        {
            var root = new GameObject($"Clue_{digitIndex}");
            root.transform.position = pos;
            var paper = Prim.Make(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.7f, 0f), new Vector3(0.5f, 0.04f, 0.36f), clue, false);
            paper.transform.localRotation = Quaternion.Euler(25f, 0f, 10f);
            Prim.Make(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.02f, 0f), new Vector3(0.9f, 0.02f, 0.9f), clue, false);
            root.AddComponent<ClueNote>().digitIndex = digitIndex;
        }

        public void SpawnDrink(Vector3 pos)
        {
            var root = new GameObject("EnergyDrink");
            root.transform.position = pos;
            Prim.Make(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.55f, 0f), new Vector3(0.28f, 0.25f, 0.28f), drink, false);
            Prim.Make(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.02f, 0f), new Vector3(0.7f, 0.02f, 0.7f), drink, false)
                .transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            root.AddComponent<EnergyDrink>();
        }

        public Prison SpawnPrison(Vector3 pos)
        {
            var root = new GameObject("Prison");
            root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, Random.Range(0, 4) * 90f, 0f));
            const float half = 2f;

            Prim.Make(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.03f, 0f), new Vector3(half * 2f, 0.06f, half * 2f), prisonFloor, false);

            // 창살 (시각용) + 벽 콜라이더 (갇힌 플레이어가 못 나가게)
            for (int side = 0; side < 4; side++)
            {
                var wall = new GameObject($"Wall_{side}");
                wall.transform.SetParent(root.transform, false);
                wall.transform.localRotation = Quaternion.Euler(0f, side * 90f, 0f);
                wall.transform.localPosition = wall.transform.localRotation * new Vector3(0f, 1.2f, half);
                var col = wall.AddComponent<BoxCollider>();
                col.size = new Vector3(half * 2f, 2.4f, 0.15f);
                for (int i = 0; i <= 8; i++)
                {
                    float x = -half + i * (half * 2f / 8f);
                    Prim.Make(PrimitiveType.Cube, wall.transform, new Vector3(x, 0f, 0f), new Vector3(0.1f, 2.4f, 0.1f), prisonFrame, false);
                }
                Prim.Make(PrimitiveType.Cube, wall.transform, new Vector3(0f, 1.2f, 0f), new Vector3(half * 2f + 0.1f, 0.15f, 0.15f), prisonFrame, false);
            }
            Prim.Make(PrimitiveType.Cube, root.transform, new Vector3(0f, 2.55f, 0f), new Vector3(0.5f, 0.3f, 0.5f), alarm, false);

            var obstacle = root.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = new Vector3(0f, 1.2f, 0f);
            obstacle.size = new Vector3(half * 2f + 0.2f, 2.4f, half * 2f + 0.2f);
            obstacle.carving = true;

            var light = new GameObject("AlarmLight").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.transform.localPosition = new Vector3(0f, 3f, 0f);
            light.type = LightType.Point;
            light.color = new Color(1f, 0.25f, 0.2f);
            light.range = 7f;
            light.intensity = 6f;

            var prison = root.AddComponent<Prison>();
            prison.range = 3.3f;
            return prison;
        }

        public ExitPoint SpawnExit(Vector3 pos)
        {
            var root = new GameObject("Exit");
            root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, Random.Range(0, 4) * 90f, 0f));
            Prim.Make(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.03f, 0f), new Vector3(2.6f, 0.03f, 2.6f), exitFrame, false);
            Prim.Make(PrimitiveType.Cube, root.transform, new Vector3(-0.9f, 1.2f, 0f), new Vector3(0.25f, 2.4f, 0.25f), exitFrame, false);
            Prim.Make(PrimitiveType.Cube, root.transform, new Vector3(0.9f, 1.2f, 0f), new Vector3(0.25f, 2.4f, 0.25f), exitFrame, false);
            Prim.Make(PrimitiveType.Cube, root.transform, new Vector3(0f, 2.45f, 0f), new Vector3(2.05f, 0.25f, 0.25f), exitFrame, false);
            Prim.Make(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 12f, 0f), new Vector3(0.35f, 12f, 0.35f), exitBeam, false);

            var light = new GameObject("ExitLight").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.transform.localPosition = new Vector3(0f, 2f, 0f);
            light.type = LightType.Point;
            light.color = new Color(0.3f, 1f, 0.5f);
            light.range = 8f;
            light.intensity = 8f;

            var exit = root.AddComponent<ExitPoint>();
            exit.range = 2.4f;
            return exit;
        }
    }

    public static class Prim
    {
        public static GameObject Make(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, Material mat, bool collider)
        {
            var go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider)
            {
                var c = go.GetComponent<Collider>();
                if (Application.isPlaying) Object.Destroy(c);
                else Object.DestroyImmediate(c);
            }
            return go;
        }
    }
}
