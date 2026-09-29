using System.Collections.Generic;
using UnityEngine;

namespace Demo.Destruction
{
    /// <summary>
    /// 파괴 화면에서 벽이 보로노이 조각으로 부서지는 것을 만든다 [지시 2026-09-22].
    /// 무늬와 지지 판정은 <see cref="FracturePattern"/>이 하고, 여기는 <b>메시만</b> 쓴다.
    ///
    /// <b>물리를 쓰지 않는다</b> [확정] — 조각에 Rigidbody도 Collider도 없다.
    /// 떨어지는 것도 정점을 아래로 밀어내는 것이지 낙하 시뮬레이션이 아니다.
    /// CLAUDE.md 규칙 4(MeshCollider 금지)에도 걸리지 않는다.
    ///
    /// 전체가 메시 하나다. 조각을 오브젝트로 만들면 수백 개가 되지만, 정점을 한 점으로 접어
    /// 지우면 드로우콜은 계속 하나다.
    ///
    /// 진행 곡선은 <b>세밀 → 큰 금 → 쾅</b>이다 [지시 2026-09-22 · 2026-09-23 재구현].
    ///
    /// <b>조각은 하나씩 빠지지 않는다.</b> 두드리는 동안에는 금이 층 전체로 번지고,
    /// 조각이 밀리고 기울며 틈이 벌어질 뿐 <b>한 개도 사라지지 않는다</b>.
    /// <see cref="shatterAt"/>에 닿는 순간 그 층이 통째로 쏟아진다.
    ///
    /// 예전에는 진행도에 비례해 조각이 하나씩 빠졌는데, 그러면 곡선을 아무리 비틀어도
    /// <b>갉아먹는</b> 그림이 된다 — 벽이 조금씩 줄어들 뿐 "부서지는 순간"이 없다.
    /// 여기 적혀 있던 "쾅"이 실제로는 한 번도 구현된 적이 없었다.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class WallFracture : MonoBehaviour
    {
        [Header("밀도 — 화면에 보이는 조각 수를 건물 크기와 무관하게 유지한다")]
        [SerializeField, Tooltip("화면 가로에 조각이 몇 개 보이게 할 것인가 [지시 2026-09-22]. " +
                                 "면적 기준으로 뽑으면 작은 건물이 덩어리 몇 개로 부서진다 — " +
                                 "판자가 조각 11개, 층당 5개였다. " +
                                 "카메라는 언제나 한 층만 잡으므로, 기준은 면적이 아니라 " +
                                 "<b>그 층에서 화면에 들어오는 폭</b>이어야 한다. " +
                                 "파편 수는 이 값의 <b>제곱</b>에 비례한다 — 가로로도 세로로도 " +
                                 "그만큼 잘아지기 때문이다. 3배로 늘리려면 루트3(약 1.73)을 곱한다")]
        int piecesAcrossFrame = 14;

        [SerializeField, Tooltip("층 높이 대비 화면 가로 폭. FollowCamera의 " +
                                 "floorsInView × focusWidthRatio와 같아야 한다 (1.2 × 1.78 ≈ 2.14)")]
        float frameWidthPerFloor = 2.14f;

        [SerializeField, Tooltip("아무리 낮은 층이어도 조각을 이보다 잘게 쪼개지 않는다(m). " +
                                 "<b>piecesAcrossFrame과 같이 움직여야 한다</b> — 이 값이 걸리면 " +
                                 "낮은 건물만 조각 수가 안 늘어난다. 층 높이 0.8m인 취약 건물은 " +
                                 "화면에 담기는 폭이 1.71m라, 14조각이면 조각이 0.12m다")]
        float minPieceSize = 0.10f;

        [SerializeField, Range(0f, 0.95f),
         Tooltip("씨앗 중 몇 할을 <b>충격점 주위에 몰 것인가</b> [지시 2026-09-23]. " +
                 "나머지는 배경 점으로 층 전체에 뿌린다. " +
                 "가운데가 촘촘하고 끝으로 갈수록 성긴 것이 맞다 — 다만 <b>절벽이 아니라 " +
                 "기울기</b>여야 한다. 0.7 / sigma 0.28에서는 안쪽 4분의 1에 절반이 몰리고 " +
                 "바깥 절반에는 다섯에 하나만 남아, 바깥이 '안 부서지고 버려진 판'으로 보였다 " +
                 "(바깥 파편이 가운데의 7배). clusterSigma와 <b>같이</b> 봐야 한다")]
        float clusterShare = 0.55f;

        [SerializeField, Range(0.08f, 0.8f),
         Tooltip("중심 뭉치가 퍼진 정도. 층 높이 대비 표준편차다 [지시 2026-09-23]. " +
                 "이것이 <b>기울기의 완만함</b>을 정한다. 작으면 가운데만 가루가 되고 " +
                 "바깥은 큼직한 판으로 남는 절벽이 되고, 넓히면 가운데에서 바깥으로 " +
                 "파편이 서서히 굵어진다. 가운데가 촘촘한 것은 그대로 유지된다")]
        float clusterSigma = 0.5f;

        [Header("진행 곡선 — 세밀 → 큰 금 → 쾅")]
        [SerializeField, Range(0.4f, 1f),
         Tooltip("층이 무너지는 시점. 한 층 진행도의 몇 할에서 터질 것인가 [지시 2026-09-23]. " +
                 "이 앞까지는 조각이 <b>하나도 안 빠진다</b> — 금만 번지고 밀린다. " +
                 "이 값에 닿는 순간 그 층이 통째로 쏟아진다. " +
                 "1보다 작으면 남는 구간이 <b>아무 일도 안 일어나는 빈 탭</b>이 된다 — " +
                 "0.8이면 마지막 층이 게이지 90%에서 이미 다 터지는데 종료는 100%라, " +
                 "다 부순 벽을 두 번 더 두드려야 끝났다. 1이면 마지막 탭이 곧 마지막 쾅이다")]
        float shatterAt = 1f;

        [SerializeField, Range(0f, 0.08f),
         Tooltip("금이 안 간 파편도 늘 벌려 둘 이음매 폭. 파편 크기 대비 비율이다.\n" +
                 "<b>기본은 0이다</b> [지시 2026-09-23] — 한 번도 안 두드린 벽은 통짜로 보여야 한다. " +
                 "0이 아니면 보로노이 무늬가 <b>처음부터</b> 드러나서, 때리기도 전에 " +
                 "이미 조각나 있는 벽이 된다. 무늬는 때린 자리에서만 나와야 한다")]
        float seamWidth = 0f;

        [SerializeField, Range(0f, 0.2f),
         Tooltip("초반 균열 폭. 파편 크기 대비 <b>비율</b>이다 [지시 2026-09-23]. " +
                 "미터로 주면 안 된다 — 진짜 보로노이는 파편 넓이가 2000배까지 차이 나서, " +
                 "큰 판에는 실금인 값이 작은 파편은 통째로 지워 버린다")]
        float crackWidthMin = 0.02f;

        [SerializeField, Range(0f, 0.45f),
         Tooltip("후반 균열 폭. 파편 크기 대비 비율. 쾅 직전에 쩍 벌어져야 한다")]
        float crackWidthMax = 0.22f;

        [SerializeField, Tooltip("초반에 금이 보이는 범위(조각 수)")]
        int lookaheadMin = 2;

        [SerializeField, Range(0f, 1f),
         Tooltip("금이 간 조각이 제자리에서 밀려나는 거리 [지시 2026-09-23]. " +
                 "미터가 아니라 <b>파편 크기의 비율</b>이다 — 0.3이면 제 크기의 3할만큼 밀린다. " +
                 "0이면 조각이 한 톨도 안 움직여서, 벌어진 틈이 균열이 아니라 " +
                 "'그려진 윤곽선'으로 읽힌다 — '금은 갔는데 왜 안 부서졌지'가 그 증상이다. " +
                 "미터로 두면 파편을 잘게 쪼갤 때마다 같이 손봐야 한다 — 0.12m는 " +
                 "0.4m 파편에서 2할이지만 0.23m 파편에서는 4할이라, 금이 아니라 " +
                 "흩어짐으로 읽힌다. 층이 끝나갈수록 커진다")]
        float crackSag = 0.30f;

        [SerializeField,
         Tooltip("금이 간 조각의 테두리에 남길 선 색 [지시 2026-09-23]. " +
                 "부순 자리를 강조하는 것이 목적이라 벽보다 <b>훨씬 어두워야</b> 한다")]
        Color crackLineColor = new Color(0.06f, 0.05f, 0.05f, 1f);

        [SerializeField, Range(0f, 0.05f),
         Tooltip("테두리선을 앞면보다 얼마나 앞에 둘 것인가(m). " +
                 "0이면 앞면과 같은 깊이라 <b>선이 깜빡인다</b>(z-파이팅). " +
                 "너무 크면 선이 벽에서 떠 보인다")]
        float crackLineLift = 0.012f;

        [SerializeField, Range(0.3f, 4f),
         Tooltip("구멍이 가장 깊은 곳에서 <b>얼마나 멀리까지 파이는가</b>(m) [지시 2026-09-23]. " +
                 "층이 진행될수록 이 반경까지 넓어진다. 이 밖의 조각은 금만 가고 안 파인다 — " +
                 "그래서 구멍이 깔때기 모양이 되고, 두드릴수록 넓고 깊어진다")]
        float crackRadius = 1.6f;

        [SerializeField, Range(0f, 0.95f),
         Tooltip("구멍이 <b>벽 두께의 몇 할까지</b> 파고 들어가는가 [지시 2026-09-23]. " +
                 "겹 두께가 아니라 <b>벽 전체</b> 기준이다 — 겹 기준으로 재면 맨 앞 " +
                 "얇은 겹이 제 두께에 묶여 구멍이 거의 안 파인다. " +
                 "타격점에서 가장 깊고 가장자리로 갈수록 얕아져 깔때기가 된다. " +
                 "0.35면 1.8m 벽에서 가장 깊은 곳이 0.63m다")]
        float carveDepth = 0.35f;

        [SerializeField, Range(0f, 0.5f),
         Tooltip("금이 간 파편이 제 무게중심을 축으로 돌아가는 각(라디안). " +
                 "밀림만으로는 부족하다 — 조금이라도 돌아가야 '떨어져 나오는 중'으로 읽힌다")]
        float crackTilt = 0.12f;

        [Header("낙하 — 지지를 잃은 덩어리")]
        [SerializeField, Tooltip("떨어져서 사라지기까지(초)")]
        float fallSeconds = 0.7f;

        [SerializeField, Tooltip("낙하 가속(m/s²). 실제 중력일 필요는 없다")]
        float fallGravity = 28f;

        [Header("내려앉기 — 아래층이 비면 위층이 한 층 내려온다")]
        [SerializeField, Tooltip("내려앉는 속도(m/s). 빠를수록 '쿵' 하고 주저앉는다")]
        float settleSpeed = 9f;

        [SerializeField, Tooltip("조각이 사라지는 순서")]
        Order removalOrder = Order.CenterOut;

        /// <summary>조각을 지우는 순서. 무늬와 배정은 그대로 두고 정렬 기준만 바뀐다.</summary>
        public enum Order
        {
            /// <summary>가운데에서 바깥으로 번진다 [지시 2026-09-22].</summary>
            CenterOut,
            /// <summary>위에서부터 차례로 무너진다. 기획 단계의 원래 그림.</summary>
            TopDown,
            /// <summary>맞은 자리에서 번진다. 참조 영상의 방식.</summary>
            FromImpact,
        }

        readonly FracturePattern pattern = new FracturePattern();

        Mesh mesh;
        Vector3[] verts;
        int[] vertStart;              // 파편마다 정점이 시작되는 자리
        CrackOutline outline;         // 금이 간 조각의 테두리선. 앞겹에만 붙인다
        float[] settleY;              // 조각이 지금까지 내려앉은 거리(m)
        float floorHeight;
        float pieceSize;
        bool drawOutline = true;
        float wallDepth;              // 벽 전체 두께(m). 구멍 깊이의 기준이다
        float bandStart;              // 이 겹이 벽의 어디서 시작하는가(m)              // 파편 한 톨의 대표 크기(m). 밀림 비율의 기준이다.
        float boxDepth;               // 칸 상자의 깊이(m). 옆면이 이만큼 펴진다
        int layoutSeed;               // 겹끼리 공유하는 무늬 씨앗
        int vertexBudget = 24000;     // 이 겹 하나가 쓸 수 있는 정점 수
        float progress;          // 건물 전체 진행도 0~1
        float localProgress;     // 지금 부수는 층 안에서의 진행도 0~1
        int lastWant = -1;
        int lastLookahead = -1;

        /// <summary>실제로 보이는 조각 수.</summary>
        public int PieceCount => pattern.LiveSeeds;
        public int RemovedCount => pattern.RemovedCount;

        /// <summary><see cref="Order.FromImpact"/>의 기준점. 벽 로컬 좌표. Build 전에 넣는다.</summary>
        public Vector2 ImpactPoint { get; set; }

        public Order RemovalOrder { get => removalOrder; set => removalOrder = value; }

        // ── 만들기 ───────────────────────────────────────────────────────────────

        /// <param name="size">벽 크기(m)</param>
        /// <param name="floors">층 수. 1이면 층이 없다. 아래층이 사라지면 위층이 지지를 잃는다</param>
        /// <param name="depth">칸 상자의 깊이(m). 0이면 예전처럼 납작한 판이 된다</param>
        /// <param name="seed">무늬 난수 씨앗. <b>겹끼리 같은 값</b>을 줘야 조각이 깊이 방향으로 맞물린다</param>
        /// <summary>
        /// 아직 서 있는 벽의 높이(m). 무너진 층만큼 줄어든다.
        /// 건물 속을 막는 판이 이 값을 따라와야 위층이 내려온 것이 눈에 보인다.
        /// </summary>
        public float LiveHeight =>
            pattern == null ? 0f
                            : (pattern.FloorCount - pattern.CollapsedFloors) * floorHeight;

        /// <summary>아직 쏟아지거나 내려앉는 중인가. 파괴 화면을 언제 닫을지 판단한다.</summary>
        public bool Animating =>
            pattern != null && (pattern.AnyFalling || pattern.AnySettling);

        /// <param name="vertexBudget">이 겹이 쓸 정점 수. 파편 크기가 여기서 역산된다</param>
        public void Build(Vector2 size, int floors, Material material, float depth, int seed,
                          int vertexBudget, bool drawOutline = true, float wallDepth = 0f)
        {
            boxDepth = Mathf.Max(0f, depth);
            layoutSeed = seed;
            this.vertexBudget = Mathf.Max(600, vertexBudget);
            this.drawOutline = drawOutline;

            // 구멍 깊이는 <b>벽 전체</b>를 기준으로 잰다. 겹 두께가 아니다 —
            // 그러면 제일 앞 얇은 겹이 제 두께(0.13m)에 묶여 구멍이 안 파인다.
            this.wallDepth = wallDepth > 0f ? wallDepth : depth;
            bandStart = transform.localPosition.z;
            // 조각 크기를 <b>층 높이</b>에서 뽑는다 [지시 2026-09-22].
            //
            // 예전에는 면적에서 뽑았는데, 그러면 판자(3.84㎡)가 조각 11개 · 층당 5개가 된다 —
            // 한 층을 다 부숴도 덩어리 다섯 개가 사라질 뿐이라 "부숴짐"이 아니다.
            // 카메라는 언제나 한 층만 잡으므로, 화면에 보이는 밀도를 일정하게 하려면
            // 기준이 면적이 아니라 층 높이여야 한다.
            // 카메라가 한 층에서 실제로 담는 폭을 먼저 구하고, 그 폭을 조각 수로 나눈다.
            // 그래야 좁은 기둥이든 넓은 코어든 화면에 비슷한 개수가 보인다 —
            // 층 높이만 기준으로 하면 폭 1.6m인 기둥이 가로 서너 조각으로 굵게 쪼개진다.
            int floorCount = Mathf.Max(1, floors);
            float floorH = size.y / floorCount;
            float framedWidth = Mathf.Min(size.x, floorH * Mathf.Max(0.5f, frameWidthPerFloor));
            float piece = Mathf.Max(minPieceSize, framedWidth / Mathf.Max(2, piecesAcrossFrame));
            pieceSize = piece;

            // 파편 수를 <b>정점 예산에서 역산</b>한다 [지시 2026-09-23].
            //
            // 다각형 하나는 변이 평균 여섯이고, 앞면 부채꼴(1+E) + 옆면(4E)으로 약 31정점이다.
            // 격자를 래스터화하던 시절에는 같은 면적에 수백 정점을 썼다 — 진짜 보로노이로
            // 바꾸면서 파편을 훨씬 잘게 쪼갤 여유가 생겼다.
            float area = Mathf.Max(0.01f, size.x * size.y);
            int wanted = Mathf.RoundToInt(area / (piece * piece));
            int seeds = Mathf.Clamp(wanted, floorCount * 2,
                                    Mathf.Max(floorCount * 2, vertexBudget / 31));

            Vector2 from = removalOrder == Order.FromImpact
                ? ImpactPoint : new Vector2(0f, size.y * 0.5f);

            // 무늬를 <b>겹끼리 똑같이</b> 뽑는다 [지시 2026-09-23].
            //
            // 씨앗 배치가 난수라, 겹마다 그냥 돌리면 앞판과 뒷판이 서로 다른 무늬가 된다.
            // 그러면 같은 자리를 때려도 뒤에서는 엉뚱한 조각이 떨어져서, 한 덩어리가 아니라
            // <b>따로 노는 벽 두 장</b>으로 보인다 — 부피감이 거기서 무너진다.
            //
            // 씨앗을 맞추면 조각이 깊이 방향의 <b>기둥</b>이 된다. 앞 큐브와 그 뒤 큐브가
            // 같은 조각에 속하므로 같이 금이 가고 같이 기울고, 시간차만 두고 앞부터 먹힌다.
            // 지터는 이미 (칸, 행) 해시, 밀림은 조각 번호 해시라 무늬만 맞으면 따라 맞는다.
            //
            // 전역 난수 상태는 빌려 쓰고 <b>돌려준다</b> — 안 그러면 이 한 번의 고정이
            // 잔해 흩뿌리기 같은 다른 시스템의 난수까지 같은 값으로 묶어 버린다.
            Random.State prevRandom = Random.state;
            Random.InitState(layoutSeed);

            pattern.Build(size, seeds, floors,
                          clusterShare, clusterSigma, removalOrder == Order.TopDown, from);

            Random.state = prevRandom;

            floorHeight = size.y / Mathf.Max(1, pattern.FloorCount);
            settleY = new float[pattern.SeedCount];

            BuildMesh(size, material);

            progress = 0f;
            localProgress = 0f;
            lastWant = -1;
            lastLookahead = -1;
            SetProgress(0f);            // 시작은 빈틈없는 벽이다
        }

        /// <summary>
        /// 파편마다 <b>무게중심 부채꼴</b> 하나와 변마다 옆면 하나.
        /// 다각형은 볼록이 보장되므로(반평면 자르기의 결과다) 부채꼴로 충분하다.
        ///
        /// 파편마다 변 수가 다르므로 정점 자리도 가변이다. 시작 위치를 미리 표에 적어 두고
        /// 매 프레임 그 자리만 다시 쓴다 — 크기가 안 변하니 버퍼를 다시 잡을 일이 없다.
        /// </summary>
        void BuildMesh(Vector2 size, Material material)
        {
            int n = pattern.SeedCount;
            vertStart = new int[n + 1];

            int total = 0;
            for (int s = 0; s < n; s++)
            {
                vertStart[s] = total;
                int e = pattern.IsEmpty(s) ? 0 : pattern.Cell(s).Points.Length;
                total += e == 0 ? 0 : 1 + e + 4 * e;      // 중심 + 테두리 + 옆면 사각형들
            }
            vertStart[n] = total;

            verts = new Vector3[total];
            var uvs = new Vector2[total];
            var normals = new Vector3[total];
            var tris = new List<int>(total * 2);

            for (int s = 0; s < n; s++)
            {
                if (pattern.IsEmpty(s)) continue;
                var cell = pattern.Cell(s);
                int e = cell.Points.Length;
                int v = vertStart[s], ring = v + 1, side = v + 1 + e;

                for (int i = 0; i < e; i++)
                {
                    // 앞면 부채꼴 — 중심에서 변 하나씩. 감기 (중심, i+1, i)이 −Z를 본다.
                    tris.Add(v); tris.Add(ring + (i + 1) % e); tris.Add(ring + i);

                    // 옆면 — 변 하나당 사각형 하나
                    int o = side + i * 4;
                    tris.Add(o); tris.Add(o + 2); tris.Add(o + 1);
                    tris.Add(o); tris.Add(o + 3); tris.Add(o + 2);
                }

                // UV는 벽 전체를 0~1로 편다. 파사드가 그 자리에 그대로 얹힌다.
                Vector2 c = VoronoiCells.Centroid(cell.Points);
                uvs[v] = Uv(c, size);

                for (int i = 0; i < e; i++)
                {
                    Vector2 a = cell.Points[i], b = cell.Points[(i + 1) % e];
                    uvs[ring + i] = Uv(a, size);

                    int o = side + i * 4;
                    uvs[o] = uvs[o + 1] = Uv(a, size);
                    uvs[o + 2] = uvs[o + 3] = Uv(b, size);

                    // 옆면 법선은 변의 방향에서 나온다. 반시계 다각형이라 (dy, −dx)가 바깥이다.
                    Vector2 d = b - a;
                    var nn = new Vector3(d.y, -d.x, 0f).normalized;
                    normals[o] = normals[o + 1] = normals[o + 2] = normals[o + 3] = nn;
                }

                normals[v] = Vector3.back;
                for (int i = 0; i < e; i++) normals[ring + i] = Vector3.back;
            }

            if (mesh != null) Destroy(mesh);
            mesh = new Mesh { name = "WallFracture" };
            mesh.indexFormat = total > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.MarkDynamic();
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.SetTriangles(tris, 0);
            mesh.normals = normals;

            if (outline == null && drawOutline)
                outline = CrackOutline.Attach(transform, pattern, crackLineColor, crackLineLift);

            GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = GetComponent<MeshRenderer>();
            if (material != null) mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static Vector2 Uv(Vector2 p, Vector2 size) =>
            new Vector2(p.x / size.x + 0.5f, p.y / Mathf.Max(0.01f, size.y));

        // ── 진행 ─────────────────────────────────────────────────────────────────

        public void SetProgress(float progress01)
        {
            if (mesh == null) return;
            progress = Mathf.Clamp01(progress01);

            // 곡선을 <b>층마다 다시 돌린다</b> [지시 2026-09-22].
            //
            // 건물 전체에 한 번만 걸면 1층이 전체 탭의 40%를 먹고 나머지 층은 순식간에 지나간다 —
            // "열심히 부쉈는데 위층이 떨어졌다"는 박자가 딱 한 번밖에 안 온다.
            // 층마다 다시 돌리면 층 수만큼 그 박자가 반복된다: 세밀 → 큰 금 → 쾅 → 위층 낙하.
            //
            // 순번이 층 우선으로 정렬돼 있어서(FractureLayout.Rank) 누적 개수만 있으면 된다.
            int floors = Mathf.Max(1, pattern.FloorCount);
            float perFloor = 1f / floors;
            int fi = Mathf.Clamp((int)(progress / perFloor), 0, floors - 1);
            float local = Mathf.Clamp01((progress - fi * perFloor) / perFloor);

            int floorStart = pattern.LiveUpToFloor(fi);
            int inThisFloor = pattern.LiveUpToFloor(fi + 1) - floorStart;

            // <b>금이 번지다가 한 번에 쾅</b> [지시 2026-09-23].
            //
            // 예전에는 조각이 진행도에 비례해 하나씩 빠졌다. 그러면 아무리 곡선을 비틀어도
            // <b>갉아먹는</b> 그림이 된다 — 벽이 조금씩 줄어들 뿐 "부서지는" 순간이 없다.
            // 파일 첫머리에 적힌 "세밀 → 큰 금 → 쾅"의 마지막 박자가 한 번도 없었던 셈이다.
            //
            // 그래서 두 구간으로 나눈다.
            //   local &lt; shatterAt : <b>하나도 안 빠진다.</b> 금만 층 전체로 번지고,
            //                        조각이 밀리고 기울며 틈이 벌어진다
            //   local ≥ shatterAt : <b>층이 통째로</b> 간다
            //
            // 한꺼번에 떨어뜨리기만 하면 되는 이유는, 순번이 된 조각이 곧바로 사라지지 않고
            // 낙하 상태로 들어가기 때문이다. 층 전체가 동시에 낙하에 들어가 같이 쏟아진다.
            bool blown = local >= Mathf.Clamp01(shatterAt);
            int want = blown ? floorStart + inThisFloor : floorStart;

            // 금은 층 전체로 <b>번진다</b>. 진행도만큼 앞서 나가되 이 층을 넘지 않는다 —
            // 위층은 부술 수 없으므로 금도 가면 안 된다.
            float spread = Mathf.Clamp01(local / Mathf.Max(0.01f, shatterAt));
            // 한 번도 안 두드렸으면 0 — 금 하나 없는 멀쩡한 벽으로 시작해야 한다.
            int lookahead = local <= 0f
                ? 0 : Mathf.Max(lookaheadMin, Mathf.RoundToInt(spread * inThisFloor));

            if (want == lastWant && lastLookahead == lookahead
                && !pattern.AnyFalling && !pattern.AnySettling) return;
            lastWant = want;
            lastLookahead = lookahead;

            // 금도 층 안에서 다시 벌어진다 — 새 층은 다시 실금부터 시작한다.
            // 쾅 직전에 최대가 되도록 shatterAt으로 정규화한다.
            localProgress = spread;
            pattern.Apply(want, lookahead, floorStart + inThisFloor);
            Rebuild();
        }

        void Update()
        {
            // 떨어지거나 내려앉는 것이 있을 때만 돈다. 평소에는 아무 일도 하지 않는다.
            if (mesh == null) return;
            if (!pattern.AnyFalling && !pattern.AnySettling) return;

            bool changed = false;
            if (pattern.AnyFalling) changed |= pattern.TickFall(Time.deltaTime, fallSeconds);
            changed |= TickSettle(Time.deltaTime);

            if (changed) Rebuild();
        }

        /// <summary>
        /// 아래층이 비어버린 조각을 한 층씩 끌어내린다. 사라지는 게 아니라 <b>내려와서 다시 선다</b> —
        /// 1층을 부수면 2층이 1층이 되고, 그 자리에서 다시 부술 수 있다 [지시 2026-09-22].
        /// </summary>
        bool TickSettle(float dt)
        {
            bool moved = false;
            float step = settleSpeed * dt;

            for (int s = 0; s < settleY.Length; s++)
            {
                float target = pattern.SettleTarget(s) * floorHeight;
                if (Mathf.Approximately(settleY[s], target)) continue;
                settleY[s] = Mathf.MoveTowards(settleY[s], target, step);
                moved = true;
            }
            return moved;
        }

        /// <summary>−0.5~0.5. 이웃끼리 상관이 없어야 칸이 제각각이 된다 — 그래서 펄린이 아니라 해시다.</summary>
        static float Hash(int x, int y, int k)
        {
            int h = (x * 73856093) ^ (y * 19349663) ^ (k * 83492791);
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF - 0.5f;
        }

        /// <summary>
        /// 그 변의 옆면을 얼마나 펼 것인가. <b>언제나 벽 두께만큼</b>이다 [지시 2026-09-23].
        ///
        /// 예전에는 이웃이 사라졌을 때만 폈다. 채움 비용을 아끼려던 것인데, 층이 통째로
        /// 터지는 방식으로 바꾼 뒤로는 <b>쾅 전까지 아무것도 안 사라진다</b> — 두드리는
        /// 시간의 대부분 동안 옆면이 한 장도 안 보였고, 금이 벌어져도 그 틈으로 두께가 아니라
        /// 뒤가 비쳤다. 입체감이 없던 정체가 이것이다.
        ///
        /// 언제나 펴도 비싸지 않다. 맞닿은 두 파편의 옆면은 법선이 정반대라 뒷면 제거가
        /// 안 보이는 쪽을 알아서 버린다 — 벽이 멀쩡할 때는 그려지지 않는 것과 같다.
        /// </summary>
        float Side(int neighbourSeed) => boxDepth;

        /// <summary>
        /// 그 조각이 속한 층의 <b>타격점</b>. 층마다 새로 부수기 시작하므로 층의 가운데다 —
        /// 순번(FractureLayout.Rank)도 같은 기준을 쓰기 때문에 금이 번지는 중심과 구멍의
        /// 중심이 저절로 맞는다.
        /// </summary>
        Vector2 ImpactAt(int seed) =>
            new Vector2(0f, (pattern.FloorOf(seed) + 0.5f) * floorHeight);

        void Rebuild()
        {
            // 금은 이음매보다 좁아질 수 없다 — 두드렸는데 틈이 <b>줄어들면</b> 이상하다.
            float gap = Mathf.Max(seamWidth,
                                  Mathf.Lerp(crackWidthMin, crackWidthMax, localProgress) * 0.5f);
            int n = pattern.SeedCount;

            for (int s = 0; s < n; s++)
            {
                if (pattern.IsEmpty(s)) continue;
                var cell = pattern.Cell(s);
                int e = cell.Points.Length;
                int v = vertStart[s], ring = v + 1, side = v + 1 + e;

                if (pattern.Gone(s))
                {
                    // 파편의 정점을 전부 한 점으로 접는다 — 삼각형이 넓이를 잃어 사라지고,
                    // 드로우콜은 그대로다.
                    for (int k = vertStart[s]; k < vertStart[s + 1]; k++) verts[k] = Vector3.zero;
                    continue;
                }

                // 두 가지 아래 방향 이동이 겹칠 수 있다 —
                // 지지를 잃어 떨어지는 것과, 아래층이 비어 내려앉는 것.
                float drop = settleY[s];
                if (pattern.Falling(s)) drop += pattern.FallDrop(s, fallGravity);

                // 금이 간 파편은 <b>움직인다</b>. 틈만 벌어지고 파편이 제자리에 붙어 있으면
                // 눈이 그걸 균열이 아니라 그려진 윤곽선으로 읽는다.
                //
                // 멀쩡한 파편도 <b>이음매만큼은</b> 벌어져 있다 [지시 2026-09-23].
                // 예전에는 빈틈없이 붙여 뒀는데, 그러면 벽이 통짜로 보여서 보로노이 무늬가
                // 두드리기 전에는 아예 안 보였다. 아주 조금 줄이면 그 틈으로 파편의 옆면이
                // 드러나고, 그 어두운 띠가 곧 무늬의 윤곽선이 된다 — 콘크리트 블록 사이의
                // 줄눈과 같은 그림이라, 갈라질 자리를 미리 읽을 수 있다.
                Vector2 c = VoronoiCells.Centroid(cell.Points);

                bool swallowed = false;
                float shrink = seamWidth, offX = 0f, spin = 0f, carve = 0f;
                if (pattern.Cracking(s))
                {
                    // 밀림은 <b>파편 크기에 비례</b>한다. 그래야 파편 수를 바꿔도
                    // 금이 벌어진 정도가 눈에 같게 보인다 — 미터로 주면 잘게 쪼갤수록
                    // 파편이 제 크기만큼 날아가서 금이 아니라 흩어짐이 된다.
                    float k = Mathf.Sqrt(localProgress) * pieceSize;
                    drop += (Hash(s, 3, 5) + 0.5f) * crackSag * k;
                    offX = Hash(s, 3, 6) * crackSag * k * 0.7f;
                    // 회전은 각도라 파편 크기와 무관하다 — k에 섞인 크기를 도로 나눈다.
                    spin = Hash(s, 3, 7) * crackTilt * Mathf.Sqrt(localProgress);
                    shrink = gap;

                    // <b>타격 지점에서부터 안쪽으로 파여 들어간다</b> [지시 2026-09-23].
                    //
                    // 이 조각은 벽 두께를 관통하는 <b>기둥</b>이다. 앞면만 +Z로 밀면 뒷면은
                    // 제자리에 남고, 기둥의 옆벽이 그만큼 짧아지면서 눈에 드러난다 —
                    // 벽이 부스러지는 게 아니라 <b>파인다</b>로 읽히는 지점이 여기다.
                    //
                    // 깊이를 모두에게 똑같이 주면 안 된다. 그러면 금이 간 범위가 통째로
                    // 같은 깊이로 내려앉아 <b>바닥이 평평한 접시</b>가 된다 — 주먹으로 판
                    // 구멍이 아니라 벽 한 켜를 벗겨 낸 그림이다. 타격점에서 멀어질수록
                    // 얕아져야 구멍에 <b>깔때기 모양</b>이 생긴다.
                    float reach = crackRadius * Mathf.Sqrt(localProgress);
                    float near = reach <= 0.001f ? 0f
                               : 1f - Mathf.Clamp01(Vector2.Distance(c, ImpactAt(s)) / reach);

                    // 구멍은 <b>벽 표면에서부터</b> 잰 하나의 깔때기다 [지시 2026-09-23].
                    //
                    // 예전에는 겹마다 제 두께를 기준으로 팠다. 그런데 쾅 전까지 눈에 보이는
                    // 것은 맨 앞 겹뿐이고, 그 겹은 두께가 0.13m라 아무리 세게 파도 0.12m가
                    // 천장이었다 — 1.8m 벽에서 7%라 파인 줄을 모른다.
                    //
                    // 벽 좌표로 재면 구멍 하나가 겹을 <b>앞에서부터 먹고</b> 들어간다.
                    // 제 띠가 통째로 구멍 안에 들어간 겹은 그 자리에서 사라지고, 걸친 겹만
                    // 앞면이 구멍 바닥까지 물러난다. 겹 경계가 구멍 안에 드러나 단차가 생긴다.
                    float pit = carveDepth * wallDepth * near * near
                                * (0.6f + 0.4f * (Hash(s, 3, 9) + 0.5f));

                    carve = pit - bandStart;
                    if (carve >= boxDepth) { swallowed = true; }
                    else carve = Mathf.Max(0f, carve);
                }

                // 구멍이 이 겹을 통째로 삼켰다 — 여기는 이미 뚫린 자리다.
                if (swallowed)
                {
                    for (int k = vertStart[s]; k < vertStart[s + 1]; k++) verts[k] = Vector3.zero;
                    continue;
                }

                float cos = Mathf.Cos(spin), sin = Mathf.Sin(spin);
                var move = new Vector3(offX, -drop, carve);

                verts[v] = (Vector3)c + move;

                for (int i = 0; i < e; i++)
                {
                    // 무게중심으로 당겨 틈을 만들고, 중심을 축으로 살짝 돌린다.
                    // 다각형은 변이 제각각 기울어 있어 변을 따로 미는 것보다 이쪽이 정확하다.
                    Vector2 r = cell.Points[i] - c;
                    float len = r.magnitude;
                    if (len > 1e-5f) r *= 1f - shrink;      // 비율이라 파편 크기에 알아서 맞는다
                    r = new Vector2(r.x * cos - r.y * sin, r.x * sin + r.y * cos);
                    verts[ring + i] = (Vector3)(c + r) + move;
                }

                for (int i = 0; i < e; i++)
                {
                    Vector3 a = verts[ring + i], b = verts[ring + (i + 1) % e];

                    // 옆면은 <b>건너편이 없어진 변에만</b> 편다. 살아 있으면 넓이 0으로 접어
                    // 채움 비용을 없앤다 — 정점 수는 어차피 고정이다.
                    // 앞면이 carve만큼 물러났으므로 옆벽은 그만큼 짧다 — 뒷면은 제자리다.
                    Vector3 d = Vector3.forward * Mathf.Max(0f, Side(cell.Neighbour[i]) - carve);
                    int o = side + i * 4;
                    verts[o] = a; verts[o + 1] = a + d; verts[o + 2] = b + d; verts[o + 3] = b;
                }
            }

            mesh.vertices = verts;
            mesh.RecalculateBounds();

            if (outline != null) outline.Refresh(pattern, verts, vertStart);
        }




        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }
    }
}
