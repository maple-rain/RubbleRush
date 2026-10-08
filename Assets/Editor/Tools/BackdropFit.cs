using Demo.Data;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 세계의 크기를 <b>구역에 맞춘다</b> [2026-10-08].
    ///
    /// <b>왜 생겼는가.</b> 건물 종류를 둘 늘리자(t7_glass·t6_brick) 띠가 다섯에서 일곱이 되면서
    /// 구역이 z=76.7m까지 깊어졌다. 그런데 지면은 120x120에 원점 중심이라 z=60에서 끝나고
    /// 북쪽 벽도 거기 있었다 — <b>t5_solid 네 채가 벽 너머 허공에 섰다</b>.
    /// 눈으로는 안 보인다. 카메라가 거기까지 못 가기 때문이다.
    ///
    /// <b>원점 중심을 버린다.</b> 구역은 구멍(z=-10)에서 북쪽으로만 뻗으므로 대칭일 이유가 없다.
    /// 건물이 실제로 차지하는 상자를 재서 그 둘레에 벽을 두르고, 지면은 거기서
    /// <b>카메라가 닿는 거리만큼</b> 더 넓힌다 — 그래야 벽 앞에 섰을 때 지면의 끝이 안 보인다.
    ///
    /// 띠 구성이 또 바뀌면 다시 누르면 된다. 숫자를 손으로 적어 둔 곳이 없다 (CLAUDE.md 규칙 7).
    /// </summary>
    public static class BackdropFit
    {
        const string ZonePath = "Assets/_Project/Data/Zones/Zone_TEMP_Test.asset";

        /// <summary>벽을 건물에서 이만큼 떨어뜨린다. 바깥 건물을 돌아 끌고 나올 통로다.</summary>
        const float WallMargin = 8f;

        const float WallHeight = 6f;
        const float WallThick = 1f;

        /// <summary>
        /// 건물과 구멍이 실제로 차지하는 바닥 상자. y는 안 쓴다.
        ///
        /// <b>yaw를 무시하고 긴 변으로 잰다.</b> 배치에 90도로 돌린 것이 섞여 있어
        /// 가로/깊이 중 어느 쪽이 x를 먹는지 한 채씩 따지지 않으면 모른다 — 넉넉한 쪽으로 잡는다.
        /// </summary>
        public static bool Zone(out Bounds box)
        {
            box = new Bounds();
            var zone = AssetDatabase.LoadAssetAtPath<ZoneData>(ZonePath);
            if (zone == null || zone.structures == null || zone.structures.Length == 0) return false;

            bool first = true;
            foreach (ZoneData.Placement p in zone.structures)
            {
                Vector3 s = p.data != null ? p.data.colliderSize : Vector3.one;
                float half = Mathf.Max(s.x, s.z) * 0.5f;
                var one = new Bounds(new Vector3(p.position.x, 0f, p.position.z),
                                     new Vector3(half * 2f, 0f, half * 2f));
                if (first) { box = one; first = false; } else box.Encapsulate(one);
            }

            // 구멍도 세계 안에 있어야 한다. 가장 큰 단계의 반지름으로 잡는다.
            float hole = 4f;
            var cfg = Object.FindFirstObjectByType<Core.GameConfigRef>();
            if (cfg != null)
            {
                var conf = new SerializedObject(cfg).FindProperty("config").objectReferenceValue as GameConfig;
                if (conf != null && conf.holeLevels != null)
                    foreach (GameConfig.HoleLevel h in conf.holeLevels) hole = Mathf.Max(hole, h.radius);
            }
            box.Encapsulate(new Bounds(zone.holePosition, new Vector3(hole * 2f, 0f, hole * 2f)));
            return true;
        }

        /// <summary>지면과 네 벽을 구역에 맞춘다. 한 일을 글로 돌려준다.</summary>
        public static string Run(BackdropView view)
        {
            var log = new System.Text.StringBuilder();
            log.AppendLine("[세계 크기] 구역에 맞춘다");

            if (!Zone(out Bounds box))
            {
                log.AppendLine("  구역을 못 읽었다 — 건너뜀");
                return log.ToString();
            }

            var ground = GameObject.Find("Ground");
            if (ground == null) { log.AppendLine("  Ground 없음 — 건너뜀"); return log.ToString(); }

            // 벽은 건물 둘레에서 조금 떨어진 자리, 지면은 거기서 카메라가 닿는 만큼 더.
            float wallX = box.extents.x + WallMargin;
            float wallZ = box.extents.z + WallMargin;
            float reach = Mathf.Max(10f, view.Valid ? view.Reach : 30f);

            Vector3 mid = new Vector3(box.center.x, 0f, box.center.z);
            float groundX = (wallX + reach) * 2f;
            float groundZ = (wallZ + reach) * 2f;

            log.AppendLine("  건물이 차지하는 범위  x " + (box.center.x - box.extents.x).ToString("F0")
                           + "~" + (box.center.x + box.extents.x).ToString("F0")
                           + "   z " + (box.center.z - box.extents.z).ToString("F0")
                           + "~" + (box.center.z + box.extents.z).ToString("F0"));

            log.AppendLine("  지면 " + ground.transform.localScale.x.ToString("F0") + "x"
                           + ground.transform.localScale.z.ToString("F0") + "m @ z"
                           + ground.transform.position.z.ToString("F0")
                           + "  ->  " + groundX.ToString("F0") + "x" + groundZ.ToString("F0")
                           + "m @ z" + mid.z.ToString("F0"));

            Undo.RecordObject(ground.transform, "지면 크기");
            ground.transform.position = new Vector3(mid.x, ground.transform.position.y, mid.z);
            ground.transform.localScale = new Vector3(groundX, ground.transform.localScale.y, groundZ);

            int moved = Wall("Wall_N", mid, wallX, wallZ, log) ? 1 : 0;
            moved += Wall("Wall_S", mid, wallX, wallZ, log) ? 1 : 0;
            moved += Wall("Wall_E", mid, wallX, wallZ, log) ? 1 : 0;
            moved += Wall("Wall_W", mid, wallX, wallZ, log) ? 1 : 0;

            log.AppendLine("  벽 " + moved + "면을 건물에서 " + WallMargin.ToString("F0") + "m 밖에 둘렀다");
            log.AppendLine("  지면은 벽보다 " + reach.ToString("F0") + "m 더 넓다 — 벽 앞에 서도 지면의 끝이 안 보인다");
            return log.ToString();
        }

        static bool Wall(string name, Vector3 mid, float x, float z, System.Text.StringBuilder log)
        {
            var go = GameObject.Find(name);
            if (go == null) { log.AppendLine("  " + name + " 없음"); return false; }

            Vector3 pos, scale;
            switch (name)
            {
                case "Wall_N": pos = new Vector3(mid.x, WallHeight * 0.5f, mid.z + z);
                               scale = new Vector3(x * 2f, WallHeight, WallThick); break;
                case "Wall_S": pos = new Vector3(mid.x, WallHeight * 0.5f, mid.z - z);
                               scale = new Vector3(x * 2f, WallHeight, WallThick); break;
                case "Wall_E": pos = new Vector3(mid.x + x, WallHeight * 0.5f, mid.z);
                               scale = new Vector3(WallThick, WallHeight, z * 2f); break;
                default:       pos = new Vector3(mid.x - x, WallHeight * 0.5f, mid.z);
                               scale = new Vector3(WallThick, WallHeight, z * 2f); break;
            }

            Undo.RecordObject(go.transform, "경계 벽");
            go.transform.position = pos;
            go.transform.localScale = scale;
            return true;
        }
    }
}
