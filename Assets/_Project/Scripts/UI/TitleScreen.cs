using Demo.Utils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Demo.UI
{
    /// <summary>
    /// 시작 화면과 옵션 [지시 2026-10-12].
    ///
    /// <b>왜 별도 씬이 아닌가.</b> CLAUDE.md 규칙 2가 "씬은 하나다"이고, 그 이유가 여기서도
    /// 그대로 성립한다 — 씬을 늘리면 빌드 씬 목록에 넣는 것을 잊을 수 있고(실제로 이 프로젝트의
    /// 빌드 목록에는 <c>SampleScene</c>만 들어 있었다), <c>GameAudio</c>·<c>GameConfigRef</c> 같은
    /// 매니저가 씬마다 겹친다. 타이틀은 <b>Main.unity 안의 한 상태</b>로 둔다.
    ///
    /// <b>멈추는 방법은 timeScale 0이다.</b> <see cref="Core.GameLoop"/>의 타이머가
    /// <c>Time.deltaTime</c>을 빼므로 그것 하나로 시간도 플레이어도 같이 선다.
    /// <see cref="Destruction.HitStop"/>은 "이미 0이면 건드리지 않는다"는 가드가 있어 안 부딪힌다.
    ///
    /// <b>슬라이더를 직접 그린다.</b> <c>GUI.HorizontalSlider</c>의 손잡이는 폰에서 손가락보다
    /// 작다 — 안드로이드 최소 터치 영역이 48dp인데 기본 손잡이는 그 절반도 안 된다.
    /// </summary>
    public sealed class TitleScreen : MonoBehaviour
    {
        enum Page { Title, Options, Playing }

        [SerializeField, Tooltip("화면에 띄울 이름. 게임 이름이 정해지면 바꾼다")]
        string title = "철거";

        [SerializeField, Tooltip("켜면 처음부터 바로 플레이로 들어간다. 검증용")]
        bool skipTitle = false;

        Page page = Page.Title;
        VolumeSettings vol;

        static TitleScreen s_current;

        GUIStyle big, mid, small;
        Texture2D px;

        /// <summary>플레이가 시작됐는가. 다른 UI가 "아직 타이틀인가"를 물을 수 있게 둔다.</summary>
        public bool Playing => page == Page.Playing;

        /// <summary>
        /// 지금 타이틀/옵션이 화면을 덮고 있는가 [2026-10-12].
        ///
        /// <b>조작 UI가 이걸 봐야 한다.</b> 안 보면 타이틀 너머로 터치가 통과한다 —
        /// 폰에서 시작 버튼을 누르려다 케이블 버튼을 같이 누르게 된다. PC에서는
        /// 터치 UI가 꺼져 있어 안 보였을 사고다.
        /// 타이틀이 아예 없는 씬에서도 조작이 되어야 하므로 <b>없으면 false</b>다.
        /// </summary>
        public static bool Blocking => s_current != null && s_current.page != Page.Playing;

        void Awake()
        {
            s_current = this;
            vol = FindFirstObjectByType<VolumeSettings>();

            px = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            px.SetPixel(0, 0, Color.white);
            px.Apply();
            px.hideFlags = HideFlags.HideAndDontSave;

            if (skipTitle) page = Page.Playing;
        }

        void OnEnable()
        {
            if (page != Page.Playing) Time.timeScale = 0f;
        }

        void OnDestroy()
        {
            if (s_current == this) s_current = null;
            if (px != null) Destroy(px);
            // 플레이 모드를 나갈 때 멈춘 채로 남지 않게 한다.
            if (page != Page.Playing) Time.timeScale = 1f;
        }

        void Update()
        {
            if (page == Page.Playing) return;

            // 시간이 멈춰 있어야 한다. 다른 데서 되돌려 놨을 수 있으니 매 프레임 확인한다.
            if (Time.timeScale != 0f) Time.timeScale = 0f;

            // 키보드로도 시작할 수 있게. 폰에서는 안 쓰이지만 PC 확인이 훨씬 빨라진다.
            var k = Keyboard.current;
            if (k == null) return;
            if (page == Page.Title && (k.enterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame)) Begin();
            else if (page == Page.Options && k.escapeKey.wasPressedThisFrame) Close();
        }

        void Begin()
        {
            page = Page.Playing;
            Time.timeScale = 1f;
        }

        void Close()
        {
            page = Page.Title;
            if (vol != null) vol.Save();      // 디스크 쓰기는 닫을 때 한 번만
        }

        void OnGUI()
        {
            if (page == Page.Playing) return;
            if (big == null) Styles();

            float h = Screen.height, w = Screen.width;
            GUI.color = Color.white;

            // 바탕을 덮는다. 뒤에 게임이 멈춰 있는 것이 비쳐 보이게 완전히 가리지는 않는다.
            Fill(new Rect(0, 0, w, h), new Color(0.06f, 0.07f, 0.09f, 0.88f));

            big.fontSize = Mathf.RoundToInt(h * 0.12f);
            mid.fontSize = Mathf.RoundToInt(h * 0.055f);
            small.fontSize = Mathf.RoundToInt(h * 0.038f);

            if (page == Page.Title) DrawTitle(w, h);
            else DrawOptions(w, h);
        }

        void DrawTitle(float w, float h)
        {
            GUI.Label(new Rect(0, h * 0.16f, w, h * 0.18f), title, big);

            float bw = w * 0.30f, bh = h * 0.13f, x = (w - bw) * 0.5f;
            if (Button(new Rect(x, h * 0.44f, bw, bh), "시작", mid)) Begin();
            if (Button(new Rect(x, h * 0.62f, bw, bh), "옵션", mid)) page = Page.Options;

            small.normal.textColor = new Color(1f, 1f, 1f, 0.45f);
            GUI.Label(new Rect(0, h * 0.86f, w, h * 0.06f),
                      "왼쪽 조이스틱으로 이동 · 오른쪽 케이블/대쉬 · 건물에 다가가면 부수기", small);
            small.normal.textColor = Color.white;
        }

        void DrawOptions(float w, float h)
        {
            GUI.Label(new Rect(0, h * 0.12f, w, h * 0.12f), "옵션", mid);

            float sw = w * 0.52f, x = (w - sw) * 0.5f;
            float sfx = vol != null ? vol.Sfx : 1f;
            float music = vol != null ? vol.Music : 0.35f;

            float v = Slider(new Rect(x, h * 0.36f, sw, h * 0.10f), "효과음", sfx);
            if (vol != null && !Mathf.Approximately(v, sfx)) vol.Sfx = v;

            v = Slider(new Rect(x, h * 0.54f, sw, h * 0.10f), "배경음악", music);
            if (vol != null && !Mathf.Approximately(v, music)) vol.Music = v;

            float bw = w * 0.24f, bh = h * 0.11f;
            if (Button(new Rect((w - bw) * 0.5f, h * 0.74f, bw, bh), "닫기", mid)) Close();
        }

        // ── 위젯 ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 손가락 크기의 버튼. <see cref="GUI.Button"/>을 쓰지 않는 이유는 기본 스킨의 높이가
        /// 폰에서 너무 낮아서다 — 여기서는 높이를 직접 준다.
        /// </summary>
        bool Button(Rect r, string text, GUIStyle s)
        {
            bool over = r.Contains(Event.current.mousePosition);
            Fill(r, over ? new Color(1f, 0.78f, 0.25f, 0.92f) : new Color(1f, 1f, 1f, 0.14f));

            Color keep = s.normal.textColor;
            s.normal.textColor = over ? new Color(0.1f, 0.08f, 0.04f) : Color.white;
            GUI.Label(r, text, s);
            s.normal.textColor = keep;

            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        /// <summary>
        /// 직접 그린 슬라이더. 막대 전체가 터치 영역이고 손잡이가 크다.
        /// 끄는 동안 값을 바로 돌려주므로 소리가 손가락을 따라온다.
        /// </summary>
        float Slider(Rect r, string name, float value)
        {
            small.alignment = TextAnchor.MiddleLeft;
            GUI.Label(new Rect(r.x, r.y - r.height * 0.72f, r.width, r.height * 0.7f), name, small);

            small.alignment = TextAnchor.MiddleRight;
            GUI.Label(new Rect(r.x, r.y - r.height * 0.72f, r.width, r.height * 0.7f),
                      Mathf.RoundToInt(value * 100f) + "%", small);
            small.alignment = TextAnchor.MiddleCenter;

            float barH = r.height * 0.28f;
            var bar = new Rect(r.x, r.y + (r.height - barH) * 0.5f, r.width, barH);
            Fill(bar, new Color(1f, 1f, 1f, 0.16f));
            Fill(new Rect(bar.x, bar.y, bar.width * value, bar.height), new Color(1f, 0.78f, 0.25f, 0.9f));

            float knob = r.height * 0.5f;
            Fill(new Rect(bar.x + bar.width * value - knob * 0.5f, r.y + (r.height - knob) * 0.5f, knob, knob),
                 Color.white);

            // 막대보다 넉넉한 영역을 잡는다 — 손가락이 정확히 막대 위에 오지 않는다.
            int id = GUIUtility.GetControlID(FocusType.Passive, r);
            Event e = Event.current;
            if ((e.type == EventType.MouseDown && r.Contains(e.mousePosition))
             || (e.type == EventType.MouseDrag && GUIUtility.hotControl == id))
            {
                GUIUtility.hotControl = id;
                value = Mathf.Clamp01((e.mousePosition.x - bar.x) / bar.width);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && GUIUtility.hotControl == id)
            {
                GUIUtility.hotControl = 0;
                e.Use();
            }
            return value;
        }

        void Fill(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, px);
            GUI.color = prev;
        }

        void Styles()
        {
            big = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                wordWrap = false,
            };
            mid = new GUIStyle(big);
            small = new GUIStyle(big) { fontStyle = FontStyle.Normal };
        }
    }
}
