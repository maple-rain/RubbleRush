using System.Collections.Generic;
using UnityEngine;

namespace Demo.Destruction
{
    /// <summary>
    /// 파괴 무늬의 <b>변하지 않는 부분</b> — 파편이 어떤 다각형인지, 파편끼리 어떻게 붙어 있는지,
    /// 어느 파편이 자기 층의 바닥에 닿아 있는지, 어떤 순서로 무너지는지.
    /// 한 번 만들면 파괴가 끝날 때까지 그대로다.
    ///
    /// 변하는 상태(사라졌나·떨어지나·내려앉았나)는 <see cref="FracturePattern"/>이 들고 있다.
    ///
    /// <b>격자를 쓰지 않는다</b> [지시 2026-09-23]. 예전에는 잘게 나눈 칸을 가장 가까운 씨앗에
    /// 배정했는데, 그건 보로노이를 래스터화한 것이라 경계가 격자를 따라 계단이 되고 파편이
    /// 전부 비슷하게 생긴다. 칸을 삼각형으로 바꿔도 모든 삼각형이 같은 모양·같은 방향이라
    /// 마찬가지였다. 지금은 <see cref="VoronoiCells"/>가 반평면 자르기로 다각형을 직접 만든다 —
    /// 변이 임의 각도의 직선이고, 씨앗을 무작위로 뿌리므로 <b>모양도 크기도 제각각</b>이다.
    /// </summary>
    public sealed class FractureLayout
    {
        public int SeedCount { get; private set; }
        public int FloorCount { get; private set; }

        /// <summary>실제로 영역을 가진 파편 수. 진행도가 이 값을 기준으로 돈다.</summary>
        public int LiveSeeds { get; private set; }

        public int[] SeedFloor { get; private set; }

        /// <summary>파편이 <b>자기 층의</b> 바닥에 닿아 있는가. 지지 탐색의 출발점이다.</summary>
        public bool[] TouchesFloorBase { get; private set; }

        /// <summary>무너지는 순서. 작을수록 먼저다.</summary>
        public int[] SeedRank { get; private set; }

        /// <summary>파편끼리 맞닿아 있는가.</summary>
        public List<int>[] Adjacent { get; private set; }

        /// <summary>0층부터 f−1층까지의 살아 있는 파편 누적 수. 길이는 층 수 + 1이다.</summary>
        public int[] LiveUpToFloor { get; private set; }

        VoronoiCells.Cell[] cells;
        Vector2[] seeds;

        public VoronoiCells.Cell Cell(int seed) => cells[seed];

        /// <summary>영역을 못 가진 파편인가. 이웃에게 다 빼앗혔거나 씨앗이 겹친 경우다.</summary>
        public bool IsEmpty(int seed) => cells[seed].Points.Length < 3;

        /// <param name="clusterShare">씨앗 중 충격점 주위에 몰 비율</param>
        /// <param name="clusterSigma">그 뭉치가 퍼진 정도. 층 높이 대비 표준편차</param>
        public void Build(Vector2 size, int seedCount, int floors,
                          float clusterShare, float clusterSigma, bool topDown, Vector2 from)
        {
            FloorCount = Mathf.Max(1, floors);
            floorHeightCache = size.y / FloorCount;
            SeedCount = Mathf.Max(FloorCount * 2, seedCount);

            SeedFloor = new int[SeedCount];
            seeds = new Vector2[SeedCount];
            cells = new VoronoiCells.Cell[SeedCount];

            PlaceSeeds(size, clusterShare, clusterSigma);
            BuildCells(size);

            LiveSeeds = 0;
            for (int s = 0; s < SeedCount; s++) if (!IsEmpty(s)) LiveSeeds++;

            BuildAdjacency(size);
            Rank(topDown, from);

            LiveUpToFloor = new int[FloorCount + 1];
            for (int s = 0; s < SeedCount; s++)
                if (!IsEmpty(s)) LiveUpToFloor[SeedFloor[s] + 1]++;
            for (int f = 1; f <= FloorCount; f++) LiveUpToFloor[f] += LiveUpToFloor[f - 1];
        }

        /// <summary>
        /// 씨앗을 층 안에 뿌린다 [ch5saeng.tistory.com/87 참조].
        ///
        /// <b>충격점 주위에 몰고, 바깥은 성글게.</b> 이 한 가지가 방사형 균열을 만든다 —
        /// 씨앗이 촘촘한 곳은 파편이 잘아지고 성근 곳은 커지므로, 가운데서 바깥으로 갈수록
        /// 굵어지는 유리창 깨짐 무늬가 저절로 나온다. 균열선을 따로 그리지 않는다.
        ///
        /// 배경 점이 <b>반드시</b> 필요하다. 중심 뭉치만 두면 바깥 셀이 벽 끝까지 뻗어
        /// 거대한 판 몇 개가 되어 버린다.
        ///
        /// 위치가 <b>완전한 난수</b>여야 한다는 것도 중요하다. 격자에 맞춰 흔들기만 하면
        /// 셀 크기가 고르게 나와서, 다각형인데도 다시 정형적으로 보인다.
        /// </summary>
        void PlaceSeeds(Vector2 size, float clusterShare, float clusterSigma)
        {
            float floorH = size.y / FloorCount;
            float sigma = Mathf.Max(0.05f, floorH * clusterSigma);
            float halfW = size.x * 0.5f;

            int perFloor = Mathf.Max(1, SeedCount / FloorCount);
            int clusterCount = Mathf.RoundToInt(perFloor * Mathf.Clamp01(clusterShare));

            for (int i = 0; i < SeedCount; i++)
            {
                int f = Mathf.Min(FloorCount - 1, (i * FloorCount) / SeedCount);
                SeedFloor[i] = f;

                float y0 = f * floorH;
                float x, y;

                if (i % perFloor < clusterCount)
                {
                    // 충격점 — 조준이 없으므로 그 층의 한가운데다
                    x = Gauss() * sigma;
                    y = y0 + floorH * 0.5f + Gauss() * sigma * 0.7f;
                }
                else
                {
                    x = (Random.value - 0.5f) * size.x;
                    y = y0 + Random.value * floorH;
                }

                seeds[i] = new Vector2(Mathf.Clamp(x, -halfW + 0.01f, halfW - 0.01f),
                                       Mathf.Clamp(y, y0 + 0.01f, y0 + floorH - 0.01f));
            }
        }

