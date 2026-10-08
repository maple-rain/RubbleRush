using UnityEngine;

namespace Demo.Collection
{
    /// <summary>
    /// 점 몇 개짜리 줄에 물리를 건다 — <b>베를레(Verlet) 적분</b> [지시 13번, 2026-10-01].
    ///
    /// <b>왜 Rigidbody가 아닌가.</b> 기획 요청은 "그물로 잡아 끄는 느낌"이다.
    /// 그 느낌의 출처는 <b>잔해 하나하나의 강체 운동이 아니라 줄이 처지고 출렁이는 모양</b>이다.
    /// 잔해 500개에 Rigidbody를 달면 모바일에서 죽고, 줄에 매달린 강체는 물리 엔진에서
    /// 거의 항상 발작한다 (<see cref="CargoTow"/> 주석에 적힌 기존 결정). 반면 줄 점 스무 개는
    /// 공짜에 가깝고, <b>잔해 개수와 무관하게 비용이 일정하다</b> — 예산이 동시 잔해 500이라
    /// 이 성질이 선택의 전부다.
    ///
    /// <b>왜 속도가 아니라 위치를 들고 있는가.</b> 베를레는 속도를 변수로 두지 않고
    /// <c>이전 위치</c>에서 암묵적으로 끌어낸다. 그래서 위치를 강제로 옮겨도(=구속 조건을 풀어도)
    /// 속도가 저절로 맞춰진다. 줄처럼 <b>구속이 지배하는</b> 물체에 맞는 적분기다 —
    /// 힘으로 풀면 뻣뻣하게 만들수록 터지는데, 여기서는 반복 횟수만 늘리면 된다.
    ///
    /// 쓰는 쪽은 <see cref="CargoTow"/>다. MonoBehaviour가 아니라 <c>[System.Serializable]</c>
    /// 평범한 클래스다 — 이 프로젝트가 <see cref="Player.PostureFix"/>·<see cref="Destruction.FractureFall"/>
    /// 에서 쓰는 것과 같은 꼴이고, 인스펙터에 접히는 묶음 하나로 나온다.
    /// </summary>
    [System.Serializable]
    public sealed class CableRope
    {
        [SerializeField, Range(4, 64), Tooltip("그물 테두리를 이루는 점의 수. " +
                 "비용은 여기에만 비례한다 — 잔해가 몇 개든 상관없다")]
        int ringPoints = 16;

        [SerializeField, Range(1, 8), Tooltip("구속을 몇 번 반복해 풀 것인가. " +
                 "클수록 줄이 뻣뻣해진다. 힘을 키우는 것과 달리 터지지 않는다")]
        int iterations = 3;

        [SerializeField, Tooltip("줄에 걸리는 중력(m/s²). 처짐의 깊이를 정한다. " +
                 "0이면 바닥에 평평하게 눕는다")]
        float gravity = 14f;

        [SerializeField, Range(0f, 0.5f), Tooltip("속도 감쇠. 0이면 영원히 출렁이고, " +
                 "크면 즉시 멎어서 물리처럼 안 보인다")]
        float damping = 0.06f;

        [SerializeField, Tooltip("테두리가 잔해 뭉치를 조이는 힘. " +
                 "클수록 그물이 짐에 착 붙고, 작으면 느슨하게 끌려온다")]
        float hugStrength = 18f;

        [SerializeField, Tooltip("테두리를 <b>자유 사슬</b>로 둔다 [2026-10-08].\n" +
                 "끄면(기본) 위의 조이는 힘이 모든 점을 같은 반지름으로 밀어 — 즉 " +
                 "<b>항상 원</b>이 된다. 켜면 그 힘을 빼고 둘레 길이만 고정해서, " +
                 "손이 당기는 쪽과 잔해가 미는 쪽에 따라 모양이 스스로 정해진다.\n" +
                 "타원을 그리는 코드는 없다 — 타원은 결과다")]
        bool freeChain;

        [SerializeField, Tooltip("바닥 높이(m). 줄이 이 밑으로 안 내려간다")]
        float floorY = 0.06f;

        [SerializeField, Range(0f, 1f), Tooltip("플레이어에게 가장 가까운 테두리 점을 " +
                 "손 쪽으로 얼마나 당길 것인가. 끌려가는 쪽이 먼저 늘어나 보이게 한다.\n" +
                 "1에 가까우면 그 점이 손에 붙어 고리가 창처럼 찌그러진다 [고침 2026-10-01]")]
        float pullToHand = 0.25f;

        Vector3[] p, prev;
        float restLength;
        bool ready;

        public int Count => p != null ? p.Length : 0;
        public bool Ready => ready;
        public Vector3 At(int i) => p[i];

        /// <summary>실측용·기획 비교용. 조이는 힘을 빼고 둘레만 고정할 것인가.</summary>
        public bool FreeChain { get => freeChain; set => freeChain = value; }

