using Demo.Data;
using UnityEngine;

namespace Demo.Utils
{
    /// <summary>
    /// 소리를 내는 한 군데 창구 [2026-10-08]. [Bootstrap]에 하나만 붙인다.
    ///
    /// <b>왜 창구가 하나인가.</b> 소리를 내야 하는 곳은 파괴·케이블·구멍·UI로 흩어져 있는데,
    /// 각자 AudioSource를 들면 <b>풀링이 깨지고</b>(CLAUDE.md 규칙 6) 음원 연결도 프리팹마다
    /// 흩어진다. <see cref="GameConfigRef"/>와 같은 꼴로 하나만 두고 전역에서 읽는다.
    ///
    /// <b>이 클래스는 "언제 울릴지"를 정하지 않는다.</b> 그건 게임 쪽 코드가 안다.
    /// 여기는 <b>어떤 음원을 어떻게 울릴지</b>만 맡는다 — 무작위 고르기, 음높이 흔들기,
    /// 같은 소리 연타 막기, 풀에서 자리 얻기.
    ///
    /// 음원이 안 꽂혀 있으면 <b>조용히 넘어간다</b>. 로그로 도배하지 않는다 —
    /// 소리는 마지막에 붙는 것이고, 그 전까지 콘솔이 쓸모없어지면 안 된다.
    /// </summary>
    [DefaultExecutionOrder(-900)]          // GameConfigRef(-1000) 다음
    public sealed class GameAudio : MonoBehaviour
    {
        [SerializeField, Tooltip("게임이 내는 소리 목록. 비면 아무 소리도 안 난다")]
        SoundBank bank;

        [SerializeField, Range(1, 32), Tooltip("동시에 울릴 수 있는 소리의 수. " +
                 "넘치면 가장 오래 울린 것을 뺏는다")]
        int voices = 12;

        [SerializeField, Range(0f, 1f), Tooltip("전체 음량. 0이면 음소거")]
        float master = 1f;

        static GameAudio s_current;
        SfxPool pool;

        /// <summary>어디서든 읽는다. Bootstrap이 깨어나기 전에 부르면 null이다.</summary>
        public static GameAudio Current => s_current;

        /// <summary>
        /// 효과음 전체 음량 0~1 [2026-10-12]. 옵션 화면의 슬라이더가 민다.
        ///
        /// <b>이미 울리고 있는 소리는 안 바뀐다.</b> 음량은 재생할 때 한 번 정해지고,
        /// 풀의 AudioSource를 나중에 건드리면 한 발짜리 소리의 중간에 크기가 변한다.
        /// 슬라이더를 움직이는 동안 들리는 것이 바로 다음 발부터라 그게 자연스럽다.
        /// </summary>
        public float Master
        {
            get => master;
            set => master = Mathf.Clamp01(value);
        }

        /// <summary>실측·디버그용. 지난 프레임까지 울린 횟수.</summary>
        public int PlayedCount { get; private set; }

        /// <summary>
        /// <see cref="SoundBank.Entry.minGap"/>에 막혀 <b>안 울린</b> 횟수 [2026-10-08].
        ///
        /// 씹히는 느낌의 두 원인 중 하나다. 다른 하나는 풀이 음성을 뺏는 것
        /// (<see cref="SfxPool.Steals"/>) — 둘은 증상이 비슷한데 고치는 법이 정반대라
        /// (이쪽은 간격을 줄이고, 저쪽은 음성을 늘리거나 소리를 짧게) 따로 세야 한다.
        /// </summary>
        public int SuppressedCount { get; private set; }

        /// <summary>실측용. 소리를 내는 풀.</summary>
        public SfxPool Pool => pool;

        /// <summary>측정을 다시 시작한다.</summary>
        public void ResetCounts()
        {
            PlayedCount = SuppressedCount = 0;
            if (pool != null) pool.ResetCounts();
        }

        void Awake()
        {
            s_current = this;
            pool = SfxPool.Ensure(transform, voices);
        }

        void OnDestroy()
        {
            if (s_current == this) s_current = null;
        }

        /// <summary>
        /// 한 발 울린다. 창구가 없거나 음원이 없으면 아무 일도 안 일어난다 —
        /// 부르는 쪽이 null 검사를 하지 않아도 되도록 static으로 둔다.
        /// </summary>
        /// <param name="at">소리가 날 자리. 평면(flat) 소리면 무시된다</param>
        /// <param name="volumeScale">그 순간의 세기. 약한 타격은 작게 울린다</param>
        /// <param name="pitchScale">그 한 발의 음높이 배율 [신설 2026-10-16].
        /// 1보다 작으면 낮고 느려져 <b>둔탁해진다</b>. 큰 건물이 무너질 때처럼
        /// "같은 소리인데 더 무겁다"를 만드는 데 쓴다.
        ///
        /// <b>흔드는 것이 아니다.</b> 부르는 쪽이 값을 정해 넘기므로 같은 상황이면
        /// 늘 같은 소리가 난다 [지시 2026-10-16: "랜덤으로 나오는게 아니라
        /// 조율이 되어서 나왔으면 해"].</param>
        public static void Play(Sfx id, Vector3 at, float volumeScale = 1f,
                                float pitchScale = 1f)
        {
            GameAudio a = s_current;
            if (a != null) a.PlayHere(id, at, volumeScale, pitchScale);
        }

