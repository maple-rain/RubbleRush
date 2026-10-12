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
    public sealed class ActionPrompt : MonoBehaviour
    {
        [SerializeField] InputRouter router;
        [SerializeField] InteractionProbe probe;
        [SerializeField] CargoTow cargo;
        [SerializeField] Camera eye;

        [SerializeField, Range(0.03f, 0.12f), Tooltip("동그라미 반경. 화면 세로 대비")]
        float knobFraction = 0.055f;

        [SerializeField, Tooltip("대상의 어느 높이에 띄울지. 건물 높이 대비 (1이면 꼭대기)")]
        float anchorHeight = 0.75f;

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
            public Vector3 World;       // 띄울 월드 좌표
        }

        readonly List<Row> rows = new List<Row>();
        int heldRow = -1;               // 지금 눌려 있는 줄. 없으면 -1
        int heldPointer = -1;

        Texture2D disc;
        GUIStyle label, keyCap;

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

        void Release()
        {
            if (heldRow >= 0) Push(rows.Count > heldRow ? rows[heldRow].Id : TouchButton.Interact, null);
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
                    World = d.transform.position + Vector3.up * size.y * anchorHeight,
                });
            }

            if (cargo != null && cargo.TotalUnits > 0)
            {
                rows.Add(new Row
                {
                    Id = TouchButton.Drop,
                    Key = "Q",
                    Text = "놓기",
                    World = cargo.transform.position + Vector3.up * 1.6f,
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
        /// </summary>
        Vector2 Anchor(int i)
        {
            if (eye == null || i >= rows.Count) return new Vector2(-9999f, -9999f);

            Vector3 p = eye.WorldToScreenPoint(rows[i].World);
            if (p.z <= 0f) return new Vector2(-9999f, -9999f);

            // 줄이 여럿이면 아래로 쌓는다. 레퍼런스와 같은 꼴이다.
            float step = Screen.height * knobFraction * 2.4f;
            return new Vector2(p.x, p.y - step * i);
        }

        // ── 그리기 ──────────────────────────────────────────────────────────────

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || rows.Count == 0) return;
            if (!Touch && !showKeysOnDesktop) return;

            if (label == null) Styles();

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
                label.fontSize = Mathf.Max(11, Mathf.RoundToInt(r * 0.56f));
                var box = new Rect(g.x + r * 1.25f, g.y - r * 0.7f, Screen.width * 0.3f, r * 1.4f);
                GUI.Label(box, rows[i].Text, label);
            }
        }

        void Styles()
        {
            label = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold,
                wordWrap = false,
            };
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
