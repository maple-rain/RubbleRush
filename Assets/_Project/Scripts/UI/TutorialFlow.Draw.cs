using UnityEngine;

namespace Demo.UI
{
    /// <summary>
    /// 지금 해야 할 것을 <b>화면 맨 위에</b> 한 줄로 띄운다.
    ///
    /// <b>왜 맨 위인가.</b> 실측한 자리들은 이미 임자가 있다 [800x600 빌드 화면 기준
    /// 2026-10-17]: 왼쪽 위는 <see cref="HUD"/>(세로 9~24%), 오른쪽 아래는
    /// <see cref="TouchControls"/>의 대쉬·케이블 단추, 왼쪽 아래는 조이스틱.
    /// HUD 위쪽 띠(세로 1.5~9%)가 비어 있어서 거기로 간다.
    ///
    /// <b>Canvas를 안 쓴다.</b> 이 프로젝트 UI는 전부 IMGUI다. 섞으면 두 체계를
    /// 동시에 건사해야 한다.
    /// </summary>
    public sealed partial class TutorialFlow
    {
        GUIStyle line;

        void OnGUI()
        {
            // 화면을 덮는 창이 떠 있으면 그리지 않는다.
            // <b>네 개를 다 봐야 한다</b> — 이 자리에서 세 번 틀렸다 (TitleScreen 만
            // 보다가 결과·업그레이드·조작법 위에 겹쳐 그린 일이 있다).
            if (TitleScreen.Blocking || ResultScreen.Blocking
                || UpgradeScreen.Blocking || HowToScreen.Blocking) return;

            if (loop == null || !loop.IsRunning) return;

            string text = Current(out bool isDone);
            if (string.IsNullOrEmpty(text)) return;

            float h = Screen.height, w = Screen.width;

            // 글꼴은 반드시 UiSkin 에서 받는다 — 직접 만들면 빌드에서 한글이
            // 빈칸이 된다 [실측 2026-10-17. 자세한 사정은 TitleScreen].
            if (line == null || line.font != UiSkin.Body)
                line = UiSkin.Label(true, TextAnchor.MiddleCenter);

            line.fontSize = Mathf.RoundToInt(h * 0.042f);
            UiSkin.Tint(line, isDone ? UiSkin.Accent : UiSkin.Ink);

            Vector2 size = line.CalcSize(new GUIContent(text));
            var r = new Rect((w - size.x) * 0.5f, h * 0.018f, size.x, size.y);

            UiSkin.Plate(r, h * 0.014f);
            UiSkin.Shadowed(r, text, line);

            Progress(w, h);
        }

        /// <summary>지금 띄울 말. 방금 끝난 단계가 있으면 그쪽을 잠깐 보여 준다.</summary>
        string Current(out bool isDone)
        {
            isDone = false;

            if (Time.time < doneUntil)
            {
                int just = Mathf.Clamp(at - 1, 0, Script.Length - 1);
                if (!string.IsNullOrEmpty(Script[just].Done))
                {
                    isDone = true;
                    return Script[just].Done;
                }
            }

            if (finished || at >= Script.Length) return null;
            return Script[at].Line;
        }

        /// <summary>
        /// 몇 단계 중 몇 번째인지 점으로 보여 준다. 숫자로 적으면 글이 길어지고,
        /// 끝이 보이지 않으면 "언제 끝나나" 싶어진다.
        ///
        /// 마지막 <c>Free</c> 단계는 세지 않는다 — 가르치는 단계가 아니다.
        /// </summary>
        void Progress(float w, float h)
        {
            int teach = Script.Length - 1;
            if (at >= teach) return;

            float d = h * 0.011f, gap = d * 1.9f;
            float total = teach * d + (teach - 1) * (gap - d);
            float x = (w - total) * 0.5f, y = h * 0.098f;

            for (int i = 0; i < teach; i++)
            {
                var dot = new Rect(x + i * gap, y, d, d);
                UiSkin.Fill(dot, i < at ? UiSkin.Accent
                                        : i == at ? UiSkin.Ink : UiSkin.InkDim);
            }
        }
    }
}
