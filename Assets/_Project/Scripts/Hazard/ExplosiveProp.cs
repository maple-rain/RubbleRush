using Demo.Data;
using UnityEngine;

namespace Demo.Hazard
{
    /// <summary>
    /// 위험 설비 — 연료탱크·가스관·배전반 (TECH_SPEC §3, GAME_DESIGN §6 위협 3).
    ///
    /// <b>아직 구현되지 않았다. 우선순위 4 · D11·D13 작업이다.</b>
    ///
    /// 딜레마는 "폭발을 도구로 쓸 것인가"다. 터뜨리면 광역 파괴라 효율이 최고인데
    /// 가까이 있으면 내가 맞는다. 피해는 시간 손실로 환산된다 (`explosionStunSeconds`).
    ///
    /// 연쇄 파괴는 `HazardData.chainDamageToTier` 이하 등급만 부순다 —
    /// 폭발 하나로 건물 코어까지 날아가면 성장 곡선이 무너진다.
    /// </summary>
    public sealed class ExplosiveProp : MonoBehaviour
    {
        public HazardData Data { get; private set; }
        public bool HasExploded { get; private set; }

        static bool s_warned;

        /// <summary>ZoneBuilder가 구역을 지을 때 부른다.</summary>
        public void Init(HazardData data)
        {
            Data = data;
            HasExploded = false;

            if (s_warned) return;
            s_warned = true;
            Debug.LogWarning("[ExplosiveProp] 위험 설비가 아직 구현되지 않았다 (D11·D13). " +
                             "배치해도 폭발하지 않는다. GAME_DESIGN §6 위협 3 참조.");
        }

        /// <summary>브레이커나 다른 폭발이 이것을 터뜨린다.</summary>
        public void Explode()
        {
            if (HasExploded || Data == null) return;
            HasExploded = true;

            // TODO(D11):
            //  1. meshIntact → meshDestroyed 교체
            //  2. explosionRadius 안의 Destructible 중 tier <= chainDamageToTier 를 ForceRubble
            //  3. 그 각각에 DebrisSpawner.SpawnFor — 광역 파괴의 보상이 여기서 나온다
            //  4. 플레이어가 반경 안이면 GameLoop.ApplyStun(explosionStunSeconds)
            //  5. 연쇄: 반경 안의 다른 ExplosiveProp도 Explode (방문 집합으로 순환 차단)
            //  6. CFXR Explosions 프리팹을 PoolManager로 꺼내 띄운다
        }
    }
}
