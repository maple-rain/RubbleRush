using System.Collections.Generic;
using UnityEngine;

namespace Demo.Destruction
{
    /// <summary>
    /// 파괴 무늬의 <b>변하는 상태</b> — 어느 조각이 사라졌고, 떨어지는 중이고, 몇 층 내려앉았는가.
    /// 변하지 않는 구조(칸 배정·인접·순번)는 <see cref="FractureLayout"/>이 들고 있다.
    ///
    /// MonoBehaviour가 아니고 메시도 모른다. 그림 그리는 일은 <see cref="WallFracture"/>가 한다.
    /// Unity에 매인 것이 Vector2/Mathf뿐이라 에디터 없이도 검증할 수 있다.
    ///
    /// 아래로 내려가는 움직임이 두 가지이고, 둘은 전혀 다른 것이다.
    /// <list type="number">
    /// <item><b>낙하</b> — 한 층 안에서 옆으로 끊긴 조각. 지지가 없으니 떨어져 <b>사라진다</b>.</item>
    /// <item><b>내려앉기</b> — 아래층이 통째로 비었을 때. 위층은 사라지지 않고 한 층 <b>내려와 다시 선다</b>.
    ///       1층을 부수면 2층이 1층이 된다 [지시 2026-09-22].</item>
    /// </list>
    /// </summary>
    public sealed class FracturePattern
    {
        readonly FractureLayout layout = new FractureLayout();
        readonly Queue<int> bfs = new Queue<int>();

        int[] settleTarget;
        bool[] gone, cracking, grounded, floorCollapsed;
        float[] fallElapsed;          // 0 미만 = 안 떨어짐

        public int SeedCount => layout.SeedCount;
        public int LiveSeeds => layout.LiveSeeds;
        public int FloorCount => layout.FloorCount;
        /// <summary>그 파편의 다각형과 변별 이웃. 메시를 그리는 쪽이 읽는다.</summary>
        public VoronoiCells.Cell Cell(int seed) => layout.Cell(seed);

        /// <summary>영역을 못 가진 파편인가.</summary>
        public bool IsEmpty(int seed) => layout.IsEmpty(seed);

        /// <summary>f층이 시작되는 순번 = 그 아래 층들의 살아 있는 조각 수 합.</summary>
        public int LiveUpToFloor(int floor) =>
            layout.LiveUpToFloor[Mathf.Clamp(floor, 0, layout.FloorCount)];

        /// <summary>이 조각이 완전히 사라졌는가. 낙하가 끝난 것도 포함한다.</summary>
        public bool Gone(int seed) => gone[seed];

        /// <summary>곧 사라질 예정이라 금이 보이는가.</summary>
        public bool Cracking(int seed) => cracking[seed];

        /// <summary>떨어지는 중인가. 아직 보이지만 아래로 밀려 있다.</summary>
        public bool Falling(int seed) => fallElapsed[seed] >= 0f;

        /// <summary>지금 떨어진 거리(m). 가속이라 시간의 제곱에 비례한다.</summary>
        public float FallDrop(int seed, float gravity) =>
            fallElapsed[seed] < 0f ? 0f : 0.5f * gravity * fallElapsed[seed] * fallElapsed[seed];

        /// <summary>이 조각이 속한 층.</summary>
        public int FloorOf(int seed) => layout.SeedFloor[seed];

        /// <summary>
        /// 몇 층만큼 내려앉아야 하는가. 아래에서 통째로 비어버린 층의 수다 —
        /// 1층이 없어지면 2층은 1이 되어 1층 자리로 내려온다.
        /// </summary>
        public int SettleTarget(int seed) => settleTarget[seed];

        /// <summary>떨어지는 조각이 있는가. 있으면 메시를 매 프레임 다시 써야 한다.</summary>
        public bool AnyFalling { get; private set; }

        /// <summary>내려앉을 곳이 남은 조각이 있는가.</summary>
        public bool AnySettling { get; private set; }

        /// <summary>
        /// 통째로 사라진 층의 수. 남은 벽의 높이가 이만큼 줄었다는 뜻이라,
        /// 건물 속을 막는 판이 여기에 맞춰 같이 내려앉는다 — 안 그러면 위층이
        /// 내려왔는데도 그 자리에 <b>안 부서지는 검은 벽</b>이 그대로 남는다.
        /// </summary>
        public int CollapsedFloors { get; private set; }

