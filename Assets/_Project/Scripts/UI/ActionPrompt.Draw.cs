using Demo.Core;
using UnityEngine;

namespace Demo.UI
{
    /// <summary>
    /// <see cref="ActionPrompt"/>의 그리는 쪽.
    ///
    /// <b>왜 갈랐나.</b> 한 파일이 300줄을 넘었다 (CLAUDE.md 규칙 3). 가른 자리는
    /// '무엇을 띄울지 정하는 일'과 '그것을 어떻게 그릴지'의 사이다 — 둘은 줄 목록
    /// 하나로만 이어져 있어서, 한쪽을 고칠 때 다른 쪽을 읽을 일이 거의 없다.
    /// <see cref="TouchControls"/>를 <c>TouchControls.Draw.cs</c>로 가른 것과 같은 자리다.
    /// </summary>
    public sealed partial class ActionPrompt
    {
        Texture2D disc;
        GUIStyle label, keyCap;

        // ── 그리기 ──────────────────────────────────────────────────────────────

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (label == null || label.font != UiSkin.Body) Styles();

            Blocked();
            if (rows.Count == 0) return;
            if (!Touch && !showKeysOnDesktop) return;

            float r = Screen.height * knobFraction;
            for (int i = 0; i < rows.Count; i++)
            {
                Vector2 c = Anchor(i);
                if (c.x < -9000f) continue;

                Vector2 g = new Vector2(c.x, Screen.height - c.y);
                bool down = heldRow == i;

                Disc(g, r, down ? new Color(1f, 0.78f, 0.25f, 0.85f)
                                : new Color(0.08f, 0.09f, 0.11f, 0.72f));
                Disc(g, r * 0.88f, down ? new Color(1f, 0.86f, 0.45f, 0.95f)
                                        : new Color(1f, 1f, 1f, 0.10f));

                keyCap.fontSize = Mathf.Max(11, Mathf.RoundToInt(r * 0.72f));
                keyCap.normal.textColor = down ? new Color(0.1f, 0.08f, 0.04f) : Color.white;
                GUI.Label(new Rect(g.x - r, g.y - r, r * 2f, r * 2f), Touch ? "" : rows[i].Key, keyCap);

                // 글자는 동그라미 오른쪽에. 레퍼런스처럼 이름이 옆으로 붙는다.
                // <b>오른쪽이 모자라면 왼쪽으로 넘긴다</b> — 화면 가장자리의 건물에서
                // 글자만 잘려 나가면 동그라미가 무엇을 하는 것인지 알 수 없다.
                label.fontSize = Mathf.Max(11, Mathf.RoundToInt(r * 0.56f));
                float wide = Screen.width * 0.3f;
                bool toLeft = g.x + r * 1.25f + wide > Screen.width;
                label.alignment = toLeft ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
                float x = toLeft ? g.x - r * 1.25f - wide : g.x + r * 1.25f;
                GUI.Label(new Rect(x, g.y - r * 0.7f, wide, r * 1.4f), rows[i].Text, label);
                label.alignment = TextAnchor.MiddleLeft;
            }
        }

        /// <summary>
        /// 왜 못 부수는지 한 줄 [2026-10-12]. 플레이어 위에 흐리게 띄운다.
        ///
        /// <b>부수기 프롬프트와 같은 자리다.</b> 거기에 뜰 것이 떴어야 하는데 못 떴다는
        /// 뜻이므로, 다른 데 띄우면 둘을 잇기 어렵다. 색은 죽여서 "할 수 있는 것"이 아니라
        /// "안 되는 이유"로 읽히게 한다.
        /// </summary>
        void Blocked()
        {
            if (string.IsNullOrEmpty(blocked) || eye == null) return;

            var mode = GameModeController.Instance;
            if (mode == null || !mode.IsField) return;

            Transform who = cargo != null ? cargo.transform : transform;
            Vector3 p = eye.WorldToScreenPoint(who.position + Vector3.up * 2.2f);
            if (p.z <= 0f) return;

            float r = Screen.height * knobFraction;
            var box = new Rect(p.x - Screen.width * 0.2f, Screen.height - p.y - r * 0.6f,
                               Screen.width * 0.4f, r * 1.2f);

            label.alignment = TextAnchor.MiddleCenter;
            label.fontSize = Mathf.Max(11, Mathf.RoundToInt(r * 0.5f));

            label.normal.textColor = new Color(0f, 0f, 0f, 0.5f);
            GUI.Label(new Rect(box.x + 2f, box.y + 2f, box.width, box.height), blocked, label);
            label.normal.textColor = new Color(1f, 0.78f, 0.6f, 0.75f);
            GUI.Label(box, blocked, label);

            label.alignment = TextAnchor.MiddleLeft;
            label.normal.textColor = new Color(1f, 1f, 1f, 0.95f);
        }

        /// <summary>글꼴은 <see cref="UiSkin"/>에서 받는다 — 직접 만들면 빌드에서
        /// 한글이 빈칸이 된다 [고침 2026-10-17. 자세한 사정은 <see cref="TitleScreen"/>].</summary>
        void Styles()
        {
            label = UiSkin.Label(true, TextAnchor.MiddleLeft);
            label.normal.textColor = new Color(1f, 1f, 1f, 0.95f);

            keyCap = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
        }

        void Disc(Vector2 center, float radius, Color color)
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
