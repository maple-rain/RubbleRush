using System.Collections.Generic;
using UnityEngine;

namespace Demo.UI
{
    /// <summary>
    /// 때린 자리에서 <b>의성어가 터진다</b> [지시 2026-10-17 · 구현 2026-10-19].
    ///
    /// 기획자: "타격에 맞춰 글자가 나오게 하거나, 수많은 잔상을 만들거나 하는
    /// <b>파워의 느낌과 속도감에서 오는 쾌감</b>을 줄 수 있도록 하고싶어."
    /// 레퍼런스는 죠죠의 "오라!"다. 낱말은 <b>한글 의성어</b>로 정해졌다 (기획자 결정).
    ///
    /// <b>크기는 레퍼런스를 재서 잡았다</b> [실측 2026-10-17, 1920x1080 두 프레임]:
    /// 글자가 많이 뜬 순간 화면의 10.9%를 덮고 가로로 화면폭의 95%까지 뻗는다.
    /// 다만 그것은 <b>여러 장이 겹쳐서</b> 만든 넓이다 — 한 장이 그만큼 큰 것이 아니다.
    /// 그래서 한 장은 적당히 두고 <b>겹치는 수</b>로 밀도를 만든다.
    ///
    /// <b>그림이 없으면 글꼴로 그린다.</b> VARCO 로 만든 의성어 그림을
    /// <see cref="UiSkin.Words"/>에 물려 두면 그것을 쓰고, 안 물려 있으면
    /// 배민 을지로체로 같은 낱말을 찍는다. 둘 다 돌아가므로 어느 쪽이 나은지
    /// 기획자가 보고 고를 수 있다.
    ///
    /// <b>IMGUI 다.</b> 이 프로젝트 UI는 전부 OnGUI 이고, 레퍼런스의 글자도
    /// 월드가 아니라 화면에 붙어 있다.
    /// </summary>
    public sealed class HitWord : MonoBehaviour
    {
        // ── 수치 [전부 제안·미승인 2026-10-19] ────────────────────────────────

        /// <summary>동시에 떠 있을 수 있는 글자 수. 초당 20타라 넉넉해야 겹친다.</summary>
        const int Pool = 14;

        /// <summary>한 장이 사는 시간(초).</summary>
        const float Life = 0.45f;

        /// <summary>화면 높이 대비 글자 높이. 약하게 칠 때와 세게 칠 때.</summary>
        const float SizeMin = 0.16f;
        const float SizeMax = 0.30f;

        /// <summary>터진 자리에서 흩어지는 거리. 화면 높이 대비.</summary>
        const float Scatter = 0.13f;

        /// <summary>사는 동안 위로 밀려 올라가는 거리. 화면 높이 대비.</summary>
        const float Rise = 0.05f;

        /// <summary>기울기(도). 레퍼런스가 전부 비스듬하다.</summary>
        const float TiltMax = 18f;

        /// <summary>터질 때 이 배로 커졌다가 1로 돌아온다. 1.0이면 안 튄다.</summary>
        const float Pop = 1.35f;

        /// <summary>너무 자주 뜨면 화면이 글자로 덮인다. 이 간격 안에는 안 띄운다(초).</summary>
        const float MinGap = 0.05f;

        /// <summary>글자색. 레퍼런스는 보라였지만 이 게임의 강조색을 쓴다 — VARCO 그림도
        /// 같은 호박색 계열로 나왔다 [실측 2026-10-19].</summary>
        static readonly Color Ink = new Color(1f, 0.72f, 0.18f, 1f);

        /// <summary>그림이 없을 때 글꼴로 찍을 낱말 [제안·미승인].
        /// <see cref="UiSkin.Words"/>의 차례와 같게 둔다.</summary>
        static readonly string[] Fallback = { "쾅", "퍽", "쿵", "콰광" };

        // ── 상태 ────────────────────────────────────────────────────────────

        struct Word
        {
            public float Left;
            public int Which;
            public Vector2 At;       // 화면 좌표 (왼쪽 위 기준)
            public Vector2 Drift;
            public float Tilt;
            public float Size;       // 화면 높이 대비
        }

        readonly List<Word> live = new List<Word>(Pool);
        static HitWord s_current;
        float nextAt;
        Camera eye;
        GUIStyle style;

        /// <summary>
        /// 어디서든 한 줄로 쓴다 — 씬에 올려 둘 것이 없다
        /// (<see cref="HitFlash"/>와 같은 수법).
        /// </summary>
        /// <param name="worldAt">때린 자리(월드).</param>
        /// <param name="power01">0이면 약한 타격, 1이면 가장 센 것. 크기를 정한다.</param>
        public static void Pop3D(Vector3 worldAt, float power01)
        {
            if (s_current == null)
            {
                var go = new GameObject("HitWord");
                s_current = go.AddComponent<HitWord>();
            }
            s_current.Fire(worldAt, power01);
        }

        void Awake() { if (s_current == null) s_current = this; }
        void OnDestroy() { if (s_current == this) s_current = null; }

