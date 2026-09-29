using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 헤드리스 시뮬레이터 — 렌더링 없이 게임 규칙만 돌린다 (TECH_SPEC §7).
    ///
    /// <b>아직 구현되지 않았다. D16~18 작업이다.</b>
    ///
    /// 목표는 하나다 — <b>"의도한 플레이가 최적해인가"</b>를 확인하는 것.
    /// 안전한 벽면 반복이 최적해로 나오면 기둥 보상을 올리거나 시간 비용을 재조정한다.
    ///
    /// 측정 항목:
    /// <list type="bullet">
    /// <item>전략별 기대 처리량 — 종류별 반복 / 몰아서 부수고 한 번에 수집 / 폭발 활용</item>
    /// <item>구멍 성장 곡선과 종류별 해금 시점</item>
    /// <item>구역 제한 시간 대비 할당량 달성률 분포</item>
    /// </list>
    ///
    /// <b>주의</b> — `Tools/balance_sim.py`와 혼동하지 말 것. 그쪽은 기획 단계의 파이썬 모형이고
    /// 입력값이 AI 창작이라 결과를 신뢰하면 안 된다고 문서에 명시돼 있다. 이 시뮬레이터는
    /// 실제 게임 코드(Destructible·CableController·Hole)를 그대로 돌려야 의미가 있다.
    /// 그러려면 견인 감속 곡선(PlayerController.ComputeMoveSpeed)이 먼저 채워져야 한다.
    /// </summary>
    public static class HeadlessSim
    {
        [MenuItem("Demo/도구/헤드리스 시뮬레이션 (미구현)", priority = 110)]
        public static void Run()
        {
            Debug.LogWarning("[HeadlessSim] 아직 구현되지 않았다 (TECH_SPEC §7 · D16~18). " +
                             "전제 조건: PlayerController.ComputeMoveSpeed()의 견인 감속 곡선.");
        }
    }
}
