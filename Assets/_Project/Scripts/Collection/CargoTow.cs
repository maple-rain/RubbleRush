using System.Collections.Generic;
using UnityEngine;

namespace Demo.Collection
{
    /// <summary>
    /// 케이블에 딸려오는 잔해 뭉치. 플레이어에 붙인다.
    ///
    /// <b>잔해를 물리로 끌지 않는다</b> — 잔해 수십 개에 Rigidbody를 달면 모바일에서 죽고,
    /// 줄에 매달린 물체는 물리로 하면 거의 항상 발작한다. 이 결정은 유지된다.
    ///
    /// <b>대신 줄과 매듭에만 물리를 건다</b> [지시 13번, 2026-10-01].
    /// "그물로 잡아 끄는 느낌"의 출처는 잔해 하나하나의 강체 운동이 아니라
    /// ① 줄이 처지고 출렁이는 모양(<see cref="CableRope"/>)과
    /// ③ 꺾을 때 짐이 바깥으로 쏠렸다 돌아오는 것(<see cref="TowTension"/>)이다.
    /// 둘 다 <b>점의 개수가 고정</b>이라 잔해가 500개여도 비용이 같다 — 예산이 그 수다.
    ///
    /// <see cref="Mode"/>로 끄고 켤 수 있다. 어느 조합이 맞는지는 기획자가 직접 보고 정한다.
    /// </summary>
    public sealed class CargoTow : MonoBehaviour
    {
        /// <summary>무엇에 물리를 걸 것인가 [지시 13번]. 기획자가 직접 비교하라고 열어 둔다.</summary>
        public enum PhysicsMode
        {
            /// <summary>예전 그대로. 짐이 앞방향에 못 박혀 따라온다</summary>
            Off = 0,
            /// <summary>③ 장력만 — 꺾으면 짐이 쏠렸다 돌아온다. 줄은 안 보인다</summary>
            TensionOnly = 1,
            /// <summary>① 줄만 — 그물이 보이고 처지지만, 짐의 자리는 예전과 같다</summary>
            RopeOnly = 2,
            /// <summary>①+③ 둘 다</summary>
            Both = 3,
        }

        [SerializeField, Tooltip("어디에 물리를 걸 것인가.\n" +
                 "Off = 2026-10-01 이전 동작 · TensionOnly = ③ · RopeOnly = ① · Both = 둘 다")]
        PhysicsMode mode = PhysicsMode.Both;

        [SerializeField, Tooltip("뭉치가 플레이어 뒤로 떨어지는 거리(m). 장력을 켜면 줄 길이가 된다")]
        float trailDistance = 2.5f;

        [SerializeField, Tooltip("슬롯 사이 간격(m)")]
        float slotSpacing = 0.9f;

        [SerializeField, Tooltip("슬롯을 따라붙는 속도. 클수록 뻣뻣하다")]
        float followLerp = 8f;

        [SerializeField, Range(0f, 1f), Tooltip("바깥쪽 잔해가 얼마나 더 늦게 따라오는가.\n" +
                 "0이면 전부 같이 움직여 판때기처럼 보이고, 크면 그물이 늘어났다 조여든다")]
        float outerLag = 0.55f;

        [SerializeField, Range(-2f, 4f), Tooltip("짐 앞 가장자리와 플레이어 사이에 둘 여유(m).\n" +
                 "0이면 플레이어가 짐의 앞 가장자리에 딱 선다. 음수면 짐 속으로 들어간다.\n" +
                 "실측: 이게 없으면 잔해 12개부터 짐이 플레이어를 덮고, 구역 전체(344개)면 " +
                 "14.19m 앞까지 묻힌다 [고침 2026-10-01]")]
        float leashClearance = 0f;

        [Header("줄 — ① Verlet")]
        [SerializeField] CableRope net = new CableRope();

        [SerializeField, Tooltip("그물을 그릴 선. 비면 안 그린다. 전개 궤적용 선과는 다른 것이다")]
        LineRenderer netLine;

        [Header("매듭 — ③ 장력")]
        [SerializeField] TowTension tension = new TowTension();

        readonly List<Debris> towed = new List<Debris>(64);

        /// <summary>끌고 있는 총 단위. 이동 속도와 카메라 줌이 읽는다.</summary>
        public int TotalUnits { get; private set; }

        public IReadOnlyList<Debris> Towed => towed;
        public bool IsEmpty => towed.Count == 0;

        /// <summary>실측용. 줄이 플레이어 뒤에서 몇 도 벌어져 있는가.</summary>
        public float SwingDegrees => tension.Ready ? tension.SwingDegrees : 0f;

