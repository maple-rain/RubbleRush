using Demo.Core;
using UnityEngine;

namespace Demo.UI
{
    /// <summary>
    /// 구역 종료 화면 — 클리어/실패와 항목별 내역 (TECH_SPEC §3).
    ///
    /// <b>아직 구현되지 않았다. D10 작업이다.</b>
    ///
    /// 지금은 <see cref="GameLoop.End"/>가 콘솔에 한 줄 찍고 끝난다. 플레이어에게는
    /// 아무것도 안 보이고, 구역이 끝나도 화면이 그대로 멈춰 있을 뿐이다.
    ///
    /// 항목별 내역은 트레일러 소재이기도 하다 — 부순 구조물 수, 구멍이 커진 단계,
    /// 남은 시간처럼 "내가 이만큼 부쉈다"가 숫자로 보이는 화면이 D19 편집에서 쓰인다.
    /// </summary>
    public sealed class ResultScreen : MonoBehaviour
    {
        static bool s_warned;

        /// <summary>GameLoop가 구역을 끝내면서 부른다.</summary>
        public void Show(GameLoop.Result result, int collected, int quota, float timeRemaining)
        {
            if (!s_warned)
            {
                s_warned = true;
                Debug.LogWarning("[ResultScreen] 결과 화면이 아직 구현되지 않았다 (D10). " +
                                 "구역이 끝나도 플레이어에게 아무것도 보이지 않는다.");
            }

            // TODO(D10):
            //  - 클리어 / 시간 초과를 크게
            //  - 수집 collected / quota, 남은 시간
            //  - 부순 구조물 수, 구멍 최종 단계와 반경
            //  - 다시 하기 / 다음 구역 버튼
            //  GameLoop.End()에서 이 메서드를 부르도록 연결하는 것도 D10에 같이 한다.
        }
    }
}
