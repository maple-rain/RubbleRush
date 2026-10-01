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

            // ② 뭉치를 끌어안는 힘 — 테두리를 목표 반지름 쪽으로 민다
            float hug = Mathf.Clamp01(hugStrength * dt);
            float r = Mathf.Max(0.05f, radius);
            for (int i = 0; i < n; i++)
            {
                Vector3 d = p[i] - center; d.y = 0f;
                float len = d.magnitude;
                if (len < 1e-4f) continue;
                Vector3 want = center + d * (r / len);
                want.y = p[i].y;
                p[i] = Vector3.Lerp(p[i], want, hug);
            }

            // ③ 구속 — 이웃 사이 길이와 바닥. 반복할수록 뻣뻣해진다
            float target = Mathf.Max(0.01f, 2f * Mathf.PI * r / n);
            restLength = Mathf.Lerp(restLength, target, Mathf.Clamp01(6f * dt));
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

            // ④ 손 쪽으로 당긴다 — <b>못 박지 않는다</b> [고침 2026-10-01].
            //
            // 예전에는 p[0]을 손에 그대로 박았다. 그런데 손은 고리 중심에서 줄 길이만큼
            // (잔해 20개면 6.5m) 떨어져 있고, 이웃 간 길이 구속은 1.58m밖에 안 된다.
            // 그래서 p[1], p[2]…가 줄줄이 끌려와 <b>고리가 창처럼 찌그러졌다</b> (실측으로 확인했다).
            // 줄은 테두리의 일부가 아니라 테두리로 가는 별도의 선이다 — Draw가 그린다.
            Hand = hand;
            Near = NearestTo(hand);
            if (pullToHand > 0f)
            {
                Vector3 d = hand - p[Near]; d.y = 0f;
                p[Near] += d * (pullToHand * Mathf.Clamp01(dt * 10f));
            }
        }

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
