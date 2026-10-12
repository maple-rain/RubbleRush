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
        /// <summary>꺼짐. 옅은 흰 동그라미 — 있다는 것만 알린다.</summary>
        static readonly Color Idle = new Color(1f, 1f, 1f, 0.16f);

        /// <summary>
        /// <b>켜짐</b> [지시 2026-10-12]. 대쉬와 케이블은 토글이라 손을 뗀 뒤에도 켜져 있는데,
        /// 전에는 손가락이 닿아 있는 동안만 색이 바뀌어 <b>켜짐/꺼짐이 화면에 안 나왔다</b> —
        /// 지금 케이블이 걸려 있는지 아닌지를 눌러 봐야 알았다.
        /// 기획자가 "눌렸을 때 조금 더 어둡게"로 정했다. 밝은 지면 위에서 어두운 쪽이
        /// '잠긴' 느낌을 주고, 누르는 순간의 주황과도 안 헷갈린다.
        /// </summary>
        static readonly Color On = new Color(0.05f, 0.06f, 0.08f, 0.62f);

        /// <summary>손가락이 닿아 있는 동안. 켜짐과 별개로 "지금 눌렸다"를 알린다.</summary>
        static readonly Color Down = new Color(1f, 0.72f, 0.2f, 0.55f);

        static readonly Color Ring = new Color(1f, 1f, 1f, 0.12f);
        static readonly Color Knob = new Color(1f, 1f, 1f, 0.45f);

        GUIStyle label;

        /// <summary>
        /// 그 버튼이 <b>켜져 있는가</b>. 토글인 것만 의미가 있다 —
        /// 부수기·놓기는 누르는 순간만 있는 것이라 켜짐이라는 상태가 없다.
        /// </summary>
        bool IsOn(TouchButton id)
        {
            if (router == null) return false;
            switch (id)
            {
                case TouchButton.Action: return router.ActionActive;
                case TouchButton.Run: return router.RunActive;
                default: return false;
            }
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || held == null) return;
            if (!Owns) return;          // 타이틀·파괴 화면에서는 안 그린다

            bool touching = stickPointer != None;
            for (int i = 0; i < held.Length && !touching; i++) touching = held[i] != None;
            if (!touching && !showWhenNoTouch) return;

            if (label == null) label = MakeLabel();

            for (int i = 0; i < pads.Length; i++)
            {
                Vector2 c = ToGui(pads[i].PixelCenter);
                float r = pads[i].PixelRadius;

                // 누름이 켜짐보다 앞선다 — 누르는 순간의 되먹임이 제일 급하다.
                bool on = IsOn(pads[i].Id);
                DrawDisc(c, r, held[i] != None ? Down : on ? On : Idle);

                // 켜져 있으면 테두리를 하나 더 두른다. 어두운 바탕 위에서는 어두운 원이
                // 배경에 묻히는데, 밝은 테가 있으면 어느 바탕에서도 윤곽이 남는다.
                if (on && held[i] == None) DrawRing(c, r, new Color(1f, 0.82f, 0.35f, 0.75f));

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

        /// <summary>
        /// 동그라미 테두리. <b>속이 빈 텍스처를 따로 만들어 쓴다</b> —
        /// 꽉 찬 원 위에 작은 원을 덮어 파낼 수는 없다. GUI의 알파는 덮어쓰기가 아니라
        /// 섞기라서, 투명한 원을 그리면 아무것도 안 그려질 뿐 구멍이 나지 않는다.
        /// </summary>
        void DrawRing(Vector2 center, float radius, Color color)
        {
            if (ring == null) ring = MakeRing(64, 0.12f);

            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), ring);
            GUI.color = prev;
        }

        void DrawDisc(Vector2 center, float radius, Color color)
        {
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), disc);
            GUI.color = prev;
        }

        /// <summary>
        /// 속이 빈 동그라미. <paramref name="thickness"/>는 반지름 대비 두께다.
        /// <see cref="MakeDisc"/>와 같은 방식으로 가장자리를 부드럽게 깎는다 —
        /// 안쪽과 바깥쪽 양쪽을 깎아야 고리의 두 테가 다 매끈하다.
        /// </summary>
        static Texture2D MakeRing(int size, float thickness)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float outer = size * 0.5f;
            float inner = outer * (1f - thickness);
            var px = new Color32[size * size];

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(outer, outer));
                    float a = Mathf.Clamp01(1f - Mathf.Max(0f, d - (outer - 1.5f)) / 1.5f)   // 바깥 테
                            * Mathf.Clamp01(1f - Mathf.Max(0f, inner - d) / 1.5f);           // 안쪽 테
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }

            tex.SetPixels32(px);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
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
