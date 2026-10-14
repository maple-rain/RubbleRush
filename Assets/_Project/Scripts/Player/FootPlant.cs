using UnityEngine;

namespace Demo.Player
{
    /// <summary>
    /// 발이 <b>실제로 땅에 닿는 순간</b>을 집어낸다 [고침 2026-10-14].
    ///
    /// <b>무엇이 틀렸었나.</b> 발소리를 <b>걸은 거리</b>로 냈다 — 2.2m마다 한 번.
    /// 그런데 다리는 애니메이션으로 움직인다. 둘은 서로를 모르므로 계속 어긋났다.
    /// 기획자 지적: <b>"발을 딛는 타이밍과 소리 재생의 타이밍이 안 맞는다."</b>
    ///
    /// <b>위상표도 틀렸다.</b> 처음에는 클립마다 발이 닿는 위상을 재서 표로 들고
    /// (Walk 0.292/0.792 · Run 0.375/0.896) 블렌드에 따라 보간했다. 끝점은 맞았지만
    /// <b>중간에서 깨졌다</b> — 실측하면 blend 0.6에서 오른발이 0.29가 아니라
    /// <b>0.66~0.77</b>에 바닥에 있었다. 두 클립을 섞으면 자세가 섞이는 것이지
    /// 착지 시점이 섞이는 것이 아니다.
    ///
    /// <b>그래서 뼈를 직접 본다.</b> 발이 언제 닿는지는 발가락 뼈가 이미 알고 있다.
    /// 높이가 내려오다 문턱을 지나는 순간이 착지다. 이러면 블렌드가 어떻든,
    /// 클립을 다시 자르든(<c>Demo/8b</c>), 배속이 얼마든 <b>언제나 맞는다.</b>
    ///
    /// <b>문턱은 스스로 맞춘다.</b> 걷기와 달리기는 발이 뜨는 높이가 다르다 —
    /// 실측하면 걷기는 0.127~0.391m, 달리기는 0.095~0.785m다. 고정값을 두면 한쪽이
    /// 안 울리거나 매 프레임 울린다. 그래서 발마다 최근 높낮이를 따라다니며
    /// 그 사이의 일정 지점을 문턱으로 쓴다.
    ///
    /// MonoBehaviour가 아닌 이유는 <see cref="LocomotionRate"/>와 같다.
    /// </summary>
    [System.Serializable]
    public sealed class FootPlant
    {
        [SerializeField, Tooltip("발가락 뼈 이름에 이 글자가 들어간 것을 찾는다.\n" +
                 "로봇은 LeftToeBase / RightToeBase다")]
        string boneMark = "ToeBase";

        [SerializeField, Range(0.005f, 0.08f),
         Tooltip("제일 낮은 자리에서 이만큼(m) 안으로 내려오면 '닿았다'로 본다.\n\n" +
                 "비율이 아니라 거리인 이유 [실측 2026-10-14]: 발이 뜨는 높이가 " +
                 "걷기는 13cm, 달리기는 69cm로 다섯 배 차이다. 비율로 잡으면 달리기에서 " +
                 "문턱이 땅에서 17.8cm나 떠서 착지보다 131ms 먼저 울렸다.\n\n" +
                 "0.01m인 이유: 걷기에서 발가락은 한 바퀴에 두 번 내려온다 — " +
                 "진짜 착지(바닥에서 0cm)와 발끝을 굴리는 얕은 골(2.8cm)이다. 그 사이를 가른다")]
        float plantMargin = 0.01f;

        [SerializeField, Range(0.02f, 0.5f),
         Tooltip("다시 울리기 전에 발이 이만큼(높이 범위 대비) 떠야 한다.\n" +
                 "바닥 근처에서 미세하게 흔들릴 때 연타로 울리는 것을 막는다")]
        float rearmAt = 0.55f;

        [SerializeField, Range(1f, 20f),
         Tooltip("최근 높낮이를 재는 구간의 길이(초). 걷기 한 바퀴(1.125초)보다 길어야 한다.\n" +
                 "짧으면 한 걸음을 통째로 못 봐서 위아래 기준이 뜻을 잃는다")]
        float forget = 6f;

        [SerializeField, Range(0f, 0.5f),
         Tooltip("이보다 느리면 발소리를 내지 않는다. 제자리 대기에서 울리지 않게 하는 문턱")]
        float minBlend = 0.08f;

        Transform[] toes;
        Foot[] feet;

        /// <summary>실측·디버그용. 지금까지 집어낸 착지 횟수.</summary>
        public int Plants { get; private set; }

