using UnityEngine;

namespace Demo.Data
{
    /// <summary>
    /// 게임이 내는 소리의 목록 [2026-10-08]. 이름 하나에 음원 여러 개를 매단다.
    ///
    /// <b>왜 코드가 AudioClip을 직접 들지 않는가.</b> 음원을 컴포넌트에 물리면 기획자가
    /// 소리를 바꿀 때마다 프리팹을 열어야 하고, 어떤 소리가 어디서 나는지 한눈에 못 본다.
    /// 수치를 <c>GameConfig</c>로 뺀 것과 같은 이유다 (CLAUDE.md 규칙 7) —
    /// <b>이 에셋 하나만 보면 게임이 내는 소리가 전부 보인다.</b>
    ///
    /// <b>한 이름에 여러 음원을 두는 이유</b>: 같은 소리가 반복되면 기계처럼 들린다.
    /// 초당 다섯 번 두드리는 게임이라 특히 그렇다. 재생할 때 하나를 무작위로 고르고,
    /// 음높이도 조금 흔든다.
    /// </summary>
    [CreateAssetMenu(fileName = "SoundBank", menuName = "Demo/Sound Bank")]
    public sealed class SoundBank : ScriptableObject
    {
        [System.Serializable]
        public sealed class Entry
        {
            [Tooltip("어느 순간의 소리인가")]
            public Sfx id;

            [Tooltip("이 중 하나를 무작위로 고른다. 비어 있으면 조용히 넘어간다")]
            public AudioClip[] clips;

            [Range(0f, 2f), Tooltip("음량 배율")]
            public float volume = 1f;

            [Range(0f, 0.4f),
             Tooltip("음높이를 위아래로 흔드는 폭. 0이면 매번 똑같이 들린다.\n\n" +
                     "<b>기본값은 0이다</b> [지시 2026-10-15]: \"사운드 가공없이\". " +
                     "받아 온 음원을 그대로 들려줘야 하므로 재생할 때도 음을 옮기지 않는다")]
            public float pitchJitter;

            [Tooltip("같은 소리를 다시 내기까지의 최소 간격(초). " +
                     "0이면 안 막는다. 한 프레임에 여러 개가 겹쳐 울리는 것을 막는 값이다")]
            public float minGap;

            [Tooltip("켜면 거리와 무관하게 같은 크기로 들린다(2D). " +
                     "단계 상승이나 할당량 달성처럼 '화면 전체의 사건'에 쓴다")]
            public bool flat;

            /// <summary>마지막으로 울린 시각. 런타임 전용이라 직렬화하지 않는다.</summary>
            [System.NonSerialized] public float LastAt = float.NegativeInfinity;

            /// <summary>
            /// 직전에 고른 음원의 번호. <b>연달아 같은 것을 안 뽑으려고</b> 들고 있다
            /// [더함 2026-10-14].
            ///
            /// 그냥 무작위로 고르면 7장이어도 <b>일곱 번에 한 번은 같은 것이 두 번 연속</b>
            /// 나온다. 초당 다섯 번 치는 소리라 그 한 번이 바로 "변하지 않는다"로 들린다.
            /// </summary>
            [System.NonSerialized] public int LastClip = -1;
        }

        [SerializeField, Tooltip("소리 목록. Demo/16으로 파일 이름에서 자동으로 채울 수 있다")]
        Entry[] entries = new Entry[0];

        public Entry[] Entries => entries;

        /// <summary>
        /// 이름으로 찾는다. 없으면 null — 부르는 쪽이 조용히 넘어가면 된다.
        /// 목록이 짧아(열댓 개) 선형 탐색이 사전보다 싸고 할당도 없다.
        /// </summary>
        public Entry Find(Sfx id)
        {
            for (int i = 0; i < entries.Length; i++)
                if (entries[i] != null && entries[i].id == id) return entries[i];
            return null;
        }
    }

    /// <summary>
    /// 소리가 날 <b>순간</b>들. 음원 파일 이름이 아니라 <b>게임의 사건</b>으로 적는다 —
    /// 음원이 바뀌어도 코드는 그대로다.
    /// </summary>
    public enum Sfx
    {
        None = 0,

