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
        [Header("쪼개짐·파임 — 손잡이는 FractureTuning.cs에 있다")]
        [SerializeField] FractureTuning tune = new FractureTuning();

        [Header("무너지는 동안의 움직임 — 낙하·내려앉기·구르기")]
        [SerializeField, Tooltip("셋이 서로 물려 있어서 한 덩어리로 본다. FractureFall 주석 참조 — " +
                                 "'위층이 아래층을 추월하면 안 된다'가 그 전부다")]
        FractureFall fall = new FractureFall();

        [SerializeField, Tooltip("조각이 사라지는 순서")]
        Order removalOrder = Order.CenterOut;

        /// <summary>
        /// 조각을 지우는 순서. 무늬와 배정은 그대로 두고 정렬 기준만 바뀐다.
        ///
        /// 셋째 값 FromImpact("맞은 자리에서 번진다")는 지웠다 [2026-10-01] —
        /// 기준점을 받던 <c>ImpactPoint</c>에 값을 넣어 주는 곳이 한 군데도 없어서
        /// 고를 수 있어도 동작하지 않는 선택지였다. git에 남아 있다.
        /// </summary>
        public enum Order
        {
            /// <summary>가운데에서 바깥으로 번진다 [지시 2026-09-22].</summary>
            CenterOut,
            /// <summary>위에서부터 차례로 무너진다. 기획 단계의 원래 그림.</summary>
            TopDown,
        }

        readonly FracturePattern pattern = new FracturePattern();

        Mesh mesh;
        Vector3[] verts;
        int[] vertStart;              // 파편마다 정점이 시작되는 자리
        CrackOutline outline;         // 금이 간 조각의 테두리선. 앞겹에만 붙인다

        // 법선을 두 벌 든다. baseNormals는 <b>벽에 붙어 있을 때</b>의 것이고,
        // normals는 구르는 조각만 돌려 쓴 사본이다. 원본이 없으면 한 번 구른 조각의
        // 법선을 되돌릴 수가 없다.
        Vector3[] baseNormals;
        Vector3[] normals;

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
            float framedWidth = Mathf.Min(size.x, floorH * Mathf.Max(0.5f, tune.frameWidthPerFloor));
            float piece = Mathf.Max(tune.minPieceSize, framedWidth / Mathf.Max(2, tune.piecesAcrossFrame));
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

            // 번지기 시작하는 자리는 <b>벽 한가운데</b>다.
            //
            // 예전에는 Order.FromImpact로 "맞은 자리에서 번진다"를 고를 수 있었고 그 기준점을
            // ImpactPoint라는 공개 프로퍼티로 받았는데, <b>그걸 넣어 주는 곳이 한 군데도 없었다</b>.
            // 아무도 안 쓰는 공개 API를 지우면서 같이 걷어냈다 [2026-10-01].
            // 되살리려면 FromImpact 분기와 함께 git에서 꺼내면 된다.
            Vector2 from = new Vector2(0f, size.y * 0.5f);

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
                          tune.clusterShare, tune.clusterSigma, removalOrder == Order.TopDown, from);

            Random.state = prevRandom;

            floorHeight = size.y / Mathf.Max(1, pattern.FloorCount);
            fall.Reset(pattern.SeedCount);

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
            baseNormals = new Vector3[total];
            var normals = baseNormals;            // 아래 채우기는 원본에 한다
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
            mesh.normals = baseNormals;

            // 구르는 조각이 덮어쓸 사본. 원본을 직접 돌리면 되돌릴 기준이 없어진다.
            this.normals = (Vector3[])baseNormals.Clone();

            if (outline == null && drawOutline)
                outline = CrackOutline.Attach(transform, pattern, tune.crackLineColor, tune.crackLineLift);

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
            // <b>나누지 말고 곱한다</b> [고침 2026-09-30].
            //
            // 예전에는 perFloor = 1f/floors를 만들어 progress를 그걸로 나눴다. 그런데
            // 1/3 = 0.33333334는 이진 부동소수로 정확하지 않아서, 되돌리면 1.0에 <b>한 틱
            // 모자란</b> 0.99999994가 나온다. shatterAt이 1이라 local >= shatterAt이 거짓이 되고,
            // <b>마지막 층이 영영 안 무너진다</b> — 금만 잔뜩 간 채로 서 있다가 화면이 닫힌다.
            // 층 수가 3·5·6·7일 때 그랬다 (2의 거듭제곱만 우연히 멀쩡했다).
            //
            // 중간 층들이 멀쩡했던 이유는 경계를 탭 하나만큼 <b>넘어서</b> 지나가기 때문이다.
            // 마지막 층만 방아쇠가 정확히 게이지 1.0에 놓여 여유분이 없다 —
            // 기획자가 "특히 마지막 층"이라고 짚은 자리가 여기다 [지적 2026-09-30].
            //
            // progress * floors는 역수를 안 거치므로 1.0에서 정확히 floors가 나온다.
            // Tap()이 "세어서 나눈다, 더해서 쌓지 않는다"로 고친 것과 같은 계열의 버그다.
            int floors = Mathf.Max(1, pattern.FloorCount);
            float scaled = progress * floors;
            int fi = Mathf.Clamp((int)scaled, 0, floors - 1);
            float local = Mathf.Clamp01(scaled - fi);

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
            bool blown = local >= Mathf.Clamp01(tune.shatterAt);
            int want = blown ? floorStart + inThisFloor : floorStart;

            // 금은 층 전체로 <b>번진다</b>. 진행도만큼 앞서 나가되 이 층을 넘지 않는다 —
            // 위층은 부술 수 없으므로 금도 가면 안 된다.
            float spread = Mathf.Clamp01(local / Mathf.Max(0.01f, tune.shatterAt));
            // 한 번도 안 두드렸으면 0 — 금 하나 없는 멀쩡한 벽으로 시작해야 한다.
            int lookahead = local <= 0f
                ? 0 : Mathf.Max(tune.lookaheadMin, Mathf.RoundToInt(spread * inThisFloor));

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
            if (pattern.AnyFalling) changed |= pattern.TickFall(Time.deltaTime, fall.FallSeconds);
            changed |= fall.TickSettle(Time.deltaTime, pattern, floorHeight);

            if (changed) Rebuild();
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
            float gap = Mathf.Max(tune.seamWidth,
                                  Mathf.Lerp(tune.crackWidthMin, tune.crackWidthMax, localProgress) * 0.5f);
            int n = pattern.SeedCount;
            bool anyRolling = false;

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
                float drop = fall.SettleOf(s);
                if (pattern.Falling(s)) drop += pattern.FallDrop(s, fall.Gravity);

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
                float shrink = tune.seamWidth, offX = 0f, spin = 0f, carve = 0f;
                if (pattern.Cracking(s))
                {
                    // 밀림은 <b>파편 크기에 비례</b>한다. 그래야 파편 수를 바꿔도
                    // 금이 벌어진 정도가 눈에 같게 보인다 — 미터로 주면 잘게 쪼갤수록
                    // 파편이 제 크기만큼 날아가서 금이 아니라 흩어짐이 된다.
                    float k = Mathf.Sqrt(localProgress) * pieceSize;
                    drop += (Hash(s, 3, 5) + 0.5f) * tune.crackSag * k;
                    offX = Hash(s, 3, 6) * tune.crackSag * k * 0.7f;
                    // 회전은 각도라 파편 크기와 무관하다 — k에 섞인 크기를 도로 나눈다.
                    spin = Hash(s, 3, 7) * tune.crackTilt * Mathf.Sqrt(localProgress);
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
                    float reach = tune.crackRadius * Mathf.Sqrt(localProgress);
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
                    float pit = tune.carveDepth * wallDepth * near * near
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

                // <b>떨어지는 조각은 공중에서 구른다</b> [지시 8번, 2026-09-30].
                //
                // 위 spin은 Z축 하나라 <b>화면 안에서</b> 돌 뿐이고, 판이 판인 것은 그대로다.
                // 조각 무리가 나란히 아래로 미끄러지면 눈은 그걸 판 한 장으로 읽는다 —
                // 이것이 "2D 이미지"로 보이던 마지막 이유다.
                // 축을 세 방향으로 열어야 옆면이 빛을 받으며 돌아가 두께가 드러난다.
                bool rolling = fall.Tumbles && pattern.Falling(s);
                Quaternion roll = rolling
                    ? fall.TumbleOf(s, pattern.FallElapsed(s)) : Quaternion.identity;

                verts[v] = (Vector3)c + move;

                for (int i = 0; i < e; i++)
                {
                    // 무게중심으로 당겨 틈을 만들고, 중심을 축으로 살짝 돌린다.
                    // 다각형은 변이 제각각 기울어 있어 변을 따로 미는 것보다 이쪽이 정확하다.
                    Vector2 r = cell.Points[i] - c;
                    float len = r.magnitude;
                    if (len > 1e-5f) r *= 1f - shrink;      // 비율이라 파편 크기에 알아서 맞는다

                    // 구르는 조각은 무게중심을 축으로 <b>통째로</b> 돈다. 굴림이 켜져 있으면
                    // 평면 회전(spin)은 쓰지 않는다 — 두 회전을 겹치면 축이 싸운다.
                    Vector3 r3 = rolling
                        ? roll * new Vector3(r.x, r.y, 0f)
                        : new Vector3(r.x * cos - r.y * sin, r.x * sin + r.y * cos, 0f);
                    verts[ring + i] = (Vector3)c + r3 + move;
                }

                // 두께를 미는 방향도 같이 돈다. 앞면만 돌리고 이것을 +Z로 두면
                // 조각이 <b>찢어진다</b> — 앞판은 굴러가는데 옆벽은 제자리를 본다.
                Vector3 depthDir = rolling ? roll * Vector3.forward : Vector3.forward;

                for (int i = 0; i < e; i++)
                {
                    Vector3 a = verts[ring + i], b = verts[ring + (i + 1) % e];

                    // 옆면은 <b>건너편이 없어진 변에만</b> 편다. 살아 있으면 넓이 0으로 접어
                    // 채움 비용을 없앤다 — 정점 수는 어차피 고정이다.
                    // 앞면이 carve만큼 물러났으므로 옆벽은 그만큼 짧다 — 뒷면은 제자리다.
                    Vector3 d = depthDir * Mathf.Max(0f, Side(cell.Neighbour[i]) - carve);
                    int o = side + i * 4;
                    verts[o] = a; verts[o + 1] = a + d; verts[o + 2] = b + d; verts[o + 3] = b;
                }

                // <b>법선도 같이 돌려야 빛이 따라온다.</b> BuildMesh가 쓴 법선은 조각이
                // 벽에 붙어 있을 때 기준이라, 구르는 조각에 그대로 두면 아무리 돌려도
                // 밝기가 안 변한다 — 구르게 만든 이유가 통째로 사라진다.
                //
                // 되돌릴 걱정은 없다. 떨어지는 조각이 낙하를 끝내는 길은 Gone 하나뿐이고
                // (TickFall), 그때는 정점이 한 점으로 접혀 아예 안 보인다.
                if (rolling)
                {
                    for (int k = vertStart[s]; k < vertStart[s + 1]; k++)
                        normals[k] = roll * baseNormals[k];
                    anyRolling = true;
                }
            }

            mesh.vertices = verts;

            // 구르는 것이 있을 때만 법선을 올린다. 평소에는 BuildMesh가 쓴 것 그대로다.
            //
            // <b>되돌리는 길은 필요 없다.</b> 구르기를 멈추는 길은 Gone 하나뿐이고(TickFall),
            // 그때 정점이 한 점으로 접혀 아예 안 그려진다. 그러니 normals에 남는 돌아간 법선은
            // 전부 <b>보이지 않는 조각</b>의 것이다. 안 구르는 자리는 BuildMesh가 넣은 값 그대로다.
            if (anyRolling) mesh.normals = normals;

            mesh.RecalculateBounds();

            if (outline != null) outline.Refresh(pattern, verts, vertStart);
        }




        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }
    }
}