        /// <summary>테두리 점 수. 렌더러가 선분 수를 맞출 때 읽는다.</summary>
        public int RingPoints => Mathf.Max(4, ringPoints);

        /// <summary>
        /// 그물을 <paramref name="center"/> 둘레 반지름 <paramref name="radius"/>로 새로 펼친다.
        /// 포획 직후 한 번만 부른다 — 매 프레임 부르면 물리가 매번 지워져 그냥 원이 된다.
        /// </summary>
        public void Reset(Vector3 center, float radius)
        {
            int n = RingPoints;
            if (p == null || p.Length != n) { p = new Vector3[n]; prev = new Vector3[n]; }

            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                p[i] = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                p[i].y = Mathf.Max(floorY, center.y);
                prev[i] = p[i];
            }
            restLength = 2f * Mathf.PI * Mathf.Max(0.01f, radius) / n;
            ready = true;
        }

        /// <summary>
        /// 한 프레임 전진한다.
        /// </summary>
        /// <param name="dt">프레임 시간</param>
        /// <param name="center">잔해 뭉치의 중심 — 그물이 감쌀 대상</param>
        /// <param name="radius">뭉치의 반지름. 짐이 줄면 그물도 조여든다</param>
        /// <param name="hand">플레이어가 줄을 쥔 자리. 가장 가까운 테두리 점이 이쪽으로 당겨진다</param>
        public void Tick(float dt, Vector3 center, float radius, Vector3 hand)
        {
            if (!ready || dt <= 0f) return;
            int n = p.Length;

            // ① 적분 — 속도는 (지금 − 직전)에 들어 있다
            float keep = 1f - damping;
            float g = gravity * dt * dt;
            for (int i = 0; i < n; i++)
            {
                Vector3 vel = (p[i] - prev[i]) * keep;
                prev[i] = p[i];
                p[i] += vel;
                p[i].y -= g;
            }

            // ② 뭉치를 끌어안는 힘 — 테두리를 목표 반지름 쪽으로 민다.
            //
            // <b>이 힘이 원을 강제한다</b>. 모든 점을 center에서 같은 거리로 밀기 때문에,
            // 당기든 밀리든 테두리는 항상 원으로 돌아온다. 그래서 주머니 모형에서는 끈다 —
            // 거기서는 모양이 입력이 아니라 결과여야 한다 [2026-10-08].
            float r = Mathf.Max(0.05f, radius);
            if (!freeChain)
            {
                float hug = Mathf.Clamp01(hugStrength * dt);
                for (int i = 0; i < n; i++)
                {
                    Vector3 d = p[i] - center; d.y = 0f;
                    float len = d.magnitude;
                    if (len < 1e-4f) continue;
                    Vector3 want = center + d * (r / len);
                    want.y = p[i].y;
                    p[i] = Vector3.Lerp(p[i], want, hug);
                }

                // 짐이 줄면 그물도 조여든다. 자유 사슬은 <b>처음 둘러싼 둘레를 그대로 쓴다</b> —
                // 그려 둔 고리가 곧 그물 크기이고, 짐이 줄어도 그물은 안 작아진다.
                float target = Mathf.Max(0.01f, 2f * Mathf.PI * r / n);
                restLength = Mathf.Lerp(restLength, target, Mathf.Clamp01(6f * dt));
            }

            // ③ 구속 — 이웃 사이 길이와 바닥. 반복할수록 뻣뻣해진다
            Relax();

            // ④ 손 쪽으로 당긴다 — <b>못 박지 않는다</b> [고침 2026-10-01].
            //
            // 예전에는 p[0]을 손에 그대로 박았다. 그런데 손은 고리 중심에서 줄 길이만큼
            // (잔해 20개면 6.5m) 떨어져 있고, 이웃 간 길이 구속은 1.58m밖에 안 된다.
            // 그래서 p[1], p[2]…가 줄줄이 끌려와 <b>고리가 창처럼 찌그러졌다</b> (실측으로 확인했다).
            // 줄은 테두리의 일부가 아니라 테두리로 가는 별도의 선이다 — Draw가 그린다.
            Hand = hand;
            Near = NearestTo(hand);

            if (freeChain)
            {
                // <b>손은 당기지 않는다. 쥐고 있다</b> [고침 2026-10-08].
                //
                // 부드러운 lerp로 당기면 그물이 못 따라온다 — 실측에서 잔해 32개일 때
                // 짐이 플레이어 뒤 15.2m까지 떨어졌다. 조이는 힘을 뺀 순간
                // <b>그물을 끌고 갈 주체가 사라진 것</b>이다 (예전에는 그 힘이 모든 점을
                // 플레이어 쪽 매듭으로 당겨서, 끄는 일까지 겸하고 있었다).
                //
                // 줄은 늘어나지 않으므로 거리 상한은 <b>구속</b>이다. 가장 가까운 점 하나만
                // 끌어당기고 <see cref="Relax"/>로 테두리에 퍼뜨린다 — 그 전파가 늦는 만큼
                // 뒤쪽이 처지고, <b>그 처짐이 곧 타원</b>이다. 모양을 그리는 코드는 없다.
                Vector3 d = p[Near] - hand; d.y = 0f;
                float len = d.magnitude;
                if (len > Leash && len > 1e-4f)
                {
                    p[Near] -= d * ((len - Leash) / len);
                    Relax();
                }
            }
            else if (pullToHand > 0f)
            {
                Vector3 d = hand - p[Near]; d.y = 0f;
                p[Near] += d * (pullToHand * Mathf.Clamp01(dt * 10f));
            }
        }

