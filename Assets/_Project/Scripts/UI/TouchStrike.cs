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

        readonly HashSet<int> down = new HashSet<int>();
        readonly List<int> gone = new List<int>();

        float flashUntil;
        bool flashRight;
        Texture2D px;

        GameModeController mode;

        bool wasActive;

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
        }

        void OnDestroy()
        {
            if (px != null) Destroy(px);
        }

        void Update()
        {
            if (mode == null) mode = GameModeController.Instance;

            if (!Active)
            {
                down.Clear();
                wasActive = false;
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

            flashRight = right;
            flashUntil = Time.unscaledTime + flashSeconds;
        }

        /// <summary>
        /// 친 쪽을 아주 잠깐 밝힌다. 손가락이 화면을 가려서 <b>맞았는지 안 맞았는지</b>가
        /// 안 보이는 일이 흔한데, 반쪽이 번쩍하면 가린 채로도 읽힌다.
        /// </summary>
        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!Active || flashSeconds <= 0f) return;

            float left = flashUntil - Time.unscaledTime;
            if (left <= 0f) return;

            float half = Screen.width * 0.5f;
            var r = new Rect(flashRight ? half : 0f, 0f, half, Screen.height);

            Color prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.10f * (left / flashSeconds));
            GUI.DrawTexture(r, px);
            GUI.color = prev;
        }
    }
}
