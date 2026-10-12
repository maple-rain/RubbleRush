using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Demo.Player
{
    /// <summary>
    /// New Input System → 이동 벡터 + 버튼 상태.
    ///
    /// 액션을 <b>코드로 정의한다.</b> .inputactions 에셋을 쓰면 인스펙터 와이어링이 하나 늘어나는데,
    /// 씬 와이어링은 사람이 손으로 하는 작업이라 줄일수록 좋다 (CLAUDE.md 규칙 1).
    ///
    /// 조준은 없다 — 조이스틱 하나 + 버튼 하나. 모바일 터치에서 듀얼 스틱은 평가가 나쁘다.
    /// 지금은 키보드 + 화면 터치만 받는다. 가상 조이스틱 위젯은 UI 작업(D10)에서 붙인다.
    /// </summary>
    public sealed class InputRouter : MonoBehaviour
    {
        InputAction moveAction;
        InputAction actionButton;
        InputAction dropAction;
        InputAction interactAction;
        InputAction runAction;

        /// <summary>이동 입력. 크기 0~1.</summary>
        public Vector2 Move { get; private set; }

        /// <summary>행동 버튼을 누르고 있는가. 홀드(파괴)와 전개(케이블) 둘 다 이 하나로 간다.</summary>
        public bool ActionHeld { get; private set; }

        /// <summary>이번 프레임에 눌렸는가. 연타 구간(D12)에서 쓴다.</summary>
        public bool ActionPressedThisFrame { get; private set; }

        /// <summary>
        /// 행동이 켜져 있는가. <b>누를 때마다 뒤집힌다</b> [지시 2026-09-22].
        ///
        /// 홀드에서 토글로 바꾼 이유는 손이다 — 파괴가 몇 초씩 걸리고 케이블은 한 바퀴를 도는 동안
        /// 계속 눌러야 해서, 홀드로 두면 손가락이 먼저 지친다. 모바일에서는 더 심하다.
        /// 판단(지금 부술 때인가 거둘 때인가)은 그대로 남고 물리적 부담만 사라진다.
        /// </summary>
        public bool ActionActive { get; private set; }

        /// <summary>토글을 강제로 끈다. 고리가 닫히거나 정지 상태가 되면 GameLoop 쪽에서 부른다.</summary>
        public void ClearAction() => ActionActive = false;

        /// <summary>
        /// 달리는 중인가. <b>누를 때마다 뒤집힌다</b> [지시 2026-09-28]. PC는 왼쪽 Shift.
        ///
        /// 홀드가 아니라 토글인 이유는 <see cref="ActionActive"/>와 같다 — 이동이 길고,
        /// 모바일에서 한 손가락은 조이스틱에 묶여 있어서 누르고 있을 손가락이 없다.
        ///
        /// 스틱을 기울인 정도로 걷기/달리기를 가르지 않는다. 가상 조이스틱은 기울기가
        /// 손가락 위치에 따라 제멋대로라 <b>의도하지 않은 전환</b>이 계속 일어난다.
        /// </summary>
        public bool RunActive { get; private set; }

        /// <summary>달리기를 강제로 끈다. 정지에 걸리거나 파괴 화면에 들어갈 때 쓴다.</summary>
        public void ClearRun() => RunActive = false;

        /// <summary>가상 달리기 버튼이 누르고 있는 상태. null이면 실제 입력을 쓴다 (터치 UI용).</summary>
        public bool? RunOverride { get; set; }

        /// <summary>
        /// 이번 타격이 <b>어느 팔</b>인가. false=왼팔, true=오른팔 [지시 2026-09-28].
        ///
        /// 세 단계로 정한다.
        /// <list type="number">
        /// <item><see cref="StrikeSideOverride"/>가 있으면 그대로 (터치 UI가 밀어넣는 값)</item>
        /// <item>포인터(마우스·터치)로 눌렀으면 <b>화면 좌우 절반</b> 중 누른 쪽</item>
        /// <item>키보드면 <b>누를 때마다 교대</b></item>
        /// </list>
        ///
        /// 누르는 <b>순간</b>에 정해서 붙잡아 둔다. 애니메이터가 나중에 다시 물으면
        /// 그 사이에 손가락이 움직여 답이 달라질 수 있다.
        /// </summary>
        public bool StrikeRightArm { get; private set; }

        /// <summary>터치 UI가 미는 타격 방향. null이면 포인터 위치나 교대를 쓴다.</summary>
        public bool? StrikeSideOverride { get; set; }

        /// <summary>이번 프레임에 '내려놓기'가 눌렸는가. PC는 Q.</summary>
        public bool DropPressedThisFrame { get; private set; }

        /// <summary>
        /// 이번 프레임에 '상호작용'이 눌렸는가. PC는 E.
        /// 건물에 다가갔을 때 파괴 화면으로 들어가는 키다 [지시 2026-09-22].
        /// </summary>
        public bool InteractPressedThisFrame { get; private set; }

        void Awake()
        {
            moveAction = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
            moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            moveAction.AddBinding("<Gamepad>/leftStick");

            actionButton = new InputAction("Action", InputActionType.Button);
            actionButton.AddBinding("<Keyboard>/space");
            actionButton.AddBinding("<Mouse>/leftButton");
            actionButton.AddBinding("<Gamepad>/buttonSouth");

            // 화물 내려놓기. 구멍에 못 넣는 잔해만 끌고 있으면 담지도 부수지도 못해서
            // 완전히 막히는데, 그 유일한 탈출구다.
            dropAction = new InputAction("Drop", InputActionType.Button);
            dropAction.AddBinding("<Keyboard>/q");
            dropAction.AddBinding("<Gamepad>/buttonWest");

            // 파괴 화면 진입. 필드에서 건물에 다가갔을 때만 의미가 있다.
            interactAction = new InputAction("Interact", InputActionType.Button);
            interactAction.AddBinding("<Keyboard>/e");
            interactAction.AddBinding("<Gamepad>/buttonNorth");

            // 달리기 토글. 모바일 버튼은 UI 작업에서 RunOverride로 붙인다.
            runAction = new InputAction("Run", InputActionType.Button);
            runAction.AddBinding("<Keyboard>/leftShift");
            runAction.AddBinding("<Gamepad>/leftStickPress");

            // 터치는 여기서 받지 않는다. <Touchscreen>/primaryTouch/press를 걸면
            // "화면 아무데나 누르면 버튼"이 되어, 왼쪽 절반의 가상 조이스틱을 미는 순간
            // 행동 버튼까지 같이 눌린다. 터치는 Demo.UI.TouchControls가 영역으로 나눠 처리하고
            // ActionOverride / MoveOverride로 밀어넣는다.
        }

        void OnEnable()
        {
            moveAction.Enable();
            actionButton.Enable();
            dropAction.Enable();
            interactAction.Enable();
            runAction.Enable();
        }

        void OnDisable()
        {
            moveAction.Disable();
            actionButton.Disable();
            dropAction.Disable();
            interactAction.Disable();
            runAction.Disable();
        }

        /// <summary>
        /// 가상 버튼이 누르고 있는 상태. null이면 실제 입력을 쓴다.
        /// 터치 UI 위젯(D10)이 여기에 값을 민다 — 위젯은 조이스틱과 버튼 둘 다 대신해야 한다.
        /// </summary>
        public bool? ActionOverride { get; set; }

        /// <summary>가상 조이스틱이 미는 이동 값. null이면 실제 입력을 쓴다.</summary>
        public Vector2? MoveOverride { get; set; }

        /// <summary>
        /// 가상 버튼이 누르고 있는 상태 [2026-10-12]. <see cref="RunOverride"/>와 같은 꼴이다.
        ///
        /// <b>이게 없으면 모바일에서 건물을 못 부순다.</b> 키보드에는 E와 Q가 있는데 터치에는
        /// 이동과 행동 둘뿐이라, 폰에서는 파괴 화면에 들어갈 방법이 아예 없었다 —
        /// 게임의 절반이 잠긴다. 누르는 <b>순간</b>만 쓰는 입력이라 눌림 상태를 받아
        /// <see cref="Update"/>에서 가장자리를 만든다.
        /// </summary>
        public bool? InteractOverride { get; set; }

        /// <inheritdoc cref="InteractOverride"/>
        public bool? DropOverride { get; set; }

        /// <summary>
        /// 터치 UI가 마우스를 손가락으로 쓰고 있다 [2026-10-12]. 켜면 <b>마우스 왼쪽 버튼이
        /// 행동으로 안 간다</b>.
        ///
        /// <b>왜 필요한가.</b> 행동 버튼에는 <c>&lt;Mouse&gt;/leftButton</c>이 묶여 있다.
        /// 실기가 없어서 마우스로 모바일 UI를 확인하게 해 놓으면, 왼쪽 절반을 끌어 조이스틱을
        /// 미는 동안 <b>그 클릭이 행동으로도 들어가</b> 걸음마다 케이블이 토글된다 —
        /// 이 파일 아래쪽에 터치에 대해 적어 둔 경고와 같은 사고가 마우스로 재현되는 것이다.
        ///
        /// 키보드와 게임패드는 그대로 둔다. 막는 것은 마우스뿐이다.
        /// </summary>
        public bool MouseTakenByTouchUi { get; set; }

        bool runHeldLast, interactHeldLast, dropHeldLast;

        void Update()
        {
            if (MoveOverride.HasValue)
            {
                Vector2 v = MoveOverride.Value;
                Move = v.sqrMagnitude > 1f ? v.normalized : v;
            }
            else
            {
                Vector2 raw = moveAction.ReadValue<Vector2>();
                Move = raw.sqrMagnitude > 1f ? raw.normalized : raw;
            }

            bool wasHeld = ActionHeld;
            ActionHeld = ActionOverride ?? ActionRaw();
            ActionPressedThisFrame = ActionHeld && !wasHeld;

            // 누르는 순간마다 뒤집는다. 떼는 것은 아무 일도 하지 않는다.
            if (ActionPressedThisFrame)
            {
                ActionActive = !ActionActive;
                StrikeRightArm = DecideStrikeSide();
            }

            DropPressedThisFrame = Edge(DropOverride, ref dropHeldLast, dropAction);
            InteractPressedThisFrame = Edge(InteractOverride, ref interactHeldLast, interactAction);

            // 달리기도 같은 토글 방식. 가상 버튼이 있으면 그 쪽의 누르는 순간을 쓴다.
            if (Edge(RunOverride, ref runHeldLast, runAction)) RunActive = !RunActive;
        }

        /// <summary>
        /// 행동 버튼이 눌려 있는가. <see cref="MouseTakenByTouchUi"/>가 켜져 있으면
        /// <b>마우스만 빼고</b> 본다 — 키보드 스페이스와 게임패드는 그대로 살아 있다.
        /// </summary>
        bool ActionRaw()
        {
            if (!MouseTakenByTouchUi) return actionButton.IsPressed();

            foreach (InputControl c in actionButton.controls)
            {
                if (c.device is Mouse) continue;
                if (c is ButtonControl b && b.isPressed) return true;
            }
            return false;
        }

        /// <summary>
        /// 눌린 <b>순간</b>을 만든다. 가상 버튼이 값을 밀고 있으면 그쪽의 가장자리를,
        /// 아니면 실제 입력의 것을 쓴다. 누름 상태만 오가는 터치 UI와 가장자리를 직접 주는
        /// Input System을 같은 모양으로 맞추는 자리다.
        /// </summary>
        static bool Edge(bool? over, ref bool heldLast, InputAction real)
        {
            bool pressed = over.HasValue ? (over.Value && !heldLast) : real.WasPressedThisFrame();
            heldLast = over ?? false;
            return pressed;
        }

        /// <summary>
        /// 이번 타격이 어느 팔인지 정한다. <see cref="StrikeRightArm"/>의 세 단계 규칙 그대로다.
        ///
        /// 포인터로 누른 것인지 키보드로 누른 것인지는 <c>activeControl.device</c>로 가른다 —
        /// 같은 액션에 스페이스바와 마우스가 함께 묶여 있어서, 어느 쪽이 눌렀는지를
        /// 장치로 되물어야 한다.
        /// </summary>
        bool DecideStrikeSide()
        {
            if (StrikeSideOverride.HasValue) return StrikeSideOverride.Value;

            InputControl ctrl = actionButton.activeControl;
            var pointer = ctrl != null ? ctrl.device as Pointer : null;
            if (pointer != null && Screen.width > 0)
                return pointer.position.ReadValue().x >= Screen.width * 0.5f;

            return !StrikeRightArm;      // 키보드 — 누를 때마다 교대
        }
    }
}
