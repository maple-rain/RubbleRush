using UnityEngine;

namespace Demo.Collection
{
    /// <summary>
    /// 그물 <b>안쪽</b>을 푼다 — 잔해가 테두리와 서로에게 부딪혀 끝쪽에 쌓인다 [지시 2026-10-08].
    ///
    /// <b>왜 새로 만드는가.</b> 기존 <see cref="CargoTow.PlaceSlots"/>는 잔해의 자리를
    /// 해바라기 나선으로 <b>계산해서 꽂는다</b>. 그래서 잔해는 무엇에도 밀릴 수 없고,
    /// 배열은 움직임과 무관하게 <b>언제나 원</b>이다. 기획 의도는 그 반대였다 —
    /// "둘러싸면 원이 되고, 그걸 끌면 당기는 쪽과 짐이 있는 쪽 때문에 <b>타원</b>이 되고,
    /// 그 안에서 케이블과 잔해가 부딪혀 <b>끝쪽에 모인다</b>".
    /// 즉 <b>그물이 경계를 만들고 잔해가 그 안에서 밀린다</b>. 인과가 거꾸로였다.
    ///
    /// <b>무엇이 모양을 만드는가.</b> 아무도 타원을 그리지 않는다. 테두리는 길이가 고정된
    /// 닫힌 사슬이고(<see cref="CableRope"/>), 손이 한 점을 당기고, 잔해가 뒤쪽 테두리를
    /// 바깥으로 민다. 앞은 당겨져 좁아지고 뒤는 눌려 불룩해진다 — 그 결과가 타원이다.
    /// 모양은 <b>출력</b>이지 입력이 아니다.
    ///
    /// <b>비용.</b> 쌍 비교는 <c>n²</c>이지만 여기서 n은 <b>그물 안에 든 수</b>다.
    /// 맵 전체 잔해 500개가 아니다 — 실측상 한 번에 끄는 최대가 32개이므로 496쌍이고,
    /// 테두리 16점과의 비교가 512회다. <see cref="Hazard.ObstacleField"/>가 500개 질의에
    /// 0.0667ms를 쓰는 것과 같은 자릿수다. (예산표의 "동시 잔해 500"은 맵에 떠 있는 수이지
    /// 한 그물에 든 수가 아니다 — 이전에 내가 그 둘을 섞어서 "물리는 못 쓴다"고 보고했다.)
    ///
    /// <b>이 클래스가 안 하는 일</b>: 건물·토대와의 충돌은 <see cref="Hazard.DebrisSnag"/>가
    /// 따로 본다. 여기서는 <b>그물과 잔해</b>, <b>잔해끼리</b>만 푼다.
    /// </summary>
    [System.Serializable]
    public sealed class NetPocket
    {
        [SerializeField, Range(1, 6), Tooltip("구속을 몇 번 반복해 풀 것인가.\n" +
                 "더 돌린다고 덜 겹치지 않는다 — 실측(견고 32개): 2회 28.6쌍 · 4회 33.1 · 6회 34.4로 " +
                 "오히려 늘고 비용만 2.3배가 된다. 꽉 찬 더미는 매 프레임 다시 눌리므로 " +
                 "반복은 수렴이 아니라 <b>조이기</b>로 작용한다.\n" +
                 "테두리를 새어 나가는 것은 2회로 이미 0이다")]
        int iterations = 2;

        [SerializeField, Range(0f, 1f), Tooltip("바닥 마찰. 1에 가까우면 잔해가 즉시 멎어 " +
                 "그물이 지나가도 안 따라오고, 0이면 영원히 미끄러진다")]
        float groundDrag = 0.12f;

        [SerializeField, Range(0.05f, 0.95f), Tooltip("부딪혔을 때 테두리가 얼마나 밀려나는가.\n" +
                 "작으면 그물이 쇠틀에 가까워 잔해만 밀리고 모양이 덜 변한다.\n" +
                 "크면 그물이 종잇장이라 잔해가 테두리를 뚫는다. 타원의 깊이를 정하는 값이다.\n" +
                 "양 끝은 막아 둔다 — 0에서는 반작용이 없어 잔해가 안 움직이는 벽에 끝없이 눌려 " +
                 "테두리가 한 점으로 무너졌다 (실측: 가로세로비 0, 비용 7배). 1에서는 구속이 사라진다")]
        float ropeGive = 0.35f;

        [SerializeField, Tooltip("한 프레임에 잔해가 움직일 수 있는 최대 거리(m). " +
                 "그물이 빨리 지나갈 때 잔해를 뚫고 나가는 것을 막는다")]
        float maxStep = 0.6f;

