using UnityEngine;

namespace Demo.Utils
{
    /// <summary>
    /// 배경음악 [2026-10-08]. <see cref="GameAudio"/>와 같은 오브젝트에 붙는다.
    ///
    /// <b>왜 SfxPool로 못 하는가.</b> 효과음은 한 발 울리고 끝나지만 음악은 <b>이어져야</b> 한다.
    /// 풀은 가장 오래 울린 것을 뺏어 쓰므로, 거기에 음악을 넣으면 타격음 몇 번에 잘린다.
    /// 전용 AudioSource 두 개를 따로 둔다.
    ///
    /// <b>왜 두 개인가.</b> 필드와 파괴 화면의 곡이 다르고, 그 사이를 <b>겹쳐 넘겨야</b> 한다.
    /// 하나로 하면 끊고 새로 트는 수밖에 없어서 전환이 툭 끊긴다. 하나가 줄어드는 동안
    /// 다른 하나가 올라온다 (crossfade).
    ///
    /// <b>이 클래스는 "언제 어떤 곡인가"를 안 정한다.</b> 그건 <see cref="Core.GameMode"/>를
    /// 아는 쪽이 정한다. 여기는 넘기는 일만 맡는다 — <see cref="GameAudio"/>와 같은 태도다.
    /// </summary>
    [DefaultExecutionOrder(-890)]            // GameAudio(-900) 다음
    public sealed class MusicPlayer : MonoBehaviour
    {
        [SerializeField, Tooltip("필드에서 흐르는 곡. 비면 조용하다")]
        AudioClip field;

        [SerializeField, Tooltip("파괴 화면에서 흐르는 곡. 비면 필드 곡을 그대로 쓴다")]
        AudioClip demolition;

        [SerializeField, Range(0f, 1f), Tooltip("음악 음량. 효과음보다 한참 작아야 한다 — " +
                 "타격음이 초당 다섯 번 울리는 게임이라 음악이 크면 그게 다 묻힌다")]
        float volume = 0.35f;

        [SerializeField, Range(0.1f, 4f), Tooltip("곡을 바꿀 때 겹쳐 넘기는 시간(초)")]
        float crossfade = 1.2f;

        static MusicPlayer s_current;
        AudioSource a, b;
        bool onA;
        AudioClip wanted;

        public static MusicPlayer Current => s_current;

        /// <summary>실측용. 지금 흐르는 곡.</summary>
        public AudioClip Playing => wanted;

        void Awake()
        {
            s_current = this;
            a = Make("MusicA");
            b = Make("MusicB");
        }

        void OnDestroy()
        {
            if (s_current == this) s_current = null;
        }

        AudioSource Make(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = true;
            src.volume = 0f;
            src.spatialBlend = 0f;        // 음악은 자리가 없다. 2D다
            return src;
        }

        void Start() => Play(field);

        /// <summary>
        /// 이 곡으로 넘어간다. 이미 그 곡이면 아무 일도 안 한다 —
        /// 상태가 자주 바뀌는 쪽에서 매 프레임 불러도 안전해야 한다.
        /// </summary>
        public void Play(AudioClip clip)
        {
            if (clip == wanted) return;
            wanted = clip;

            AudioSource next = onA ? b : a;
            onA = !onA;

            if (clip != null)
            {
                next.clip = clip;
                next.volume = 0f;
                next.Play();
            }
        }

        /// <summary>파괴 화면으로 들어가고 나올 때 부른다. 곡이 없으면 필드 곡을 그대로 쓴다.</summary>
        public void SetDemolition(bool on)
        {
            Play(on && demolition != null ? demolition : field);
        }

        void Update()
        {
            // 들어오는 쪽은 올리고 나가는 쪽은 내린다. 다 내려간 것은 멈춘다.
            float step = crossfade > 0f ? Time.unscaledDeltaTime / crossfade : 1f;
            Fade(onA ? a : b, wanted, step);
            Fade(onA ? b : a, null, step);
        }

        void Fade(AudioSource src, AudioClip toward, float step)
        {
            if (src == null) return;

            bool rising = toward != null && src.clip == toward;
            float target = rising ? volume : 0f;
            src.volume = Mathf.MoveTowards(src.volume, target, step * Mathf.Max(0.01f, volume));

            if (!rising && src.volume <= 0.001f && src.isPlaying) src.Stop();
        }
    }
}
