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

        [SerializeField, Tooltip("이 속도 아래여야 상호작용이 뜬다. 지나가며 눌리는 것을 막는다")]
        float stillSpeedThreshold = 0.6f;

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

            if (player == null || player.IsStunned) { BlockedReason = "정지 중"; return; }

            // 끌고 가는 중에는 부술 수 없다 — 두 동사가 갈려야 판단이 된다 (GAME_DESIGN §3)
            if (cargo != null && !cargo.IsEmpty) { BlockedReason = "견인 중"; return; }

            if (player.CurrentSpeed > stillSpeedThreshold) { BlockedReason = "이동 중"; return; }

            Destructible near = FindNearest();
            if (near == null) { BlockedReason = null; return; }

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
