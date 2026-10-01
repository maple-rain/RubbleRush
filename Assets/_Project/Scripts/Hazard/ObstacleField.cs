using System.Collections.Generic;
using UnityEngine;

namespace Demo.Hazard
{
    /// <summary>
    /// 구역의 <b>움직이지 않는 장애물</b>을 격자에 구워 둔다 [지시 14번, 2026-10-01].
    ///
    /// <b>왜 격자인가.</b> 14번이 묻는 것은 잔해끼리가 아니라 <b>잔해가 건물·토대에
    /// 부딪혔을 때</b>다. 그래서 N체 문제가 아니다 — 한쪽(건물 30채)은 개수가 적고
    /// 한 판 내내 안 움직인다. 격자를 한 번 구우면 질의가 칸 하나만 보면 끝나고,
    /// 잔해가 500개여도 비용이 <b>개수에 선형</b>이다. 쌍별로 보면 12만 쌍이 된다.
    ///
    /// <b>왜 Physics.OverlapSphere가 아닌가.</b> 잔해 500개면 매 프레임 500번을 부르게 되는데,
    /// 그건 관리 코드에서 네이티브로 500번 넘어가는 것이고 결과 배열도 매번 나온다.
    /// 여기서는 넘어가지 않고 칸 하나를 훑는다. 실측은 <c>Tools/</c>의 측정 결과를 볼 것.
    ///
    /// <b>상태가 바뀌는 것은 콜라이더가 알려준다.</b> 건물이 잔해가 되면
    /// <see cref="Destruction.Destructible"/>이 BoxCollider를 끈다. 여기서는 그 플래그만 본다 —
    /// 따로 통지받지 않으므로 구역을 다시 짓기 전까지 <see cref="Rebuild"/>를 다시 부를 일이 없다.
    /// </summary>
    public static class ObstacleField
    {
        /// <summary>장애물 하나. BoxCollider를 바닥 평면 사각형으로 눌러 둔 것.</summary>
        struct Box
        {
            public float cx, cz;        // 중심 (월드)
            public float hx, hz;        // 반너비
            public float cos, sin;      // yaw
            public BoxCollider src;     // 켜져 있는지 보려고 들고 있는다
        }

        const float CellSize = 6f;      // 가장 큰 건물(12m)의 절반. 칸당 후보가 몇 개 안 남는다

        /// <summary>
        /// 장애물을 칸에 넣을 때 미리 부풀려 두는 여유(m).
        ///
        /// <b>왜 있는가</b> [고침 2026-10-01]. 처음에는 여유 없이 굽고 질의 때 3×3칸을 봤는데,
        /// 그러면 질의 한 번에 <b>Dictionary 조회가 아홉 번</b> 돈다. 실측에서 그게
        /// <c>Physics.OverlapSphere</c>보다 2.3배 느렸다 — 네이티브 BVH 한 번을 못 이긴다.
        /// 등록할 때 반경만큼 부풀려 두면 질의가 <b>한 칸</b>이면 끝난다.
        /// 굽는 것은 구역당 한 번이고 질의는 매 프레임 수백 번이니, 비용을 그쪽으로 옮기는 것이 맞다.
        ///
        /// 가장 큰 잔해 반경이 1.17m(T5Solid)라 그보다 넉넉하게 잡았다.
        /// 이보다 큰 반경으로 물어보면 알아서 3×3으로 되돌아간다.
        /// </summary>
        const float QueryMargin = 1.5f;

        static readonly List<Box> s_boxes = new List<Box>(64);
        static readonly Dictionary<int, List<int>> s_grid = new Dictionary<int, List<int>>(256);
        static bool s_ready;

        /// <summary>격자가 서 있는가. 안 서 있으면 모든 질의가 '안 막힘'을 돌려준다.</summary>
        public static bool Ready => s_ready;

        /// <summary>구운 장애물 수. 실측용.</summary>
        public static int Count => s_boxes.Count;

