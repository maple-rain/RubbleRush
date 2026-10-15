using Demo.Data;
using UnityEngine;

namespace Demo.UI
{
    /// <summary>
    /// 등급 눈금이 박힌 점수 막대 [지시 2026-10-15, 참고 이미지].
    ///
    /// 기획자가 짚은 그림이다 — 막대 하나가 왼쪽에서 오른쪽으로 차오르고, 그 위에
    /// 등급(B·A·S·SS)이 제 자리에 박혀 있다. 지금 몇 점인지보다 <b>다음 글자까지
    /// 얼마나 남았는지</b>가 곁눈으로 읽히는 것이 요점이다.
    ///
    /// <b>왜 할당량 막대를 대신하는가.</b> 기획자: "치워야하는 잔해와 달성 점수간의
    /// 유기적 연결이 보이지 않음." 할당량 막대는 60단위를 채우면 끝이라 그 뒤로는
    /// 아무것도 안 알려 줬다. 점수 막대는 판이 끝날 때까지 계속 움직인다.
    ///
    /// <b>값을 만들지 않는다.</b> 눈금 자리도 폭도 전부 <see cref="GameConfig.grades"/>에서
    /// 읽는다. 표에 칸을 더 넣으면 눈금이 하나 더 생기고, 글자를 비워 두면 눈금만 선다.
    /// </summary>
    public sealed partial class UiSkin
    {
        /// <summary>
        /// 점수 막대를 그린다. <paramref name="labelRow"/>는 막대 <b>바로 위</b>의 띠로,
        /// 등급 글자가 거기 앉는다. 폭이 0이면 글자 없이 눈금만 그린다.
        /// </summary>
        /// <param name="bar">막대 자리</param>
        /// <param name="labelRow">등급 글자가 앉을 자리. 보통 막대 바로 위</param>
        /// <param name="score">지금 점수</param>
        /// <param name="grades">등급표. <see cref="GameConfig.grades"/> 그대로</param>
        /// <param name="style">글자 모양. 가운데 정렬로 바꿔 쓰고 되돌려 놓는다</param>
        public static void GradeBar(Rect bar, Rect labelRow, int score,
                                    GameConfig.Grade[] grades, GUIStyle style)
        {
            // 막대의 오른쪽 끝은 <b>가장 높은 등급</b>이다. 표가 비면 그릴 기준이 없다.
            float top = 0f;
            if (grades != null)
                foreach (GameConfig.Grade g in grades)
                    if (g != null && g.minScore > top) top = g.minScore;

            if (top <= 0f)
            {
                // 등급표가 없거나 전부 0점이다. 눈금 없이 빈 막대만 — 거짓말은 안 한다.
                Bar(bar, 0f, Accent);
                return;
            }

            Bar(bar, score / top, Accent);

            if (grades == null) return;

            TextAnchor keepAnchor = style.alignment;
            Color keepColor = style.normal.textColor;
            style.alignment = TextAnchor.MiddleCenter;

            float tickW = Mathf.Max(1f, bar.height * 0.12f);

            foreach (GameConfig.Grade g in grades)
            {
                if (g == null || g.minScore <= 0f) continue;   // 맨 아래 칸은 0점이라 눈금이 없다

                float at = Mathf.Clamp01(g.minScore / top);
                float x = bar.x + bar.width * at;

                // 눈금. 오른쪽 끝 등급은 막대 밖으로 나가지 않게 안쪽으로 당긴다.
                float tx = Mathf.Min(x, bar.xMax - tickW);
                Fill(new Rect(tx, bar.y, tickW, bar.height), Ink);

                if (labelRow.width <= 0f || string.IsNullOrEmpty(g.label)) continue;

                // <b>닿은 등급은 밝게, 아직인 등급은 흐리게.</b> 숫자를 읽지 않아도
                // 어디까지 왔는지가 보인다 — 그게 이 막대를 두는 이유다.
                style.normal.textColor = score >= g.minScore ? Accent : InkDim;

                float lw = labelRow.height * 2.2f;
                var cell = new Rect(x - lw * 0.5f, labelRow.y, lw, labelRow.height);

                // 양 끝에서 글자가 판 밖으로 새지 않게 가둔다.
                if (cell.x < bar.x) cell.x = bar.x;
                if (cell.xMax > bar.xMax) cell.x = bar.xMax - cell.width;

                GUI.Label(cell, g.label, style);
            }

            style.alignment = keepAnchor;
            style.normal.textColor = keepColor;
        }
    }
}
