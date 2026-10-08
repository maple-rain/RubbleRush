using System.Text;
using UnityEngine;

namespace Demo.Data
{
    /// <summary>
    /// 파괴 대상 한 종의 정의.
    /// 구성은 벽면 / 기둥 2종 [확정 2026-09-22]. 종 수는 코드가 모르며, 에셋 개수로만 결정된다.
    ///
    /// 수치는 전부 기획자가 채운다. AI는 기본값을 넣지 않았다 —
    /// 빈 칸은 Validate()가 이름으로 짚어낸다.
    /// </summary>
    [CreateAssetMenu(menuName = "Demo/Structure Data", fileName = "Structure_")]
    public sealed class StructureData : ScriptableObject
    {
        [Header("식별")]
        public string id;
        public string displayName;

        [Tooltip("구멍 크기 게이팅용 등급. 벽면 0 / 기둥 1.\n" +
                 "게이팅 규칙 자체는 미정이며 D9에 결정한다 (GAME_DESIGN §10).")]
        public int tier;

        [Header("메시 — 3단계 교체. 물리 파괴가 아니다")]
        [Tooltip("온전")] public GameObject meshIntact;
        [Tooltip("파손")] public GameObject meshDamaged;
        [Tooltip("잔해. 이 상태로 맵에 남는다 — 스카이라인이 바뀌어야 한다")]
        public GameObject meshRubble;

        [Header("파사드 — 부술 때 조각 판에 입히는 면 그림 [지시 2026-09-23]")]
        [Tooltip("정면. 1층에 입구가 있어야 하고, 맨 위는 옥상이어야 한다")]
        public Texture2D facadeFront;
        [Tooltip("우측면. 입구 없음")] public Texture2D facadeRight;
        [Tooltip("후면. 입구 없음")]   public Texture2D facadeBack;
        [Tooltip("좌측면. 입구 없음")] public Texture2D facadeLeft;

        [Header("파괴 단계별 정면 — 필드의 상자에 입힌다 [2026-10-08]")]
        [Tooltip("파손. 금이 가고 벽돌이 떨어진 같은 건물.\n" +
                 "비면 온전한 것을 그대로 쓴다 — 단계가 안 보일 뿐 깨지지 않는다")]
        public Texture2D facadeDamaged;

        [Tooltip("잔해. 1층 그루터기만 남은 같은 건물.\n" +
                 "상자도 RubbleShrink로 납작해지므로 그림의 아랫부분만 보인다")]
        public Texture2D facadeRubble;

        /// <summary>
        /// 그 <b>단계</b>의 정면 그림 [2026-10-08]. 없으면 한 단계 앞의 것으로 물러선다.
        ///
        /// <b>파괴 화면과는 다른 축이다.</b> 거기서는 벽이 보로노이로 조각나 떨어지므로
        /// 그림을 갈아 끼울 자리가 없다 — 단계는 <b>필드에 서 있는 상자</b>가 보여 준다.
        /// </summary>
        public Texture2D FacadeForState(Destruction.Destructible.State state)
        {
            switch (state)
            {
                case Destruction.Destructible.State.Damaged: return facadeDamaged != null ? facadeDamaged : facadeFront;
                case Destruction.Destructible.State.Rubble:  return facadeRubble != null ? facadeRubble : facadeDamaged != null ? facadeDamaged : facadeFront;
                default: return facadeFront;
            }
        }

        /// <summary>
        /// 그 면의 그림. 없으면 null — 부르는 쪽이 건물 머티리얼로 물러선다.
        ///
        /// 조각 판의 UV는 아래가 0, 위가 1이다. 그림도 <b>아래가 1층, 위가 옥상</b>이어야
        /// 층이 무너져 내려앉는 연출과 그림이 맞는다. 뒤집히면 옥상부터 부서진다.
        /// </summary>
        public Texture2D Facade(int face)
        {
            switch (face)
            {
                case Destruction.StructureFace.Front: return facadeFront;
                case Destruction.StructureFace.Right: return facadeRight;
                case Destruction.StructureFace.Back:  return facadeBack;
                case Destruction.StructureFace.Left:  return facadeLeft;
                default: return null;
            }
        }

        /// <summary>
        /// 타격 길이의 두 갈래 [지시 2026-09-22].
        /// 정확한 초는 <see cref="breakTime"/>이 갖고, 이 값은 <b>플레이어가 읽는 분류</b>다 —
        /// "이건 금방 되는 놈, 저건 오래 걸리는 놈"이 종류 단위로 기억되게 하는 것이 목적이다
        /// (3차 지시 "건물 종류당으로 사고를 유도").
        /// </summary>
        public enum BreakClass
        {
            /// <summary>짧게 두드리면 끝난다. 이동 중에 들러도 부담이 없다.</summary>
            Quick,
            /// <summary>오래 두드려야 한다. 들어갈지 말지가 그 자체로 판단이 된다.</summary>
            Tough,
        }

