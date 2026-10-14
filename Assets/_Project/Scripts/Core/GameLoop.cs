using Demo.Collection;
using Demo.Data;
using Demo.Destruction;
using UnityEngine;

namespace Demo.Core
{
    /// <summary>
    /// 구역 하나의 진행을 맡는다 — 타이머, 할당량, 정지 처리.
    ///
    /// 체력바가 없다. 피해는 전부 <b>시간 손실</b>로 환산된다 (GAME_DESIGN §6) —
    /// 자원이 타이머 하나라 UI가 가볍고, 붕괴에 깔린 비용과 잔해를 안 치운 비용을
    /// 같은 축에서 비교할 수 있어 시뮬레이션 검증이 간단해진다.
    /// </summary>
    [DefaultExecutionOrder(-800)]
    public sealed class GameLoop : MonoBehaviour
    {
        public enum Result { Playing, Cleared, TimeOut }

        public static GameLoop Instance { get; private set; }

        [SerializeField] ZoneData zone;
        [SerializeField] ZoneBuilder builder;
        [SerializeField] Player.PlayerController player;
        [SerializeField] CargoTow cargo;
        [SerializeField] DebrisSpawner spawner;

        [SerializeField, Tooltip("켜면 시작하자마자 구역을 짓는다")]
        bool autoStart = true;

        public bool IsRunning { get; private set; }
        public Result Outcome { get; private set; } = Result.Playing;

        /// <summary>남은 시간(초).</summary>
        public float TimeRemaining { get; private set; }

        /// <summary>구멍에 넣은 누적 단위.</summary>
        public int Collected { get; private set; }

        public int Quota => zone != null ? zone.quota : 0;
        public float Progress01 => Quota > 0 ? Mathf.Clamp01((float)Collected / Quota) : 0f;

        public CargoTow Cargo => cargo;
        public ZoneData Zone => zone;

        /// <summary>
        /// 이 구역에서 잔해로 만든 건물 수. 결과 화면이 쓴다 [2026-10-13].
        ///
        /// 수집량(Collected)과 다르다 — 부수기만 하고 안 나른 건물도 여기 센다.
        /// 나중에 점수로 환산할 자리이기도 하다.
        /// </summary>
        public int StructuresBroken { get; private set; }

        /// <summary>
        /// 건물이 <b>처음</b> 잔해가 될 때 한 번 부른다. Destructible이 전이 지점에서 부른다.
        /// 구역이 끝난 뒤에 들어오는 것은 세지 않는다.
        /// </summary>
        public void NotifyBroken()
        {
            if (IsRunning) StructuresBroken++;
        }

        /// <summary>이 구역의 구멍. 하나뿐이다. ZoneBuilder가 만들면서 등록한다.</summary>
        public Hole ActiveHole { get; private set; }

        /// <summary>ZoneBuilder 전용. 매 프레임 Find를 돌리지 않으려고 한 번만 받아 둔다.</summary>
        public void RegisterHole(Hole h) => ActiveHole = h;
        public Vector3 PlayerPosition => player != null ? player.transform.position : Vector3.zero;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[GameLoop] 인스턴스가 둘이다. 나중 것을 끈다.", this);
                enabled = false;
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            // 시작 씬에서 고른 것이 있으면 그것으로. 없으면 인스펙터에 물린 것 그대로 —
            // 에디터에서 Main.unity를 직접 열어 돌리는 일이 개발 중에 제일 잦다.
            if (autoStart) StartZone(Data.StagePick.Or(zone));
        }

        public void StartZone(ZoneData data)
        {
            if (data == null)
            {
                Debug.LogError("[GameLoop] ZoneData가 물려 있지 않다. [Bootstrap] 인스펙터를 확인할 것.", this);
                return;
            }

            string problem = data.Validate();
            if (problem != null) Debug.LogError($"[GameLoop] {problem}", data);

            zone = data;
            Collected = 0;
            StructuresBroken = 0;
            Upgrades.Reset();           // 지난 판의 카드를 들고 시작하지 않게
            TimeRemaining = data.timeLimit;
            Outcome = Result.Playing;

            if (spawner != null) spawner.Prewarm(Mathf.Min(64, GuessDebrisNeed(data)));
            if (builder != null) builder.Build(data);

            IsRunning = true;
        }

        /// <summary>구멍이 삼킨 만큼 더한다.</summary>
        public void AddCollected(int units)
        {
            if (!IsRunning || units <= 0) return;

            Collected += units;
            if (Collected >= Quota) End(Result.Cleared);
        }

        /// <summary>붕괴에 깔리거나 폭발에 맞았을 때. 피해 = 시간 손실이므로 정지가 곧 손해다.</summary>
        public void ApplyStun(float seconds)
        {
            if (player != null) player.ApplyStun(seconds);
        }

        void Update()
        {
            if (!IsRunning) return;

            TimeRemaining -= Time.deltaTime;
            if (TimeRemaining <= 0f)
            {
                TimeRemaining = 0f;
                End(Result.TimeOut);
            }
        }

        void End(Result result)
        {
            IsRunning = false;
            Outcome = result;
            Debug.Log($"[GameLoop] 구역 종료 — {result} · 수집 {Collected}/{Quota} · " +
                      $"남은 시간 {TimeRemaining:0.0}s · 부순 건물 {StructuresBroken}채");

            // 결과 화면은 구역이 끝날 때 딱 한 번 찾는다 — Update가 아니라 여기라서 괜찮다.
            if (resultScreen == null) resultScreen = FindFirstObjectByType<UI.ResultScreen>();
            if (resultScreen != null)
                resultScreen.Show(result, Collected, Quota, TimeRemaining, StructuresBroken, ActiveHole);
            else
                Debug.LogWarning("[GameLoop] 씬에 ResultScreen이 없다 — 구역이 끝나도 " +
                                 "플레이어에게 아무것도 안 보인다.", this);
        }

        UI.ResultScreen resultScreen;

        /// <summary>구역이 낼 수 있는 잔해 개수 어림값. 풀 예열 크기에만 쓴다.</summary>
        static int GuessDebrisNeed(ZoneData data)
        {
            int sum = 0;
            if (data.structures == null) return 16;
            foreach (var p in data.structures)
                if (p?.data != null) sum += p.data.debrisCount;
            return Mathf.Max(16, sum / 2);
        }
    }
}
