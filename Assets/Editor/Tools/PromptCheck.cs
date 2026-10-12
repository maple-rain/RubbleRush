using System.Text;
using Demo.Data;
using Demo.UI;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 부수기 안내가 <b>화면 안에 있는가</b> [지적 2026-10-12].
    ///
    /// <b>무엇이 틀려 있었나.</b> 안내를 건물 높이의 <c>anchorHeight</c>(75%)에 달아 뒀다.
    /// 카메라는 50도로 내려다보고 시야 위쪽 모서리가 수평에서 20도 아래라, 건물 옆에 섰을 때
    /// 화면 맨 위에 걸리는 세계의 높이는 4m 남짓이다 — <b>높은 건물일수록 안내가 머리 위
    /// 화면 밖에 뜬다.</b> "E가 가끔 안 뜬다"의 정체이고, 실측으로 가장 큰 건물은
    /// 어느 방향에서 다가가도 안 보였다.
    ///
    /// <b>접근 방향을 24개로 훑는다.</b> 한 방향만 재면 거짓 합격이 나온다 — 카메라 수평각이
    /// 45도로 고정이라 같은 건물도 어느 쪽에서 다가가느냐에 따라 안내가 화면 위로 가기도 하고
    /// 아래로 가기도 한다. <b>한 방향이라도 밖이면 플레이어는 그 방향에서 못 본다.</b>
    ///
    /// <b>플레이 모드가 필요 없다.</b> 구역을 짓지 않고 종류 데이터만 읽는다 — 안내 자리는
    /// 건물의 치수와 카메라만으로 정해지고, 구역 안의 어느 자리에 서 있는가는 상관이 없다.
    ///
    /// <b>안 재는 것</b>: 실제 화면 캡처 · 손가락이 닿는지 · 글자가 겹치는지.
    /// 겹침은 <see cref="TouchLayoutCheck"/>가 본다.
    /// </summary>
    public static class PromptCheck
    {
        /// <summary>훑을 접근 방향 수. 24면 15도마다다.</summary>
        const int Directions = 24;

        /// <summary>벽에서 떨어져 서는 거리(m). 상호작용 사거리 안쪽이면 된다.</summary>
        const float Standoff = 1.2f;

        [MenuItem("Demo/24. 부수기 안내가 화면 안에 있는가", priority = 124)]
        public static void Run() => Debug.Log(Report());

        public static string Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine("부수기 안내 자리 점검");
            sb.AppendLine();

            BackdropView view = BackdropView.Measure();
            if (!view.Valid)
                return sb.Append("  카메라를 못 쟀다 — Main.unity를 열고 다시 누를 것.").ToString();

            var prompt = Object.FindFirstObjectByType<ActionPrompt>();
            if (prompt == null)
                return sb.Append("  씬에 ActionPrompt가 없다.").ToString();

            // <b>씬에 박힌 값을 읽는다.</b> C# 기본값을 읽으면 씬이 다른 값을 들고 있을 때
            // 고쳐 놓고도 통과한다 — 이 프로젝트에서 두 번 당한 함정이다.
            var so = new SerializedObject(prompt);
            float knobFraction = so.FindProperty("knobFraction").floatValue;
            float anchorHeight = so.FindProperty("anchorHeight").floatValue;

            float h = BackdropView.ScreenWide * 9f / 16f;
            float knob = h * knobFraction;
            float ceiling = h - knob * ActionPrompt.CeilingMargin;

            sb.AppendLine(string.Format("  화면 {0:0}x{1:0} · 내려다보기 {2:0}도 · 수평 {3:0}도 · 시야 {4:0}도 · 거리 {5:0.0}m",
                                        BackdropView.ScreenWide, h, view.Pitch, view.Yaw, view.Fov, view.Distance));
            sb.AppendLine(string.Format("  동그라미 반경 {0:0}px · 천장 y={1:0} · 머리 높이 = 건물의 {2:0}%",
                                        knob, ceiling, anchorHeight * 100f));
            sb.AppendLine();
            sb.AppendLine(string.Format("  {0,-12} {1,-6} {2,-22} {3}", "종류", "높이", "머리에 고정하면", "모서리를 타면"));

            int broken = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:StructureData"))
            {
                var data = AssetDatabase.LoadAssetAtPath<StructureData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null || data.facadeFront == null) continue;   // 구역이 안 쓰는 옛 에셋은 건너뛴다

                int offOld = 0, offNew = 0, seen = 0;
                float worstOld = float.MinValue, worstNew = float.MinValue;

                for (int k = 0; k < Directions; k++)
                {
                    if (!Sample(data, k, view, anchorHeight, ceiling, out float oldY, out float newY)) continue;
                    seen++;
                    if (oldY > ceiling) offOld++;
                    if (newY > ceiling + 0.5f) offNew++;
                    worstOld = Mathf.Max(worstOld, oldY);
                    worstNew = Mathf.Max(worstNew, newY);
                }

                if (seen == 0) { sb.AppendLine(string.Format("  {0,-12} {1,4:0.0}m  (카메라 뒤라 못 쟀다)", data.id, data.colliderSize.y)); continue; }
                if (offNew > 0) broken++;

                sb.AppendLine(string.Format("  {0,-12} {1,4:0.0}m  밖 {2,2}/{3,2}방향 y={4,6:0}   밖 {5}/{6}방향 y={7:0}",
                                            data.id, data.colliderSize.y, offOld, seen, worstOld, offNew, seen, worstNew));
            }

            sb.AppendLine();
            sb.AppendLine(broken == 0 ? "  모든 종류가 모든 방향에서 화면 안이다."
                                      : "  " + broken + "종이 아직 화면 밖으로 나간다.");
            sb.AppendLine("  '밖'은 안내가 화면 위 끝을 넘어간 접근 방향의 수다. 한 방향이라도 밖이면 그 방향에서는 못 본다.");
            sb.AppendLine("  안 재는 것: 실제 캡처 · 손가락 닿는 범위 · 글자 겹침(Demo/22) · 건물에 가려지는 경우");
            return sb.ToString();
        }

        /// <summary>
        /// <paramref name="k"/>번째 방향에서 다가갔을 때 안내가 찍히는 화면 y 둘.
        /// 돌려주는 값이 false면 그 방향에서는 안내가 카메라 뒤라 잴 수가 없다.
        /// </summary>
        static bool Sample(StructureData data, int k, BackdropView view, float anchorHeight,
                           float ceiling, out float oldY, out float newY)
        {
            oldY = newY = 0f;

            Vector3 size = data.colliderSize;
            float a = k * Mathf.PI * 2f / Directions;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));

            // 그 방향으로 벽이 어디서 끝나는가 — 상자라 큰 축이 정한다.
            float reach = Mathf.Max(Mathf.Abs(dir.x) * size.x, Mathf.Abs(dir.z) * size.z) * 0.5f + Standoff;
            Vector3 stand = dir * reach;

            Quaternion rot = Quaternion.Euler(view.Pitch, view.Yaw, 0f);
            Vector3 cam = stand - rot * Vector3.forward * view.Distance;

            if (!Project(rot, cam, view, Vector3.up * size.y * anchorHeight, out float head)) return false;
            if (!Project(rot, cam, view, Vector3.up * ActionPrompt.FootHeight, out float foot)) return false;

            oldY = head;
            newY = head;
            if (head > ceiling)
            {
                float span = head - foot;
                newY = Mathf.Lerp(foot, head, Mathf.Abs(span) > 1f ? Mathf.Clamp01((ceiling - foot) / span) : 0f);
            }
            newY = Mathf.Clamp(newY, 0f, ceiling);
            return true;
        }

        /// <summary>
        /// 월드 한 점의 화면 y. <b>카메라를 만들지 않고 직접 센다</b> — 에디트 모드에서
        /// 씬 뷰의 카메라를 빌리면 그때 마우스가 멈춘 자리를 재게 된다.
        /// </summary>
        static bool Project(Quaternion rot, Vector3 cam, BackdropView view, Vector3 world, out float screenY)
        {
            screenY = 0f;
            Vector3 v = Quaternion.Inverse(rot) * (world - cam);
            if (v.z <= 0.01f) return false;

            float h = BackdropView.ScreenWide * 9f / 16f;
            float tanHalf = Mathf.Tan(view.Fov * 0.5f * Mathf.Deg2Rad);
            screenY = (v.y / (v.z * tanHalf) + 1f) * 0.5f * h;
            return true;
        }
    }
}
