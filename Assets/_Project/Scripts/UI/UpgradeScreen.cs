using System.Collections.Generic;
using Demo.Core;
using Demo.Data;
using UnityEngine;

namespace Demo.UI
{
    /// <summary>
    /// 구멍 단계가 오를 때 뜨는 카드 고르기 [지시 2026-10-13].
    ///
    /// <b>세 장 중 하나.</b> 자동으로 주면 지금의 반경 성장과 똑같아서 고르는 맛이 없다.
    /// 고르지 않은 두 장은 다음 단계에 다시 올라오므로, 한 판에 네 번 고르고
    /// 일곱 장은 못 본 채 끝난다 — 그게 다시 켜는 이유가 된다.
    ///
    /// <b>시간을 멈춘다.</b> 구멍이 커지는 순간은 보통 잔해를 밀어 넣는 와중이라,
    /// 멈추지 않으면 읽는 동안 로봇이 떠내려간다.
    ///
    /// <b>Canvas를 안 쓴다.</b> HUD·타이틀과 같은 이유다 (CLAUDE.md 규칙 1).
    /// </summary>
    public sealed class UpgradeScreen : MonoBehaviour
    {
        [SerializeField, Range(0.02f, 0.06f), Tooltip("설명 글자 크기. 화면 세로 대비")]
        float textFraction = 0.026f;

        readonly List<GameConfig.Upgrade> offer = new List<GameConfig.Upgrade>();

        int stage;
        float keepTimeScale = 1f;

        GUIStyle head, title, body;

        /// <summary>떠 있는 동안인가. HUD가 자기를 숨기는 데 쓴다.</summary>
        public static bool Blocking { get; private set; }

        /// <summary>
        /// 그 단계의 카드를 내민다. 후보가 없으면 아무것도 안 하고 false를 돌려준다 —
        /// 부르는 쪽이 "멈췄는데 화면이 안 뜬다"를 안 겪게 한다.
        /// </summary>
        public bool Offer(int stageNumber)
        {
            if (Blocking) return false;

            GameConfig cfg = GameConfigRef.Current;
            Upgrades.Offer(cfg, stageNumber, offer);
            if (offer.Count == 0) return false;

            stage = stageNumber;
            Blocking = true;
            keepTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            return true;
        }

        void OnDestroy()
        {
            if (Blocking) Time.timeScale = keepTimeScale <= 0f ? 1f : keepTimeScale;
            Blocking = false;
        }

        void Pick(GameConfig.Upgrade u)
        {
            Upgrades.Take(u);
            Demo.Utils.GameAudio.Play(Demo.Data.Sfx.UpgradePick, transform.position);
            offer.Clear();
            Blocking = false;
            Time.timeScale = keepTimeScale <= 0f ? 1f : keepTimeScale;
        }

        void OnGUI()
        {
            if (!Blocking || offer.Count == 0) return;
            if (title == null || title.font != UiSkin.Body) Styles();

            float w = Screen.width, h = Screen.height;
            float line = h * textFraction;

            head.fontSize = Mathf.RoundToInt(h * 0.055f);
            title.fontSize = Mathf.RoundToInt(line * 1.5f);
            body.fontSize = Mathf.RoundToInt(line);

            UiSkin.Fill(new Rect(0f, 0f, w, h), UiSkin.Veil);

            // ── 왜 떴는지 ──────────────────────────────────────────────────────
            head.normal.textColor = UiSkin.Accent;
            UiSkin.Shadowed(new Rect(0f, h * 0.10f, w, h * 0.08f), "구멍이 " + stage + "단계가 됐다", head);

            body.alignment = TextAnchor.MiddleCenter;
            body.normal.textColor = UiSkin.InkDim;
            UiSkin.Shadowed(new Rect(0f, h * 0.19f, w, line * 1.6f),
                            offer.Count > 1 ? "하나를 고르세요" : "보상", body);

            // ── 카드 ───────────────────────────────────────────────────────────
            int n = offer.Count;
            float gap = w * 0.025f;
            float cw = Mathf.Min(w * 0.24f, (w * 0.84f - gap * (n - 1)) / n);
            float ch = h * 0.42f;
            float x = (w - (cw * n + gap * (n - 1))) * 0.5f;
            float y = h * 0.28f;

            for (int i = 0; i < n; i++)
            {
                var r = new Rect(x + (cw + gap) * i, y, cw, ch);
                if (Card(r, offer[i], line)) { Pick(offer[i]); return; }
            }

            body.alignment = TextAnchor.MiddleLeft;
        }

        /// <summary>
        /// 카드 한 장. 올리면 테두리가 호박색으로 서고 판이 밝아진다.
        ///
        /// 그리기는 Repaint에서만 하고 누름은 보이지 않는 버튼에 맡긴다 —
        /// 한데 묶으면 마우스 위치가 한 프레임 늦는다.
        /// </summary>
        bool Card(Rect r, GameConfig.Upgrade u, float line)
        {
            bool over = r.Contains(Event.current.mousePosition);

            if (Event.current.type == EventType.Repaint)
            {
                UiSkin.Fill(r, over ? new Color(1f, 1f, 1f, 0.10f) : UiSkin.Panel);
                UiSkin.Outline(r, over ? UiSkin.Accent : UiSkin.Line, over ? 3f : 1f);

                float pad = r.width * 0.08f;

                title.alignment = TextAnchor.MiddleCenter;
                title.normal.textColor = over ? UiSkin.Accent : UiSkin.Ink;
                UiSkin.Shadowed(new Rect(r.x + pad, r.y + r.height * 0.18f,
                                         r.width - pad * 2f, line * 2f), u.title, title);

                body.alignment = TextAnchor.UpperCenter;
                body.wordWrap = true;
                body.normal.textColor = UiSkin.InkDim;
                GUI.Label(new Rect(r.x + pad, r.y + r.height * 0.42f,
                                   r.width - pad * 2f, r.height * 0.45f), u.detail, body);
                body.wordWrap = false;
            }

            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        void Styles()
        {
            head = UiSkin.Label(true, TextAnchor.MiddleCenter);
            title = UiSkin.Label(true, TextAnchor.MiddleCenter);
            body = UiSkin.Label(false, TextAnchor.MiddleCenter);
        }
    }
}
