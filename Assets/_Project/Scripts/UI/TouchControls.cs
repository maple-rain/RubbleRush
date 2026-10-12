using Demo.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Demo.UI
{
    /// <summary>
    /// 모바일 터치 입력 — 왼쪽 절반 가상 조이스틱 + 오른쪽 버튼들 (GAME_DESIGN §3).
    ///
    /// <b>Canvas도 EventSystem도 쓰지 않는다.</b> 화면 영역으로 직접 나누고 터치를 읽는다.
    /// uGUI로 만들면 프리팹과 씬 와이어링이 늘어나는데 그건 사람이 손으로 하는 작업이고
    /// (CLAUDE.md 규칙 1), 이 정도 입력에 EventSystem 한 겹을 얹을 이유가 없다.
    ///
    /// 조이스틱은 <b>고정이 아니라 떠 있다</b> — 왼쪽 절반 어디를 눌러도 그 자리가 중심이 된다.
    /// 모바일에서 고정 스틱은 손가락이 위치를 먼저 찾아야 해서 반응이 느려진다.
    ///
    /// <b>버튼이 넷이다</b> [2026-10-12]. 전에는 행동 하나뿐이어서 폰에서는 E(부수기)·Q(놓기)·
    /// Shift(달리기)를 쓸 방법이 없었다 — 건물을 부술 수가 없으니 게임의 절반이 잠겼다.
    /// 배치는 <see cref="TouchPad"/>가 갖고, 늘리는 비용은 한 줄이다.
    ///
    /// PC에서는 스스로 꺼진다. 손으로 확인하려면 <see cref="mouseActsAsTouch"/>만 켠다.
    /// </summary>
    public sealed partial class TouchControls : MonoBehaviour
    {
        [SerializeField] InputRouter router;

        [SerializeField, Tooltip("터치스크린이 없으면 스스로 꺼진다. PC 조작을 방해하지 않기 위해서다")]
        bool autoDisableWithoutTouchscreen = true;

        [SerializeField, Range(0.04f, 0.30f), Tooltip("조이스틱을 최대로 기울이는 거리. 화면 높이 대비")]
        float stickRadiusFraction = 0.12f;

        [SerializeField, Tooltip("버튼 배치. 비면 TouchPad.Default()를 쓴다")]
        TouchPad[] pads = new TouchPad[0];

        [SerializeField, Tooltip("마우스를 손가락처럼 쓴다. 실기가 없어서 기본으로 켜 둔다 — " +
                                 "에디터에서 왼쪽 절반을 끌면 조이스틱, 오른쪽 동그라미는 버튼이다.\n" +
                                 "켜면 마우스 왼쪽 버튼은 행동으로 안 간다 (스페이스는 그대로).")]
        bool mouseActsAsTouch = true;

        [SerializeField, Tooltip("아무도 안 누르고 있어도 버튼을 보여 준다")]
        bool showWhenNoTouch = true;

        const int None = -1;

        int stickPointer = None;
        Vector2 stickOrigin, stickCurrent;

        /// <summary>버튼마다 그것을 쥐고 있는 손가락. 없으면 <see cref="None"/>.</summary>
        int[] held;

        Texture2D disc;

        float StickRadius => Screen.height * stickRadiusFraction;

        /// <summary>실측·점검용. 지금 쓰는 배치.</summary>
        public TouchPad[] Pads => pads;

        Core.GameModeController mode;

        /// <summary>
        /// 지금 이 조작이 화면을 맡고 있는가. 필드에서만 맡는다 —
        /// 타이틀은 <see cref="TitleScreen"/>, 파괴 화면은 <see cref="TouchStrike"/>가 맡는다.
        /// <b>모드를 모르면 맡는다</b> — 그래야 GameModeController가 없는 씬에서도 조작이 된다.
        /// </summary>
        public bool Owns
        {
            get
            {
                if (TitleScreen.Blocking) return false;
                if (mode == null) mode = Core.GameModeController.Instance;
                return mode == null || mode.IsField;
            }
        }

        void Awake()
        {
            if (router == null) router = FindFirstObjectByType<InputRouter>();
            if (pads == null || pads.Length == 0) pads = TouchPad.Default();
            held = new int[pads.Length];
            for (int i = 0; i < held.Length; i++) held[i] = None;

            // 터치도 없고 마우스도 안 쓰기로 했으면 조용히 빠진다.
            if (autoDisableWithoutTouchscreen && Touchscreen.current == null && !mouseActsAsTouch)
            {
                enabled = false;
                return;
            }

            // 마우스를 손가락으로 쓰는 동안은 행동 버튼의 마우스 바인딩을 막는다.
            // 안 막으면 왼쪽 절반을 끌어 걷는 내내 그 클릭이 행동으로도 들어가 케이블이 토글된다.
            if (router != null) router.MouseTakenByTouchUi = mouseActsAsTouch;

            disc = MakeDisc(64);
        }

        void OnDestroy()
        {
            if (disc != null) Destroy(disc);
        }

        /// <summary>
        /// 눌려 있지 않은 버튼은 override를 <b>비워 둔다</b> (null). false로 두면 키보드가
        /// 영영 안 먹는다 — override가 값을 갖고 있는 동안은 실제 입력을 안 보기 때문이다.
        /// </summary>
        void Update()
        {
            if (router == null || held == null) return;

            // 타이틀/옵션이 떠 있으면 손을 뗀다. 안 그러면 시작 버튼을 누르려다
            // 그 밑의 케이블 버튼까지 같이 눌린다.
            //
            // 파괴 화면에서도 비켜선다 [지시 2026-10-12]. 거기서는 화면 전체가 좌/우 주먹이라
            // (<see cref="TouchStrike"/>) 조이스틱과 버튼이 떠 있으면 칠 자리를 잡아먹는다.
            if (!Owns) { Clear(); return; }

            bool sawStick = false;
            var seen = new bool[pads.Length];

            var ts = Touchscreen.current;
            if (ts != null)
                foreach (TouchControl t in ts.touches)
                {
                    if (!t.press.isPressed) continue;
                    Route(t.touchId.ReadValue(), t.position.ReadValue(), ref sawStick, seen);
                }

            if (mouseActsAsTouch && Mouse.current != null && Mouse.current.leftButton.isPressed)
                Route(int.MinValue, Mouse.current.position.ReadValue(), ref sawStick, seen);

            if (!sawStick) { stickPointer = None; router.MoveOverride = null; }

            for (int i = 0; i < pads.Length; i++)
            {
                if (!seen[i]) held[i] = None;
                Push(pads[i].Id, seen[i] ? (bool?)true : null);
            }
        }

        /// <summary>쥐고 있던 것을 전부 놓고 실제 입력에 자리를 돌려준다.</summary>
        void Clear()
        {
            stickPointer = None;
            router.MoveOverride = null;
            for (int i = 0; i < pads.Length; i++)
            {
                held[i] = None;
                Push(pads[i].Id, null);
            }
        }

        /// <summary>그 버튼의 눌림을 라우터에 민다. null이면 실제 입력에 자리를 돌려준다.</summary>
        void Push(TouchButton id, bool? state)
        {
            switch (id)
            {
                case TouchButton.Action: router.ActionOverride = state; break;
                case TouchButton.Interact: router.InteractOverride = state; break;
                case TouchButton.Drop: router.DropOverride = state; break;
                case TouchButton.Run: router.RunOverride = state; break;
            }
        }

        /// <summary>
        /// 포인터 하나를 버튼이나 조이스틱에 배정한다. <b>한 번 배정되면 뗄 때까지 유지된다</b> —
        /// 안 그러면 누른 채 손가락을 끌 때 버튼 사이를 넘나들며 연타로 들어간다.
        /// </summary>
        void Route(int id, Vector2 pos, ref bool sawStick, bool[] seen)
        {
            // 이미 어떤 버튼이 쥐고 있는 손가락인가
            for (int i = 0; i < pads.Length; i++)
                if (held[i] == id) { seen[i] = true; return; }

            // 새 손가락이 버튼 위에 닿았는가. 버튼이 조이스틱보다 먼저다.
            for (int i = 0; i < pads.Length; i++)
            {
                if (held[i] != None || !pads[i].Contains(pos)) continue;
                held[i] = id;
                seen[i] = true;
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
    }
}