        Vector3[] pos, prev;
        float[] rad;
        int count;

        /// <summary>그물에 든 조각 수.</summary>
        public int Count => count;

        // 튜닝 도구가 쓸어 보기 위한 입구. 값은 기획자가 정한다 — 여기 기본값은 [제안·미승인]이다.
        public int Iterations { get => iterations; set => iterations = Mathf.Clamp(value, 1, 6); }
        public float RopeGive { get => ropeGive; set => ropeGive = Mathf.Clamp(value, 0.05f, 0.95f); }
        public float GroundDrag { get => groundDrag; set => groundDrag = Mathf.Clamp01(value); }

        public Vector3 PositionOf(int i) => pos[i];

        /// <summary>푼 뒤의 직전 위치. 부른 쪽이 조각에 되돌려 줘야 속도가 이어진다.</summary>
        public Vector3 PrevOf(int i) => prev[i];

        // ── 실측용 — 이 모형이 주장하는 것을 밖에서 셀 수 있어야 한다 ──────────────
        /// <summary>테두리 밖으로 나간 조각 수. 구속이 성립하면 0이다.</summary>
        public int Escaped { get; private set; }

        /// <summary>서로 파고든 쌍의 수. 분리가 성립하면 0이다.</summary>
        public int Overlaps { get; private set; }

        /// <summary>테두리를 밀고 있는 접촉 수. 0이면 잔해가 그물에 안 닿아 모양이 안 변한다.</summary>
        public int Contacts { get; private set; }

        /// <summary>조각 수에 맞춰 자리를 잡는다. 늘어날 때만 다시 잡는다 — 매 프레임 new 하지 않는다.</summary>
        public void Begin(int n)
        {
            if (pos == null || pos.Length < n)
            {
                int cap = Mathf.NextPowerOfTwo(Mathf.Max(8, n));
                System.Array.Resize(ref pos, cap);
                System.Array.Resize(ref prev, cap);
                System.Array.Resize(ref rad, cap);
            }
            count = n;
            Escaped = Overlaps = Contacts = 0;
        }

        /// <summary>
        /// 조각 하나의 현재 상태를 넣는다. 속도는 변수가 아니라 <paramref name="was"/>와의
        /// 차이로 들어온다 — 그래서 <b>부른 쪽이 조각마다 직전 위치를 들고 있어야 한다</b>
        /// (<see cref="Debris.TowPrev"/>). 여기서 인덱스로 들고 있으면 목록 중간이 빠질 때 섞인다.
        /// </summary>
        public void Set(int i, Vector3 p, Vector3 was, float r)
        {
            pos[i] = p;
            prev[i] = was;
            rad[i] = Mathf.Max(0.05f, r);
        }

        /// <summary>
        /// 한 프레임 푼다. <paramref name="rope"/>는 이미 이번 프레임 전진을 마친 상태여야 한다 —
        /// 테두리가 먼저 움직이고, 그 움직인 테두리가 잔해를 민다. 순서가 바뀌면 한 프레임 늦는다.
        /// </summary>
        public void Solve(float dt, CableRope rope)
        {
            if (count == 0 || dt <= 0f || rope == null || !rope.Ready) return;

            // <b>가두고 나서 떼어낸다</b>, 그 반대가 아니다 [고침 2026-10-08].
            // 떼어낸 뒤에 가두면 마지막 한 수가 안쪽으로 미는 것이라 다시 겹친다 —
            // 실측에서 잔해 32개일 때 겹친 쌍이 15개로 남았다.
            Integrate(dt);
            for (int k = 0; k < iterations; k++)
            {
                Contain(rope);
                Separate();
            }
            Count_(rope);
        }

        /// <summary>① 적분 — 테두리와 같은 베를레다. 속도는 (지금 − 직전)에 들어 있다.</summary>
        void Integrate(float dt)
        {
            float keep = 1f - Mathf.Clamp01(groundDrag);
            float lim = Mathf.Max(0.01f, maxStep);
            for (int i = 0; i < count; i++)
            {
                Vector3 v = (pos[i] - prev[i]) * keep;
                v.y = 0f;
                if (v.sqrMagnitude > lim * lim) v = v.normalized * lim;
                prev[i] = pos[i];
                pos[i] += v;
            }
        }

