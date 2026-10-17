using System.Collections.Generic;
using Demo.Collection;
using Demo.Core;
using Demo.Data;
using Demo.Destruction;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Demo.Utils
{
    /// <summary>
    /// 잔해를 몇 개까지 띄울 수 있는지 <b>실기에서</b> 찾는다 [지시 5번, 2026-09-30].
    ///
    /// <b>왜 이 도구가 필요한가.</b> <see cref="GameConfig.debrisMaxActive"/>의 80은
    /// 그 필드 주석이 밝히듯 "CLAUDE.md 표에서 온 값"이고 <b>실측으로 정한 적이 없다</b>.
    /// 그렇다고 에디터에서 잴 수도 없다 — 에디터는 PC_RPAsset으로 그림자를 켜고 돌아서
    /// 여기서 나온 fps는 폰과 아무 관계가 없다. 답은 실기에서만 나온다.
    ///
    /// <b>재는 방법.</b> <see cref="stepSize"/>개씩 늘리며 각 단계에서
    /// <see cref="holdSeconds"/>초를 버틴다. 늘린 직후 <see cref="settleSeconds"/>초는
    /// 버린다 — 생성과 GC가 그 구간에 몰려서, 그걸 세면 '유지 가능한 수'가 아니라
    /// '생성 스파이크'를 재게 된다.
    ///
    /// <b>평균이 아니라 95분위로 판정한다.</b> 지시는 "프레임이 60 이하로 떨어지지 않는 선"이다.
    /// 평균 60fps는 절반이 그 아래라는 뜻이라 조건을 만족하지 못한다.
    ///
    /// 드로우콜과 삼각형도 같이 적는다. fps는 기기마다 다르지만 <b>이 둘은 옮겨 간다</b> —
    /// CLAUDE.md 예산(드로우콜 100 · 삼각형 150k)과 바로 비교할 수 있다.
    /// </summary>
    public sealed class DebrisBenchmark : MonoBehaviour
    {
        // <b>출시 빌드에는 안 나온다</b> [지시 2026-10-01 · 적용 2026-10-17].
        // FrameMeter 는 처음부터 이 지시를 지켰는데 이 파일은 빠져 있었다.
        // 첫 알파 WebGL 빌드 화면 위쪽에 'F9' 가 그대로 찍혀 나왔다 [실측 2026-10-17].
        // 런타임 if 로 막으면 코드와 문자열이 빌드에 그대로 실린다 — 조건부 컴파일로 막는다.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [SerializeField, Tooltip("한 단계에서 늘릴 잔해 수")]
        int stepSize = 10;

        [SerializeField, Tooltip("한 단계를 버티는 시간(초). 이 구간의 프레임만 센다")]
        float holdSeconds = 2f;

        [SerializeField, Tooltip("늘린 직후 버릴 시간(초). 생성·GC 스파이크를 빼기 위함이다")]
        float settleSeconds = 0.5f;

        [SerializeField, Tooltip("이 프레임 아래로 떨어지면 멈춘다")]
        float targetFps = 60f;

        [SerializeField, Tooltip("몇 분위로 판정하는가. 0.95면 '100프레임 중 95개가 이보다 빠르다'")]
        float percentile = 0.95f;

        [SerializeField, Tooltip("아무리 좋아도 여기서 멈춘다. 무한히 늘리다 메모리를 터뜨리지 않게")]
        int hardCeiling = 400;

        DebrisSpawner spawner;
        StructureData sample;
        GUIStyle style;

        bool running;
        int step;                        // 지금 단계에서 목표로 한 잔해 수
        float stepStartedAt;
        readonly List<float> frames = new List<float>(600);
        readonly List<string> report = new List<string>();
        int best;                        // 조건을 만족한 마지막 수
        string verdict;

        void Awake()
        {
            spawner = FindFirstObjectByType<DebrisSpawner>();
            foreach (var d in Resources.FindObjectsOfTypeAll<StructureData>())
                if (d.debrisCount > 0 && (sample == null || d.debrisRadius > sample.debrisRadius))
                    sample = d;   // 가장 큰 잔해 = 화소를 가장 많이 먹는 최악
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f9Key.wasPressedThisFrame)
            {
                if (running) Stop("사용자가 멈췄다");
                else Begin();
            }
            if (running) Tick();
        }

        void Begin()
        {
            if (spawner == null || sample == null)
            {
                verdict = "DebrisSpawner나 StructureData를 못 찾았다";
                return;
            }

            // 상한을 풀어 둔다. 안 그러면 80에서 막혀 그 위를 못 잰다 —
            // 재려는 대상이 바로 그 상한이다.
            GameConfig cfg = GameConfigRef.Current;
            if (cfg != null) cfg.debrisMaxActive = hardCeiling;

            running = true;
            step = 0;
            best = 0;
            verdict = null;
            report.Clear();
            report.Add($"기기 {SystemInfo.deviceModel}  GPU {SystemInfo.graphicsDeviceName}");
            report.Add($"화면 {Screen.width}x{Screen.height}  목표 {targetFps:F0}fps  " +
                       $"{percentile * 100f:F0}분위 판정");

            // <b>잔해 0개부터 잰다.</b> 기준선이 이미 목표에 못 미치면 그건 잔해의
            // 문제가 아니므로, 결론을 내면 안 된다 — 아래 Tick이 그걸 가려낸다.
            frames.Clear();
            stepStartedAt = Time.unscaledTime;
        }

        void NextStep()
        {
            step += stepSize;

            // SpawnFor는 한 번에 data.debrisCount개를 뿌린다. 개수를 직접 세지 않고
            // 목표에 닿을 때까지 부른다 — 종류마다 한 번에 나오는 수가 다르기 때문이다.
            int guard = 0;
            while (Debris.Active.Count < step && guard++ < 64)
                spawner.SpawnFor(sample, RandomNearby());

            frames.Clear();
            stepStartedAt = Time.unscaledTime;
        }

        Vector3 RandomNearby()
        {
            Vector2 r = Random.insideUnitCircle * 18f;
            return new Vector3(transform.position.x + r.x, 0f, transform.position.z + r.y);
        }

        void Tick()
        {
            float elapsed = Time.unscaledTime - stepStartedAt;
            if (elapsed > settleSeconds) frames.Add(Time.unscaledDeltaTime);
            if (elapsed < settleSeconds + holdSeconds) return;

            int live = Debris.Active.Count;
            float slow = Percentile(frames, percentile);      // 느린 쪽 프레임 시간
            float fps = slow > 0f ? 1f / slow : 0f;
            bool ok = fps >= targetFps;

            report.Add($"{live,4}개   {fps,6:F1}fps ({slow * 1000f:F1}ms)   {(ok ? "OK" : "미달")}");

            // <b>기준선이 미달이면 이 환경에서는 못 잰다.</b> 잔해가 하나도 없는데
            // 목표에 못 미치는 것은 잔해의 문제가 아니다. 여기서 "버틴 최대 = 0개"라고
            // 내놓으면 거짓말이 된다 — 에디터는 포커스를 잃으면 프레임을 던지므로
            // 이 길로 자주 빠진다 [실측 2026-09-30].
            if (step == 0 && !ok)
            {
                best = -1;      // Stop보다 먼저. Stop이 이 값을 보고 결론 줄을 뺀다
                Stop($"잔해 0개에서 이미 {fps:F1}fps다. 이 환경에서는 잴 수 없다 — " +
                     (Application.isEditor
                        ? "에디터는 포커스를 잃으면 프레임을 던진다. 게임 창을 클릭해 두거나 실기 빌드에서 돌릴 것"
                        : "목표 프레임 자체가 이 기기에 안 맞는다"));
                return;
            }

            if (ok) best = live;
            if (!ok) { Stop($"{live}개에서 {fps:F1}fps로 떨어졌다"); return; }
            if (live >= hardCeiling) { Stop($"상한 {hardCeiling}개까지 버텼다"); return; }
            if (live < step) { Stop($"{live}개에서 더 안 늘어난다 (풀 한계이거나 자리가 없다)"); return; }

            NextStep();
        }

        /// <summary>
        /// 느린 쪽에서 센 분위수. 0.95면 "100프레임 중 95개가 이보다 빠르다"이므로
        /// 정렬한 뒤 95% 지점의 <b>프레임 시간</b>을 집는다.
        /// </summary>
        static float Percentile(List<float> xs, float p)
        {
            if (xs.Count == 0) return 0f;
            var sorted = new List<float>(xs);
            sorted.Sort();
            int i = Mathf.Clamp(Mathf.RoundToInt((sorted.Count - 1) * Mathf.Clamp01(p)), 0, sorted.Count - 1);
            return sorted[i];
        }

        void Stop(string why)
        {
            running = false;
            verdict = best < 0 ? why : $"{why}\n>> 버틴 최대 = {best}개";
            report.Add("");
            report.Add(verdict);
            Debug.LogWarning("[DebrisBenchmark] " + string.Join("\n", report));
        }

        void OnGUI()
        {
            if (style == null)
                style = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true };

            var r = new Rect(Screen.width - 430f, 10f, 420f, 30f);
            GUI.Label(r, running
                ? $"<b>잔해 벤치마크</b> 진행 중 — {Debris.Active.Count}개  (F9 중단)"
                : "<b>F9</b> 잔해 벤치마크 시작", style);

            if (report.Count == 0) return;
            r.y += 24f;
            foreach (string line in report)
            {
                GUI.Label(r, line, style);
                r.y += 20f;
            }

            // 실기가 아니면 결과를 믿으면 안 된다. 화면에 못을 박아 둔다.
            r.y += 8f;
            GUI.Label(r, Application.isEditor
                ? "<color=#ff8080>에디터다. 이 fps는 폰과 무관하다 — 실기 빌드에서 다시 잴 것</color>"
                : "<color=#80ff80>실기</color>", style);
        }
#endif
    }
}
