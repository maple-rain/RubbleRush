using Demo.Collection;
using Demo.Core;
using UnityEngine;

namespace Demo.UI
{
    /// <summary>
    /// 인게임 HUD — 타이머, 할당량 진행, 구멍 반경, 견인 적재량 (TECH_SPEC §3).
    ///
    /// <b>아직 구현되지 않았다. D10 작업이다.</b>
    ///
    /// 지금 그 역할은 <see cref="Demo.Utils.DebugHotkeys"/>의 OnGUI 오버레이가 대신하고 있다.
    /// 그쪽은 시연·검증용이고 D15 동결 때 제거 대상이므로, 출시용 HUD는 따로 필요하다.
    ///
    /// 설계상 주의할 것 둘:
    /// <list type="bullet">
    /// <item>체력바가 없다. 자원은 <b>타이머 하나</b>뿐이다 — 피해는 전부 시간 손실로 환산된다
    ///       (GAME_DESIGN §6). UI가 가벼워지는 것이 이 선택의 배당금이니 도로 늘리지 말 것.</item>
    /// <item><b>경험치는 화면에 보이지 않는다</b> [확정]. 플레이어는 숫자가 아니라 구멍이 커지는
    ///       것으로 진행을 읽는다. 단 이 결정은 추후 번복될 수 있다고 명시돼 있으므로,
    ///       경험치 바를 넣을 자리는 비워 두되 기본은 꺼 둔다.</item>
    /// </list>
    /// </summary>
    public sealed class HUD : MonoBehaviour
    {
        [SerializeField, Tooltip("경험치 바를 보여줄지. 기본은 꺼짐 — 설계상 보이지 않는 값이다")]
        bool showXpBar = false;

        /// <summary>경험치 바를 그릴지. 이 결정은 추후 번복될 수 있어 스위치로 남겨 둔다.</summary>
        public bool ShowXpBar { get => showXpBar; set => showXpBar = value; }

        GameLoop loop;
        Hole hole;
        CargoTow cargo;

        static bool s_warned;

        void Awake()
        {
            loop = GameLoop.Instance;
            cargo = FindFirstObjectByType<CargoTow>();

            if (s_warned) return;
            s_warned = true;
            Debug.LogWarning("[HUD] 출시용 HUD가 아직 구현되지 않았다 (D10). " +
                             "지금 화면에 보이는 것은 DebugHotkeys의 시연용 오버레이이며 " +
                             "D15 동결 때 제거 대상이다.");
        }

        void Update()
        {
            if (loop == null) loop = GameLoop.Instance;
            if (hole == null && loop != null) hole = loop.ActiveHole;

            // TODO(D10): Canvas + TextMeshPro로 아래를 그린다.
            //  - 남은 시간 (loop.TimeRemaining) — 유일한 실패 조건이므로 가장 크게
            //  - 할당량 진행 (loop.Collected / loop.Quota)
            //  - 구멍 반경 (hole.Radius) — 숫자보다 "지금 무엇을 넣을 수 있는가"의 표현으로
            //  - 견인 적재량 (cargo.TotalUnits), 그리고 막힘 상태 경고
            //  - showXpBar가 켜졌을 때만 hole.LevelProgress01
            //  폰트는 Assets/_Project/Art/Fonts/ (배민 도현·을지로) 중에서 고른다.
        }
    }
}
