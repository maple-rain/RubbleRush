using System.Collections.Generic;
using UnityEngine;

namespace Demo.Collection
{
    /// <summary>
    /// 케이블 궤적이 스스로 교차해 고리가 닫혔는지 본다.
    ///
    /// 판정은 바닥 평면 기준 2D다 (GAME_DESIGN §5.1). 월드 XZ를 Vector2(x, z)로 쓴다.
    /// 층이 나뉘면 층별로 독립 처리하므로 여기서 높이를 볼 일이 없다.
    ///
    /// 순수 함수만 둔다 — Unity 없이도 테스트할 수 있어야 한다.
    /// </summary>
    public static class LoopDetector
    {
        const float Epsilon = 1e-8f;

        /// <summary>
        /// 가장 최근 선분이 과거 궤적과 교차하는지 본다.
        /// </summary>
        /// <param name="pts">궤적 포인트. 마지막이 현재 위치</param>
        /// <param name="startIndex">교차당한 과거 선분의 시작 인덱스</param>
        /// <param name="hit">교차점</param>
        public static bool TryFindClosure(IReadOnlyList<Vector2> pts, out int startIndex, out Vector2 hit)
        {
            startIndex = -1;
            hit = default;

            int n = pts.Count;
            if (n < 4) return false;

            Vector2 head0 = pts[n - 2];
            Vector2 head1 = pts[n - 1];

            // i는 n-4까지만. [n-3, n-2]는 새 선분과 끝점을 공유해서 항상 교차로 잡힌다.
            //
            // 여러 개와 동시에 교차하면 가장 오래된 것을 고른다 = 가장 큰 고리.
            // 플레이어에게 유리한 쪽이며, 반대로 하려면 아래 루프를 역순으로 돌리면 된다.
            // 포인트 간격이 촘촘하면 거의 일어나지 않는 상황이다.
            for (int i = 0; i <= n - 4; i++)
            {
                if (SegmentIntersect(head0, head1, pts[i], pts[i + 1], out hit))
                {
                    startIndex = i;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 닫힌 고리의 폴리곤을 만든다. TryFindClosure가 true를 준 뒤에만 부른다.
        ///
        /// 고리는 교차점에서 시작해 궤적을 따라 머리 직전까지 간다.
        /// 머리(pts[n-1])는 교차점을 이미 지나쳐서 고리 바깥이므로 넣지 않는다.
        /// </summary>
        public static void BuildPolygon(IReadOnlyList<Vector2> pts, int startIndex, Vector2 hit,
                                        List<Vector2> outPolygon)
        {
            outPolygon.Clear();
            outPolygon.Add(hit);
            for (int i = startIndex + 1; i <= pts.Count - 2; i++)
                outPolygon.Add(pts[i]);
        }

        /// <summary>
        /// 두 선분이 실제로 가로지르는지. 평행·공선은 교차로 보지 않는다.
        /// </summary>
        public static bool SegmentIntersect(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1,
                                            out Vector2 hit)
        {
            hit = default;

            Vector2 r = a1 - a0;
            Vector2 s = b1 - b0;

            float denom = Cross(r, s);
            if (Mathf.Abs(denom) < Epsilon) return false;   // 평행하거나 한 직선 위

            Vector2 d = b0 - a0;
            float t = Cross(d, s) / denom;
            float u = Cross(d, r) / denom;

            if (t < 0f || t > 1f || u < 0f || u > 1f) return false;

            hit = a0 + t * r;
            return true;
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        /// <summary>월드 좌표를 바닥 평면 좌표로. 높이는 버린다.</summary>
        public static Vector2 Flatten(Vector3 world) => new Vector2(world.x, world.z);
    }
}
