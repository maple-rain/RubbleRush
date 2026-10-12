using Demo.Core;
using Demo.Data;
using Demo.Destruction;
using UnityEngine;

namespace Demo.Player
{
    /// <summary>
    /// 근접한 건물을 찾아 "부술 수 있다"는 상태를 만든다 [지시 2026-09-22].
    ///
    /// 파괴가 필드에서 일어나지 않게 되면서 <see cref="Breaker"/>가 하던 일이 둘로 갈렸다.
    /// 여기는 <b>대상을 고르고 들어갈지 판단하는 것</b>까지만 하고,
    /// 실제 파괴는 <see cref="Destruction.DemolitionSession"/>이 파괴 화면에서 맡는다.
    ///
    /// 조준이 없으므로 대상 선택은 <b>가장 가까운 것</b> 하나다. 고를 수 있게 만들면
    /// 조준이 생기고, 그건 [확정]으로 배제된 것이다.
    /// </summary>
    public sealed class InteractionProbe : MonoBehaviour
    {
        [SerializeField] InputRouter input;
        [SerializeField] PlayerController player;
        [SerializeField] Collection.CargoTow cargo;

        [SerializeField, Tooltip("대상이 있는 레이어")]
        LayerMask targetMask = ~0;

        [SerializeField, Tooltip("이 속도 아래여야 상호작용이 뜬다. 지나가며 눌리는 것을 막는다.\n" +
                 "<b>0.6은 어떤 속도로도 만족할 수 없는 값이었다</b> [고침 2026-10-01].\n" +
                 "PlayerController가 입력 크기를 무시하고 0 아니면 전속력을 내기 때문이다 — " +
                 "실측하면 정지 0.00 / 걷기 2.24 / 달리기 6.00 m/s 셋뿐이고, " +
                 "0이 아닌 가장 느린 값이 문턱의 3.7배다. 그래서 '천천히 다가가면 된다'가 아니라 " +
                 "<b>완전히 멈춰야만</b> 열렸고, 막히는 동안에는 프롬프트도 안 떠서 " +
                 "플레이어가 E를 누를 수 있는지조차 알 수 없었다 [지적 2026-10-01].\n" +
                 "3.0은 걷기(2.24)는 통과시키고 달리기(6.00)는 막는 값이다 [제안·미승인] — " +
                 "'지나가며 눌리는 것을 막는다'는 원래 의도가 그것이다. " +
                 "걷기에서도 막고 싶으면 2.0 아래로, 달리기에서도 열려면 6.1 위로 둔다")]
        float stillSpeedThreshold = 3.0f;

        readonly Collider[] hits = new Collider[16];

        /// <summary>지금 들어갈 수 있는 대상. 없으면 null.</summary>
        public Destructible Candidate { get; private set; }

        /// <summary>상호작용 키를 화면에 띄워야 하는가. HUD가 읽는다.</summary>
        public bool PromptVisible => Candidate != null;

        /// <summary>
        /// 프롬프트가 안 뜨는 이유. 화면에 그대로 보여줘도 되는 문구다.
        /// "왜 안 되지"를 플레이어가 추측하게 두면 안 된다.
        /// </summary>
        public string BlockedReason { get; private set; }

        void Awake()
        {
            if (input == null) input = GetComponent<InputRouter>();
            if (player == null) player = GetComponent<PlayerController>();
            if (cargo == null) cargo = GetComponent<Collection.CargoTow>();
        }

        void Update()
        {
            GameModeController mode = GameModeController.Instance;
            if (mode == null || !mode.IsField) { Candidate = null; return; }

            Candidate = null;
            BlockedReason = null;
            if (player == null) return;

            // <b>대상을 먼저 찾는다</b> [고침 2026-10-12]. 예전에는 막힌 조건을 먼저 보고
            // 바로 빠져나갔는데, 그러면 <b>근처에 건물이 없어도</b> 사유가 들어찬다 —
            // 짐을 끌고 빈 벌판을 걸어가는 내내 "견인 중"이 떠 있었다.
            // 사유는 <b>부술 것이 눈앞에 있는데 못 부를 때</b>만 뜻이 있다.
            Destructible near = FindNearest();
            if (near == null) return;

            // 넘어져 있는 동안은 아무 말도 안 한다. 쓰러진 그림이 이미 이유다.
            if (player.IsStunned) return;

            // 끌고 가는 중에는 부술 수 없다 — 두 동사가 갈려야 판단이 된다 (GAME_DESIGN §3).
            // 이것 하나만 글로 말한다. 짐을 놓으면 열린다는 것이 화면에 안 적혀 있다.
            if (cargo != null && !cargo.IsEmpty) { BlockedReason = "견인 중"; return; }

            // <b>달리는 중이라는 말은 안 띄운다</b> [지시 2026-10-12]. 멈추면 저절로 열리는
            // 것이라 글로 설명할 값어치가 없고, 이동이 잦은 만큼 화면에 늘 떠 있게 된다.
            // 문턱 자체는 그대로다 — 지나가며 눌리는 것을 막는 것이 원래 목적이다.
            if (player.CurrentSpeed > stillSpeedThreshold) return;

            Candidate = near;

            if (input != null && input.InteractPressedThisFrame)
                mode.EnterDemolition(near);
        }

        Destructible FindNearest()
        {
            GameConfig cfg = GameConfigRef.Current;
            if (cfg == null) return null;

            int count = Physics.OverlapSphereNonAlloc(
                transform.position, cfg.breakerRange, hits, targetMask, QueryTriggerInteraction.Ignore);

            Destructible best = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                Destructible d = Destructible.From(hits[i]);
                if (d == null || !d.IsBreakable) continue;

                float sqr = (d.transform.position - transform.position).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = d; }
            }

            return best;
        }

        void OnDrawGizmosSelected()
        {
            GameConfig cfg = GameConfigRef.Current;
            if (cfg == null) return;
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, cfg.breakerRange);
        }
    }
}