        /// <summary>
        /// 구역을 지은 직후 한 번 부른다. 씬의 모든 <see cref="BoxCollider"/>를 굽는다.
        /// </summary>
        public static void Rebuild(IEnumerable<BoxCollider> colliders)
        {
            Clear();
            if (colliders == null) return;

            foreach (BoxCollider c in colliders)
            {
                if (c == null) continue;

                Transform t = c.transform;
                Vector3 s = t.lossyScale;
                Vector3 world = t.TransformPoint(c.center);
                float yaw = t.eulerAngles.y * Mathf.Deg2Rad;

                var b = new Box
                {
                    cx = world.x,
                    cz = world.z,
                    hx = Mathf.Abs(c.size.x * s.x) * 0.5f,
                    hz = Mathf.Abs(c.size.z * s.z) * 0.5f,
                    cos = Mathf.Cos(yaw),
                    sin = Mathf.Sin(yaw),
                    src = c,
                };
                int id = s_boxes.Count;
                s_boxes.Add(b);

                // 회전한 사각형을 감싸는 축정렬 상자의 칸들에 전부 등록한다.
                // 모서리로 삐져나간 칸까지 넣는 편이, 질의 때 이웃 칸을 더 보는 것보다 싸다.
                // QueryMargin만큼 더 부풀려서 질의가 한 칸만 봐도 되게 한다.
                float r = Mathf.Sqrt(b.hx * b.hx + b.hz * b.hz) + QueryMargin;
                int x0 = Mathf.FloorToInt((b.cx - r) / CellSize), x1 = Mathf.FloorToInt((b.cx + r) / CellSize);
                int z0 = Mathf.FloorToInt((b.cz - r) / CellSize), z1 = Mathf.FloorToInt((b.cz + r) / CellSize);
                for (int x = x0; x <= x1; x++)
                    for (int z = z0; z <= z1; z++)
                    {
                        int key = Key(x, z);
                        if (!s_grid.TryGetValue(key, out List<int> cell))
                        {
                            cell = new List<int>(4);
                            s_grid[key] = cell;
                        }
                        cell.Add(id);
                    }
            }
            s_ready = s_boxes.Count > 0;
        }

        public static void Clear()
        {
            s_boxes.Clear();
            s_grid.Clear();
            s_ready = false;
        }

        static int Key(int x, int z) => (x * 73856093) ^ (z * 19349663);

        /// <summary>
        /// <paramref name="p"/>를 중심으로 한 반지름 <paramref name="radius"/>의 원이
        /// 켜져 있는 장애물과 겹치는가.
        /// </summary>
        /// <param name="push">[출력] 겹침을 푸는 최소 이동. 겹치지 않으면 0</param>
        /// <returns>겹치면 true</returns>
        public static bool Overlap(Vector3 p, float radius, out Vector3 push)
        {
            push = Vector3.zero;
            if (!s_ready) return false;

            int gx = Mathf.FloorToInt(p.x / CellSize), gz = Mathf.FloorToInt(p.z / CellSize);

            // 등록할 때 QueryMargin만큼 부풀려 뒀으므로 보통은 <b>자기 칸 하나</b>면 된다.
            // 그보다 큰 반경으로 물어보면 그때만 3×3으로 넓힌다.
            int span = radius <= QueryMargin ? 0 : 1;

            float best = 0f;
            for (int dx = -span; dx <= span; dx++)
                for (int dz = -span; dz <= span; dz++)
                {
                    if (!s_grid.TryGetValue(Key(gx + dx, gz + dz), out List<int> cell)) continue;
                    for (int i = 0; i < cell.Count; i++)
                    {
                        Box b = s_boxes[cell[i]];
                        if (b.src == null || !b.src.enabled) continue;   // 잔해가 된 건물은 안 막는다

                        // 상자의 로컬 좌표로 내려서 본다 — 회전한 사각형 판정이 축정렬이 된다
                        float dxw = p.x - b.cx, dzw = p.z - b.cz;
                        float lx = dxw * b.cos - dzw * b.sin;
                        float lz = dxw * b.sin + dzw * b.cos;

                        float qx = Mathf.Abs(lx) - b.hx;
                        float qz = Mathf.Abs(lz) - b.hz;
                        if (qx >= radius || qz >= radius) continue;

                        // 바깥이면 모서리까지의 거리, 안이면 가장 가까운 면까지
                        float depth;
                        float nx, nz;
                        if (qx > 0f || qz > 0f)
                        {
                            float ox = Mathf.Max(qx, 0f), oz = Mathf.Max(qz, 0f);
                            float d = Mathf.Sqrt(ox * ox + oz * oz);
                            if (d >= radius) continue;
                            depth = radius - d;
                            nx = (qx > 0f ? Mathf.Sign(lx) : 0f) * (d > 1e-5f ? ox / d : 0f);
                            nz = (qz > 0f ? Mathf.Sign(lz) : 0f) * (d > 1e-5f ? oz / d : 0f);
                            if (nx == 0f && nz == 0f) { nx = Mathf.Sign(lx); nz = 0f; }
                        }
                        else
                        {
                            // 완전히 안에 있다. 제일 가까운 면으로 민다
                            if (qx > qz) { depth = radius - qx; nx = Mathf.Sign(lx); nz = 0f; }
                            else { depth = radius - qz; nx = 0f; nz = Mathf.Sign(lz); }
                        }

                        if (depth <= best) continue;
                        best = depth;
                        // 로컬 법선을 월드로 되돌린다
                        push = new Vector3(nx * b.cos + nz * b.sin, 0f, -nx * b.sin + nz * b.cos) * depth;
                    }
                }
            return best > 0f;
        }
    }
}
