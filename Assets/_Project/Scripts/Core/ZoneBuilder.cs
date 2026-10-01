using Demo.Collection;
using Demo.Data;
using Demo.Destruction;
using UnityEngine;

namespace Demo.Core
{
    /// <summary>
    /// ZoneData를 읽어 구조물·구멍을 만든다.
    ///
    /// CLAUDE.md 규칙 2 — 씬은 하나뿐이고 구역은 씬도 프리팹도 아니다.
    /// 여기서 만드는 것이 곧 레벨이며, 레벨 구성이 사람 손이 아니라 데이터에 남는다.
    ///
    /// 지지 관계(SupportLink)는 읽지 않는다. 붕괴 연쇄는 우선순위 2(D7)다.
    /// </summary>
    public sealed class ZoneBuilder : MonoBehaviour
    {
        [SerializeField, Tooltip("생성물이 붙을 부모. 씬의 [Zone] 빈 오브젝트")]
        Transform zoneRoot;

        [SerializeField, Tooltip("구멍으로 쓸 프리팹. Hole 컴포넌트가 붙어 있어야 한다")]
        GameObject holePrefab;

        public Transform ZoneRoot { get => zoneRoot; set => zoneRoot = value; }
        public GameObject HolePrefab { get => holePrefab; set => holePrefab = value; }

        /// <summary>지금 구역에 서 있는 구조물 수.</summary>
        public int StructureCount { get; private set; }

        public void Build(ZoneData zone)
        {
            if (zone == null || zoneRoot == null) return;

            Clear();

            BuildStructures(zone);
            BuildHoles(zone);

            GameConfig cfg = GameConfigRef.Current;
            int cap = cfg != null ? cfg.structureMaxActive : 60;
            if (StructureCount > cap)
                Debug.LogWarning($"[ZoneBuilder] 동시 구조물 {StructureCount}개 — 성능 예산 {cap} 초과. " +
                                 "CLAUDE.md 모바일 성능 예산 참조.");

            // 잔해가 무엇에 걸릴지를 격자에 구워 둔다 [14번, 2026-10-01].
            // 구역을 짓고 난 <b>뒤에</b> 해야 한다 — 콜라이더가 그때 생긴다.
            // 건물은 한 판 내내 안 움직이므로 한 번 구우면 다시 구울 일이 없다.
            // 부서져서 막지 않게 되는 것은 BoxCollider.enabled로 드러나고, ObstacleField가 그걸 본다.
            Hazard.ObstacleField.Rebuild(zoneRoot.GetComponentsInChildren<BoxCollider>(true));

            if (zone.supports != null && zone.supports.Length > 0)
                Debug.Log($"[ZoneBuilder] 지지 관계 {zone.supports.Length}건이 데이터에 있지만 " +
                          "붕괴 연쇄는 아직 구현 전이다 (우선순위 2 · D7).");
        }

        void BuildStructures(ZoneData zone)
        {
            StructureCount = 0;
            if (zone.structures == null) return;

            foreach (ZoneData.Placement p in zone.structures)
            {
                if (p?.data == null) continue;

                string problem = p.data.Validate();
                if (problem != null) Debug.LogError($"[ZoneBuilder] {problem}", p.data);

                var go = new GameObject($"S{p.id}_{p.data.id}");
                go.transform.SetParent(zoneRoot, false);
                go.transform.SetPositionAndRotation(p.position, Quaternion.Euler(0f, p.yaw, 0f));

                go.AddComponent<Destructible>().Init(p.data);
                StructureCount++;
            }
        }

        /// <summary>구멍은 구역에 하나뿐이다 [확정 2026-09-22].</summary>
        void BuildHoles(ZoneData zone)
        {
            if (holePrefab == null) return;

            GameObject go = Instantiate(holePrefab, zoneRoot);
            go.name = "Hole";
            go.transform.position = zone.holePosition;

            var hole = go.GetComponent<Hole>();
            if (hole == null)
            {
                Debug.LogError("[ZoneBuilder] 구멍 프리팹에 Hole 컴포넌트가 없다.", holePrefab);
                return;
            }

            hole.Init();
            if (GameLoop.Instance != null) GameLoop.Instance.RegisterHole(hole);
        }

        /// <summary>구역을 다시 짓기 전에 비운다. 정적 조회표도 같이 비워야 유령 참조가 안 남는다.</summary>
        public void Clear()
        {
            if (zoneRoot == null) return;

            for (int i = zoneRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = zoneRoot.GetChild(i);
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }

            Destructible.ClearRegistry();
            Debris.ClearRegistry();
            Hazard.ObstacleField.Clear();
            StructureCount = 0;
        }
    }
}