        /// <summary>
        /// 손과 테두리 사이 줄의 길이(m). 자유 사슬일 때만 쓴다 — 이 거리를 넘으면 테두리가 끌려온다.
        /// <see cref="CargoTow"/>가 매 프레임 넣는다.
        /// </summary>
        public float Leash { get; set; } = 2.5f;

        /// <summary>플레이어가 줄을 쥔 자리. Draw가 여기서 선을 시작한다.</summary>
        public Vector3 Hand { get; private set; }

        /// <summary>손에 가장 가까운 테두리 점의 번호. 줄이 여기로 들어간다.</summary>
        public int Near { get; private set; }

        int NearestTo(Vector3 q)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < p.Length; i++)
            {
                Vector3 d = p[i] - q; d.y = 0f;
                float s = d.sqrMagnitude;
                if (s < bd) { bd = s; best = i; }
            }
            return best;
        }

        /// <summary>
        /// 길이·바닥 구속을 다시 맞춘다. 밖에서 <see cref="Push"/>로 테두리를 밀었으면
        /// 이웃 간 길이가 어긋나 있으므로 <b>민 쪽이 직접 불러야 한다</b>.
        /// 베를레의 성질이다 — 위치를 억지로 옮겨도 반복만 돌리면 속도까지 맞아 들어간다.
        /// </summary>
        public void Relax()
        {
            if (!ready) return;
            int n = p.Length;
            for (int k = 0; k < iterations; k++)
            {
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    Vector3 d = p[j] - p[i];
                    float len = d.magnitude;
                    if (len < 1e-5f) continue;
                    Vector3 fix = d * ((len - restLength) / len * 0.5f);
                    p[i] += fix; p[j] -= fix;
                }
                for (int i = 0; i < n; i++) if (p[i].y < floorY) p[i].y = floorY;
            }
        }

        /// <summary>
        /// 테두리 점 하나를 민다 — <see cref="NetPocket"/>의 반작용 입구다.
        /// 이게 없으면 그물은 쇠틀이고, 잔해가 아무리 밀어도 모양이 안 바뀐다.
        /// </summary>
        public void Push(int i, Vector3 delta)
        {
            if (!ready || i < 0 || i >= p.Length) return;
            delta.y = 0f;
            p[i] += delta;
        }

        /// <summary>테두리의 무게중심. 안팎을 가를 때 쓴다 — 볼록하지 않아도 대체로 맞는다.</summary>
        public Vector3 Centroid
        {
            get
            {
                if (!ready) return Vector3.zero;
                Vector3 s = Vector3.zero;
                for (int i = 0; i < p.Length; i++) s += p[i];
                return s / p.Length;
            }
        }

        /// <summary>지금 둘레 길이(m). 자유 사슬이면 처음 둘러싼 고리의 길이 그대로다.</summary>
        public float Perimeter => ready ? restLength * p.Length : 0f;

        /// <summary>
        /// 손에서 뻗어 나가 테두리를 한 바퀴 도는 한 줄로 그린다.
        ///
        /// 선 하나로 그리는 이유는 LineRenderer가 끊어진 구간을 못 그리기 때문이다 —
        /// 손과 고리를 따로 그리려면 렌더러가 둘 필요하고, 그건 드로우콜 하나를 더 쓴다
        /// (예산 100 중 잔해 0개인 씬이 이미 82~84를 쓴다).
        /// 손에서 <see cref="Near"/>로 들어가 고리를 돌아 제자리로 돌아온다.
        /// </summary>
        public void Draw(LineRenderer line)
        {
            if (!ready || line == null) { if (line != null) line.positionCount = 0; return; }
            int n = p.Length;
            if (line.positionCount != n + 2) line.positionCount = n + 2;
            line.SetPosition(0, Hand);
            for (int i = 0; i <= n; i++) line.SetPosition(i + 1, p[(Near + i) % n]);
        }

        public void Clear() { ready = false; }
    }
}
