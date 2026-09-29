using System.Collections.Generic;
using Demo.Core;
using Demo.Data;
using UnityEngine;

namespace Demo.Destruction
{
    /// <summary>
    /// 파괴 대상 하나. 온전 → 파손 → 잔해 3단계 상태 머신.
    ///
    /// <b>물리 파괴가 아니라 메시 교체다</b> (GAME_DESIGN §4.2). 교체는 프레임 하나에 끝나고
    /// 먼지·흔들림·소리가 그 프레임을 덮는다. 비용이 일정해서 모바일에서 돈다.
    /// 설득력은 메시가 아니라 연출 레이어에서 나온다.
    /// </summary>
    public sealed class Destructible : MonoBehaviour
    {
        public enum State { Intact, Damaged, Rubble }

        // 콜라이더 → 본체 조회표. Breaker가 매 프레임 OverlapSphere 결과를 훑는데,
        // 거기서 GetComponent를 부르면 CLAUDE.md 규칙 5 위반이다.
        static readonly Dictionary<Collider, Destructible> s_byCollider =
            new Dictionary<Collider, Destructible>(128);

        /// <summary>겹친 콜라이더에서 본체를 찾는다. 없으면 null.</summary>
        public static Destructible From(Collider c) =>
            c != null && s_byCollider.TryGetValue(c, out Destructible d) ? d : null;

        /// <summary>씬을 다시 지을 때 호출한다.</summary>
        public static void ClearRegistry() => s_byCollider.Clear();

        public StructureData Data { get; private set; }
        public State Current { get; private set; } = State.Intact;

        /// <summary>파괴 진행도 0~1. 연출(셰이크·균열·음 피치)이 이 값 하나를 기준으로 돈다.</summary>
        public float Progress01 { get; private set; }

        /// <summary>아직 부술 수 있는가.</summary>
        public bool IsBreakable => Current != State.Rubble;

        GameObject intact, damaged, rubble;
        BoxCollider box;
        float elapsed;

        /// <summary>ZoneBuilder가 구역을 지을 때 부른다.</summary>
        public void Init(StructureData data)
        {
            Data = data;

            // 메시 3종을 자식으로 달아두고 켜고 끈다. 매번 Instantiate하면 파괴 프레임에 GC가 튄다.
            intact = SpawnMesh(data.meshIntact, "Intact");
            damaged = SpawnMesh(data.meshDamaged, "Damaged");
            rubble = SpawnMesh(data.meshRubble, "Rubble");

            box = gameObject.AddComponent<BoxCollider>();
            box.size = data.colliderSize;
            box.center = new Vector3(0f, data.colliderSize.y * 0.5f, 0f);
            s_byCollider[box] = this;

            SetState(State.Intact);
        }

        void OnDestroy()
        {
            if (box != null) s_byCollider.Remove(box);
        }

        GameObject SpawnMesh(GameObject prefab, string label)
        {
            if (prefab == null) return null;
            GameObject go = Instantiate(prefab, transform);
            go.name = label;

            // 생성 메시에 붙어 오는 콜라이더는 버린다 — 논컨벡스 고폴리라 모바일에서 못 쓴다
            // (CLAUDE.md 규칙 4). 콜라이더는 StructureData.colliderSize가 유일한 출처다.
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);

            go.SetActive(false);
            return go;
        }

        /// <summary>
        /// 홀드 진행. 이번 호출에서 잔해가 되면 true를 돌려준다 —
        /// 잔해 생성과 연출은 부른 쪽(Breaker)이 그 한 번에 처리한다.
        /// </summary>
        public bool AddProgress(float deltaSeconds)
        {
            if (Current == State.Rubble || Data == null) return false;

            elapsed += deltaSeconds;
            Progress01 = Mathf.Clamp01(elapsed / Data.breakTime);

            GameConfig cfg = GameConfigRef.Current;
            float damagedAt = cfg != null ? cfg.structureDamagedAt01 : 1f;

            if (Progress01 >= 1f)
            {
                SetState(State.Rubble);
                return true;
            }

            if (Current == State.Intact && Progress01 >= damagedAt) SetState(State.Damaged);
            return false;
        }

        /// <summary>
        /// 진행도를 직접 설정한다. 파괴 화면의 게이지가 이걸 구동한다 [지시 2026-09-22].
        ///
        /// AddProgress가 시간을 누적하는 것과 달리 여기는 <b>값을 그대로 받는다</b> —
        /// 게이지의 주인이 탭 입력이지 시간이 아니기 때문이다.
        /// </summary>
        /// <returns>이번 호출에서 잔해가 되었으면 true</returns>
        public bool SetProgress(float progress01)
        {
            if (Current == State.Rubble || Data == null) return false;

            Progress01 = Mathf.Clamp01(progress01);
            elapsed = Progress01 * Data.breakTime;   // AddProgress와 섞여도 어긋나지 않게

            GameConfig cfg = GameConfigRef.Current;
            float damagedAt = cfg != null ? cfg.structureDamagedAt01 : 1f;

            if (Progress01 >= 1f) { SetState(State.Rubble); return true; }
            if (Current == State.Intact && Progress01 >= damagedAt) SetState(State.Damaged);
            return false;
        }

        /// <summary>붕괴 연쇄나 폭발이 통째로 무너뜨릴 때. 진행도를 건너뛴다. (D7·D11에서 쓴다)</summary>
        public bool ForceRubble()
        {
            if (Current == State.Rubble) return false;
            Progress01 = 1f;
            SetState(State.Rubble);
            return true;
        }

        void SetState(State next)
        {
            Current = next;

            if (intact != null) intact.SetActive(next == State.Intact);
            if (damaged != null) damaged.SetActive(next == State.Damaged);
            if (rubble != null) rubble.SetActive(next == State.Rubble);

            // 잔해가 된 것은 더 이상 부딪히지 않는다 — 부순 자리를 지나갈 수 있어야 한다.
            // 오브젝트 자체는 남긴다. 들어올 때와 나갈 때의 스카이라인이 달라야 하기 때문이다.
            if (box != null) box.enabled = next != State.Rubble;
        }
    }
}
