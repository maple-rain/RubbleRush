using System.Text;
using Demo.Data;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 부수는 동안 <b>무엇이 매끄러움을 깎는지</b> 잰다 [2026-10-08].
    ///
    /// <b>"매끄럽지 않다"는 한 가지가 아니다.</b> 적어도 넷을 가리킬 수 있고 고치는 법이
    /// 전부 다르다 — 시간이 멈춰서 · 그림이 툭 바뀌어서 · 조각이 너무 적어서 ·
    /// 프레임이 떨어져서. 말로 좁히는 것보다 <b>넷을 다 재서 어느 쪽이 큰지 보는 편</b>이 빠르다.
    ///
    /// <b>제일 큰 것은 거의 늘 히트스톱이다.</b> 한 방짜리 게임이면 멋있는 장치인데,
    /// 이 게임은 초당 대여섯 번 두드린다 — 한 번에 40ms를 멈추면 <b>1초에 0.2초가 정지</b>다.
    ///
    /// <b>안 재는 것</b>: 실기 프레임 하락(실측 필요) · GC 스파이크 · 보기에 매끄러운가.
    /// </summary>
    public static class SmoothnessCheck
    {
        /// <summary>목표 프레임. 정지 시간을 '잃은 프레임'으로 환산하는 데만 쓴다.</summary>
        const float Fps = 60f;

        /// <summary>
        /// 탭 간격 후보. <c>Tools/balance/punch.csv</c>의 <c>rush.slowGap 0.3</c> ~
        /// <c>rush.fastGap 0.1</c> 범위를 그대로 쓴다.
        /// </summary>
        static readonly float[] Gaps = { 0.30f, 0.20f, 0.15f };

        [MenuItem("Demo/21. 부숴짐 매끄러움 점검", priority = 75)]
        public static void Run() => Debug.Log(Report());

        public static string Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine("부숴짐 매끄러움 점검 — '매끄럽지 않다'가 가리킬 수 있는 것들");
            sb.AppendLine();

            HitStop(sb);
            Stages(sb);
            Pieces(sb);

            sb.AppendLine();
            sb.AppendLine("  안 잰 것: 실기 프레임 하락 · GC 스파이크 · 보기에 매끄러운가");
            return sb.ToString();
        }

        /// <summary>
        /// 시간이 멈추는 비율. <b>이 게임에서 제일 큰 항목이다.</b>
        ///
        /// 한 방짜리 격투 게임이면 3~10프레임 정지가 "단단했다"는 신호가 되지만,
        /// 여기서는 그 정지가 <b>초당 대여섯 번</b> 온다. 같은 장치가 반대로 작동한다.
        /// </summary>
        static void HitStop(StringBuilder sb)
        {
            sb.AppendLine("[1] 히트스톱 — 때릴 때마다 시간이 멈춘다 (Time.timeScale = 0)");

            var cfgRef = Object.FindFirstObjectByType<Core.GameConfigRef>();
            var fx = Object.FindFirstObjectByType<Destruction.DestructionFX>();
            if (cfgRef == null || fx == null) { sb.AppendLine("  못 쟀다 — 씬을 열 것"); return; }

            var conf = new SerializedObject(cfgRef).FindProperty("config").objectReferenceValue as GameConfig;
            if (conf == null) { sb.AppendLine("  GameConfig를 못 읽음"); return; }

            var so = new SerializedObject(fx);
            float baseSec = conf.hitStopSeconds;
            float strike = Prop(so, "strikeHitStop", 0.5f);
            float above = Prop(so, "rushSuppressAbove", 0.5f);
            float one = baseSec * strike;

            sb.AppendLine("  기본 " + baseSec.ToString("F3") + "초 x 두드림 배수 " + strike.ToString("F2")
                          + " = 한 번에 " + (one * 1000f).ToString("F0") + "ms ("
                          + (one * Fps).ToString("F1") + "프레임)");
            sb.AppendLine();
            sb.AppendLine(string.Format("  {0,-12} {1,10} {2,12} {3}", "탭 간격", "초당 정지", "잃은 프레임/초", "판정"));

            foreach (float gap in Gaps)
            {
                float share = one / gap;
                sb.AppendLine(string.Format("  {0,-12} {1,9:P0} {2,12:F1}  {3}",
                    gap.ToString("F2") + "초", share, share * Fps, Verdict(share)));
            }

            sb.AppendLine("  러쉬 " + above.ToString("F2") + " 초과: 정지를 안 건다 (rushSuppressAbove)");
            sb.AppendLine();
            sb.AppendLine("  stop.strike를 바꾸면 (punch.csv 한 줄 · 0.20초 간격 기준):");
            foreach (float s in new[] { 0.5f, 0.35f, 0.25f, 0.15f })
                sb.AppendLine("    " + s.ToString("F2") + " -> 한 번에 "
                              + (baseSec * s * 1000f).ToString("F0") + "ms ("
                              + (baseSec * s * Fps).ToString("F1") + "프레임) · 초당 "
                              + (baseSec * s / 0.2f).ToString("P0") + " 정지"
                              + (baseSec * s * Fps < 1f ? "   <- 한 프레임 미만. 있으나 마나다" : ""));
            sb.AppendLine("  * 큰 박자(층 붕괴 1.0 · 완파 2.2 · 러쉬 마무리 3.0)는 그대로 둔다 —");
            sb.AppendLine("    정지가 '단단했다'는 신호인 것은 맞고, 다만 그게 초당 다섯 번 오면 안 된다.");
        }

        static string Verdict(float share)
        {
            if (share < 0.06f) return "거의 안 느껴진다";
            if (share < 0.12f) return "느껴진다";
            if (share < 0.22f) return "계속 걸린다";
            return "심함 — 이것이 '매끄럽지 않다'의 정체일 가능성이 높다";
        }

        /// <summary>
        /// 단계가 바뀔 때 섞어 주는가. 지금은 메시를 <c>SetActive</c>로 갈아 끼우므로
        /// <b>한 프레임에 툭 바뀐다</b>. 필드에서는 그게 "스카이라인이 바뀐다"는 신호라 괜찮지만,
        /// 가까이서 보면 그 순간이 그대로 보인다.
        /// </summary>
        static void Stages(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("[2] 단계 전환 — 온전 / 파손 / 잔해");
            sb.AppendLine("  메시를 켜고 끄는 방식이다 — 섞는 구간이 없어 한 프레임에 바뀐다.");
            sb.AppendLine("  * 부수기 화면에서는 안 보인다 (DemolitionSession이 조각 판만 굴린다).");
            sb.AppendLine("  * 필드에서 가까이 있을 때만 보인다. 고치려면 페이드가 필요하고,");
            sb.AppendLine("    그건 머티리얼이 둘로 갈려 드로우콜이 는다 — 값을 치르는 쪽이다.");
        }

        /// <summary>조각이 적으면 '부서졌다'가 아니라 '쪼개졌다'로 보인다.</summary>
        static void Pieces(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("[3] 조각 수 — 적으면 깨진 게 아니라 갈라진 것처럼 보인다");

            // FractureTuning은 MonoBehaviour가 아니라 WallFracture 안에 박힌 직렬화 클래스다.
            // 그래서 주인을 거쳐 읽는다 — 씬에 박힌 값이어야 하므로 C# 기본값을 보면 안 된다.
            var wall = Object.FindFirstObjectByType<Destruction.WallFracture>(FindObjectsInactive.Include);
            if (wall == null)
            {
                sb.AppendLine("  WallFracture가 씬에 없다 — 부수기 화면에서만 생기는 것일 수 있다");
                return;
            }

            var p = new SerializedObject(wall).FindProperty("tune.piecesAcrossFrame");
            int across = p != null ? p.intValue : 0;
            sb.AppendLine("  화면 가로로 " + across + "조각");
            sb.AppendLine("  * 층마다 따로 쪼개지므로 세로는 층수가 정한다 (floors).");
            sb.AppendLine("    6층짜리 t5_solid는 " + across + " x 6 = 약 " + (across * 6) + "조각이 된다.");
        }

        static float Prop(SerializedObject so, string field, float fallback)
        {
            var p = so.FindProperty(field);
            return p != null ? p.floatValue : fallback;
        }
    }
}
