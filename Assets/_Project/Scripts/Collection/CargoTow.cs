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
    public sealed partial class CargoTow : MonoBehaviour
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

        /// <summary>
        /// 잔해의 자리를 <b>누가 정하는가</b> [지시 2026-10-08]. 인과가 서로 반대라 값이 아니라 모형이다.
        /// </summary>
        public enum HaulShape
        {
            /// <summary>계산해서 꽂는다 — 해바라기 나선. 움직임과 무관하게 <b>항상 원</b>이다</summary>
            Slots = 0,
            /// <summary>그물이 경계를 만들고 잔해가 그 안에서 밀린다. 모양이 <b>결과</b>로 나온다</summary>
            Pocket = 1,
        }

        [SerializeField, Tooltip("어디에 물리를 걸 것인가.\n" +
                 "Off = 2026-10-01 이전 동작 · TensionOnly = ③ · RopeOnly = ① · Both = 둘 다")]
        PhysicsMode mode = PhysicsMode.Both;

        [SerializeField, Tooltip("잔해의 자리를 누가 정하는가.\n" +
                 "Pocket = 그물 안에서 밀린다. 끌면 타원이 되고 끝쪽에 쌓인다 [확정 2026-10-08]\n" +
                 "Slots = 해바라기 나선에 꽂는다(2026-10-08 이전). 끌어도 원 모양 그대로다\n" +
                 "Pocket은 줄(RopeOnly·Both)이 켜져 있어야 뜻이 있다 — 경계가 곧 그물이다")]
        HaulShape shape = HaulShape.Pocket;

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

        [Header("주머니 — 그물 안쪽 [2026-10-08]")]
        [SerializeField, Tooltip("Pocket 모형일 때만 돈다")]
        NetPocket pocket = new NetPocket();

        [Header("매듭 — ③ 장력")]
        [SerializeField] TowTension tension = new TowTension();

        [Header("걸림 — 14번")]
        [SerializeField, Tooltip("끌려오던 잔해가 건물·토대에 걸렸을 때 무엇을 할 것인가")]
        Hazard.DebrisSnag snag = new Hazard.DebrisSnag();

        readonly List<Debris> towed = new List<Debris>(64);

        // 걸린 조각을 모아 뒀다 루프가 끝난 뒤에 처리한다 —
        // 도는 중에 towed를 건드리면 인덱스가 어긋난다. 매 프레임 new 하면 GC가 튄다.
        readonly List<Debris> snagged = new List<Debris>(32);

        /// <summary>끌고 있는 총 단위. 이동 속도와 카메라 줌이 읽는다.</summary>
        public int TotalUnits { get; private set; }

        /// <summary>
        /// 짐의 <b>뒤끝</b>까지의 거리(m) — 플레이어에서 가장 먼 조각의 바깥면까지, 진행 반대 방향으로.
        /// 카메라가 짐을 담을 때 읽는다 [2026-10-08].
        ///
        /// <b>왜 단위 수가 아닌가.</b> 카메라를 <see cref="TotalUnits"/>로 몰았더니 작은 종류에서
        /// 짐이 통째로 화면 밖이었다. 짐 뒤끝 ÷ 단위가 2.50 → 0.54로 <b>전혀 비례하지 않기</b>
        /// 때문이다 — 그물 크기는 조각 수가 아니라 흩어진 넓이와 줄 길이가 정한다.
        /// 실제로 뻗은 만큼을 재서 주면 종류를 안 가린다.
        /// </summary>
        public float LoadBack { get; private set; }

        public IReadOnlyList<Debris> Towed => towed;
        public bool IsEmpty => towed.Count == 0;

        /// <summary>실측용. 줄이 플레이어 뒤에서 몇 도 벌어져 있는가.</summary>
        public float SwingDegrees => tension.Ready ? tension.SwingDegrees : 0f;

        /// <summary>실측용·기획 비교용. 지금 걸린 물리 방식.</summary>
        public PhysicsMode Mode { get => mode; set => mode = value; }

        /// <summary>실측용. 그물 테두리 점의 월드 좌표. 준비 안 됐으면 0개.</summary>
        public CableRope Net => net;

        /// <summary>실측용·기획 비교용. 잔해 자리를 누가 정하는가.</summary>
        public HaulShape Shape { get => shape; set => shape = value; }

        /// <summary>실측용. 그물 안쪽의 구속 결과 — 새어 나간 수, 겹친 쌍, 테두리 접촉 수.</summary>
        public NetPocket Pocket => pocket;

        /// <summary>실측용·기획 비교용. 걸림 정책 [14번].</summary>
        public Hazard.DebrisSnag Snag => snag;

        /// <summary>
        /// 걸려 있는 동안의 속도 배율. 안 걸렸으면 1 [14번].
        /// <see cref="Player.PlayerController"/>가 곱한다 — 견인 감속 곡선(기획자 TODO)과는 별개 축이다.
        /// </summary>
        public float SpeedScale => snag.SpeedScale;

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
                d.TowPrev = d.transform.position;   // 주머니 모형의 속도를 0에서 시작한다
                towed.Add(d);
                TotalUnits += d.SizeClass;
            }

            // 빈 상태에서 처음 받을 때만 펼친다. 매번 펼치면 물리가 지워져 그냥 원이 된다.
            if (wasEmpty && towed.Count > 0) Deploy();

            // <b>막 거둬들인 직후에는 걸림 판정을 쉰다</b> [지시 2026-10-12].
            //
            // 잔해는 제가 나온 건물의 토대 위에 놓인다. 토대가 장애물이 되면 그 조각들은
            // 태어나자마자 장애물 안에 있어서, 원을 그려 거두는 순간 전부 걸린 것으로 판정돼
            // 플레이어가 잘못한 것 없이 벌칙부터 터진다. 끌어내는 동안만 쉬고,
            // 토대를 벗어난 뒤부터가 진짜 판정이다.
            snag.ExemptAfterPickup();
        }

        void Deploy()
        {
            Vector3 back = -transform.forward;
            tension.Length = Leash;
            tension.Reset(transform.position, back);

            // 주머니 모형은 조이는 힘이 있으면 성립하지 않는다 — 그 힘이 원을 강제한다.
            // 기획자가 스위치 하나만 넘기면 되도록 여기서 같이 맞춘다 [2026-10-08].
            net.FreeChain = shape == HaulShape.Pocket;

            if (shape == HaulShape.Pocket)
            {
                // <b>둘러싼 자리에 둘러싼 크기로 편다</b> [고침 2026-10-08].
                //
                // DiscRadius(=0.9√n)는 해바라기 슬롯이 몇 겹으로 깔릴지를 말하는 수이지
                // 플레이어가 실제로 그린 고리가 아니다. 자유 사슬은 둘레가 고정이라
                // 이 수가 그대로 영구적인 그물 크기가 된다 — 잔해 32개면 반경 5.09m,
                // 둘레 32m짜리 자루가 되어 짐이 플레이어 뒤 10m에서 논다 (실측).
                // 지금 잡은 것이 퍼져 있는 만큼으로 재면 둘러싼 모양에서 출발한다.
                net.Reset(CapturedCenter(out float r), r);
            }
            else net.Reset(transform.position + back * Leash, DiscRadius);
        }

        /// <summary>방금 잡은 것들의 중심과, 그것을 다 덮는 반지름.</summary>
        Vector3 CapturedCenter(out float radius)
        {
            Vector3 mid = Vector3.zero;
            int c = 0;
            for (int i = 0; i < towed.Count; i++)
                if (towed[i] != null) { mid += towed[i].transform.position; c++; }
            if (c == 0) { radius = 1f; return transform.position; }
            mid /= c;

            float far = 0f, biggest = 0f;
            for (int i = 0; i < towed.Count; i++)
            {
                if (towed[i] == null) continue;
                Vector3 d = towed[i].transform.position - mid; d.y = 0f;
                if (d.magnitude > far) far = d.magnitude;
                if (towed[i].SizeRadius > biggest) biggest = towed[i].SizeRadius;
            }
            radius = Mathf.Max(0.5f, far + biggest);

            // <b>케이블보다 긴 고리는 그릴 수 없다</b> [고침 2026-10-08].
            //
            // 둘러싼 것이 넓게 흩어져 있으면 이 식이 케이블 길이를 넘는 반지름을 돌려준다 —
            // 견고 건물(흩어짐 8.0m)이면 둘레 57.6m가 나오는데 케이블은 그보다 짧다.
            // 자유 사슬은 둘레가 고정이라 그 수가 영구적인 그물 크기가 되고, 실측에서
            // 짐이 플레이어 뒤 26.1m까지 늘어나 <b>어느 각도에서도 화면에 안 들어왔다</b>.
            // 물리적으로도 틀렸다. 상한은 케이블이 정한다.
            Data.GameConfig cfg = Core.GameConfigRef.Current;
            if (cfg != null && cfg.cableMaxLength > 0f)
                radius = Mathf.Min(radius, cfg.cableMaxLength / (2f * Mathf.PI));
            return mid;
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
            if (units > 0) Utils.GameAudio.Play(Data.Sfx.Drop, transform.position);
            return units;
        }
    }
}