        /// <summary>실측용·기획 비교용. 지금 걸린 물리 방식.</summary>
        public PhysicsMode Mode { get => mode; set => mode = value; }

        /// <summary>실측용. 그물 테두리 점의 월드 좌표. 준비 안 됐으면 0개.</summary>
        public CableRope Net => net;

        bool UseTension => mode == PhysicsMode.TensionOnly || mode == PhysicsMode.Both;
        bool UseRope => mode == PhysicsMode.RopeOnly || mode == PhysicsMode.Both;

        /// <summary>뭉치가 퍼져 있는 반지름. 해바라기 배열은 sqrt로만 자란다.</summary>
        public float DiscRadius => slotSpacing * Mathf.Sqrt(Mathf.Max(1, towed.Count));

        /// <summary>
        /// 지금 줄 길이.
        ///
        /// <b>곱이 아니라 최댓값이다</b> [고침 2026-10-01]. <c>trailDistance + 원판R × 계수</c> 꼴로 쓰면
        /// 한 상수가 모든 개수를 못 맞춘다 — 실측에서 잔해 20개일 때 "플레이어가 짐 밖"과
        /// "짐이 화면 안"을 동시에 만족하는 계수 구간이 0.38~0.44뿐이었고, 32개부터는 아예 빈다.
        /// <c>max(기본, 원판R + 여유)</c>로 쓰면 <b>플레이어가 항상 짐의 앞 가장자리에 선다</b> —
        /// 개수와 무관하게 성립하고, 짐이 작을 때는 기본 거리를 지킨다.
        ///
        /// <see cref="PhysicsMode.Off"/>에서도 같은 식을 쓴다. 이건 물리가 아니라 <b>자리</b>의 문제다.
        /// </summary>
        public float Leash => Mathf.Max(trailDistance, DiscRadius + leashClearance);

        /// <summary>고리가 닫혀 포획된 잔해를 넘겨받는다. 견인 중에 또 둘러싸면 여기로 합쳐진다.</summary>
        public void Add(IReadOnlyList<Debris> captured)
        {
            bool wasEmpty = towed.Count == 0;

            for (int i = 0; i < captured.Count; i++)
            {
                Debris d = captured[i];
                if (d == null || d.IsTowed) continue;

                d.IsTowed = true;
                towed.Add(d);
                TotalUnits += d.SizeClass;
            }

            // 빈 상태에서 처음 받을 때만 펼친다. 매번 펼치면 물리가 지워져 그냥 원이 된다.
            if (wasEmpty && towed.Count > 0) Deploy();
        }

        void Deploy()
        {
            Vector3 back = -transform.forward;
            tension.Length = Leash;
            tension.Reset(transform.position, back);
            net.Reset(transform.position + back * Leash, DiscRadius);
        }

        /// <summary>
        /// 조각 하나를 화물에서 뺀다. 구멍이 삼킬 때 부른다.
        ///
        /// 일괄 반납이 아니라 개체 단위다 — 그물이 구멍 위를 지나가는 동안 들어가는 것만
        /// 차례로 빠지고, 구멍보다 큰 것은 그대로 남아 계속 끌려온다.
        /// </summary>
        public bool Detach(Debris d)
        {
            if (d == null) return false;
            int i = towed.IndexOf(d);
            if (i < 0) return false;

            towed.RemoveAt(i);
            TotalUnits -= d.SizeClass;
            d.IsTowed = false;
            if (towed.Count == 0) Stow();
            return true;
        }

        /// <summary>끌고 있는 것 중 가장 큰 것의 반경. 구멍이 얼마나 커져야 하는지를 HUD가 보여줄 때 쓴다.</summary>
        public float LargestRadius
        {
            get
            {
                float max = 0f;
                for (int i = 0; i < towed.Count; i++)
                    if (towed[i] != null && towed[i].SizeRadius > max) max = towed[i].SizeRadius;
                return max;
            }
        }

        /// <summary>
        /// 끌고 있던 것을 그 자리에 내려놓는다. 풀로 돌려보내지 않는다 —
        /// 잔해는 맵에 남아서 다시 주울 수 있어야 한다.
        /// </summary>
        /// <returns>내려놓은 총 단위</returns>
        public int DropAll()
        {
            int units = TotalUnits;
            for (int i = 0; i < towed.Count; i++)
                if (towed[i] != null) towed[i].IsTowed = false;

            towed.Clear();
            TotalUnits = 0;
            Stow();
            return units;
        }

        void Stow()
        {
            net.Clear();
            tension.Clear();
            if (netLine != null) netLine.positionCount = 0;
        }

