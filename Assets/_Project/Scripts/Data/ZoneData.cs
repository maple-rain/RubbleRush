using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Demo.Data
{
    /// <summary>
    /// 구역 하나 = 스테이지 하나.
    ///
    /// CLAUDE.md 규칙 2 — 씬은 Main.unity 하나뿐이다. 구역은 씬도 프리팹도 아니고
    /// 이 에셋이며, ZoneBuilder가 런타임에 생성한다. 이렇게 해야 레벨 구성이
    /// 사람 손이 아니라 데이터에 남는다.
    /// </summary>
    [CreateAssetMenu(menuName = "Demo/Zone Data", fileName = "Zone_")]
    public sealed class ZoneData : ScriptableObject
    {
        /// <summary>구조물 한 개의 배치. id는 SupportLink가 가리키는 키다.</summary>
        [System.Serializable]
        public sealed class Placement
        {
            [Tooltip("이 구역 안에서 유일해야 한다. SupportLink가 이 번호로 참조한다")]
            public int id;

            public StructureData data;
            public Vector3 position;
            public float yaw;
        }

        /// <summary>위험 설비 한 개의 배치.</summary>
        [System.Serializable]
        public sealed class HazardPlacement
        {
            public int id;
            public HazardData data;
            public Vector3 position;
            public float yaw;
        }

        /// <summary>
        /// 지지 관계. supporter가 Rubble이 되면 dependents가 무너진다.
        /// 레이캐스트나 물리로 유도하지 않는다 — 결정적이고 튜닝 가능해야 한다.
        /// </summary>
        [System.Serializable]
        public sealed class SupportLink
        {
            [Tooltip("지지하는 쪽 (기둥). Placement.id")]
            public int supporterId;

            [Tooltip("이것이 무너지면 같이 무너지는 것들. Placement.id 목록")]
            public int[] dependentIds;
        }

        [Header("식별")]
        public string id;
        public string displayName;

        [Header("클리어 조건")]
        [Tooltip("제한 시간(초)")]
        public float timeLimit;

        [Tooltip("클리어에 필요한 잔해 총 단위. StructureData.debrisSizeClass 기준")]
        public int quota;

        [Header("구멍")]
        [Tooltip("구멍 위치. **구역당 하나뿐이다** [확정 2026-09-22].\n" +
                 "반경은 여기가 아니라 GameConfig.holeLevels가 정한다 — 경험치로 단계적으로 커진다")]
        public Vector3 holePosition;

        [Header("배치")]
        public Placement[] structures;
        public HazardPlacement[] hazards;

        [Tooltip("지지 관계. 비워두면 붕괴 연쇄가 일어나지 않는다")]
        public SupportLink[] supports;

        /// <summary>배치된 구조물이 낼 수 있는 잔해 총 단위. 할당량이 달성 가능한지 보는 데 쓴다.</summary>
        public int TotalAvailableUnits
        {
            get
            {
                int sum = 0;
                if (structures == null) return 0;
                foreach (var p in structures)
                    if (p?.data != null) sum += p.data.TotalDebrisUnits;
                return sum;
            }
        }

        /// <summary>
        /// 구역 데이터의 정합성을 본다. 문제가 없으면 null.
        ///
        /// 여기서 잡는 것은 수치가 아니라 <b>참조가 깨졌는가</b>다 —
        /// 존재하지 않는 id를 가리키는 지지 관계는 런타임에 조용히 아무 일도 하지 않아서
        /// "기둥을 부쉈는데 아무것도 안 무너진다"로만 드러난다. 찾기 가장 나쁜 종류의 버그다.
        /// </summary>
        public string Validate()
        {
            var sb = new StringBuilder();

            if (timeLimit <= 0f) sb.Append("timeLimit 미입력, ");
            if (quota <= 0) sb.Append("quota 미입력, ");
            if (structures == null || structures.Length == 0) sb.Append("structures 비어 있음, ");

            var seen = new HashSet<int>();
            if (structures != null)
            {
                foreach (var p in structures)
                {
                    if (p == null) continue;
                    if (p.data == null) sb.Append($"structures[id={p.id}].data 비어 있음, ");
                    if (!seen.Add(p.id)) sb.Append($"id {p.id} 중복, ");
                }
            }

            if (supports != null)
            {
                foreach (var link in supports)
                {
                    if (link == null) continue;
                    if (!seen.Contains(link.supporterId))
                        sb.Append($"supports: 없는 supporterId {link.supporterId}, ");
                    if (link.dependentIds == null) continue;
                    foreach (int dep in link.dependentIds)
                    {
                        if (!seen.Contains(dep))
                            sb.Append($"supports[{link.supporterId}]: 없는 dependentId {dep}, ");
                        if (dep == link.supporterId)
                            sb.Append($"supports[{link.supporterId}]: 자기 자신을 지지함, ");
                    }
                }
            }

            if (quota > 0 && TotalAvailableUnits > 0 && quota > TotalAvailableUnits)
                sb.Append($"할당량 {quota} > 배치된 구조물 총 {TotalAvailableUnits}단위 (달성 불가), ");

            if (sb.Length == 0) return null;
            sb.Length -= 2;
            return $"ZoneData '{name}': {sb}";
        }
    }
}
