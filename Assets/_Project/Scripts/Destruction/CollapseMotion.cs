using UnityEngine;

namespace Demo.Destruction
{
    /// <summary>
    /// 지지를 잃은 구조물의 낙하 연출과 접촉 판정 (TECH_SPEC §3·§4).
    ///
    /// <b>아직 구현되지 않았다. D7 작업이다.</b>
    ///
    /// <b>물리가 아니다.</b> 낙하는 트윈이고, 접촉은 매 프레임 `Physics.OverlapSphere`로
    /// 플레이어만 검사한다. Rigidbody를 달면 구조물 60개가 전부 시뮬레이션 대상이 되고,
    /// 모바일에서 감당이 안 되며 결과가 결정적이지 않아 튜닝도 불가능해진다.
    ///
    /// 닿으면 `GameLoop.ApplyStun(StructureData.collapseStunSeconds)`.
    /// 체력바가 없으므로 피해는 전부 시간 손실로 환산된다 (GAME_DESIGN §6).
    ///
    /// 여기에 PART B §11의 "붕괴 반응 예산" 체계를 붙일지는 <b>아직 미결</b>이다 (HANDOFF §2.2).
    /// 채택하면 낙하 경고 시간을 역산해야 하고, 그 값이 견인량에 따라 달라진다.
    /// </summary>
    public sealed class CollapseMotion : MonoBehaviour
    {
        static bool s_warned;

        /// <summary>낙하를 시작한다. 끝나면 대상이 Rubble이 되고 잔해가 뿌려진다.</summary>
        public void Begin(Destructible target, float warningSeconds, float fallSeconds)
        {
            if (!s_warned)
            {
                s_warned = true;
                Debug.LogWarning("[CollapseMotion] 붕괴 낙하가 아직 구현되지 않았다 (D7). " +
                                 "TECH_SPEC §4 '낙하 접촉' 참조 — 트윈 + OverlapSphere, 물리 아님.");
            }

            // TODO(D7):
            //  1. warningSeconds 동안 대상이 흔들리며 예고한다 (플레이어가 피할 시간)
            //  2. fallSeconds 동안 아래로 트윈. 회전을 약간 섞으면 무게가 실린다
            //  3. 매 프레임 Physics.OverlapSphere로 플레이어만 검사
            //  4. 닿으면 GameLoop.ApplyStun(target.Data.collapseStunSeconds)
            //  5. 끝나면 target.ForceRubble() + DebrisSpawner.SpawnFor()
        }
    }
}