        [Header("파괴")]
        [Tooltip("타격 길이 분류. 초 단위 값은 breakTime이 갖고, 이건 플레이어가 읽는 갈래다")]
        public BreakClass breakClass = BreakClass.Quick;

        [Tooltip("홀드 소요 시간(초)")]
        public float breakTime;

        [Tooltip("켜면 홀드 → 연타 → 최후의 일격 시퀀스로 간다. D12 기능")]
        public bool isHighlight;

        [Tooltip("켜면 붕괴 유발원. 이것이 Rubble이 되면 SupportGraph가 종속 대상을 무너뜨린다")]
        public bool isSupport;

        [Tooltip("파괴 화면에서 이 구조물을 몇 층으로 나눌 것인가. 1이면 층이 없다.\n" +
                 "층을 나누면 조각이 층 경계를 넘지 않아 가로 이음매(바닥 슬래브)가 생기고,\n" +
                 "지지 탐색이 맨 아랫줄에서 출발하므로 **아래층이 사라지면 위층이 떨어진다**.\n" +
                 "규칙을 따로 넣은 게 아니라 연결 판정의 결과다 [지시 2026-09-22].")]
        public int floors = 1;

        [Header("잔해")]
        [Tooltip("파괴 시 흩어지는 잔해 개수")]
        public int debrisCount;

        [Tooltip("산란 반경(m). 케이블 고리 크기를 결정하므로 수집 시간까지 여기서 갈린다")]
        public float debrisScatterRadius;

        [Tooltip("잔해 1개의 단위 무게. 할당량·견인 감속·경험치가 전부 이 단위로 계산된다")]
        public int debrisSizeClass;

        [Tooltip("잔해 1개의 물리적 크기(반경 m). **구멍보다 작아야 들어간다** —\n" +
                 "큰 것을 부순 잔해는 큰 구멍이 필요하다. 무게(debrisSizeClass)와는 별개 축이다")]
        public float debrisRadius;

        [Header("위험")]
        [Tooltip("붕괴에 깔렸을 때 플레이어 정지 시간(초). isSupport가 켜진 대상만 의미가 있다")]
        public float collapseStunSeconds;

        [Header("콜라이더")]
        [Tooltip("BoxCollider 크기. 메시에서 유도하지 않는다 — 생성 메시는 논컨벡스 고폴리다 " +
                 "(CLAUDE.md 규칙 4: MeshCollider 금지)")]
        public Vector3 colliderSize;

        /// <summary>파괴 1회로 들어오는 총 잔해 단위.</summary>
        public int TotalDebrisUnits => debrisCount * debrisSizeClass;

        /// <summary>
        /// 미입력 필드를 찾는다. 전부 채워져 있으면 null.
        /// ZoneBuilder가 구역 생성 시 호출하고, 결과를 에러 로그로 낸다.
        /// </summary>
        public string Validate()
        {
            var sb = new StringBuilder();

            if (string.IsNullOrWhiteSpace(id)) sb.Append("id, ");
            if (meshIntact == null) sb.Append("meshIntact, ");
            if (meshDamaged == null) sb.Append("meshDamaged, ");
            if (meshRubble == null) sb.Append("meshRubble, ");
            if (breakTime <= 0f) sb.Append("breakTime, ");
            if (debrisCount <= 0) sb.Append("debrisCount, ");
            if (debrisScatterRadius <= 0f) sb.Append("debrisScatterRadius, ");
            if (debrisSizeClass <= 0) sb.Append("debrisSizeClass, ");
            if (debrisRadius <= 0f) sb.Append("debrisRadius, ");
            if (colliderSize == Vector3.zero) sb.Append("colliderSize, ");
            // [지시 2026-09-22] 부수는 건물은 기본적으로 2층 이상이다.
            // 1층이면 "아래층을 부수면 위층이 내려앉는다"가 한 번도 일어나지 않는다.
            if (floors < 2) sb.Append("floors(2 이상이어야 함 — 부수는 건물은 2층 이상), ");
            if (isSupport && collapseStunSeconds <= 0f) sb.Append("collapseStunSeconds, ");

            if (sb.Length == 0) return null;
            sb.Length -= 2;
            return $"StructureData '{name}' 미입력: {sb}";
        }
    }
}
