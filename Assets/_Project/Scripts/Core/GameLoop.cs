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
            SwallowedStructures = 0;
            Upgrades.Reset();           // 지난 판의 카드를 들고 시작하지 않게
            TimeRemaining = data.timeLimit;
            Outcome = Result.Playing;

            if (spawner != null) spawner.Prewarm(Mathf.Min(64, GuessDebrisNeed(data)));
            if (builder != null) builder.Build(data);

            IsRunning = true;

            // 튜토리얼 구역이면 가르치는 흐름이 붙는다. 아니면 스스로 아무것도
            // 안 만든다 — 판정은 저쪽이 한다 [지시 2026-10-17].
            UI.TutorialFlow.OnZoneStart(data);
        }

        /// <summary>구멍이 삼킨 만큼 더한다.</summary>
        public void AddCollected(int units)
        {
            if (!IsRunning || units <= 0) return;

            // <b>할당량으로 끝나지 않는다</b> [지시 2026-10-15].
            //
            // 전에는 여기서 `Collected >= Quota`면 바로 끝냈다. 그래서 구역에 504단위가
            // 깔려 있는데 할당량 60(12%)만 치우면 판이 닫혔고, 초과 수집 배율은 그 프레임에
            // 들어온 몇 단위에만 붙어 사실상 죽은 항목이었다.
            // 기획자: "잔해 갯수를 없애고 점수제로 가는게 맞다고 판단됨."
            //
            // 이제 끝내는 것은 시간뿐이다 (<see cref="Update"/>). 치운 만큼이 곧 점수다.
            Collected += units;
        }

        /// <summary>
        /// 건물 하나를 <b>부수지 않고 통째로</b> 삼켰다 [지시 2026-10-15].
        ///
        /// 잔해 단위는 <see cref="AddCollected"/>가 따로 받는다. 여기는 <b>사건의 수</b>만
        /// 센다 — 기획자가 점수 가산을 거기에 걸기로 했기 때문이다. 6단계 구멍의
        /// "통째로 밀어넣는다"가 점수로도 보상되는 자리다.
        /// </summary>
        public void AddSwallowedStructure()
        {
            if (!IsRunning) return;
            SwallowedStructures++;
        }

        /// <summary>이번 판에 통째로 삼킨 건물 수. 부순 건물(StructuresBroken)과 다르다.</summary>
        public int SwallowedStructures { get; private set; }

        /// <summary>
        /// 지금까지의 점수. HUD가 매 프레임 읽는다 [지시 2026-10-15].
        ///
        /// <b>쌓아 두지 않고 매번 센다.</b> 항목이 넷(잔해·부순 건물·삼킨 건물·구멍 단계)뿐이고
        /// 전부 이미 세고 있는 수라, 곱셈 네 번이면 끝난다. 따로 쌓아 두면
        /// <b>더하는 자리를 빠뜨렸을 때 결과 화면의 점수와 어긋나는데</b>, 그 어긋남은
        /// 판이 끝나야 보인다. 같은 식을 한 군데서만 쓰면 어긋날 수가 없다.
        /// </summary>
        public int CurrentScore => Score.Of(GameConfigRef.Current, Collected,
                                            StructuresBroken, SwallowedStructures,
                                            ActiveHole != null ? ActiveHole.Level : 0);

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
            Debug.Log($"[GameLoop] 구역 종료 — {result} · 수집 {Collected}단위 · " +
                      $"부순 건물 {StructuresBroken}채 · 통째로 삼킨 건물 {SwallowedStructures}채");

            // 결과 화면은 구역이 끝날 때 딱 한 번 찾는다 — Update가 아니라 여기라서 괜찮다.
            if (resultScreen == null) resultScreen = FindFirstObjectByType<UI.ResultScreen>();
            if (resultScreen != null)
                resultScreen.Show(result, Collected, StructuresBroken, SwallowedStructures, ActiveHole);
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