        /// <summary>꺼질 때 남은 글자를 지운다 — 안 그러면 멈춘 채 굳는다.</summary>
        void OnDisable() { live.Clear(); }

        void Fire(Vector3 worldAt, float power01)
        {
            if (Time.unscaledTime < nextAt) return;
            nextAt = Time.unscaledTime + MinGap;

            if (eye == null)
            {
                eye = Camera.main;
                if (eye == null) eye = FindFirstObjectByType<Camera>();
                if (eye == null) return;
            }

            Vector3 sp = eye.WorldToScreenPoint(worldAt);
            if (sp.z <= 0f) return;                       // 카메라 뒤면 안 띄운다

            int count = HowMany();
            if (live.Count >= Pool) live.RemoveAt(0);     // 오래된 것부터 밀어낸다

            float h = Screen.height;
            float ang = Random.Range(0f, Mathf.PI * 2f);
            float rad = Random.Range(0f, Scatter) * h;

            live.Add(new Word
            {
                Left = Life,
                Which = Random.Range(0, Mathf.Max(1, count)),
                // 화면 좌표는 아래가 0, GUI는 위가 0이라 뒤집는다.
                At = new Vector2(sp.x + Mathf.Cos(ang) * rad,
                                 (h - sp.y) + Mathf.Sin(ang) * rad),
                Drift = new Vector2(Random.Range(-0.3f, 0.3f), -1f),
                Tilt = Random.Range(-TiltMax, TiltMax),
                Size = Mathf.Lerp(SizeMin, SizeMax, Mathf.Clamp01(power01)),
            });
        }

        /// <summary>쓸 수 있는 낱말이 몇 가지인가. 그림이 있으면 그림 수, 없으면 글자 수.</summary>
        static int HowMany()
        {
            Texture2D[] w = UiSkin.Words;
            return (w != null && w.Length > 0) ? w.Length : Fallback.Length;
        }

        void Update()
        {
            // 히트스톱 중에도 흘러야 한다 — 같이 얼면 '끊겼다'로 보인다.
            float dt = Time.unscaledDeltaTime;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                Word k = live[i];
                k.Left -= dt;
                if (k.Left <= 0f) { live.RemoveAt(i); continue; }
                live[i] = k;
            }
        }

        void OnGUI()
        {
            if (live.Count == 0) return;
            if (TitleScreen.Blocking || ResultScreen.Blocking
                || UpgradeScreen.Blocking || HowToScreen.Blocking) return;

            float h = Screen.height;
            Texture2D[] art = UiSkin.Words;
            bool useArt = art != null && art.Length > 0;

            Matrix4x4 keep = GUI.matrix;
            Color keepColor = GUI.color;

            for (int i = 0; i < live.Count; i++)
            {
                Word k = live[i];
                float age = 1f - k.Left / Life;            // 0 = 막 터짐

                // 터질 때 크게 튀었다가 제자리로. 끝에서 옅어진다.
                float pop = Mathf.Lerp(Pop, 1f, Mathf.Clamp01(age * 3f));
                float alpha = age < 0.65f ? 1f : 1f - (age - 0.65f) / 0.35f;

                float side = k.Size * pop * h;
                Vector2 c = k.At + k.Drift * (age * Rise * h);
                var r = new Rect(c.x - side * 0.5f, c.y - side * 0.5f, side, side);

                GUI.matrix = keep;
                GUIUtility.RotateAroundPivot(k.Tilt, c);

                if (useArt)
                {
                    Texture2D t = art[k.Which % art.Length];
                    if (t != null)
                    {
                        GUI.color = new Color(1f, 1f, 1f, alpha);
                        GUI.DrawTexture(r, t, ScaleMode.ScaleToFit);
                    }
                }
                else
                {
                    Draw(r, Fallback[k.Which % Fallback.Length], side, alpha);
                }
            }

            GUI.matrix = keep;
            GUI.color = keepColor;
        }

        /// <summary>
        /// 그림이 없을 때. 검은 외곽선을 여덟 방향으로 깔고 가운데를 색으로 찍는다 —
        /// 레퍼런스의 글자가 사는 이유의 절반이 <b>두꺼운 검은 테두리</b>다
        /// [실측 2026-10-17].
        /// </summary>
        void Draw(Rect r, string text, float side, float alpha)
        {
            if (style == null || style.font != (UiSkin.Title != null ? UiSkin.Title : UiSkin.Body))
            {
                style = UiSkin.Heading(TextAnchor.MiddleCenter);
                style.wordWrap = false;
            }
            style.fontSize = Mathf.RoundToInt(side * 0.78f);

            float o = Mathf.Max(2f, side * 0.045f);
            UiSkin.Tint(style, new Color(0f, 0f, 0f, alpha));
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    GUI.Label(new Rect(r.x + dx * o, r.y + dy * o, r.width, r.height), text, style);
                }

            UiSkin.Tint(style, new Color(Ink.r, Ink.g, Ink.b, alpha));
            GUI.Label(r, text, style);
        }
    }
}
