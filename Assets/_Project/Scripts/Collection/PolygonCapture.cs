using System.Collections.Generic;
using UnityEngine;

namespace Demo.Collection
{
    /// <summary>
    /// 닫힌 고리 안에 무엇이 들어 있는지 판정한다.
    ///
    /// <b>폐합 순간에 1회만 부른다.</b> 잔해 80개 × 폴리곤 변 160개 = 12,800회 판정인데,
    /// 매 프레임 돌리면 모바일에서 죽고 한 번만 돌리면 공짜다 (TECH_SPEC §4).
    ///
    /// 순수 함수만 둔다.
    /// </summary>
    public static class PolygonCapture
    {
        /// <summary>
        /// 점이 폴리곤 내부인가. 수평 반직선과 변의 교차 횟수 홀짝(ray casting).
        /// 볼록·오목 상관없이 동작한다 — 케이블 궤적은 거의 항상 오목하다.
        /// </summary>
        public static bool Contains(IReadOnlyList<Vector2> polygon, Vector2 p)
        {
            int n = polygon.Count;
            if (n < 3) return false;

            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[j];

                // 변이 점의 수평선을 걸치는가. 한쪽 끝점만 포함시켜 꼭짓점 이중 계산을 막는다.
                if ((a.y > p.y) == (b.y > p.y)) continue;

                float x = (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x;
                if (p.x < x) inside = !inside;
            }

            return inside;
        }

        /// <summary>
        /// 폴리곤 면적(m²). 신발끈 공식. 감기 방향과 무관하게 항상 양수다.
        ///
        /// 제자리에서 케이블을 비볐을 때 생기는 바늘 모양 고리를 걸러내는 데 쓴다
        /// (GameConfig.cableMinLoopArea).
        /// </summary>
        public static float Area(IReadOnlyList<Vector2> polygon)
        {
            int n = polygon.Count;
            if (n < 3) return 0f;

            float sum = 0f;
            for (int i = 0, j = n - 1; i < n; j = i++)
                sum += (polygon[j].x + polygon[i].x) * (polygon[j].y - polygon[i].y);

            return Mathf.Abs(sum) * 0.5f;
        }

        /// <summary>
        /// 폴리곤 둘레(m). 고리를 그리는 데 쓴 케이블 길이다.
        /// </summary>
        public static float Perimeter(IReadOnlyList<Vector2> polygon)
        {
            int n = polygon.Count;
            if (n < 2) return 0f;

            float sum = 0f;
            for (int i = 0, j = n - 1; i < n; j = i++)
                sum += Vector2.Distance(polygon[j], polygon[i]);

            return sum;
        }

        /// <summary>
        /// 폴리곤의 XZ 바운딩 박스. 전체 순회 전에 명백히 바깥인 것을 걸러내는 데 쓴다.
        /// 잔해가 많을수록 이 사전 검사가 대부분을 쳐낸다.
        /// </summary>
        public static void Bounds(IReadOnlyList<Vector2> polygon, out Vector2 min, out Vector2 max)
        {
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);

            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 v = polygon[i];
                if (v.x < min.x) min.x = v.x;
                if (v.y < min.y) min.y = v.y;
                if (v.x > max.x) max.x = v.x;
                if (v.y > max.y) max.y = v.y;
            }
        }

        /// <summary>
        /// 고리 안의 잔해를 골라 outCaptured에 담는다. 폐합 순간 1회.
        /// 바운딩 박스로 먼저 쳐내고 남은 것만 ray casting한다.
        /// </summary>
        public static void SelectInside<T>(IReadOnlyList<Vector2> polygon,
                                           IReadOnlyList<T> candidates,
                                           System.Func<T, Vector2> positionOf,
                                           List<T> outCaptured)
        {
            outCaptured.Clear();
            if (polygon.Count < 3) return;

            Bounds(polygon, out Vector2 min, out Vector2 max);

            for (int i = 0; i < candidates.Count; i++)
            {
                Vector2 p = positionOf(candidates[i]);
                if (p.x < min.x || p.x > max.x || p.y < min.y || p.y > max.y) continue;
                if (Contains(polygon, p)) outCaptured.Add(candidates[i]);
            }
        }
    }
}