        /// <summary>사라진 조각 수. 낙하 중인 것은 세지 않는다 — 아직 보인다.</summary>
        public int RemovedCount { get; private set; }

        /// <param name="size">벽 크기(m)</param>
        /// <param name="floors">층 수. 1이면 층이 없다</param>
        /// <param name="clusterShare">씨앗 중 충격점 주위에 몰 비율</param>
        /// <param name="clusterSigma">그 뭉치가 퍼진 정도. 층 높이 대비 표준편차</param>
        /// <param name="topDown">위에서부터 무너뜨릴 것인가. 아니면 가운데에서 바깥으로</param>
        /// <param name="from">가운데에서 퍼질 때의 기준점. 벽 로컬 좌표</param>
        public void Build(Vector2 size, int seedCount, int floors,
                          float clusterShare, float clusterSigma, bool topDown, Vector2 from)
        {
            layout.Build(size, seedCount, floors, clusterShare, clusterSigma, topDown, from);

            int n = layout.SeedCount;
            gone = new bool[n];
            cracking = new bool[n];
            grounded = new bool[n];
            settleTarget = new int[n];
            fallElapsed = new float[n];
            for (int s = 0; s < n; s++) fallElapsed[s] = -1f;

            floorCollapsed = new bool[layout.FloorCount];
            RemovedCount = 0;
            AnyFalling = false;
            AnySettling = false;
        }

        /// <summary>
        /// 진행도를 적용한다. 순번이 앞선 조각부터 사라지고, 지지를 잃은 덩어리는 낙하를 시작한다.
        /// </summary>
        /// <param name="want">사라질 조각 수</param>
        /// <param name="lookahead">사라지기 몇 순번 전부터 금이 보이는가</param>
        /// <param name="floorEnd">
        /// 지금 부수는 층의 <b>마지막 순번</b>. 금도 제거도 이 선을 넘지 않는다 [지시 2026-09-23].
        ///
        /// 예전에는 <c>lookahead</c>(최대 16)가 층 경계를 그냥 넘어가서, 1층을 거의 다 부순
        /// 시점에 <b>2층에 금이 먼저 그어졌다.</b> 제거는 층 안에 머무르므로 위층은
        /// "금은 갔는데 안 떨어지는" 상태로 남는다 — 부서지다 만 것처럼 보이는 원인이었다.
        ///
        /// 위층은 부술 수 없다 [확정]. 아래층이 비면 <b>내려앉아서</b> 그때 1층이 되고,
        /// 그때부터 부술 수 있다. 그것이 이 게임의 파괴가 다른 건물부수기와 갈리는 지점이다.
        /// </param>
        public void Apply(int want, int lookahead, int floorEnd)
        {
            want = Mathf.Clamp(want, 0, layout.LiveSeeds);
            floorEnd = Mathf.Clamp(floorEnd, want, layout.LiveSeeds);
            int[] rank = layout.SeedRank;

            for (int s = 0; s < layout.SeedCount; s++)
            {
                bool empty = layout.IsEmpty(s);
                bool removed = rank[s] < want;

                // 순번이 된 조각은 <b>떨어뜨린다</b>. 예전에는 바로 gone으로 보냈는데,
                // 그러면 금이 그어진 조각이 그 자리에서 <b>증발</b>한다 —
                // "테두리는 쳐졌는데 떨어지지는 않는" 것으로 읽힌다 [지시 2026-09-23].
                // 낙하 장치는 이미 있다(TickFall). 여기서 태워 보내기만 하면 되고,
                // 다 떨어진 뒤에 TickFall이 gone으로 넘긴다.
                if (empty) { gone[s] = true; fallElapsed[s] = -1f; }
                else if (removed && !gone[s] && !Falling(s)) fallElapsed[s] = 0f;

                // 기준은 <b>lookahead</b>지 want가 아니다. 층이 통째로 터지는 방식으로 바꾼 뒤로
                // want는 쾅 직전까지 floorStart에 머무는데, 1층은 floorStart가 0이라
                // want로 재면 <b>1층에는 금이 영영 안 간다</b>.
                // 한 번도 안 두드렸으면 부르는 쪽이 lookahead를 0으로 준다.
                //
                // floorEnd로 자르는 것도 핵심이다 — 금이 위층으로 새면 안 부서지는 층에
                // 금만 그어진 채 남는다.
                cracking[s] = lookahead > 0 && !gone[s] && !Falling(s)
                              && rank[s] < Mathf.Min(want + lookahead, floorEnd);
            }

            DropUnsupported();
            Recount();
        }

