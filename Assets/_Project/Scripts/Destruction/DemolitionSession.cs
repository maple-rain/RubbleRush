using Demo.Core;
using Demo.Data;
using UnityEngine;

namespace Demo.Destruction
{
    /// <summary>
    /// 파괴 화면 한 판 [지시 2026-09-22].
    ///
    /// 건물에 다가가 상호작용하면 여기로 넘어온다. 이동과 케이블이 멈추고,
    /// <b>화면을 두드리는 것만</b>으로 게이지가 오른다. 게이지가 차면 건물이 무너지고 필드로 돌아간다.
    ///
    /// 파괴를 별도 화면으로 뺀 이유는 성능이다 — 조각 파쇄와 연결 그래프는 건물 하나에
    /// 조각 수십 개와 인접 관계 수십 쌍을 만든다. 필드의 구조물 60개에 동시에 돌릴 수 없지만
    /// <b>한 번에 하나면</b> 감당할 수 있다.
    ///
    /// <b>지금 구현된 것</b>: 모드 전환, 탭 입력, 게이지, 게이지에 연동된 3단계 메시 교체,
    /// 완료 시 잔해 생성과 필드 복귀.
    ///
    /// <b>아직 아닌 것</b>: 조각 파쇄와 연결 그래프. 참조 영상(@데브조생 "게임에서 벽은 어떻게 부술까")의
    /// 기법 — 파쇄 무늬를 미리 만들고, 맞는 순간 충격점 근처만 자르고, 떼어낸 조각만 물리로 두고,
    /// 남은 조각은 인접 관계로 붙여 둔 뒤 바닥에서 연결 탐색해 끊어진 덩어리만 떨어뜨린다.
    /// 조각을 격자로 할지 보로노이로 할지가 미결이라(HANDOFF) 여기서는 자리만 비워 뒀다.
    /// </summary>
    public sealed class DemolitionSession : MonoBehaviour
    {
        [SerializeField] Player.InputRouter input;
        [SerializeField] DebrisSpawner spawner;
        [SerializeField] DestructionFX fx;

        /// <summary>0~1. 화면을 두드릴 때마다 오른다.</summary>
        public float Gauge { get; private set; }

        /// <summary>지금 부수고 있는 대상. 필드에서는 null.</summary>
        public Destructible Target { get; private set; }

        /// <summary>이번 판에 두드린 횟수. 연출과 결과 화면이 읽는다.</summary>
        public int TapCount { get; private set; }

        Vector3 targetPoint;
        Core.FollowCamera view;   // 전환이 끝났는가를 ModeBlend로 알려 준다
        [Header("조각 판 더미 — 세우고, 보여주고, 치운다")]
        [SerializeField, Tooltip("벽을 겹으로 세우는 일은 전부 FractureProxy.cs에 있다. "
                                 + "이 세션은 게이지와 탭만 본다")]
        FractureProxy proxy = new FractureProxy();

        [SerializeField, Range(0.2f, 3f),
         Tooltip("마지막 쾅 뒤에 쏟아지는 것을 보여 줄 최대 시간(초). " +
                 "낙하와 내려앉기가 먼저 끝나면 그때 바로 닫는다 — 이 값은 안전장치다")]
        float finishMaxSeconds = 1.2f;

        void Awake()
        {
            if (input == null) input = FindFirstObjectByType<Player.InputRouter>();
            if (spawner == null) spawner = FindFirstObjectByType<DebrisSpawner>();

            // 전환이 끝났는지 물어볼 곳. 규칙 5 — Update에서 Find를 돌리지 않는다.
            // 못 찾으면 교체가 곧바로 걸려서 예전과 같게 굴러간다.
            view = FindFirstObjectByType<Core.FollowCamera>();

            // 연출 컴포넌트는 <b>스스로 붙인다</b> [2026-09-28].
            //
            // 씬에 DestructionFX가 한 번도 없었고, 그래서 fx가 null이라 OnStrike/OnBroken
            // 호출이 전부 조용히 무시되고 있었다 — 연출을 구현해도 아무 일이 안 일어난다.
            // 씬 와이어링은 사람이 하는 것이 원칙이지만(CLAUDE.md 규칙 1),
            // <b>와이어링이 필요 없게 만드는 것</b>이 그 원칙에 어긋나지 않는다.
            // 인스펙터에서 다른 것을 꽂아 두면 그쪽이 이긴다.
            if (fx == null) fx = GetComponent<DestructionFX>();
            if (fx == null) fx = gameObject.AddComponent<DestructionFX>();
        }

        void OnEnable()
        {
            if (GameModeController.Instance != null)
                GameModeController.Instance.Changed += OnModeChanged;
        }