        void LateUpdate()
        {
            if (towed.Count == 0)
            {
                if (netLine != null && netLine.positionCount != 0) Stow();
                return;
            }

            float dt = Time.deltaTime;
            Vector3 here = transform.position;
            Vector3 back = -transform.forward;

            // ① 매듭이 어디에 있는가
            Vector3 anchor, dir;
            if (UseTension)
            {
                tension.Length = Leash;              // 짐이 줄거나 늘면 줄 길이도 따라간다
                tension.Tick(dt, here, back, TotalUnits);
                anchor = tension.Position;
                dir = tension.Direction;
            }
            else
            {
                anchor = here + back * Leash;
                dir = back;
            }
            anchor.y = here.y;

            // ② 잔해를 그 둘레에 깐다. 실제로 얼마나 퍼졌는지를 돌려받는다
            float spread = PlaceSlots(anchor, dir, dt);

            // ③ 줄을 푼다
            if (UseRope)
            {
                // 그물을 쥔 손은 플레이어 쪽 — 줄이 거기서 뻗어 나가야 끌려오는 것으로 보인다
                Vector3 hand = here + dir * 0.4f;
                hand.y = here.y;

                // <b>공칭 반지름이 아니라 실제 퍼진 만큼을 감싼다</b> [고침 2026-10-01].
                // DiscRadius(4.02m)로 감싸면 끌리면서 뒤처진 바깥 잔해(실측 5.56m)가
                // 그물 밖으로 삐져나온다 — 그물이 짐을 싼 것으로 안 보인다.
                net.Tick(dt, anchor, spread, hand);
                net.Draw(netLine);
            }
            else if (netLine != null && netLine.positionCount != 0) netLine.positionCount = 0;
        }

        /// <summary>
        /// 그물에 싸여 끌려오는 덩어리다 [지시 2026-09-22].
        ///
        /// 예전에는 뒤로 줄을 세웠는데, 그러면 꼬리처럼 보이고 개수가 늘수록 화면 밖으로 나갔다.
        /// 대신 <b>해바라기 배열(피보나치 나선)</b>로 원판을 채운다 — 개수가 늘어도 반지름이
        /// sqrt로만 자라고 간격이 고르다. 덩어리가 구멍 위를 지나가면 바깥쪽부터 빠진다.
        ///
        /// <b>바깥쪽일수록 늦게 따라온다</b> [지시 13번, 2026-10-01]. 전부 같은 속도로 따라오면
        /// 뭉치가 한 장의 판때기로 움직여서, 그물이 아니라 썰매처럼 보인다.
        /// </summary>
        /// <returns>가장 바깥 잔해까지의 거리(m). 그물이 이만큼을 감싼다</returns>
        float PlaceSlots(Vector3 anchor, Vector3 dir, float dt)
        {
            const float Golden = 2.39996323f;                 // 황금각 ≈ 137.5°
            Vector3 right = new Vector3(dir.z, 0f, -dir.x);   // 뭉치를 줄 방향에 맞춰 돌린다
            float inv = 1f / Mathf.Sqrt(Mathf.Max(1, towed.Count));
            float spreadSqr = 0f, biggest = 0f;

            for (int i = 0; i < towed.Count; i++)
            {
                Debris d = towed[i];
                if (d == null) continue;

                // 구멍이 당기는 중이면 놓아준다. 두 힘이 맞서면 잔해가 그 사이에서 떤다.
                if (d.IsBeingSucked) continue;

                float r = slotSpacing * Mathf.Sqrt(i + 0.5f);
                float a = i * Golden;
                Vector3 slot = anchor + (right * Mathf.Cos(a) + dir * Mathf.Sin(a)) * r;
                slot.y = d.transform.position.y;

                // 바깥쪽(i가 클수록)이 더 느리다. sqrt로 재면 0~1로 고르게 퍼진다.
                float outer = Mathf.Sqrt(i + 0.5f) * inv;
                float rate = followLerp * (1f - outerLag * outer);
                d.transform.position = Vector3.Lerp(d.transform.position, slot,
                                                    1f - Mathf.Exp(-rate * dt));

                // 제곱으로 비교한다 — 잔해 500개에 Sqrt를 500번 부를 이유가 없다.
                // 가장 큰 조각도 여기서 같이 본다. LargestRadius를 부르면 500개를 또 훑는다.
                Vector3 off = d.transform.position - anchor; off.y = 0f;
                if (off.sqrMagnitude > spreadSqr) spreadSqr = off.sqrMagnitude;
                if (d.SizeRadius > biggest) biggest = d.SizeRadius;
            }

            // 조각 반경만큼 더 싸야 그물이 잔해를 스치지 않고 감싼다
            return Mathf.Max(DiscRadius, Mathf.Sqrt(spreadSqr) + biggest);
        }
    }
}
