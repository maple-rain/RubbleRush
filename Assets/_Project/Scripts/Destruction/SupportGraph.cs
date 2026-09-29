using System.Collections.Generic;
using Demo.Data;
using UnityEngine;

namespace Demo.Destruction
{
    /// <summary>
    /// 지지 관계를 들고 있다가, 지지원이 Rubble이 되면 종속 대상의 붕괴를 촉발한다
    /// (TECH_SPEC §3·§4, GAME_DESIGN §6 위협 1).
    ///
    /// <b>그래프 보관과 순회까지는 동작한다. 실제 붕괴 연출 호출은 비어 있다 — D7 작업이다.</b>
    ///
    /// 물리 시뮬레이션도 레이캐스트도 쓰지 않는다. 관계는 `ZoneData.supports`에 데이터로 박혀 있고,
    /// 그래야 결정적이고 튜닝 가능하다. 재귀를 허용하되(무너진 것이 또 다른 것을 무너뜨림)
    /// 방문 집합으로 순환을 막는다.
    ///
    /// 이것이 "기둥을 칠 것인가, 벽부터 안전하게 갈 것인가" 딜레마의 본체다.
    /// 지금은 붕괴가 없으므로 기둥을 쳐도 아무 일이 일어나지 않는다.
    /// </summary>
    public sealed class SupportGraph : MonoBehaviour
    {
        readonly Dictionary<int, List<int>> dependents = new Dictionary<int, List<int>>();
        readonly Dictionary<int, Destructible> byId = new Dictionary<int, Destructible>();
        readonly HashSet<int> visited = new HashSet<int>();

        static bool s_warned;

        /// <summary>ZoneBuilder가 구조물을 만들면서 하나씩 등록한다.</summary>
        public void Register(int placementId, Destructible d) => byId[placementId] = d;

        /// <summary>ZoneData.supports를 그대로 옮겨 담는다.</summary>
        public void Build(ZoneData.SupportLink[] links)
        {
            dependents.Clear();
            if (links == null) return;

            foreach (var link in links)
            {
                if (link?.dependentIds == null) continue;
                if (!dependents.TryGetValue(link.supporterId, out List<int> list))
                {
                    list = new List<int>(link.dependentIds.Length);
                    dependents[link.supporterId] = list;
                }
                list.AddRange(link.dependentIds);
            }
        }

        /// <summary>
        /// 지지원이 무너졌다. 종속 대상을 연쇄로 훑는다.
        /// </summary>
        public void OnSupporterDestroyed(int placementId)
        {
            visited.Clear();
            Cascade(placementId);

            if (s_warned) return;
            s_warned = true;
            Debug.LogWarning("[SupportGraph] 연쇄 순회는 돌지만 실제 붕괴(CollapseMotion)가 " +
                             "연결되지 않았다 — D7 작업. 지금은 기둥을 부숴도 위의 것이 안 무너진다.");
        }

        void Cascade(int id)
        {
            if (!visited.Add(id)) return;                       // 순환 차단
            if (!dependents.TryGetValue(id, out List<int> list)) return;

            foreach (int dep in list)
            {
                if (!byId.TryGetValue(dep, out Destructible d) || d == null) continue;
                if (!d.IsBreakable) continue;

                // TODO(D7): d에 CollapseMotion을 붙여 낙하 트윈 + 접촉 판정을 시작한다.
                //           깔리면 GameLoop.ApplyStun(d.Data.collapseStunSeconds).
                //           무너진 뒤 DebrisSpawner로 잔해를 뿌린다.

                Cascade(dep);                                    // 무너진 것이 또 다른 것을 무너뜨린다
            }
        }

        /// <summary>구역을 다시 지을 때.</summary>
        public void Clear()
        {
            dependents.Clear();
            byId.Clear();
            visited.Clear();
        }
    }
}