        /// <summary>
        /// <b>자기 층의</b> 바닥에 닿은 조각에서 인접 관계를 타고 번진다.
        /// 닿지 못한 덩어리는 옆으로 끊긴 것이라 떨어지기 시작한다.
        /// 물리를 쓰지 않으므로 낙하는 정해진 시간 동안의 오프셋이다.
        /// </summary>
        void DropUnsupported()
        {
            int n = layout.SeedCount;
            for (int s = 0; s < n; s++) grounded[s] = false;

            bfs.Clear();
            for (int s = 0; s < n; s++)
                if (!gone[s] && !Falling(s) && layout.TouchesFloorBase[s])
                { grounded[s] = true; bfs.Enqueue(s); }

            while (bfs.Count > 0)
            {
                int cur = bfs.Dequeue();
                var a = layout.Adjacent[cur];
                for (int i = 0; i < a.Count; i++)
                {
                    int m = a[i];
                    if (gone[m] || Falling(m) || grounded[m]) continue;
                    grounded[m] = true;
                    bfs.Enqueue(m);
                }
            }

            for (int s = 0; s < n; s++)
                if (!gone[s] && !Falling(s) && !grounded[s])
                {
                    fallElapsed[s] = 0f;      // 낙하 시작
                    cracking[s] = false;
                }
        }

        /// <summary>낙하를 진행시킨다. 다 떨어진 조각은 사라진다. 메시를 다시 써야 하면 true.</summary>
        public bool TickFall(float deltaSeconds, float fallSeconds)
        {
            if (!AnyFalling) return false;

            bool changed = false;
            for (int s = 0; s < layout.SeedCount; s++)
            {
                if (fallElapsed[s] < 0f) continue;
                fallElapsed[s] += deltaSeconds;
                changed = true;

                if (fallElapsed[s] >= fallSeconds) { fallElapsed[s] = -1f; gone[s] = true; }
            }

            // 한 덩어리가 떨어지면 그 위에 얹혀 있던 것도 지지를 잃는다 — 연쇄가 여기서 난다.
            DropUnsupported();
            Recount();
            return changed;
        }

        void Recount()
        {
            RemovedCount = 0;
            AnyFalling = false;

            for (int s = 0; s < layout.SeedCount; s++)
            {
                if (gone[s] && !layout.IsEmpty(s)) RemovedCount++;
                if (fallElapsed[s] >= 0f) AnyFalling = true;
            }

            UpdateSettle();
        }

        /// <summary>
        /// 통째로 빈 층을 찾고, 그 위층들이 몇 층 내려앉아야 하는지 센다.
        /// 위층은 사라지지 않는다 — 내려와서 계속 서 있고, 다시 부술 수 있다.
        /// </summary>
        void UpdateSettle()
        {
            int floors = layout.FloorCount;
            for (int f = 0; f < floors; f++) floorCollapsed[f] = true;

            for (int s = 0; s < layout.SeedCount; s++)
            {
                if (layout.IsEmpty(s)) continue;

                // 떨어지는 중인 조각은 <b>이미 없는 것으로 친다</b> [지시 2026-09-23].
                //
                // 예전에는 "아직 그 자리에 있다"며 층이 비었다고 보지 않았다. 그러면 낙하가
                // 다 끝나고(0.7초) 나서야 위층이 내려앉기 시작한다 — 무너지는 게 아니라
                // 쏟아진 뒤에 뒤늦게 얹히는 그림이다. 실제 붕괴는 아래가 <b>빠지는 동시에</b>
                // 위가 내려온다. 두 움직임이 겹쳐야 무게가 실린다.
                if (!gone[s] && !Falling(s)) floorCollapsed[layout.SeedFloor[s]] = false;
            }

            CollapsedFloors = 0;
            for (int f = 0; f < floors; f++) if (floorCollapsed[f]) CollapsedFloors++;

            AnySettling = false;
            for (int s = 0; s < layout.SeedCount; s++)
            {
                int below = 0;
                for (int f = 0; f < layout.SeedFloor[s]; f++) if (floorCollapsed[f]) below++;
                settleTarget[s] = below;
                if (below > 0 && !gone[s]) AnySettling = true;
            }
        }
    }
}
