using UnityEngine;

namespace Demo.UI
{
    /// <summary>
    /// 터치 조작을 화면에 그리는 쪽 [분할 2026-10-12].
    ///
    /// 입력을 읽는 쪽과 가른 이유는 <see cref="Player.PunchRush"/>·<see cref="Player.PostureFix"/>와
    /// 같다 — 한 파일이 커지면 부분 수정이 비싸진다 (CLAUDE.md 규칙 3). 그리는 쪽은
    /// 라우터를 전혀 모르고, 읽는 쪽은 색을 모른다.
    ///
    /// <b>OnGUI로 그린다.</b> Canvas를 쓰면 프리팹과 씬 와이어링이 늘고, 그건 사람이 손으로
    /// 하는 작업이다 (CLAUDE.md 규칙 1). 버튼 넷과 조이스틱 하나라 비용이 문제 되는 양이 아니다.
    /// </summary>
    public sealed partial class TouchControls
    {
        static readonly Color Idle = new Color(1f, 1f, 1f, 0.16f);
        static readonly Color Down = new Color(1f, 0.72f, 0.2f, 0.55f);
        static readonly Color Ring = new Color(1f, 1f, 1f, 0.12f);
        static readonly Color Knob = new Color(1f, 1f, 1f, 0.45f);

        GUIStyle label;

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || held == null) return;
            if (TitleScreen.Blocking) return;

            bool touching = stickPointer != None;
            for (int i = 0; i < held.Length && !touching; i++) touching = held[i] != None;
            if (!touching && !showWhenNoTouch) return;

            if (label == null) label = MakeLabel();

            for (int i = 0; i < pads.Length; i++)
            {
                Vector2 c = ToGui(pads[i].PixelCenter);
                float r = pads[i].PixelRadius;
                DrawDisc(c, r, held[i] != None ? Down : Idle);

                if (string.IsNullOrEmpty(pads[i].Label)) continue;

                // 글자 크기는 버튼에 맞춘다. 해상도가 제각각이라 고정 pt는 기종마다 어긋난다.
                label.fontSize = Mathf.Max(10, Mathf.RoundToInt(r * 0.42f));
                GUI.Label(new Rect(c.x - r, c.y - r, r * 2f, r * 2f), pads[i].Label, label);
            }

            // 조이스틱 — 누르고 있을 때만 보인다. 늘 떠 있으면 왼쪽 시야를 가린다.
            if (stickPointer == None) return;

            DrawDisc(ToGui(stickOrigin), StickRadius, Ring);

            Vector2 delta = stickCurrent - stickOrigin;
            if (delta.magnitude > StickRadius) delta = delta.normalized * StickRadius;
            DrawDisc(ToGui(stickOrigin + delta), StickRadius * 0.42f, Knob);
        }

        static GUIStyle MakeLabel()
        {
            var s = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                wordWrap = false,
            };
            s.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
            return s;
        }

        /// <summary>입력 좌표는 왼쪽 아래 기준, GUI는 왼쪽 위 기준이다.</summary>
        static Vector2 ToGui(Vector2 screen) => new Vector2(screen.x, Screen.height - screen.y);

        void DrawDisc(Vector2 center, float radius, Color color)
        {
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), disc);
            GUI.color = prev;
        }

        static Texture2D MakeDisc(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float r = size * 0.5f;
            var px = new Color32[size * size];

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    // 가장자리 1.5px를 부드럽게 깎아 계단을 없앤다
                    float edge = Mathf.Clamp01(1f - Mathf.Max(0f, d - (r - 1.5f)) / 1.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(edge * 255f));
                }

            tex.SetPixels32(px);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }
    }
}
