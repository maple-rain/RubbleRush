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

        /// <summary>실측·디버그용. 지난 프레임까지 울린 횟수.</summary>
        public int PlayedCount { get; private set; }

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
        public static void Play(Sfx id, Vector3 at, float volumeScale = 1f)
        {
            GameAudio a = s_current;
            if (a != null) a.PlayHere(id, at, volumeScale);
        }

        void PlayHere(Sfx id, Vector3 at, float volumeScale)
        {
            if (bank == null || pool == null || master <= 0f) return;

            SoundBank.Entry e = bank.Find(id);
            if (e == null || e.clips == null || e.clips.Length == 0) return;

            // 같은 소리가 한 프레임에 겹쳐 울리면 한 발보다 오히려 작게 들린다(위상 간섭).
            // 두드림처럼 잦은 소리에서 실제로 생기는 일이라 간격을 막아 둔다.
            float now = Time.unscaledTime;
            if (e.minGap > 0f && now - e.LastAt < e.minGap) return;
            e.LastAt = now;

            float pitch = 1f + Random.Range(-e.pitchJitter, e.pitchJitter);
            pool.PlayAny(e.clips, at, e.volume * volumeScale * master, pitch, e.flat);
            PlayedCount++;
        }

        /// <summary>실측용. 지금 물려 있는 목록.</summary>
        public SoundBank Bank { get => bank; set => bank = value; }
    }
}
