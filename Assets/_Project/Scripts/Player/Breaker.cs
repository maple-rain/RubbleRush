using Demo.Collection;
using Demo.Core;
using Demo.Data;
using Demo.Destruction;
using UnityEngine;

namespace Demo.Player
{
    /// <summary>
    /// 케이블 전개의 게이트.
    ///
    /// <b>파괴는 더 이상 여기서 하지 않는다</b> [지시 2026-09-22]. 건물에 다가가
    /// 상호작용하면 전용 화면으로 넘어가고, 거기서 <see cref="Destruction.DemolitionSession"/>이 맡는다.
    /// 대상 선택은 <see cref="InteractionProbe"/>로 갈라져 나갔다.
    ///
    /// 남은 책임은 셋이다 — 케이블을 언제 펼칠지, 못 먹는 잔해로 막혔는지, 화물을 내려놓을지.
    /// </summary>
    public sealed class Breaker : MonoBehaviour
    {
        [SerializeField] InputRouter input;
        [SerializeField] PlayerController player;
        [SerializeField] CableController cable;
        [SerializeField] CargoTow cargo;

        [SerializeField, Tooltip("이 속도 아래면 '정지'로 본다(m/s)")]
        float stillSpeedThreshold = 0.2f;

        void Awake()
        {
            if (input == null) input = GetComponent<InputRouter>();
            if (player == null) player = GetComponent<PlayerController>();
            if (cable == null) cable = GetComponent<CableController>();
            if (cargo == null) cargo = GetComponent<CargoTow>();
        }

        void Update()
        {
            if (input == null || player == null) return;

            if (player.IsStunned)
            {
                if (cable != null) cable.Tick(false);
                return;
            }

            // 파괴 화면에 들어가 있으면 케이블도 이동도 멈춘다.
            GameModeController mode = GameModeController.Instance;
            if (mode != null && !mode.IsField)
            {
                if (cable != null) cable.Tick(false);
                return;
            }

            // 홀드가 아니라 토글이다 [지시 2026-09-22]. 판단은 그대로 남고 손만 편해진다.
            bool active = input.ActionActive;
            bool moving = player.CurrentSpeed > stillSpeedThreshold;

            // 지금 구멍에 못 넣는 잔해를 끌고 있으면 더 담지 못한다 [지시 2026-09-22].
            // 못 넣는 것을 안은 채 계속 줍는 것을 막아, "먼저 비우고 오라"는 압박을 만든다.
            bool jammed = IsJammed();

            bool deploying = active && moving && !jammed;
            if (cable != null) cable.Tick(deploying);

            if (input.DropPressedThisFrame) DropCargo();
        }

        /// <summary>
        /// 지금 구멍이 삼킬 수 없는 잔해를 끌고 있는가.
        ///
        /// 이 상태에서는 더 담을 수 없다. 그런데 담지도 못하고 부수지도 못하면
        /// (파괴는 화물이 빈 상태를 요구한다) 플레이어가 완전히 막힌다 —
        /// 기둥 잔해만 잔뜩 안은 채 구멍이 작으면 경험치를 얻을 길이 없어진다.
        /// 그래서 <see cref="DropCargo"/>를 탈출구로 둔다.
        /// </summary>
        public bool IsJammed()
        {
            if (cargo == null || cargo.IsEmpty) return false;
            Hole hole = GameLoop.Instance != null ? GameLoop.Instance.ActiveHole : null;
            if (hole == null) return false;
            return cargo.LargestRadius >= hole.Radius;
        }

        /// <summary>끌고 있던 것을 그 자리에 내려놓는다. 잔해는 사라지지 않고 맵에 남는다.</summary>
        public void DropCargo()
        {
            if (cargo == null || cargo.IsEmpty) return;
            int n = cargo.DropAll();
            Debug.Log($"[Breaker] 화물 {n}단위를 내려놓았다.");
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
