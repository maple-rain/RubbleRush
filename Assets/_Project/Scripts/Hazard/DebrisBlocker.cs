using UnityEngine;

namespace Demo.Hazard
{
    /// <summary>
    /// 잔해가 점유한 영역을 조회한다 — 케이블 절단 판정에 쓴다 (TECH_SPEC §3).
    ///
    /// <b>아직 구현되지 않았다. 우선순위 4 · D11 작업이다.</b>
    ///
    /// 기획서의 한 줄이 이 클래스의 존재 이유다 (GAME_DESIGN §5.1):
    /// <i>"쌓인 잔해 위로는 케이블이 지나가지 못한다."</i>
    ///
    /// 이 한 줄로 잔해 누적이 별도 페널티가 아니라 수집 메커닉의 일부가 된다 —
    /// 아무렇게나 부수면 자기 동선을 자기가 막는다. 판단이 하나 늘지만 시스템은 늘지 않는다.
    ///
    /// 지금은 이것이 없어서 <b>케이블이 잔해를 그냥 통과한다.</b> 위협 2가 통째로 빠져 있는 셈이다.
    /// </summary>
    public static class DebrisBlocker
    {
        static bool s_warned;

        /// <summary>
        /// 두 점을 잇는 선분이 잔해 더미를 가로지르는가. 케이블 궤적을 그릴 때 매 포인트마다 묻는다.
        /// </summary>
        public static bool IsBlocked(Vector2 from, Vector2 to)
        {
            if (!s_warned)
            {
                s_warned = true;
                Debug.LogWarning("[DebrisBlocker] 잔해의 케이블 차단이 아직 구현되지 않았다 (D11). " +
                                 "지금은 케이블이 잔해를 통과한다 — GAME_DESIGN §5.1의 " +
                                 "'쌓인 잔해 위로는 케이블이 지나가지 못한다'가 빠져 있다.");
            }

            // TODO(D11): Collection.Debris.Active를 훑어 선분-원 교차를 본다.
            //            견인 중인 잔해는 제외한다 — 내가 끌고 있는 것이 내 케이블을 막으면 안 된다.
            //            막히면 CableController가 궤적을 끊고 그때까지의 고리를 포기한다.
            return false;
        }
    }
}