        /// <summary>
        /// ② 잔해끼리 밀어낸다. <b>이것이 "끝쪽에 모인다"를 만든다</b> —
        /// 뒤쪽 테두리에 눌린 조각이 뒤에 오는 조각을 받아 주므로 더미가 쌓인다.
        /// 밀어내기가 없으면 전부 테두리 한 줄에 달라붙고 가운데가 빈다.
        /// </summary>
        void Separate()
        {
            for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                {
                    Vector3 d = pos[j] - pos[i]; d.y = 0f;
                    float need = rad[i] + rad[j];
                    float sq = d.sqrMagnitude;
                    if (sq >= need * need || sq < 1e-8f) continue;

                    float len = Mathf.Sqrt(sq);
                    Vector3 fix = d * ((need - len) / len * 0.5f);
                    pos[i] -= fix; pos[j] += fix;
                }
        }

        /// <summary>
        /// ③ 테두리 안에 가둔다. 잔해는 안쪽으로, 테두리는 <b>같은 힘만큼 바깥으로</b> 밀린다 —
        /// 반작용이 없으면 그물이 쇠틀이 되어 모양이 영원히 안 변한다.
        /// </summary>
        void Contain(CableRope rope)
        {
            int n = rope.Count;
            Vector3 cen = rope.Centroid;
            float give = Mathf.Clamp(ropeGive, 0.05f, 0.95f);

            for (int k = 0; k < count; k++)
            {
                // <b>가장 깊이 파고든 선분 하나만 푼다</b> [고침 2026-10-08].
                //
                // 선분마다 밀기를 더하면 뒤쪽 테두리에 눌린 조각이 양옆 선분 서넛에게
                // 동시에 떠밀려 더미 속으로 쑤셔 박힌다 — 그 다음 분리가 못 따라가서
                // 겹친 쌍이 15개로 남았다. 깊은 것 하나만 풀고 반복에 맡기면 수렴한다.
                int hit = -1; float worst = 0f; float hitT = 0f; Vector3 hitDir = Vector3.zero;

                for (int i = 0; i < n; i++)
                {
                    Vector3 c = ClosestOnSegment(rope.At(i), rope.At((i + 1) % n), pos[k], out float t);

                    Vector3 away = pos[k] - c; away.y = 0f;
                    Vector3 inward = cen - c; inward.y = 0f;
                    if (inward.sqrMagnitude < 1e-8f) continue;

                    float dist = away.magnitude;
                    bool outside = Vector3.Dot(away, inward) < 0f;
                    float push = outside ? dist + rad[k] : rad[k] - dist;
                    if (push <= worst) continue;

                    worst = push; hit = i; hitT = t; hitDir = inward.normalized;
                }

                if (hit < 0) continue;
                pos[k] += hitDir * (worst * (1f - give));
                rope.Push(hit, hitDir * (-worst * give * (1f - hitT)));
                rope.Push((hit + 1) % n, hitDir * (-worst * give * hitT));
            }

            rope.Relax();   // 밀린 테두리의 길이 구속을 다시 맞춘다
        }

        /// <summary>실측용 집계. 주장을 숫자로 돌려준다.</summary>
        void Count_(CableRope rope)
        {
            Escaped = Overlaps = Contacts = 0;
            int n = rope.Count;
            Vector3 cen = rope.Centroid;

            for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                {
                    Vector3 d = pos[j] - pos[i]; d.y = 0f;
                    float need = rad[i] + rad[j];
                    if (d.sqrMagnitude < need * need - 1e-4f) Overlaps++;
                }

            for (int k = 0; k < count; k++)
            {
                bool out_ = false, touch = false;
                for (int i = 0; i < n; i++)
                {
                    Vector3 c = ClosestOnSegment(rope.At(i), rope.At((i + 1) % n), pos[k], out _);
                    Vector3 away = pos[k] - c; away.y = 0f;
                    Vector3 inward = cen - c; inward.y = 0f;
                    if (Vector3.Dot(away, inward) < 0f) out_ = true;
                    if (away.magnitude < rad[k] + 0.05f) touch = true;
                }
                if (out_) Escaped++;
                if (touch) Contacts++;
            }
        }

        static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 q, out float t)
        {
            Vector3 ab = b - a; ab.y = 0f;
            float l2 = ab.sqrMagnitude;
            if (l2 < 1e-8f) { t = 0f; return a; }
            Vector3 aq = q - a; aq.y = 0f;
            t = Mathf.Clamp01(Vector3.Dot(aq, ab) / l2);
            return a + ab * t;
        }

        public void Clear() { count = 0; Escaped = Overlaps = Contacts = 0; }
    }
}
