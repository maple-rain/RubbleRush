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
    public sealed partial class Destructible : MonoBehaviour
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
            // 크기는 전부 colliderSize가 정한다 — 프리팹에 박힌 치수를 쓰면 때리는 판정과
            // 보이는 상자가 조용히 어긋난다 (MeshFit 참조).
            intact = SpawnMesh(data.meshIntact, "Intact", data.colliderSize);
            damaged = SpawnMesh(data.meshDamaged, "Damaged", data.colliderSize);
            rubble = SpawnMesh(data.meshRubble, "Rubble", RubbleSize(data));

            box = gameObject.AddComponent<BoxCollider>();
            s_byCollider[box] = this;

            SetState(State.Intact);
        }

        void OnDestroy()
        {
            if (box != null) s_byCollider.Remove(box);
        }

        /// <summary>
        /// 한 단계의 겉모습을 짓는다. <b>껍데기를 한 겹 둔다</b> —
        /// <see cref="MeshFit"/>이 메시를 늘리느라 스케일을 건드리므로, 지붕처럼 나중에 덧붙이는
        /// 것을 그 밑에 달면 같이 늘어난다. 껍데기는 스케일 1로 남아 기준자 노릇을 한다.
        /// </summary>
        GameObject SpawnMesh(GameObject prefab, string label, Vector3 size)
        {
            if (prefab == null) return null;

            var host = new GameObject(label);
            host.transform.SetParent(transform, false);

            GameObject go = Instantiate(prefab, host.transform);

            // 생성 메시에 붙어 오는 콜라이더는 버린다 — 논컨벡스 고폴리라 모바일에서 못 쓴다
            // (CLAUDE.md 규칙 4). 콜라이더는 StructureData.colliderSize가 유일한 출처다.
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);

            MeshFit.Fit(go, size);

            // 온전 단계만 <b>네 면을 따로</b> 입는다 [2026-10-12].
            //
            // 기본 Cube는 서브메시가 하나라 그림도 한 장만 붙는다. 그 한 장이 정면도였으므로
            // 필드에서는 어느 쪽으로 돌아가도 입구가 보였고, 플레이어는 측면을 정면으로 알고
            // E를 눌렀다 — "정면에서 눌렀는데 측면이 나온다"의 정체다 (FaceSkin 참조).
            // 파손·잔해는 한 장으로 족하다. 그 단계에서는 면을 가려 들어갈 일이 없다.
            if (label == "Intact" && Data.facadeAtlas != null)
            {
                FaceSkin.Apply(go);
                intactAtlassed = true;
            }

            // 토대는 <b>옆면에 입면, 윗면에 자갈</b>을 쓴다 [지시 2026-10-12].
            // 잔해 그림은 정면도라 위쪽 28~45%가 하늘인데, 민짜 Cube에 그대로 깔면
            // 그 하늘이 윗면에 통째로 얹힌다 — 50도로 내려다보는 화면에서 제일 큰 면이다.
            if (label == "Rubble" && Data.facadeRubbleAtlas != null)
            {
                FaceSkin.ApplyRubble(go);
            }

            // 단계마다 제 렌더러를 기억해 둔다. 그림을 입힐 때 <b>보이는 쪽</b>에 입혀야 한다
            // (규칙 5 — 찾는 일은 여기서 한 번만).
            var r = go.GetComponentInChildren<Renderer>(true);
            if (r != null)
            {
                if (label == "Damaged") { skinDamaged = r; baseDamaged = r.sharedMaterial; }
                else if (label == "Rubble") { skinRubble = r; baseRubble = r.sharedMaterial; }
                else { skinIntact = r; baseIntact = r.sharedMaterial; }

                // 윗면은 50도 시야에서 어느 벽보다 크게 보인다. 거기에 벽 그림을 두면
                // 건물이 바닥에 깔린 그림판처럼 보인다 (RoofCap).
                if (label != "Rubble" && Data.facadeRoof != null)
                    RoofCap.Make(host.transform, size, StageSkin.For(Data.facadeRoof, r.sharedMaterial));
            }

            host.SetActive(false);
            return host;
        }

        /// <summary>잔해 단계의 겉보기 치수. <see cref="ApplyCollider"/>가 상자에 쓰는 것과 같다.</summary>
        static Vector3 RubbleSize(StructureData d) =>
            new Vector3(d.colliderSize.x * RubbleShrinkX, RubbleHeight, d.colliderSize.z * RubbleShrinkZ);

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

        // 토대 치수 — 5종 전부 같다 [실측 2026-10-01].
        // 발자국은 원본의 (x 0.90, z 1.00), 높이는 0.40.
        const float RubbleShrinkX = 0.90f;
        const float RubbleShrinkZ = 1.00f;
        const float RubbleHeight = 0.40f;

        /// <summary>토대의 높이(m). 잔해를 그 위에 올려놓을 때 바깥에서 읽는다.</summary>
        public static float FoundationHeight => RubbleHeight;

        /// <summary>토대의 바닥 발자국(월드 중심·반너비). 케이블 차단 판정이 읽는다 [11번 CableOnly].</summary>
        public void FoundationFootprint(out Vector3 center, out float halfX, out float halfZ)
        {
            Vector3 s = Data != null ? Data.colliderSize : Vector3.one;
            center = transform.position;
            halfX = s.x * RubbleShrinkX * 0.5f;
            halfZ = s.z * RubbleShrinkZ * 0.5f;
        }

        float rubbleSince = -1f;

        void SetState(State next)
        {
            Current = next;

            if (intact != null) intact.SetActive(next == State.Intact);
            if (damaged != null) damaged.SetActive(next == State.Damaged);
            if (rubble != null) rubble.SetActive(next == State.Rubble);

            ApplySkin(next);

            if (next == State.Rubble && rubbleSince < 0f)
            {
                rubbleSince = Time.time;

                // 결과 화면이 "몇 채를 부쉈나"를 센다. 전이가 한 번뿐이라 여기가 맞는 자리다.
                Core.GameLoop loop = Core.GameLoop.Instance;
                if (loop != null) loop.NotifyBroken();

                // 케이블 차단 판정은 콜라이더와 별개로 산다 — CableOnly에서는 콜라이더가 꺼져 있다.
                FoundationFootprint(out Vector3 c, out float hx, out float hz);
                Hazard.ObstacleField.AddFoundation(c, hx, hz, transform.eulerAngles.y);
            }

            // 오브젝트 자체는 남긴다. 들어올 때와 나갈 때의 스카이라인이 달라야 하기 때문이다.
            ApplyCollider();
        }

        /// <summary>
        /// 콜라이더를 지금 상태에 맞춘다.
        ///
        /// <b>토대가 되면 상자도 토대 크기로 줄인다</b> [고침 2026-10-02].
        /// 예전에는 잔해가 되면 그냥 껐으므로 크기가 뭐든 상관없었는데,
        /// 11번에서 토대를 막게 할 수 있게 되면서 <b>원본 건물 크기 그대로 막아 버리는</b>
        /// 문제가 생긴다 — T5Solid면 높이 9.8m짜리 보이지 않는 벽이 선다.
        /// </summary>
        void ApplyCollider()
        {
            if (box == null || Data == null) return;

            if (Current != State.Rubble)
            {
                box.size = Data.colliderSize;
                box.center = new Vector3(0f, Data.colliderSize.y * 0.5f, 0f);
                box.enabled = true;
                return;
            }

            box.size = new Vector3(Data.colliderSize.x * RubbleShrinkX, RubbleHeight,
                                   Data.colliderSize.z * RubbleShrinkZ);
            box.center = new Vector3(0f, RubbleHeight * 0.5f, 0f);
            box.enabled = FoundationSolid();
        }

        /// <summary>지금 규칙에서 토대가 길을 막는가 [11번].</summary>
        bool FoundationSolid()
        {
            GameConfig cfg = GameConfigRef.Current;
            if (cfg == null) return false;

            switch (cfg.foundationRule)
            {
                case GameConfig.FoundationRule.Obstacle:
                case GameConfig.FoundationRule.Platform:
                    return true;
                case GameConfig.FoundationRule.Decay:
                    return rubbleSince >= 0f &&
                           (Time.time - rubbleSince) < (cfg.foundationDecaySeconds * Core.Upgrades.Mul(Core.Upgrades.QuickFoundation));
                default:
                    return false;      // None · CableOnly — 걸어서는 지나간다
            }
        }

        /// <summary>
        /// 소멸 규칙(Decay)일 때만 일을 한다 [11번].
        ///
        /// <b>왜 Update인가.</b> 토대가 <b>스스로</b> 꺼지는 것은 이 규칙뿐이고, 꺼지는 순간을
        /// 알려 줄 쪽이 없다. 다른 네 규칙에서는 첫 줄에서 바로 돌아 나오므로,
        /// 구조물 30개가 내는 비용은 이른 반환 30번이다.
        /// </summary>
        void Update()
        {
            if (Current != State.Rubble || !box.enabled) return;

            GameConfig cfg = GameConfigRef.Current;
            if (cfg == null || cfg.foundationRule != GameConfig.FoundationRule.Decay) return;

            if ((Time.time - rubbleSince) >= (cfg.foundationDecaySeconds * Core.Upgrades.Mul(Core.Upgrades.QuickFoundation))) box.enabled = false;
        }
    }
}
