using System.Collections.Generic;
using Demo.Core;
using Demo.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Demo.UI
{
    /// <summary>
    /// 파괴 화면의 터치 — <b>화면을 좌우로 갈라 주먹을 낸다</b> [지시 2026-10-12].
    ///
    /// 기획자가 정한 모양이다: 부수기에 들어가면 조이스틱과 버튼은 사라지고,
    /// <b>왼쪽을 치면 왼손, 오른쪽을 치면 오른손</b>이 나간다. 그 화면에서는 이동도
    /// 케이블도 쓸 일이 없으니 조작을 하나로 줄이는 것이 맞고, 좌우로 가르면
    /// 양손 연타가 손가락 두 개로 자연스럽게 된다.
    ///
    /// <b>손가락이 새로 닿을 때마다 한 방</b>이다. 누름 상태로 밀면 두 손가락이 겹칠 때
    /// true가 끊기지 않아 연타가 한 번으로 뭉개진다 — <see cref="InputRouter.InjectActionPress"/>
    /// 주석에 적어 둔 그 이유다.
    ///
    /// <b>여기서만 쓰는 규칙이다.</b> 필드에서는 <see cref="TouchControls"/>가 맡고,
    /// 둘은 모드로 갈려 절대 같이 돌지 않는다.
    /// </summary>
    [DefaultExecutionOrder(-50)]        // InputRouter보다 먼저 밀어 넣는다
    public sealed class TouchStrike : MonoBehaviour
    {
        [SerializeField] InputRouter router;

        [SerializeField, Tooltip("마우스로도 칠 수 있게 한다. 실기가 없어서 기본으로 켜 둔다")]
        bool mouseActsAsTouch = true;

        [SerializeField, Range(0f, 0.5f), Tooltip("친 쪽이 번쩍하는 시간(초). 0이면 안 그린다")]
        float flashSeconds = 0.12f;

        [SerializeField, Tooltip("몇 대 치면 안내가 옅어지는가. 알고 나면 벽을 가리기만 한다")]
        int hintFadesAfter = 4;

        [SerializeField, Range(0f, 0.6f), Tooltip("옅어진 뒤에도 남는 정도. 0이면 완전히 사라진다")]
        float hintResidue = 0.3f;

        [SerializeField, Tooltip("양쪽에 쓰는 글자 [지시 2026-10-12]")]
        string hintLabel = "TOUCH!";

        [SerializeField, Range(0.03f, 0.2f), Tooltip("동그라미 반경. 화면 세로 대비")]
        float hintRadius = 0.094f;        // 0.075에서 25% 키움 [지시 2026-10-12]

        [SerializeField, Range(0.2f, 0.95f), Tooltip("동그라미 높이. 0=화면 위, 1=아래")]
        float hintHeight = 0.52f;         // 0.74에서 30% 올림 [지시 2026-10-12]

        [SerializeField, Range(0.08f, 0.45f), Tooltip("동그라미가 가장자리에서 얼마나 안쪽인가. " +
                 "화면 가로 대비. 작을수록 양 끝으로 간다")]
        float hintInset = 0.21f;          // 0.25에서 4% 바깥으로 [지시 2026-10-12]

        [SerializeField, Range(0f, 4f), Tooltip("깜빡이는 속도(초당 횟수). 0이면 안 깜빡인다")]
        float blinkHz = 1.4f;

        [SerializeField, Range(0f, 1f), Tooltip("깜빡일 때 제일 어두워지는 정도. 0이면 완전히 꺼진다")]
        float blinkFloor = 0.35f;

        readonly HashSet<int> down = new HashSet<int>();
        readonly List<int> gone = new List<int>();

        [SerializeField, Range(0f, 0.6f), Tooltip("친 자리에서 고리가 퍼지는 시간(초). 0이면 안 그린다")]
        float rippleSeconds = 0.3f;

        [SerializeField, Range(0f, 1f), Tooltip("친 쪽 동그라미가 얼마나 커졌다 돌아오는가")]
        float popScale = 0.35f;

        float flashUntil;
        bool flashRight;

        /// <summary>마지막으로 친 자리와 그때. 퍼지는 고리와 눌린 쪽 강조가 이걸 본다.</summary>
        Vector2 hitAt;
        float hitTime = -99f;

        Texture2D px, disc, ring;

        GUIStyle hand;

        GameModeController mode;

        bool wasActive;

        /// <summary>이번 파괴 화면에서 몇 대 쳤는가. 안내가 옅어지는 기준이다.</summary>
        int hits;

        /// <summary>
        /// 안내를 얼마나 진하게 그릴까 (0~1).
        ///
        /// <b>처음엔 진하고 치다 보면 옅어진다.</b> 화면 전체가 버튼이라는 것은 한 번 알면
        /// 끝인데, 안내가 계속 진하면 정작 부수는 벽을 가린다. 그렇다고 아주 지우면 오래
        /// 쉬었다 온 사람이 다시 헤매므로 흐리게 남긴다.
        /// <b>구역마다 다시 진해진다</b> — 파괴 화면에 들어올 때마다 0부터 센다.
        /// </summary>
        float HintStrength
        {
            get
            {
                if (hintFadesAfter <= 0) return hintResidue;
                float t = Mathf.Clamp01(hits / (float)hintFadesAfter);
                return Mathf.Lerp(1f, hintResidue, t);
            }
        }

        /// <summary>
        /// 깜빡임 (0~1) [지시 2026-10-12]. 눈길을 끄는 것이 목적이라 <b>켜고 끄는 대신
        /// 부드럽게 오간다</b> — 딱딱 꺼지면 화면이 번쩍이고, 부술 벽을 보고 있는 눈이 피로하다.
        ///
        /// <b>반드시 실제 시간으로 센다.</b> 이 화면은 때릴 때마다 히트스톱으로 timeScale이
        /// 0이 되는데, 게임 시간을 쓰면 치는 동안 깜빡임이 멈춰 선다.
        /// </summary>
        float Blink
        {
            get
            {
                if (blinkHz <= 0f) return 1f;
                float wave = 0.5f + 0.5f * Mathf.Cos(Time.unscaledTime * blinkHz * Mathf.PI * 2f);
                return Mathf.Lerp(blinkFloor, 1f, wave);
            }
        }

        /// <summary>
        /// 이 화면이 지금 조작을 맡고 있는가.
        ///
        /// <b><c>!IsField</c>가 아니라 <c>IsDemolition</c>이다</b> [고침 2026-10-12].
        /// 모드가 셋이다 — Field · Demolition · <b>Ended</b>. 뒤집어 쓰면 구역이 끝난 뒤에도
        /// 화면을 치면 주먹이 나간다. Ended는 "입력을 받지 않는다"고 못 박힌 상태다.
        /// </summary>
        bool Active => mode != null && mode.IsDemolition && !TitleScreen.Blocking;

        void Awake()
        {
            if (router == null) router = FindFirstObjectByType<InputRouter>();

            px = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            px.SetPixel(0, 0, Color.white);
            px.Apply();
            px.hideFlags = HideFlags.HideAndDontSave;

            disc = MakeDisc(64);
            ring = MakeRing(96, 0.10f);
        }

        void OnDestroy()
        {
            if (px != null) Destroy(px);
            if (disc != null) Destroy(disc);
            if (ring != null) Destroy(ring);
        }

        void Update()
        {
            if (mode == null) mode = GameModeController.Instance;

            if (!Active)
            {
                down.Clear();
                wasActive = false;
                hits = 0;                  // 다음 건물에서는 안내가 다시 진해진다
                return;
            }

            var ts = Touchscreen.current;

            // <b>들어오는 순간 이미 닿아 있던 손가락은 센 것으로 친다</b> [고침 2026-10-12].
            // 부수기 프롬프트를 누른 그 손가락이 화면에 남아 있는 채로 모드가 바뀌므로,
            // 그대로 두면 <b>누르지도 않은 한 방</b>이 공짜로 들어간다 — 들어가자마자
            // 벽이 한 대 맞는다. 첫 프레임에는 세기만 하고 치지 않는다.
            if (!wasActive)
            {
                wasActive = true;
                down.Clear();
                if (ts != null)
                    foreach (TouchControl t in ts.touches)
                        if (t.press.isPressed) down.Add(t.touchId.ReadValue());
                return;
            }
            if (ts != null)
            {
                gone.Clear();
                foreach (int id in down) gone.Add(id);

                foreach (TouchControl t in ts.touches)
                {
                    int id = t.touchId.ReadValue();
                    if (!t.press.isPressed) continue;

                    gone.Remove(id);
                    if (!down.Add(id)) continue;        // 이미 닿아 있던 손가락
                    Hit(t.position.ReadValue());
                }

                foreach (int id in gone) down.Remove(id);
            }

            if (!mouseActsAsTouch || Mouse.current == null) return;
            if (Mouse.current.leftButton.wasPressedThisFrame) Hit(Mouse.current.position.ReadValue());
        }

        /// <summary>그 자리를 쳤다. 화면 가운데를 기준으로 어느 손인지 정한다.</summary>
        void Hit(Vector2 pos)
        {
            if (router == null) return;

            bool right = pos.x >= Screen.width * 0.5f;
            router.InjectActionPress(right);
            hits++;

            flashRight = right;
            flashUntil = Time.unscaledTime + flashSeconds;
            hitAt = pos;
            hitTime = Time.unscaledTime;
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !Active) return;
            if (hand == null) Styles();

            Hint();
            Flash();
            Ripple();
        }

        /// <summary>
        /// <b>화면이 좌우로 갈린다는 것을 보여 준다</b> [지시 2026-10-12].
        ///
        /// 이 화면은 버튼이 안 보이는 것이 아니라 <b>화면 전체가 버튼</b>인데, 아무 표시가
        /// 없으면 그걸 알 길이 없다 — 어디를 눌러야 하는지도, 좌우가 다른 손이라는 것도.
        /// 가운데 선 하나와 양쪽의 손 표시로 둘을 한 번에 말한다.
        ///
        /// <b>벽을 가리면 안 된다.</b> 부수는 장면이 주인공이므로 전부 아래쪽에 깔고,
        /// 몇 대 치고 나면 옅어진다 (<see cref="HintStrength"/>).
        /// </summary>
        void Hint()
        {
            float a = HintStrength;
            if (a <= 0.01f) return;

            float w = Screen.width, h = Screen.height;
            float half = w * 0.5f;

            // 가운데 선. 좌우가 갈린다는 것을 한 획으로 말한다.
            Fill(new Rect(half - 1f, h * 0.30f, 2f, h * 0.62f), new Color(1f, 1f, 1f, 0.16f * a));

            // 양쪽의 누를 자리. 깜빡이는 것은 이것뿐이다 — 가운데 선까지 같이 깜빡이면
            // 화면이 산만해지고, 선은 '갈려 있다'는 구조라 가만히 있는 편이 읽힌다.
            float r = h * hintRadius;
            float y = h * hintHeight;
            // 친 쪽은 잠깐 커진다. 손가락이 가린 채로도 "이쪽이 먹었다"가 보인다.
            float blink = a * Blink;
            float pop = Pop();
            Hand(new Vector2(w * hintInset, y), r * (flashRight ? 1f : pop), hintLabel, blink);
            Hand(new Vector2(w * (1f - hintInset), y), r * (flashRight ? pop : 1f), hintLabel, blink);

            // <b>글로 쓴 안내는 뺐다</b> [지시 2026-10-12].
            // 양쪽의 TOUCH! 두 개가 같은 말을 이미 하고 있어서 한 번 더 적을 자리가 아니었다.
        }

        /// <summary>
        /// 친 쪽 동그라미의 배율 (1 이상) [지시 2026-10-12].
        ///
        /// 기획자가 "터치 시 강조가 되었으면 한다"고 물었다. 쓸 수 있는 손이 넷이었다:
        /// <list type="number">
        /// <item>반쪽 번쩍 — 이미 있다. 넓어서 눈에 띄지만 <b>어디를 눌렀는지</b>는 안 알려 준다</item>
        /// <item><b>눌린 쪽이 커졌다 돌아온다</b> — 누른 것과 반응이 같은 자리라 제일 잘 붙는다</item>
        /// <item><b>친 자리에서 고리가 퍼진다</b> — 손가락 바로 밑이라 가려도 가장자리가 보인다</item>
        /// <item>색이 확 밝아진다 — 깜빡임과 섞여 무엇이 무엇인지 흐려진다</item>
        /// </list>
        /// 2와 3을 같이 쓴다. 1은 그대로 두되 바탕 역할만 한다. 4는 안 쓴다.
        ///
        /// 커졌다가 <b>돌아오는</b> 모양이라 한 방마다 끊겨 보인다 — 연타해도 뭉개지지 않는다.
        /// </summary>
        float Pop()
        {
            if (popScale <= 0f || flashSeconds <= 0f) return 1f;

            float t = (Time.unscaledTime - hitTime) / flashSeconds;
            if (t < 0f || t > 1f) return 1f;

            // 빠르게 커지고 천천히 돌아온다. 같은 속도로 오가면 '튕김'이 안 느껴진다.
            return 1f + popScale * Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.55f));
        }

        /// <summary>
        /// 친 자리에서 퍼지는 고리. <b>손가락이 가리는 자리를 피해 바깥으로 자란다</b> —
        /// 점으로 찍으면 손가락 밑에 깔려 안 보인다.
        /// </summary>
        void Ripple()
        {
            if (rippleSeconds <= 0f || ring == null) return;

            float t = (Time.unscaledTime - hitTime) / rippleSeconds;
            if (t < 0f || t > 1f) return;

            float r = Screen.height * Mathf.Lerp(0.03f, 0.16f, t);
            Vector2 g = new Vector2(hitAt.x, Screen.height - hitAt.y);

            Color prev = GUI.color;
            GUI.color = new Color(1f, 0.85f, 0.4f, 0.55f * (1f - t));
            GUI.DrawTexture(new Rect(g.x - r, g.y - r, r * 2f, r * 2f), ring);
            GUI.color = prev;
        }

        void Hand(Vector2 c, float r, string text, float a)
        {
            Disc(c, r, new Color(0.06f, 0.07f, 0.09f, 0.42f * a));
            Disc(c, r * 0.9f, new Color(1f, 1f, 1f, 0.07f * a));

            // "TOUCH!"는 "왼손"보다 세 배 길다. 비율을 그대로 두면 동그라미를 넘친다.
            hand.fontSize = Mathf.Max(11, Mathf.RoundToInt(r * 0.36f));
            Shadow(new Rect(c.x - r, c.y - r, r * 2f, r * 2f), text, hand,
                   new Color(1f, 1f, 1f, 0.85f * a));
        }

        /// <summary>
        /// 친 쪽을 아주 잠깐 밝힌다. 손가락이 화면을 가려서 <b>맞았는지 안 맞았는지</b>가
        /// 안 보이는 일이 흔한데, 반쪽이 번쩍하면 가린 채로도 읽힌다.
        /// </summary>
        void Flash()
        {
            if (flashSeconds <= 0f) return;

            float left = flashUntil - Time.unscaledTime;
            if (left <= 0f) return;

            float half = Screen.width * 0.5f;
            Fill(new Rect(flashRight ? half : 0f, 0f, half, Screen.height),
                 new Color(1f, 1f, 1f, 0.10f * (left / flashSeconds)));
        }

        // ── 그리기 도구 ──────────────────────────────────────────────────────────

        void Styles()
        {
            hand = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                wordWrap = false,
            };
        }

        /// <summary>글자 뒤에 그림자를 깐다. 벽이 밝으면 흰 글자만으로는 안 읽힌다.</summary>
        void Shadow(Rect r, string text, GUIStyle s, Color color)
        {
            s.normal.textColor = new Color(0f, 0f, 0f, color.a * 0.6f);
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, s);
            s.normal.textColor = color;
            GUI.Label(r, text, s);
        }

        void Fill(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, px);
            GUI.color = prev;
        }

        void Disc(Vector2 center, float radius, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), disc);
            GUI.color = prev;
        }

        /// <summary>속이 빈 동그라미. 안팎 양쪽 테를 깎아야 두 선이 다 매끈하다.</summary>
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
                    float a = Mathf.Clamp01(1f - Mathf.Max(0f, d - (outer - 1.5f)) / 1.5f)
                            * Mathf.Clamp01(1f - Mathf.Max(0f, inner - d) / 1.5f);
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
