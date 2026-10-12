using System.Text;
using Demo.UI;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 터치 버튼 배치를 여러 기종에서 검사한다 [2026-10-12].
    ///
    /// <b>실기가 없어서 만든 도구다.</b> 폰이 없으면 "눌러 보면 안다"를 못 한다. 그런데
    /// 터치 UI가 틀어지는 방식은 거의 정해져 있고 전부 계산으로 잡힌다 —
    /// 버튼이 겹치거나, 화면 밖으로 나가거나, 손가락보다 작거나, 조이스틱 영역을 침범하거나.
    ///
    /// <b>손가락 크기가 기준이다.</b> 안드로이드 가이드라인의 최소 터치 영역은 48dp다.
    /// 화면 비율로 적은 반경은 해상도가 아니라 <b>dpi</b>에 따라 실제 손가락 크기가 바뀌므로,
    /// 기종마다 dp로 환산해서 본다.
    ///
    /// <b>안 재는 것</b>: 엄지가 닿는가(손 크기와 쥐는 법), 노치·제스처 바에 가리는가
    /// (Safe Area는 실기에서만 알 수 있다), 보기 좋은가.
    /// </summary>
    public static class TouchLayoutCheck
    {
        /// <summary>안드로이드 가이드라인의 최소 터치 영역(지름).</summary>
        const float MinTouchDp = 48f;

        /// <summary>대표 기종. 가로 화면 기준 (가로, 세로, dpi, 이름).</summary>
        static readonly (int w, int h, float dpi, string name)[] Phones =
        {
            (1280,  720, 294f, "보급형 720p"),
            (1920, 1080, 401f, "16:9 1080p"),
            (2340, 1080, 409f, "19.5:9 (요즘 흔한 것)"),
            (2400, 1080, 395f, "20:9 (길쭉한 것)"),
            (2778, 1284, 458f, "고해상도 큰 폰"),
        };

        [MenuItem("Demo/22. 터치 버튼 배치 점검", priority = 76)]
        public static void Run() => Debug.Log(Report());

        public static string Report()
        {
            TouchPad[] pads = Pads();
            var sb = new StringBuilder();

            sb.AppendLine("터치 버튼 배치 점검 — 실기 없이 계산으로 잡히는 것들");
            sb.AppendLine();
            sb.AppendLine("  배치 (화면 비율, 반경은 세로 기준):");
            foreach (TouchPad p in pads)
                sb.AppendLine("    " + p.Id.ToString().PadRight(9)
                              + "중심 (" + p.Center.x.ToString("F2") + ", " + p.Center.y.ToString("F2")
                              + ")  반경 " + p.Radius.ToString("F3")
                              + (string.IsNullOrEmpty(p.Label) ? "" : "  \"" + p.Label + "\""));

            Overlap(sb, pads);
            StickZone(sb, pads);
            Fingers(sb, pads);

            sb.AppendLine();
            sb.AppendLine("  안 잰 것: 엄지가 닿는가 · 노치/제스처 바에 가리는가 · 보기 좋은가");
            return sb.ToString();
        }

        /// <summary>
        /// 버튼끼리 겹치는가. <b>가로세로 비율마다 답이 다르다</b> — 반경은 세로 기준인데
        /// 가로 거리는 가로 기준이라, 화면이 길쭉해질수록 버튼 사이가 멀어진다.
        /// 제일 안 길쭉한 기종에서 겹치지 않으면 나머지는 안전하다.
        /// </summary>
        static void Overlap(StringBuilder sb, TouchPad[] pads)
        {
            sb.AppendLine();
            sb.AppendLine("[1] 버튼끼리 겹치는가 (제일 좁은 16:9 기준)");

            float aspect = 1280f / 720f;          // 가로 ÷ 세로. 작을수록 버튼이 가까워진다
            int bad = 0;

            for (int i = 0; i < pads.Length; i++)
                for (int j = i + 1; j < pads.Length; j++)
                {
                    // 세로를 1로 두고 잰다. 가로 비율 차이는 aspect로 늘린다.
                    float dx = (pads[i].Center.x - pads[j].Center.x) * aspect;
                    float dy = pads[i].Center.y - pads[j].Center.y;
                    float gap = Mathf.Sqrt(dx * dx + dy * dy) - (pads[i].Radius + pads[j].Radius);

                    if (gap < 0f)
                    {
                        bad++;
                        sb.AppendLine("    " + pads[i].Id + " <-> " + pads[j].Id
                                      + "  겹침 " + (-gap).ToString("F3") + "   <- 둘 다 눌린다");
                    }
                    else if (gap < 0.03f)
                    {
                        sb.AppendLine("    " + pads[i].Id + " <-> " + pads[j].Id
                                      + "  틈 " + gap.ToString("F3") + "   <- 아슬아슬하다");
                    }
                }

            if (bad == 0) sb.AppendLine("    겹치는 쌍 없음");
        }

        /// <summary>
        /// 왼쪽 절반은 어디를 눌러도 조이스틱이 된다. 거기 걸친 버튼은 누를 때마다
        /// 캐릭터가 같이 움직인다 — 실제로 일어나는 사고라 따로 본다.
        /// </summary>
        static void StickZone(StringBuilder sb, TouchPad[] pads)
        {
            sb.AppendLine();
            sb.AppendLine("[2] 조이스틱 영역(왼쪽 절반)을 침범하는가");

            float aspect = 2400f / 1080f;         // 제일 길쭉한 쪽이 가장 위험하다
            int bad = 0;
            foreach (TouchPad p in pads)
            {
                float leftEdge = p.Center.x - p.Radius / aspect;    // 반경을 가로 비율로 환산
                if (leftEdge >= 0.5f) continue;
                bad++;
                sb.AppendLine("    " + p.Id + " 왼쪽 끝이 " + leftEdge.ToString("F3")
                              + "   <- 0.5 아래면 누를 때 캐릭터가 움직인다");
            }
            if (bad == 0) sb.AppendLine("    전부 오른쪽 절반 안에 있다");
        }

        /// <summary>버튼이 손가락보다 큰가. 기종마다 dpi가 달라 dp로 환산해 본다.</summary>
        static void Fingers(StringBuilder sb, TouchPad[] pads)
        {
            sb.AppendLine();
            sb.AppendLine("[3] 손가락보다 큰가 (최소 " + MinTouchDp.ToString("F0") + "dp)");
            sb.AppendLine(string.Format("  {0,-22} {1}", "기종", "버튼 지름(dp)"));

            foreach (var ph in Phones)
            {
                var line = new StringBuilder();
                bool small = false;
                foreach (TouchPad p in pads)
                {
                    float dp = p.Radius * 2f * ph.h / (ph.dpi / 160f);
                    if (dp < MinTouchDp) small = true;
                    line.Append(p.Id.ToString().Substring(0, 3) + " " + dp.ToString("F0") + "  ");
                }
                sb.AppendLine(string.Format("  {0,-22} {1}{2}", ph.name, line,
                                            small ? "  <- 48dp 미만이 있다" : ""));
            }

            sb.AppendLine("  * 화면 밖으로 나가는지도 같이 본다:");
            foreach (TouchPad p in pads)
            {
                float top = p.Center.y + p.Radius;
                float right = p.Center.x + p.Radius / (2400f / 1080f);
                if (top > 1f || right > 1f || p.Center.y - p.Radius < 0f)
                    sb.AppendLine("    " + p.Id + " 화면 밖으로 나간다 (위 " + top.ToString("F2")
                                  + " · 오른쪽 " + right.ToString("F2") + ")");
            }
        }

        /// <summary>씬의 배치를 읽는다. 없으면 코드의 기본값을 본다 — 그 사실을 밝힌다.</summary>
        static TouchPad[] Pads()
        {
            var tc = Object.FindFirstObjectByType<TouchControls>(FindObjectsInactive.Include);
            if (tc != null && tc.Pads != null && tc.Pads.Length > 0) return tc.Pads;
            return TouchPad.Default();
        }
    }
}
