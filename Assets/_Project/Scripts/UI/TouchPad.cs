using UnityEngine;

namespace Demo.UI
{
    /// <summary>
    /// 터치 버튼 하나의 자리와 크기 [2026-10-12].
    ///
    /// <b>표로 뺀 이유.</b> 전에는 행동 버튼 하나가 <see cref="TouchControls"/>에 직접 박혀
    /// 있었다. 그래서 폰에서는 이동과 행동만 되고 <b>E(부수기)·Q(놓기)·Shift(달리기)가 아예
    /// 없었다</b> — 건물을 부술 수가 없으니 게임의 절반이 잠긴 셈이다. 버튼을 늘리는 일이
    /// 한 줄이 되어야 그런 구멍이 안 생긴다.
    ///
    /// 자리와 크기를 <b>화면 비율</b>로 잡는다. 폰은 해상도도 비율도 제각각이라 픽셀로 적으면
    /// 기종마다 버튼이 손가락 밖으로 나간다. 세로를 기준으로 삼는 이유는 가로 화면에서
    /// 세로가 짧은 쪽이고, 엄지가 닿는 거리를 정하는 것도 그쪽이기 때문이다.
    /// </summary>
    public enum TouchButton
    {
        /// <summary>주먹 / 케이블. 누르고 있는 동안 유지된다.</summary>
        Action,

        /// <summary>부수기 화면 진입 (키보드 E). 누르는 순간만 쓴다.</summary>
        Interact,

        /// <summary>짐 놓기 (키보드 Q). 누르는 순간만 쓴다.</summary>
        Drop,

        /// <summary>달리기 토글 (키보드 Shift).</summary>
        Run,
    }

    /// <summary>한 버튼의 배치. 전부 화면 비율이다 — 반경은 <b>화면 세로</b> 기준.</summary>
    [System.Serializable]
    public struct TouchPad
    {
        public TouchButton Id;

        /// <summary>중심. 화면 가로·세로 대비 0~1, 왼쪽 아래가 원점이다.</summary>
        public Vector2 Center;

        /// <summary>반경. 화면 <b>세로</b> 대비.</summary>
        public float Radius;

        /// <summary>버튼 위에 쓰는 글자. 비면 안 쓴다.</summary>
        public string Label;

        public TouchPad(TouchButton id, float x, float y, float radius, string label)
        {
            Id = id;
            Center = new Vector2(x, y);
            Radius = radius;
            Label = label;
        }

        public Vector2 PixelCenter => new Vector2(Screen.width * Center.x, Screen.height * Center.y);
        public float PixelRadius => Screen.height * Radius;

        public bool Contains(Vector2 pos)
        {
            float r = PixelRadius;
            return (pos - PixelCenter).sqrMagnitude <= r * r;
        }

        /// <summary>
        /// 기본 배치 — <b>늘 쓰는 것만 둘</b> [지시 2026-10-12].
        ///
        /// 처음에는 넷을 깔았는데 기획자가 레퍼런스(원신 모바일)를 짚으며 "좌측 조이스틱,
        /// 우측에 대쉬·케이블 둘"로 정했다. <b>부수기와 놓기는 고정 버튼에서 뺐다</b> —
        /// 그 둘은 조건이 맞을 때만 쓰는 것이라 늘 떠 있으면 화면만 가린다.
        /// 대신 <see cref="ActionPrompt"/>가 대상 옆에 띄운다.
        ///
        /// <b>전부 화면 오른쪽 절반에 둔다</b> — 왼쪽 절반은 어디를 눌러도 조이스틱이 되는
        /// 영역이라, 거기 버튼을 두면 누를 때마다 캐릭터가 같이 움직인다.
        ///
        /// 겹침과 손가락 크기는 <c>Demo/22. 터치 버튼 배치 점검</c>이 확인한다.
        /// </summary>
        public static TouchPad[] Default() => new[]
        {
            new TouchPad(TouchButton.Action, 0.89f, 0.22f, 0.125f, "케이블"),
            new TouchPad(TouchButton.Run,    0.72f, 0.33f, 0.085f, "대쉬"),
        };
    }
}
