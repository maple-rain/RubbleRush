using System.Collections.Generic;
using UnityEngine;

namespace Demo.Destruction
{
    /// <summary>
    /// 진짜 보로노이 셀을 <b>기하로</b> 계산한다 [지시 2026-09-23 · ch5saeng.tistory.com/87].
    ///
    /// 예전에는 잘게 나눈 격자 칸을 가장 가까운 씨앗에 배정하는 방식이었다. 그건 보로노이를
    /// <b>래스터화</b>한 것이라 셀 경계가 격자를 따라 계단이 되고, 칸을 삼각형으로 바꿔 봐야
    /// 모든 삼각형이 같은 모양·같은 방향이라 여전히 정형적이다. 무늬가 아니라 무늬 흉내였다.
    ///
    /// 여기서는 <b>반평면 자르기</b>로 셀 자체를 만든다. 씨앗 s의 셀은
    /// "다른 모든 씨앗 t보다 s에 가까운 점들"이고, 그 조건 하나하나가 s와 t의
    /// <b>수직이등분선</b>으로 자르는 것과 같다. 사각형에서 시작해 차례로 잘라내면 남는 것이 셀이다.
    ///
    /// 얻는 것이 둘이다.
    /// <list type="bullet">
    /// <item><b>모양</b> — 변이 임의 각도의 직선이다. 계단도 규칙성도 생길 수가 없다</item>
    /// <item><b>비용</b> — 변 6개짜리 셀을 부채꼴로 그리면 앞면이 7정점이다.
    ///       격자 방식은 같은 면적에 수백 정점을 썼다</item>
    /// </list>
    ///
    /// 자르는 횟수는 층마다 (씨앗 수)²이다. 층당 30개면 900번이고 한 번만 돈다 — 무시해도 된다.
    /// </summary>
    public static class VoronoiCells
    {
        /// <summary>셀 하나. 다각형과, 변마다 <b>건너편 씨앗</b>이 누구인지.</summary>
        public struct Cell
        {
            /// <summary>반시계 방향 꼭짓점. 비었으면 이웃에게 영역을 다 빼앗긴 씨앗이다.</summary>
            public Vector2[] Points;

            /// <summary>
            /// <c>Neighbour[i]</c> = 꼭짓점 i에서 i+1로 가는 변의 건너편 씨앗.
            /// −1이면 벽이나 층의 경계다 — 건너편이 없으니 거기는 언제나 단면이 보인다.
            /// </summary>
            public int[] Neighbour;
        }

        /// <param name="bounds">셀이 잘려 나올 사각형. 보통 한 층의 띠다</param>
        /// <param name="seeds">씨앗 위치</param>
        /// <param name="ids">그 씨앗의 전역 번호. 이웃 표시에 쓴다</param>
        public static void Build(Rect bounds, IList<Vector2> seeds, IList<int> ids, Cell[] into)
        {
            var poly = new List<Vector2>(16);
            var owner = new List<int>(16);
            var nextPoly = new List<Vector2>(16);
            var nextOwner = new List<int>(16);

            for (int i = 0; i < seeds.Count; i++)
            {
                RectPoly(bounds, poly, owner);

                for (int j = 0; j < seeds.Count && poly.Count >= 3; j++)
                {
                    if (j == i) continue;
                    Vector2 d = seeds[j] - seeds[i];
                    if (d.sqrMagnitude < 1e-8f) continue;      // 겹친 씨앗은 건너뛴다
                    Clip(poly, owner, (seeds[i] + seeds[j]) * 0.5f, d, ids[j], nextPoly, nextOwner);
                }

                into[ids[i]] = poly.Count >= 3
                    ? new Cell { Points = poly.ToArray(), Neighbour = owner.ToArray() }
                    : new Cell { Points = System.Array.Empty<Vector2>(), Neighbour = System.Array.Empty<int>() };
            }
        }

        /// <summary>자를 바탕. 반시계이고, 네 변 모두 건너편이 없다(−1).</summary>
        static void RectPoly(Rect r, List<Vector2> poly, List<int> owner)
        {
            poly.Clear(); owner.Clear();
            poly.Add(new Vector2(r.xMin, r.yMin));
            poly.Add(new Vector2(r.xMax, r.yMin));
            poly.Add(new Vector2(r.xMax, r.yMax));
            poly.Add(new Vector2(r.xMin, r.yMax));
            for (int i = 0; i < 4; i++) owner.Add(-1);
        }

        /// <summary>
        /// <paramref name="mid"/>를 지나고 <paramref name="dir"/>에 수직인 선으로 자른다.
        /// <c>dir</c> 쪽(= 상대 씨앗 쪽)을 버린다. Sutherland–Hodgman이다.
        ///
        /// 변마다 주인을 같이 옮기는 것이 핵심이다 — 나중에 "이 변 건너편이 누구인가"를
        /// 알아야 인접 관계와 단면 표시를 정할 수 있고, 다시 계산하면 부동소수 오차로 어긋난다.
        /// </summary>
        static void Clip(List<Vector2> poly, List<int> owner, Vector2 mid, Vector2 dir, int cutBy,
                         List<Vector2> outPoly, List<int> outOwner)
        {
            outPoly.Clear(); outOwner.Clear();
            int n = poly.Count;

            for (int i = 0; i < n; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % n];
                int edgeOwner = owner[i];                       // a→b 변의 건너편

                float da = Vector2.Dot(a - mid, dir);
                float db = Vector2.Dot(b - mid, dir);
                bool ain = da <= 0f, bin = db <= 0f;

                if (ain)
                {
                    outPoly.Add(a); outOwner.Add(edgeOwner);
                    if (!bin)
                    {
                        // 여기서 잘린다. 새로 생기는 변은 자른 씨앗의 것이다.
                        outPoly.Add(Vector2.LerpUnclamped(a, b, da / (da - db)));
                        outOwner.Add(cutBy);
                    }
                }
                else if (bin)
                {
                    // 바깥에서 안으로 들어온다. 남는 토막은 원래 변의 일부다.
                    outPoly.Add(Vector2.LerpUnclamped(a, b, da / (da - db)));
                    outOwner.Add(edgeOwner);
                }
            }

            poly.Clear(); poly.AddRange(outPoly);
            owner.Clear(); owner.AddRange(outOwner);
        }

        /// <summary>다각형의 무게중심. 부채꼴의 중심점이자 파편이 기울 때의 회전축이 된다.</summary>
        public static Vector2 Centroid(Vector2[] p)
        {
            var c = Vector2.zero;
            for (int i = 0; i < p.Length; i++) c += p[i];
            return p.Length > 0 ? c / p.Length : Vector2.zero;
        }
    }
}
