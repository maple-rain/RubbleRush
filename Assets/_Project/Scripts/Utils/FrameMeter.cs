using UnityEngine;

namespace Demo.Utils
{
    /// <summary>
    /// 화면 구석에 프레임을 띄운다 — <b>테스트 빌드에서만</b> [지시 2026-10-01].
    ///
    /// <b>왜 필요한가.</b> 프레임을 60으로 못 박아 뒀지만(GameConfig.targetFrameRate),
    /// 고정은 <b>목표치를 정하는 것이지 보장이 아니다</b>. 기기가 못 따라오면 그 아래로
    /// 떨어지는데, 빌드에는 Stats 오버레이가 없어서 그게 보이지 않는다.
    /// 폰에서는 F9(DebrisBenchmark)도 못 누르므로, 이것 말고는 60이 지켜지는지
    /// 확인할 방법이 없다.
    ///
    /// <b>출시 빌드에는 안 나온다</b> [지시 2026-10-01]. 조건부 컴파일로 막는다 —
    /// 런타임 if로 막으면 코드와 문자열이 빌드에 그대로 실린다.
    /// 에디터와 Development Build에서만 클래스 본문이 존재한다.
    ///
    /// <b>평균이 아니라 분위수로 보여준다.</b> 평균 60은 절반이 그 아래라는 뜻이라
    /// "60 이하로 안 떨어진다"를 확인할 수 없다. DebrisBenchmark가 95분위로 판정하는 것과
    /// 같은 이유다. 여기서는 최근 구간의 <b>가장 느린 프레임</b>도 같이 띄운다.
    /// </summary>
    public sealed class FrameMeter : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [SerializeField, Range(0.1f, 2f), Tooltip("숫자를 몇 초마다 갱신할 것인가. " +
                 "매 프레임 갱신하면 숫자가 떨려서 읽을 수가 없다")]
        float refreshSeconds = 0.5f;

        [SerializeField, Range(30, 300), Tooltip("느린 프레임을 찾을 표본 수. " +
                 "60fps에서 120이면 최근 2초를 본다")]
        int sampleCount = 120;

        [SerializeField, Tooltip("이 아래로 떨어지면 빨갛게 띄운다. 0이면 GameConfig의 목표치를 쓴다")]
        float warnBelow = 0f;

        float[] samples;
        int head;
        int filled;
        float nextRefresh;
        string line = "";
        Color tint = Color.white;
        GUIStyle style;

        void Awake() => samples = new float[Mathf.Max(8, sampleCount)];

        void Update()
        {
            // <b>실제 시간</b>으로 잰다. 히트스톱이 timeScale을 0으로 끌어내리는 동안에도
            // 화면은 계속 그려지므로, 게임 시간으로 재면 그 구간이 0fps로 보인다.
            samples[head] = Time.unscaledDeltaTime;
            head = (head + 1) % samples.Length;
            if (filled < samples.Length) filled++;

            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + refreshSeconds;

            float sum = 0f, worst = 0f;
            for (int i = 0; i < filled; i++)
            {
                sum += samples[i];
                if (samples[i] > worst) worst = samples[i];
            }
            if (filled == 0 || sum <= 0f) return;

            float avg = sum / filled;
            float avgFps = 1f / avg;
            float worstFps = worst > 0f ? 1f / worst : 0f;

            float target = warnBelow > 0f ? warnBelow : TargetFps();
            tint = worstFps < target ? new Color(1f, 0.45f, 0.4f) : new Color(0.75f, 1f, 0.75f);
            line = string.Format("{0:F0} fps  (최저 {1:F0} · {2:F1}ms)", avgFps, worstFps, avg * 1000f);
        }

        /// <summary>판정 기준. GameConfig가 못 박아 둔 목표치를 그대로 쓴다.</summary>
        static float TargetFps()
        {
            var cfg = Core.GameConfigRef.Current;
            return cfg != null && cfg.targetFrameRate > 0 ? cfg.targetFrameRate : 60f;
        }

        void OnGUI()
        {
            if (line.Length == 0) return;
            if (style == null)
                style = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.UpperRight };

            // <b>오른쪽 아래.</b> 왼쪽 위는 HUD, 오른쪽 위는 DebrisBenchmark가 쓴다 —
            // 거기 두면 'F9 잔해 벤치마크 시작'과 같은 줄에 겹친다 (실측으로 확인했다).
            var r = new Rect(Screen.width - 330f, Screen.height - 32f, 320f, 26f);

            Color was = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);          // 밝은 배경에서도 읽히게 그림자 한 겹
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), line, style);
            GUI.color = tint;
            GUI.Label(r, line, style);
            GUI.color = was;
        }
#endif
    }
}