        /// <summary>
        /// 발 하나가 기억하는 것.
        ///
        /// <b>높낮이를 두 벌 들고 있다</b> [고침 2026-10-14]. 지난 구간에서 본 것(Low/High)과
        /// 지금 구간에서 모으고 있는 것(WinLow/WinHigh)이다. 쓸 때는 <b>둘을 합친다.</b>
        ///
        /// 처음에는 매 프레임 조금씩 좁히는 지수 감쇠를 썼는데, 그러면 <b>평형에서 창이
        /// 늘 실제보다 좁다.</b> 좁은 창에서 "아래 12%"를 잡으면 그 자리가 실제로는 더 높아서,
        /// 걷기의 가짜 골(발끝 굴림, 15.4cm)까지 착지로 세었다.
        /// 구간을 통째로 갈아 끼우면 창이 줄어들지 않는다.
        /// </summary>
        struct Foot
        {
            public float Low, High;          // 지난 구간
            public float WinLow, WinHigh;    // 지금 모으는 중
            public float WinAge;
            public bool Armed;
        }

        /// <summary>
        /// 발가락 뼈를 찾아 둔다. <b>Awake에서 한 번만</b> 부른다 (CLAUDE.md 규칙 5).
        /// </summary>
        public void Bind(Transform root)
        {
            var found = new System.Collections.Generic.List<Transform>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>())
                if (t.name.IndexOf(boneMark, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    found.Add(t);

            toes = found.ToArray();
            feet = new Foot[toes.Length];
            for (int i = 0; i < feet.Length; i++)
                feet[i] = new Foot
                {
                    Low = float.MaxValue, High = float.MinValue,
                    WinLow = float.MaxValue, WinHigh = float.MinValue,
                    Armed = true,
                };
        }

        /// <summary>
        /// 한 프레임 보고, <b>이번 프레임에 땅에 닿은 발의 수</b>를 돌려준다.
        /// 보통 0이고 가끔 1이다.
        /// </summary>
        /// <param name="root">발 높이를 재는 기준. 플레이어의 발밑이다</param>
        /// <param name="blend">0=정지, 1=최고속. <see cref="LocomotionRate.Blend"/></param>
        public int Tick(Transform root, float blend, float dt)
        {
            if (toes == null || toes.Length == 0 || root == null) return 0;

            // 멈춰 있을 때는 재지 않는다. 대기 동작의 미세한 발 흔들림으로 울리면 안 된다.
            if (blend < minBlend)
            {
                for (int i = 0; i < feet.Length; i++) feet[i].Armed = false;
                return 0;
            }

            float baseY = root.position.y;
            int hits = 0;

            for (int i = 0; i < toes.Length; i++)
            {
                if (toes[i] == null) continue;
                float y = toes[i].position.y - baseY;

                Foot f = feet[i];

                // 지금 구간에 쌓는다
                f.WinLow = Mathf.Min(f.WinLow, y);
                f.WinHigh = Mathf.Max(f.WinHigh, y);
                f.WinAge += dt;

                // 구간이 차면 통째로 갈아 끼운다. 조금씩 좁히지 않으므로 창이 안 줄어든다.
                if (f.WinAge >= forget)
                {
                    f.Low = f.WinLow;
                    f.High = f.WinHigh;
                    f.WinLow = f.WinHigh = y;
                    f.WinAge = 0f;
                }

                // 판정은 <b>지난 구간과 지금 구간을 합쳐서</b> 한다. 그래야 막 걷기 시작했을 때
                // (지금 구간이 아직 한 걸음도 안 됐을 때) 엉뚱한 문턱이 안 나온다.
                float low = Mathf.Min(f.Low, f.WinLow);
                float high = Mathf.Max(f.High, f.WinHigh);
                float span = high - low;

                if (span > 0.01f)
                {
                    // 닿는 판정은 <b>바닥에서의 거리</b>로, 다시 받는 판정은 <b>범위의 비율</b>로.
                    // 뜨는 높이는 걷기와 달리기가 다섯 배 다르지만 땅은 한 곳이다.
                    if (f.Armed && y <= low + plantMargin)
                    {
                        f.Armed = false;                 // 닿았다
                        hits++;
                    }
                    else if (!f.Armed && y >= low + span * rearmAt)
                    {
                        f.Armed = true;                  // 충분히 떴다 — 다음 착지를 받는다
                    }
                }

                feet[i] = f;
            }

            Plants += hits;
            return hits;
        }
    }
}
