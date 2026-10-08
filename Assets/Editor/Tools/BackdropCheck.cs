using System.Text;
using Demo.Data;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 배경과 건물 비율을 잰다 [2026-10-08]. <b>고치기 전에 먼저 이걸 돌린다.</b>
    ///
    /// <b>왜 눈으로 안 보고 재는가.</b> "배경이 밋밋하다"는 느낌이지만 원인은 숫자다 —
    /// 지면 텍스처가 1m당 몇 픽셀인지, 네모난 그림이 몇 배로 늘어나 붙었는지는
    /// 보면 "좀 이상한데" 정도로만 보이고 <b>무엇을 얼마나 고쳐야 하는지는 안 나온다</b>.
    ///
    /// 기준은 전부 <see cref="BackdropView"/>가 카메라에서 끌어낸다 — 내가 고른 숫자가 아니다.
    ///
    /// 재는 것: 지면 해상도 · 면당 그림 왜곡 · 시야에 드는 범위 · 그림자 · 안개
    /// <b>안 재는 것</b>: 실제 드로우콜(플레이 모드 필요) · 실기 프레임 · 보기 좋은가
    /// </summary>
    public static class BackdropCheck
    {
        [MenuItem("Demo/18. 배경·비율 점검", priority = 72)]
        public static void Run() => Debug.Log(Report());

        /// <summary>
        /// 점검 결과를 글로 돌려준다. <see cref="Run"/>은 이걸 콘솔에 찍기만 한다 —
        /// <b>콘솔은 긴 글을 자른다.</b> 바깥(MCP·테스트)에서 전문을 받으려면 이쪽을 부른다.
        /// </summary>
        public static string Report()
        {
            var sb = new StringBuilder();
            var view = BackdropView.Measure();

            sb.AppendLine("배경·비율 점검");
            sb.AppendLine();

            Camera(sb, view);
            Ground(sb, view);
            Sight(sb, view);
            Facades(sb);
            Lighting(sb);

            sb.AppendLine();
            sb.AppendLine("안 잰 것: 실제 드로우콜(플레이 모드) · 실기 프레임 · 보기 좋은가");
            return sb.ToString();
        }

        static void Camera(StringBuilder sb, BackdropView v)
        {
            sb.AppendLine("[카메라] 모든 기준이 여기서 나온다 (에디트 모드 위치가 아니라 설정값)");
            if (!v.Valid) { sb.AppendLine("  못 쟀다 — 카메라·FollowCamera·GameConfigRef 중 하나가 없다"); return; }

            sb.AppendLine("  거리 " + v.Distance.ToString("F1") + "m · 내림각 " + v.Pitch.ToString("F0")
                          + "도 · FOV " + v.Fov.ToString("F0") + "도");
            sb.AppendLine("  초점에서 담는 폭 " + v.ViewWide.ToString("F1") + "m (16:9)");
            sb.AppendLine("  화면 1m = " + v.PixelsPerMeter.ToString("F0") + "px  <- 지면이 이만큼은 돼야 안 뭉개진다");
        }

        static void Ground(StringBuilder sb, BackdropView v)
        {
            sb.AppendLine();
            sb.AppendLine("[지면] 화면의 대부분을 차지한다");

            var go = GameObject.Find("Ground");
            if (go == null) { sb.AppendLine("  Ground 없음"); return; }

            var r = go.GetComponent<Renderer>();
            Vector3 s = go.transform.localScale;
            sb.AppendLine("  크기 " + s.x.ToString("F0") + " x " + s.z.ToString("F0") + "m"
                          + (r != null && r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off
                             ? "   <- 그림자를 드리운다. 지면이 드리울 곳은 없다" : ""));

            if (r == null || r.sharedMaterial == null) { sb.AppendLine("  머티리얼 없음"); return; }

            Material m = r.sharedMaterial;
            var tex = (m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.mainTexture) as Texture2D;
            Vector2 tile = m.HasProperty("_BaseMap") ? m.GetTextureScale("_BaseMap") : Vector2.one;

            if (tex == null) { sb.AppendLine("  머티리얼 " + m.name + " — 텍스처 없음 (민짜)"); return; }

            float repeat = tile.x > 0.001f ? s.x / tile.x : s.x;
            float have = repeat > 0.01f ? tex.width / repeat : 0f;

            sb.AppendLine("  머티리얼 " + m.name + " · 텍스처 " + tex.name + " " + tex.width + "px"
                          + " · 이방성 " + tex.anisoLevel + (tex.anisoLevel < 2 ? "   <- 비스듬히 보면 뭉개진다" : ""));
            sb.AppendLine("  타일링 " + tile.x.ToString("F1") + " -> " + repeat.ToString("F1") + "m마다 반복");
            sb.AppendLine("  1m당 " + have.ToString("F1") + "px" + Verdict(have, v.PixelsPerMeter));

            if (v.Valid && repeat > 0.01f)
                sb.AppendLine("  화면 한 폭에 같은 타일이 " + (v.ViewWide / repeat).ToString("F1")
                              + "번 보인다" + (v.ViewWide / repeat > 2.5f ? "   <- 반복이 눈에 띈다" : ""));
        }

        static string Verdict(float have, float want)
        {
            if (want < 1f) return "";
            if (have < want * 0.4f) return "   <- 많이 모자람. 확대돼 뭉개진다";
            if (have < want * 0.8f) return "   <- 모자람";
            if (have > want * 3f) return "   <- 과함. 용량만 먹는다";
            return "   <- 적당";
        }

        /// <summary>
        /// 화면에 <b>무엇이 들어올 수 있는가</b> [실측 2026-10-08].
        ///
        /// 처음에는 경계 벽에 스카이라인을 입히려 했다. 재 보니 헛일이었다 —
        /// 시야의 위쪽 모서리가 수평에서 20도 <b>아래</b>를 보므로 모든 시선이 지면에 꽂힌다.
        /// 그래서 (1) 하늘이 한 픽셀도 안 보이고 (2) 60m 밖의 경계 벽도 안 보인다.
        /// <b>이 게임에서 '배경'은 지면 전부다.</b> 먼 배경에 들일 노력은 전부 지면으로 간다.
        /// </summary>
        static void Sight(StringBuilder sb, BackdropView v)
        {
            sb.AppendLine();
            sb.AppendLine("[시야] 화면에 무엇이 들어오는가");
            if (!v.Valid) { sb.AppendLine("  못 쟀다"); return; }

            sb.AppendLine("  시야 위쪽 모서리: 수평에서 " + Mathf.Abs(v.TopDeg).ToString("F0") + "도 "
                          + (v.TopDeg > 0f ? "아래" : "위"));

            if (v.TopDeg > 0.5f)
            {
                sb.AppendLine("  하늘: 안 보인다 (모든 시선이 지면에 꽂힌다)");
                sb.AppendLine("  지면이 보이는 최대 거리 " + v.Reach.ToString("F0") + "m — 그 너머는 화면 밖");
                sb.AppendLine("  * 경계 벽(60m)과 하늘상자는 손대도 안 보인다. 배경 = 지면 전부다.");
            }
            else
            {
                sb.AppendLine("  하늘이 보인다 — 하늘상자와 지평선이 화면에 든다");
            }

            sb.AppendLine("  건물 꼭대기가 화면에 드는 최대 거리:");
            foreach (StructureData d in All())
            {
                float far = v.TopVisibleAt(d.colliderSize.y);
                sb.AppendLine("    " + d.id.PadRight(12) + " 높이 " + d.colliderSize.y.ToString("F1") + "m -> "
                              + (far > 0.1f ? far.ToString("F1") + "m" : "최대 줌에서도 꼭대기가 잘린다"));
            }
        }

        /// <summary>
        /// 네모난 그림을 네모가 아닌 면에 붙이면 늘어난다. 얼마나 늘어나는지를 잰다.
        /// 큐브의 UV는 면마다 0~1이므로 <b>면의 가로세로비가 곧 늘어나는 배율</b>이다.
        /// </summary>
        static void Facades(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("[건물 비율] 네모난 그림이 몇 배로 늘어나 붙는가");
            sb.AppendLine(string.Format("  {0,-12} {1,-20} {2,7} {3,8}  {4}",
                                        "종류", "가로x높이x깊이", "앞면", "옆면", "판정"));

            foreach (StructureData d in All())
            {
                Vector3 s = d.colliderSize;
                if (s.y < 0.01f) continue;

                float src = d.facadeFront.height > 0 ? (float)d.facadeFront.width / d.facadeFront.height : 1f;
                float front = Stretch(s.x / s.y, src);
                float side = Stretch(s.z / s.y, src);

                sb.AppendLine(string.Format("  {0,-12} {1,-20} {2,6:F2}x {3,7:F2}x  {4}",
                    d.id,
                    s.x.ToString("F1") + " x " + s.y.ToString("F1") + " x " + s.z.ToString("F1"),
                    front, side, Stretched(Mathf.Max(front, side))));
            }

            sb.AppendLine("  * 1.00x면 그림 그대로다. 2.00x면 두 배로 늘어났다는 뜻이다.");
            sb.AppendLine("  * 지금은 앞면 그림을 네 면에 다 쓰므로 옆면 배율이 그대로 보인다.");
        }

        /// <summary>면의 비와 그림의 비가 몇 배 어긋나는가. 늘어나든 눌리든 1보다 큰 값으로 돌려준다.</summary>
        static float Stretch(float face, float src)
        {
            if (face < 0.001f || src < 0.001f) return 1f;
            float k = face / src;
            return k < 1f ? 1f / k : k;
        }

        static string Stretched(float k)
        {
            if (k < 1.15f) return "그대로";
            if (k < 1.5f) return "조금 늘어남";
            if (k < 2.5f) return "눈에 띔";
            return "심함 — 건물로 안 보인다";
        }

        static void Lighting(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("[조명·안개]");

            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                sb.AppendLine("  " + l.name + ": " + l.type + " · 그림자 " + l.shadows
                              + " · 세기 " + l.intensity.ToString("F2")
                              + (l.shadows != LightShadows.None
                                 ? "   <- 실시간 그림자. 예산은 '사용 안 함'이지만 블롭 대체가 아직 없다" : ""));

            sb.AppendLine("  안개: " + (RenderSettings.fog
                ? RenderSettings.fogMode + " " + RenderSettings.fogStartDistance.ToString("F0")
                  + "~" + RenderSettings.fogEndDistance.ToString("F0") + "m " + RenderSettings.fogColor
                : "꺼짐   <- 시야 끝에서 지면이 잘리는 자리가 그대로 보인다"));
        }

        /// <summary>그림이 붙은 건물만. id나 정면도가 없는 것은 아직 쓰이지 않는 에셋이다.</summary>
        internal static System.Collections.Generic.IEnumerable<StructureData> All()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:StructureData"))
            {
                var d = AssetDatabase.LoadAssetAtPath<StructureData>(AssetDatabase.GUIDToAssetPath(guid));
                if (d != null && !string.IsNullOrEmpty(d.id) && d.facadeFront != null) yield return d;
            }
        }
    }
}