        // ── 파괴 ──────────────────────────────────────────
        /// <summary>팔을 휘두르는 순간. 맞기 전이다</summary>
        Punch = 1,
        /// <summary>벽에 맞는 순간. 초당 다섯 번 난다 — 제일 자주 들리는 소리다</summary>
        Strike = 2,
        /// <summary>한 층이 통째로 쏟아진다</summary>
        FloorCollapse = 3,
        /// <summary>건물이 완전히 부서진다</summary>
        Broken = 4,
        /// <summary>
        /// <b>마지막 한 방</b> — 건물을 무너뜨리는 그 타격 [지시 2026-10-16].
        ///
        /// 보통 타격(<see cref="Strike"/>)과 가르는 이유는 역할이 달라서다. 평타는
        /// 초당 다섯 번 나므로 짧고 가벼워야 하고, 이것은 한 판에 몇 번뿐이라
        /// <b>길게 끌려야</b> 한다. 실측해서 고른 음원이 저역 36.5% · 고역 22.1% ·
        /// 감쇠 820ms로, 평타(짧고 날카로움)와 정반대다.
        ///
        /// 번호가 5인 것은 파괴 묶음(1~4) 바로 뒤라서다. <b>기존 번호는 건드리지 않는다</b> —
        /// 번호를 밀면 SoundBank에 박힌 클립 연결이 통째로 어긋난다.
        /// </summary>
        Finisher = 5,
        /// <summary>
        /// <b>연출에 따라붙는 소리</b> [지시 2026-10-16].
        ///
        /// 기획자: "소닉붐과 캐논파이어는 (…) VFX에 따라오는 SFX가 있다면 그거를
        /// 담당하는 소리로 넣고싶어. 물론 지금 VFX가 보이지 않지만 말이야."
        ///
        /// 소리를 셋으로 가르는 마지막 조각이다 —
        /// <see cref="Punch"/>(휘두름) + <see cref="Strike"/>(맞는 곳) + 여기(연출).
        /// 앞의 둘은 '무엇이 어디에 닿았는가'를 말하고, 이것은 <b>그래서 무엇이
        /// 터졌는가</b>를 말한다. 그래서 타격 자체와 섞이지 않고 따로 울려야 한다.
        ///
        /// 소닉붐 7 : 캐논파이어 3 [지시].
        /// </summary>
        VfxBurst = 6,
        /// <summary>
        /// 건물이 <b>무너지는 동안</b> [지시 2026-10-16]. VfxBurst 의 긴 판이다.
        /// 막타와 완파 사이의 공백을 메운다.
        /// </summary>
        CollapseBurst = 7,

        // ── 수집 ──────────────────────────────────────────
        /// <summary>케이블을 펴기 시작한다</summary>
        CableOpen = 10,
        /// <summary>고리가 닫혔는데 아무것도 못 잡았다</summary>
        CableMiss = 11,
        /// <summary>고리가 닫히며 잔해를 잡았다</summary>
        Capture = 12,
        /// <summary>끌던 짐이 건물·토대에 걸렸다 [14번]</summary>
        Snag = 13,
        /// <summary>짐을 내려놓는다</summary>
        Drop = 14,

        // ── 구멍 ──────────────────────────────────────────
        /// <summary>잔해 하나가 구멍에 빨려 들어간다</summary>
        Swallow = 20,
        /// <summary>구멍이 한 단계 커진다</summary>
        HoleLevelUp = 21,
        /// <summary>할당량을 채웠다</summary>
        QuotaDone = 22,

        // ── 조작·UI [지시 2026-10-14] ──────────────────────
        // 반쯤 캐쥬얼로 방향을 잡으며 더했다. 발소리 음원은 2026-10-08부터
        // 폴더에 있었는데 이 목록에 없어서 한 번도 울리지 않고 있었다.
        /// <summary>달릴 때의 발소리. 걸은 거리로 터뜨린다</summary>
        Footstep = 30,
        /// <summary>업그레이드 카드를 골랐다</summary>
        UpgradePick = 31,
        /// <summary>대쉬·케이블 같은 토글 버튼을 눌렀다</summary>
        UiToggle = 32,
    }
}
