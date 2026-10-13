using System.Collections.Generic;
using Demo.Collection;
using Demo.Core;
using Demo.Destruction;
using Demo.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Demo.UI
{
    /// <summary>
    /// 지금 할 수 있는 것을 <b>대상 옆에</b> 띄운다 [지시 2026-10-12].
    ///
    /// 기획자가 레퍼런스(원신 모바일)를 짚으며 정한 모양이다 — 건물에 가까이 가면 그 자리에
    /// "부수기"가 뜨고, 멀어지면 사라진다. 고정 버튼으로 두지 않는 이유는 둘이다:
    /// 조건이 맞을 때만 쓰는 것이라 늘 떠 있으면 화면을 가리고, <b>무엇에 대한 행동인지가
    /// 자리로 설명되기 때문</b>이다 — 어느 건물을 부수는지 화살표 없이 알 수 있다.
    ///
    /// <b>세로로 쌓는다.</b> 조건이 겹칠 수 있어서다 — 짐을 끌고 건물 앞에 서면 "부수기"와
    /// "놓기"가 같이 뜬다. 레퍼런스도 같은 꼴이다.
    ///
    /// <b>Canvas를 안 쓴다.</b> <see cref="TouchControls"/>와 같은 이유 — 씬 와이어링은
    /// 사람이 손으로 하는 작업이라 (CLAUDE.md 규칙 1) 코드가 소유할 수 있는 쪽을 고른다.
    ///
    /// PC에서는 키 이름(E·Q)을, 터치에서는 누를 수 있는 동그라미를 보여 준다.
    /// </summary>
    [DefaultExecutionOrder(-50)]        // InputRouter보다 먼저 밀어 넣는다
    public sealed partial class ActionPrompt : MonoBehaviour
    {
        [SerializeField] InputRouter router;
        [SerializeField] InteractionProbe probe;
        [SerializeField] CargoTow cargo;
        [SerializeField] Camera eye;

        [SerializeField, Range(0.03f, 0.12f), Tooltip("동그라미 반경. 화면 세로 대비")]
        float knobFraction = 0.055f;

        [SerializeField, Tooltip("대상의 어느 높이에 띄울지. 건물 높이 대비 (1이면 꼭대기)")]
        float anchorHeight = 0.525f;

        [SerializeField, Tooltip("터치가 없으면 키 이름을 보여 준다")]
        bool showKeysOnDesktop = true;

        [SerializeField, Tooltip("마우스로도 누를 수 있게 한다. 실기가 없어서 기본으로 켜 둔다")]
        bool mouseActsAsTouch = true;

        /// <summary>한 줄. 무엇을 누르면 무엇이 일어나는가.</summary>
        struct Row
        {
            public TouchButton Id;
            public string Key;          // PC에서 보여 줄 키 이름
            public string Text;

            /// <summary>띄우고 싶은 자리. 대상의 <b>꼭대기 쪽</b>이다.</summary>
            public Vector3 Head;

            /// <summary>
            /// 같은 대상의 <b>발치</b>. 꼭대기가 화면 위로 벗어났을 때 이 둘을 잇는
            /// 선을 타고 내려온다 — 원근 투영은 직선을 직선으로 보내므로,
            /// 화면에서 두 점을 선형으로 섞은 자리가 곧 그 세로 모서리 위의 한 점이다.
            /// </summary>
            public Vector3 Foot;
        }

        readonly List<Row> rows = new List<Row>();
        int heldRow = -1;               // 지금 눌려 있는 줄. 없으면 -1
        int heldPointer = -1;

        /// <summary>부수기가 막힌 사유. 없으면 null.</summary>
        string blocked;

        bool Touch => Touchscreen.current != null;

        void Awake()
        {
            if (router == null) router = FindFirstObjectByType<InputRouter>();
            if (probe == null) probe = FindFirstObjectByType<InteractionProbe>();
            if (cargo == null) cargo = FindFirstObjectByType<CargoTow>();
            if (eye == null) eye = Camera.main;

            disc = MakeDisc(64);
        }

        void OnDestroy()
        {
            if (disc != null) Destroy(disc);
        }

        void Update()
        {
            Gather();

            // 눌린 줄을 찾는다. 한 번 잡으면 뗄 때까지 유지한다.
            bool stillDown = false;

            if (Touch)
                foreach (TouchControl t in Touchscreen.current.touches)
                {
                    if (!t.press.isPressed) continue;
                    if (Claim(t.touchId.ReadValue(), t.position.ReadValue())) { stillDown = true; break; }
                }

            // 실기가 없어서 마우스로도 누를 수 있어야 한다 — 손가락 하나로 친다.
            if (!stillDown && mouseActsAsTouch && Mouse.current != null && Mouse.current.leftButton.isPressed)
                stillDown = Claim(int.MinValue, Mouse.current.position.ReadValue());

            if (!stillDown) { Release(); return; }
            if (heldRow >= 0 && heldRow < rows.Count) Push(rows[heldRow].Id, true);
        }

        /// <summary>그 포인터가 어떤 줄을 쥐고 있는가. 새로 잡기도 한다.</summary>
        bool Claim(int id, Vector2 pos)
        {
            if (heldPointer == id) return true;
            if (heldRow >= 0) return false;

            int hit = RowAt(pos);
            if (hit < 0) return false;

            heldRow = hit;
            heldPointer = id;
            return true;
        }

        /// <summary>
        /// 손을 뗀다. <b>이 컴포넌트가 미는 채널을 전부 비운다</b> [고침 2026-10-12].
        ///
        /// 전에는 줄 번호로 기억했다가 그 줄의 것만 비웠는데, 줄 목록은 매 프레임 다시
        /// 만들어진다 — 잡을 때 0번이 '부수기'였어도 놓을 때 0번이 '놓기'일 수 있고,
        /// 그러면 놓기만 풀리고 <b>부수기가 눌린 채로 남는다</b>. 눌린 채로 남으면
        /// 가장자리(false→true)가 영영 안 와서 E가 다시는 안 먹는다.
        /// 채널이 둘뿐이라 전부 비우는 쪽이 싸고 틀릴 여지가 없다.
        /// </summary>
        void Release()
        {
            if (heldRow >= 0 || heldPointer != -1)
            {
                Push(TouchButton.Interact, null);
                Push(TouchButton.Drop, null);
            }
            heldRow = -1;
            heldPointer = -1;
        }

        void Push(TouchButton id, bool? state)
        {
            if (router == null) return;
            if (id == TouchButton.Interact) router.InteractOverride = state;
            else if (id == TouchButton.Drop) router.DropOverride = state;
        }

        /// <summary>
        /// 지금 뜰 줄들을 모은다. <b>매 프레임 다시 만든다</b> — 조건이 매 프레임 바뀌고,
        /// 줄이 많아야 둘이라 아끼는 의미가 없다.
        /// </summary>
        void Gather()
        {
            rows.Clear();
            blocked = null;

            // 타이틀이 덮고 있으면 아무것도 안 띄운다 — 그 너머로 눌리면 안 된다.
            if (TitleScreen.Blocking) return;

            var mode = GameModeController.Instance;
            if (mode == null || !mode.IsField) return;      // 파괴 화면에서는 안 띄운다

            if (probe != null && probe.PromptVisible)
            {
                Destructible d = probe.Candidate;
                Vector3 size = d.Data != null ? d.Data.colliderSize : Vector3.one * 2f;
                rows.Add(new Row
                {
                    Id = TouchButton.Interact,
                    Key = "E",
                    Text = "부수기",
                    Head = d.transform.position + Vector3.up * size.y * anchorHeight,
                    Foot = d.transform.position + Vector3.up * FootHeight,
                });
            }
            else if (probe != null)
            {
                // 막혀 있으면 <b>왜 막혔는지</b>를 띄운다 [2026-10-12].
                // InteractionProbe는 가까이에 건물이 있을 때만 사유를 채운다 —
                // "왜 안 되지"를 추측하게 두지 않되, 들판에서 혼잣말을 하지도 않는다.
                blocked = probe.BlockedReason;
            }

            // <b>막혔다고 이 줄까지 지우지 않는다</b> [고침 2026-10-12].
            //
            // 예전에는 사유가 있으면 바로 빠져나갔다. 그런데 사유 중 하나가 "견인 중"이라,
            // <b>짐을 끄는 내내 '놓기'가 안 떴다</b> — 놓을 수 있다는 안내가 필요한 바로 그때
            // 사라져 있었다. 못 하는 것을 설명하는 일이 할 수 있는 것을 가려서는 안 된다.
            if (cargo != null && cargo.TotalUnits > 0)
            {
                rows.Add(new Row
                {
                    Id = TouchButton.Drop,
                    Key = "Q",
                    Text = "놓기",
                    Head = cargo.transform.position + Vector3.up * 1.6f,
                    Foot = cargo.transform.position + Vector3.up * FootHeight,
                });
            }
        }

        /// <summary>그 자리에 있는 줄. 없으면 -1.</summary>
        int RowAt(Vector2 screenPos)
        {
            float r = Screen.height * knobFraction;
            for (int i = 0; i < rows.Count; i++)
            {
                Vector2 c = Anchor(i);
                if (c.x < -9000f) continue;
                if ((screenPos - c).sqrMagnitude <= r * r) return i;
            }
            return -1;
        }

        /// <summary>
        /// i번째 줄의 화면 좌표(왼쪽 아래 기준). 카메라 뒤에 있으면 아주 작은 x를 돌려준다 —
        /// <see cref="Camera.WorldToScreenPoint"/>는 뒤쪽 점도 화면 안 좌표를 내므로
        /// z를 안 보면 등 뒤의 건물에 프롬프트가 뜬다.
        ///
        /// <b>높은 건물에서는 꼭대기가 화면 밖이다</b> [지적 2026-10-12].
        /// 카메라는 50도로 내려다보고 시야 위쪽 모서리가 수평에서 20도 아래라, 건물 옆에
        /// 섰을 때 화면 맨 위에 걸리는 세계의 높이는 4m 남짓이다. 안내를 건물 높이의
        /// 75%에 달아 두었으므로 <c>t4_heavy</c>(5.3m)부터 넘치고 <c>t5_solid</c>는
        /// 7.35m — <b>뜨기는 뜨는데 화면 위에 있다.</b> "가끔 E가 안 뜬다"의 정체다.
        ///
        /// 그래서 꼭대기와 발치를 둘 다 넘겨받아, 꼭대기가 넘치면 <b>그 둘을 잇는 선</b>을
        /// 타고 화면 안까지 내려온다. 원근 투영은 직선을 직선으로 보내므로 화면에서 섞은
        /// 자리가 곧 건물의 세로 모서리 위의 한 점이다 — 건물에서 떨어지지 않는다.
        /// </summary>
        Vector2 Anchor(int i)
        {
            var hidden = new Vector2(-9999f, -9999f);
            if (eye == null || i >= rows.Count) return hidden;

            Vector3 head = eye.WorldToScreenPoint(rows[i].Head);
            Vector3 foot = eye.WorldToScreenPoint(rows[i].Foot);
            if (head.z <= 0f && foot.z <= 0f) return hidden;

            float r = Screen.height * knobFraction;
            float ceiling = Screen.height - r * CeilingMargin;

            Vector2 p = head.z > 0f ? new Vector2(head.x, head.y) : new Vector2(foot.x, foot.y);

            if (head.z > 0f && foot.z > 0f && p.y > ceiling)
            {
                var a = new Vector2(foot.x, foot.y);
                var b = new Vector2(head.x, head.y);
                float span = b.y - a.y;
                p = Vector2.Lerp(a, b, Mathf.Abs(span) > 1f ? Mathf.Clamp01((ceiling - a.y) / span) : 0f);
            }

            // 줄이 여럿이면 아래로 쌓는다. 레퍼런스와 같은 꼴이다.
            p.y -= r * 2.4f * i;

            // 그래도 밖이면 가장자리에 붙인다. 안 보이는 안내는 없는 것과 같다.
            p.x = Mathf.Clamp(p.x, r * CeilingMargin, Screen.width - r * CeilingMargin);
            p.y = Mathf.Clamp(p.y, r * CeilingMargin, ceiling);
            return p;
        }

        /// <summary>발치를 땅에서 조금 띄운다. 0으로 두면 지면에 반쯤 묻혀 보인다.</summary>
        public const float FootHeight = 0.6f;

        /// <summary>
        /// 화면 위 끝에서 동그라미 반경의 몇 배를 비워 둘 것인가.
        /// <b>검사 도구가 같은 값을 읽어야 한다</b> — 따로 적어 두면 한쪽만 고쳤을 때
        /// "고쳤는데 검사는 통과한다"가 된다 (하네스는 씬과 코드를 읽어야 한다).
        /// </summary>
        public const float CeilingMargin = 1.3f;

    }
}