        void OnDisable()
        {
            if (GameModeController.Instance != null)
                GameModeController.Instance.Changed -= OnModeChanged;
        }

        void OnModeChanged(Mode prev, Mode next)
        {
            if (next == Mode.Demolition) Begin(GameModeController.Instance.Target);
            else if (prev == Mode.Demolition) End();
        }

        void Begin(Destructible target)
        {
            Target = target;
            Gauge = 0f;
            TapCount = 0;
            targetPoint = target != null ? target.transform.position : Vector3.zero;

            proxy.Build(target);
        }

        void OnDestroy() => proxy.Dispose();

        void End()
        {
            proxy.Clear();
            Target = null;
            Gauge = 0f;
        }

        void Update()
        {
            GameModeController mode = GameModeController.Instance;
            if (mode == null || !mode.IsDemolition || Target == null) return;

            // 카메라가 날아와 정면에 서면 그때 조각 판으로 바꾼다 (FractureProxy.Build 주석 참조).
            if (!proxy.Shown && (view == null || view.ModeBlend >= 1f)) proxy.Show();

            // 마지막 쾅 뒤에는 입력을 안 받는다. 쏟아지는 것을 보여 주고 나서 닫는다.
            if (finishing) { TickFinish(); return; }

            if (input == null || !input.ActionPressedThisFrame) return;

            // 때리는 순간에는 무조건 바꾼다 — 숨겨 둔 벽에 금이 가면 안 된다.
            proxy.Show();
            Tap();
        }

        bool finishing;
        float finishElapsed;

        /// <summary>
        /// 마지막 층이 쏟아지는 <b>동안</b>을 기다린다 [지시 2026-09-23].
        ///
        /// 예전에는 게이지가 1이 되는 순간 바로 닫았다. 그래서 shatterAt이 1보다 작아야만
        /// 했다 — 마지막 층을 미리 터뜨려 두고, 남은 빈 탭 동안 쏟아지는 것을 보여 준 것이다.
        /// 그 대가로 <b>다 부순 벽을 몇 번 더 두드려야</b> 끝났다.
        /// 기다리는 일을 여기로 옮기면 마지막 탭이 곧 마지막 쾅이 된다.
        /// </summary>
        void TickFinish()
        {
            finishElapsed += Time.deltaTime;

            // 멈춰 서 있는데 안 끝나는 일이 없도록 상한을 둔다.
            if (proxy.Animating && finishElapsed < finishMaxSeconds) return;

            finishing = false;
            CloseOut();
        }