        /// <summary>표준정규 난수 하나. Box-Muller.</summary>
        static float Gauss()
        {
            float u = Mathf.Max(1e-6f, Random.value);
            return Mathf.Sqrt(-2f * Mathf.Log(u)) * Mathf.Cos(2f * Mathf.PI * Random.value);
        }

        /// <summary>
        /// 층마다 따로 자른다. 씨앗이 층 경계를 넘지 않아야 가로 이음매가 생기고,
        /// 그 이음매가 바닥 슬래브로 읽힌다 — 층이 층으로 보이는 이유다.
        /// </summary>
        void BuildCells(Vector2 size)
        {
            float floorH = size.y / FloorCount;
            var idx = new List<int>(SeedCount);
            var pts = new List<Vector2>(SeedCount);

            for (int f = 0; f < FloorCount; f++)
            {
                idx.Clear(); pts.Clear();
                for (int s = 0; s < SeedCount; s++)
                    if (SeedFloor[s] == f) { idx.Add(s); pts.Add(seeds[s]); }
                if (idx.Count == 0) continue;

                var bounds = new Rect(-size.x * 0.5f, f * floorH, size.x, floorH);
                VoronoiCells.Build(bounds, pts, idx, cells);
            }
        }

        /// <summary>
        /// 변의 주인에서 인접 관계를 읽는다. 다시 거리를 재지 않는 이유는, 자르면서 기록해 둔
        /// 값이 정확하기 때문이다 — 되계산하면 부동소수 오차로 이웃 한둘이 어긋난다.
        ///
        /// "바닥"이 맨 아랫줄이 아니라 <b>각 층의 맨 아랫줄</b>인 것이 핵심이다.
        /// 층은 슬래브 위에 얹혀 있으므로 아래층이 통째로 없어져도 위층은 사라지지 않는다 —
        /// 한 층 내려앉아 그 자리에서 다시 선다.
        /// </summary>
        void BuildAdjacency(Vector2 size)
        {
            Adjacent = new List<int>[SeedCount];
            for (int i = 0; i < SeedCount; i++) Adjacent[i] = new List<int>(8);

            TouchesFloorBase = new bool[SeedCount];
            float floorH = size.y / FloorCount;
            float eps = Mathf.Max(0.002f, floorH * 0.02f);

            for (int s = 0; s < SeedCount; s++)
            {
                if (IsEmpty(s)) continue;

                var cell = cells[s];
                for (int i = 0; i < cell.Neighbour.Length; i++)
                {
                    int m = cell.Neighbour[i];
                    if (m >= 0 && m != s && !Adjacent[s].Contains(m)) Adjacent[s].Add(m);
                }

                float baseY = SeedFloor[s] * floorH;
                for (int i = 0; i < cell.Points.Length; i++)
                    if (cell.Points[i].y <= baseY + eps) { TouchesFloorBase[s] = true; break; }
            }
        }

        /// <summary>
        /// 무너지는 순서. 층이 우선이고, 층 안에서는 <b>기준점에서 가까운 것부터</b>다 —
        /// 그래서 금이 가운데에서 바깥으로 퍼진다.
        /// </summary>
        void Rank(bool topDown, Vector2 from)
        {
            var order = new int[SeedCount];
            for (int i = 0; i < SeedCount; i++) order[i] = i;

            System.Array.Sort(order, (a, b) =>
            {
                // 영역 없는 씨앗은 언제나 뒤로. 앞 순번은 보이는 파편만 갖는다 —
                // 안 그러면 탭 몇 번이 아무 일도 안 하는 구간이 생긴다.
                bool ea = IsEmpty(a), eb = IsEmpty(b);
                if (ea != eb) return ea ? 1 : -1;

                int fa = SeedFloor[a], fb = SeedFloor[b];
                if (fa != fb) return topDown ? fb.CompareTo(fa) : fa.CompareTo(fb);

                // 같은 층 안에서는 그 층의 한가운데에서 가까운 순. 기준 y를 층마다 다시 잡아야
                // 방사형으로 퍼진다 — 건물 전체 중심을 쓰면 1층에서는 좌우 거리만 남는다.
                Vector2 c = new Vector2(from.x, FloorCenterY(fa));
                return (seeds[a] - c).sqrMagnitude.CompareTo((seeds[b] - c).sqrMagnitude);
            });

            SeedRank = new int[SeedCount];
            for (int rank = 0; rank < SeedCount; rank++) SeedRank[order[rank]] = rank;
        }

        float floorHeightCache;
        float FloorCenterY(int floor) => (floor + 0.5f) * floorHeightCache;
    }
}