        /// <summary>
        /// <b>이것 하나만 듣는다</b> [신설 2026-10-16]. None 이면 꺼진 것이다.
        ///
        /// 기획자: "일단은 VfxBurst 사운드를 제외하고 다 꺼봐줄 수 있나?"
        /// 소리가 겹쳐 있으면 어느 것이 박자가 어긋났는지 가릴 수가 없다.
        /// 하나만 남기면 그 소리의 타이밍만 들린다.
        ///
        /// <b>SoundBank 의 음량을 건드리지 않는다.</b> 0 으로 적어 끄면 기획자가
        /// 맞춰 둔 값이 지워지고 되돌릴 때 기억에 의존하게 된다. 여기서 거르면
        /// 원래 값이 그대로 남는다.
        /// </summary>
        /// <b>지금은 VfxBurst 로 켜 둔다</b> [지시 2026-10-16] — 그것만 듣고 박자를
        /// 보기 위해서다. <b>F8 로 끌 수 있고</b>, 확인이 끝나면 None 으로 되돌릴 것.
        public static Sfx Solo
        {
            get => s_solo;
            set
            {
                s_solo = value;
                // <b>배경음과 환경음도 같이 끈다</b> [지시 2026-10-16].
                // 하나만 들으려는 것인데 음악이 깔려 있으면 여운이 그 위에 묻힌다.
                MusicPlayer m = MusicPlayer.Current;
                if (m != null) m.Volume = value == Sfx.None ? s_musicWas : 0f;
                foreach (AudioSource src in Object.FindObjectsByType<AudioSource>(
                             FindObjectsSortMode.None))
                    if (src.loop) src.mute = value != Sfx.None;   // 루프 = 환경음
            }
        }

        static Sfx s_solo = Sfx.VfxBurst;

        /// <summary>솔로를 켜기 전의 배경음 크기. 끌 때 되돌린다.</summary>
        static float s_musicWas = 0.35f;

        void PlayHere(Sfx id, Vector3 at, float volumeScale, float pitchScale)
        {
            if (bank == null || pool == null || master <= 0f) return;
            if (s_solo != Sfx.None && id != s_solo) return;

            SoundBank.Entry e = bank.Find(id);
            if (e == null || e.clips == null || e.clips.Length == 0) return;

            // 같은 소리가 한 프레임에 겹쳐 울리면 한 발보다 오히려 작게 들린다(위상 간섭).
            // 두드림처럼 잦은 소리에서 실제로 생기는 일이라 간격을 막아 둔다.
            float now = Time.unscaledTime;
            if (e.minGap > 0f && now - e.LastAt < e.minGap) { SuppressedCount++; return; }
            e.LastAt = now;

            // pitchJitter 는 전부 0 이다 [지시 2026-10-15]. 남겨 둔 것은 에셋에
            // 값이 박혀 있어서지 쓰라는 뜻이 아니다. 음높이를 바꾸는 것은
            // 부르는 쪽이 넘기는 pitchScale 뿐이다.
            float pitch = (1f + Random.Range(-e.pitchJitter, e.pitchJitter))
                        * Mathf.Clamp(pitchScale, 0.25f, 4f);
            // 타격 화면에서는 무조건 플레이어 기준 가운데에서 낸다 [지시 2026-10-14].
            // 1인칭으로 붙어 치는 화면이라 소리가 좌우로 치우치면 내가 치는 것 같지 않다.
            bool flat = e.flat || InDemolition();
            pool.Play(e.clips[NextClip(e)], at, e.volume * volumeScale * master, pitch, flat);
            PlayedCount++;
        }

        /// <summary>
        /// 다음에 울릴 음원의 번호. <b>직전에 쓴 것은 안 고른다</b> [지시 2026-10-14].
        ///
        /// <b>왜 그냥 무작위로 하면 안 되는가.</b> 7장을 매번 똑같이 무작위로 고르면
        /// <b>일곱 번에 한 번은 같은 것이 두 번 연속</b> 나온다. 초당 다섯 번 치는
        /// 소리라서 그 한 번이 바로 "사운드 변함이 안 느껴진다"로 들린다.
        ///
        /// 남은 것 중에서만 고른다 — 7장이면 6장 중 하나다. 그래서 <b>칠 때마다
        /// 반드시 다른 소리</b>가 난다.
        /// </summary>
        static int NextClip(SoundBank.Entry e)
        {
            int n = e.clips.Length;
            if (n <= 1) return 0;

            // 직전 것을 빼고 뽑는다. 뽑은 번호가 직전 이상이면 하나 밀어 건너뛴다 —
            // 이렇게 하면 다시 뽑는 반복문 없이 한 번에 균등하게 고를 수 있다.
            int k = Random.Range(0, n - 1);
            if (e.LastClip >= 0 && k >= e.LastClip) k++;
            return e.LastClip = k % n;
        }

        /// <summary>
        /// 타격 화면인가. 한 번만 찾아 들고 있는다 — 소리마다 Find를 돌릴 수는 없다.
        /// </summary>
        bool InDemolition()
        {
            if (!lookedForMode) { lookedForMode = true; mode = Core.GameModeController.Instance; }
            return mode != null && mode.IsDemolition;
        }

        Core.GameModeController mode;
        bool lookedForMode;

        /// <summary>실측용. 지금 물려 있는 목록.</summary>
        public SoundBank Bank { get => bank; set => bank = value; }
    }
}