        /// <summary>화면을 한 번 두드렸다.</summary>
        void Tap()
        {
            GameConfig cfg = GameConfigRef.Current;
            if (cfg == null || Target.Data == null) return;

            TapCount++;

            // 탭 하나가 올리는 양은 breakTime에서 유도한다 —
            // '몇 초 걸리는가'는 기획자가 채운 값이고, '초당 몇 번 두드리는가'는 손의 특성이라
            // 대상별이 아니라 전역이다. 새 수치는 tapsPerSecond 하나만 늘어난다.
            // "두 배로 부수기"는 필요 타수를 줄인다 [지시 2026-10-13].
            // <b>타수는 정수다. 모자라면 올린다</b> [지시 2026-10-15].
            //
            // 전에는 실수였다. breakTime 2.5초짜리면 2.5 x 5 = 12.5탭이고, 여기에
            // "두 배로 부수기"(x0.5)가 붙으면 6.25탭이 된다. 그러면 마지막 한 대가
            // <b>반 대</b>가 되어 게이지가 12탭에 0.96, 13탭에 1.04로 튄다 —
            // 건물마다 마지막 타격의 무게가 달라지고, 몇 대면 되는지를 셀 수가 없다.
            //
            // 올림인 이유: 내림하면 6.25탭짜리가 6탭에 끝나 <b>원래보다 쉬워진다</b>.
            // 올림은 "모자라면 한 대 더"라 breakTime이 뜻하던 난이도를 넘지 않는다.
            int taps = Mathf.Max(1, Mathf.CeilToInt(Target.Data.breakTime * cfg.tapsPerSecond
                                                  * Core.Upgrades.Mul(Core.Upgrades.FastBreak)));
            float before = Gauge;

            // <b>세어서 나눈다. 더해서 쌓지 않는다.</b>
            //
            // 예전에는 <c>Gauge += 1f / taps</c>였는데, 1/40 = 0.025 같은 값은 이진
            // 부동소수로 정확히 표현되지 않아 40번을 더해도 1.0에 닿지 못했다.
            // 그래서 견고 건물이 <b>40탭이 아니라 41탭</b>에 부서졌다 —
            // 1~80 중 <b>31개</b> 값(12·19·23·27·28·31·33·37·40…)이 같은 증상이었다
            // [리스크_설계.md §4에서 지적, 2026-09-29 재현·수정].
            //
            // 횟수를 세어 한 번만 나누면 오차가 쌓이지 않는다 — taps가 정수일 때
            // TapCount == taps 순간의 몫이 정확히 1.0이 된다.
            // (float) 없이는 <b>정수 나눗셈</b>이 되어 게이지가 0 아니면 1만 나온다.
            Gauge = Mathf.Clamp01(TapCount / (float)taps);

            // 층이 <b>방금 쏟아졌는가</b>. 이 게임에서 가장 자주 오는 큰 박자다.
            //
            // WallFracture가 알려 주게 할 수도 있지만, 여기서 게이지만 보면 된다 —
            // 층 f는 진행도가 (f+1)/층수를 넘는 순간 통째로 간다(shatterAt=1).
            // 그 경계를 넘었는지는 내림한 값이 바뀌었는지로 알 수 있다.
            int floorsOf = Mathf.Max(1, Target.Data.floors);
            bool floorJustBlew = Mathf.FloorToInt(Gauge * floorsOf)
                               > Mathf.FloorToInt(before * floorsOf);

            // 게이지가 곧 파괴 진행도다. 파괴 화면에서는 <b>조각 판만</b> 이 값을 본다.
            //
            // Destructible을 같이 굴리지 않는 이유 [지시 2026-09-22]:
            // 3단계 메시 교체는 structureDamagedAt01을 넘는 순간 Damaged 메시를 켜는데,
            // 그 메시가 조각 판 뒤에서 통짜로 나타나 "부서지다 말고 원본이 되살아난" 것처럼 보인다.
            // 어차피 이 화면을 나가면 잔해가 되므로, 중간 단계를 보여줄 이유가 없다.
            proxy.SetGauge(Gauge, Target.Data != null ? Target.Data.floors : 1);

            if (fx != null)
            {
                // 마지막 층은 완파와 같은 탭에 터진다. 둘을 같이 울리면 셰이크가 겹쳐
                // 한 번의 큰 흔들림으로 뭉개지므로, 끝나는 탭에서는 층 붕괴를 건너뛴다.
                // 주먹은 <b>먼저, 조건 없이</b>. 어느 박자가 울리든 플레이어는 쳤다.
                fx.OnPunch(input != null && input.StrikeRightArm);

                // <b>마지막 탭에도 타격음이 난다</b> [고침 2026-10-15].
                //
                // 전에는 둘째 줄이 `else if (!floorJustBlew)`였다. 마지막 탭은
                // floorJustBlew가 참이면서 Gauge가 1이라 <b>두 조건을 다 빗나갔다</b> —
                // 어느 쪽도 안 울려서 주먹 바람소리만 나고 건물이 조용히 무너졌다.
                // 완파음(OnBroken)은 CloseOut에서 최대 1.2초 뒤에나 나오므로 그 사이가
                // 통째로 비었다. 기획자: "마지막 타격시 타격 사운드가 발생하지 않고 무너지는 현상".
                //
                // 때린 것은 때린 소리가 나야 한다. 건너뛸 것은 겹치는 층 붕괴 쪽이다.
                // <b>마지막 한 방은 소리가 다르다</b> [지시 2026-10-16].
                // 건물을 무너뜨리는 타격이라 길게 끌리는 음원을 따로 쓴다.
                if (Gauge >= 1f) fx.OnFinishingStrike(targetPoint);
                else if (floorJustBlew) fx.OnFloorCollapse(targetPoint);
                else fx.OnStrike(Gauge, targetPoint);
            }

            if (Gauge >= 1f) Finish();
        }

        /// <summary>마지막 쾅. 닫는 것은 다 쏟아진 뒤다.</summary>
        void Finish()
        {
            finishing = true;
            finishElapsed = 0f;
        }

        void CloseOut()
        {
            // 여기서 한 번에 잔해로 넘긴다. 중간 단계를 건너뛰므로 Damaged 메시는 아예 안 나온다.
            Target.ForceRubble();

            if (spawner != null && Target.Data != null)
                spawner.SpawnFor(Target.Data, Target.transform.position);

            if (fx != null)
            {
                fx.OnBroken(targetPoint);
                // 필드로 돌아가기 전에 걸려 있는 주먹을 거둔다 [고침 2026-10-16].
                // 안 거두면 소비되지 않은 방아쇠가 필드에서 터져 허공을 친다.
                fx.OnLeaveDemolition();
            }

            Debug.Log($"[DemolitionSession] {Target.Data?.id} 파괴 — 탭 {TapCount}회");
            GameModeController.Instance.ExitDemolition();
        }
    }
}
