using Demo.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Demo.UI
{
    /// <summary>
    /// 모바일 터치 입력 — 왼쪽 절반 가상 조이스틱 + 오른쪽 버튼 (GAME_DESIGN §3).
    ///
    /// <b>Canvas도 EventSystem도 쓰지 않는다.</b> 화면 영역으로 직접 나누고 터치를 읽는다.
    /// uGUI로 만들면 프리팹과 씬 와이어링이 늘어나는데 그건 사람이 손으로 하는 작업이고
    /// (CLAUDE.md 규칙 1), 이 정도 입력에 EventSystem 한 겹을 얹을 이유가 없다.
    ///
    /// 조이스틱은 <b>고정이 아니라 떠 있다</b> — 왼쪽 절반 어디를 눌러도 그 자리가 중심이 된다.
    /// 모바일에서 고정 스틱은 손가락이 위치를 먼저 찾아야 해서 반응이 느려진다.
    ///
    /// 조준이 없으므로 이 둘이 입력의 전부다.
    ///
    /// <b>[지시 2026-09-22] 지금은 PC 우선이다.</b> 이 컴포넌트는 터치스크린이 실제로 있을 때만
    /// 스스로 켜진다. 모바일로 넘어갈 때 별도 작업 없이 그대로 동작하고, PC에서 손으로
    /// 확인하고 싶으면 <see cref="mouseActsAsTouch"/>만 켜면 된다.
    /// </summary>
    public sealed class TouchControls : MonoBehaviour
    {
        [SerializeField] InputRouter router;

        [SerializeField, Tooltip("터치스크린이 없으면 스스로 꺼진다. PC 조작을 방해하지 않기 위해서다")]
        bool autoDisableWithoutTouchscreen = true;

        [SerializeField, Range(0.04f, 0.30f), Tooltip("조이스틱을 최대로 기울이는 거리. 화면 높이 대비")]
        float stickRadiusFraction = 0.12f;

        [SerializeField, Range(0.04f, 0.30f), Tooltip("행동 버튼 반경. 화면 높이 대비")]
        float buttonRadiusFraction = 0.11f;

        [SerializeField, Tooltip("행동 버튼 중심. 화면 크기 대비 (0~1)")]
        Vector2 buttonCenterFraction = new Vector2(0.86f, 0.22f);

        [SerializeField, Tooltip("마우스를 손가락처럼 쓴다. PC 우선인 동안은 꺼 둔다 — " +
                                 "켜면 클릭이 조이스틱이 되어 키보드 조작과 섞인다")]
        bool mouseActsAsTouch = false;

        [SerializeField, Tooltip("터치가 전혀 없으면 키보드가 그대로 동작하게 둔다")]
        bool showWhenNoTouch = true;

        const int None = -1;
        int stickPointer = None;
        int buttonPointer = None;
        Vector2 stickOrigin, stickCurrent;

        Texture2D disc;

        float StickRadius => Screen.height * stickRadiusFraction;
        float ButtonRadius => Screen.height * buttonRadiusFraction;
        Vector2 ButtonCenter => new Vector2(Screen.width * buttonCenterFraction.x,
                                            Screen.height * buttonCenterFraction.y);

        void Awake()
        {
            if (router == null) router = FindFirstObjectByType<InputRouter>();

            // PC에서는 조용히 빠진다. 켜져 있으면 OnGUI가 버튼을 그려서 시야를 가린다.
            if (autoDisableWithoutTouchscreen && Touchscreen.current == null && !mouseActsAsTouch)
            {
                enabled = false;
                return;
            }

            disc = MakeDisc(64);
        }

        void OnDestroy()
        {
            if (disc != null) Destroy(disc);
        }

        void Update()
        {
            if (router == null) return;

            bool sawStick = false, sawButton = false;

            var ts = Touchscreen.current;
            if (ts != null)
            {
                foreach (TouchControl t in ts.touches)
                {
                    if (!t.press.isPressed) continue;
                    Route(t.touchId.ReadValue(), t.position.ReadValue(), ref sawStick, ref sawButton);
                }
            }

            if (mouseActsAsTouch && Mouse.current != null && Mouse.current.leftButton.isPressed)
                Route(int.MinValue, Mouse.current.position.ReadValue(), ref sawStick, ref sawButton);

            if (!sawStick) { stickPointer = None; router.MoveOverride = null; }
            if (!sawButton) { buttonPointer = None; router.ActionOverride = null; }
        }

        /// <summary>포인터 하나를 조이스틱이나 버튼에 배정한다. 한 번 배정되면 뗄 때까지 유지된다.</summary>
        void Route(int id, Vector2 pos, ref bool sawStick, ref bool sawButton)
        {
            if (id == buttonPointer || (buttonPointer == None && IsOnButton(pos)))
            {
                buttonPointer = id;
                sawButton = true;
                router.ActionOverride = true;
                return;
            }

            if (id == stickPointer)
            {
                sawStick = true;
                stickCurrent = pos;
                router.MoveOverride = (pos - stickOrigin) / StickRadius;
                return;
            }

            // 새 손가락이 왼쪽 절반에 닿았다 — 거기가 스틱 중심이 된다
            if (stickPointer == None && pos.x < Screen.width * 0.5f)
            {
                stickPointer = id;
                stickOrigin = stickCurrent = pos;
                sawStick = true;
                router.MoveOverride = Vector2.zero;
            }
        }

        bool IsOnButton(Vector2 pos) =>
            (pos - ButtonCenter).sqrMagnitude <= ButtonRadius * ButtonRadius;

        // ── 임시 시각화 ──────────────────────────────────────────────────────────
        // OnGUI로 그린다. D10에 제대로 된 UI로 교체할 자리이며, 지금은 "어디를 눌러야 하는가"가
        // 보이기만 하면 된다.

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;

            bool touching = stickPointer != None || buttonPointer != None;
            if (!touching && !showWhenNoTouch) return;

            // 행동 버튼
            Vector2 bc = ToGui(ButtonCenter);
            DrawDisc(bc, ButtonRadius, buttonPointer != None
                ? new Color(1f, 0.72f, 0.2f, 0.55f) : new Color(1f, 1f, 1f, 0.16f));

            // 조이스틱 — 누르고 있을 때만 보인다
            if (stickPointer != None)
            {
                Vector2 o = ToGui(stickOrigin);
                DrawDisc(o, StickRadius, new Color(1f, 1f, 1f, 0.12f));

                Vector2 delta = stickCurrent - stickOrigin;
                if (delta.magnitude > StickRadius) delta = delta.normalized * StickRadius;
                DrawDisc(ToGui(stickOrigin + delta), StickRadius * 0.42f,
                         new Color(1f, 1f, 1f, 0.45f));
            }
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
