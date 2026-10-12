using UnityEngine;

namespace Demo.Utils
{
    /// <summary>
    /// 효과음·음악 음량을 기억한다 [2026-10-12]. 옵션 화면이 읽고 쓴다.
    ///
    /// <b>이름이 왜 VolumeSettings인가.</b> <c>AudioSettings</c>는 유니티가 이미 쓰는 이름이라
    /// (<see cref="UnityEngine.AudioSettings"/>) 같은 이름을 쓰면 <c>using UnityEngine;</c>이
    /// 들어간 파일마다 모호해진다.
    ///
    /// <b>PlayerPrefs를 쓴다.</b> 값이 둘뿐이고 폰에서 앱을 껐다 켜도 남아야 한다 —
    /// 이만한 일에 저장 포맷을 만들 이유가 없다. 설정이 늘면 그때 옮긴다.
    ///
    /// <b>static으로 읽을 수 있게 둔다.</b> 옵션 화면은 타이틀에도 있고 나중에 일시정지에도
    /// 생길 텐데, 둘이 각자 PlayerPrefs를 만지면 한쪽이 쓴 값을 다른 쪽이 모른다.
    /// </summary>
    [DefaultExecutionOrder(-880)]        // GameAudio(-900) · MusicPlayer(-890) 다음
    public sealed class VolumeSettings : MonoBehaviour
    {
        const string SfxKey = "vol.sfx";
        const string MusicKey = "vol.music";

        /// <summary>처음 켤 때의 값. 음악은 효과음보다 낮다 — 타격음이 초당 다섯 번 울린다.</summary>
        const float SfxDefault = 1.0f;
        const float MusicDefault = 0.35f;

        static VolumeSettings s_current;

        [SerializeField, Range(0f, 1f)] float sfx = SfxDefault;
        [SerializeField, Range(0f, 1f)] float music = MusicDefault;

        public static VolumeSettings Current => s_current;

        /// <summary>효과음 음량 0~1. 넣으면 바로 들린다.</summary>
        public float Sfx
        {
            get => sfx;
            set { sfx = Mathf.Clamp01(value); Apply(); }
        }

        /// <summary>음악 음량 0~1. 넣으면 바로 들린다.</summary>
        public float Music
        {
            get => music;
            set { music = Mathf.Clamp01(value); Apply(); }
        }

        void Awake()
        {
            s_current = this;
            sfx = PlayerPrefs.GetFloat(SfxKey, SfxDefault);
            music = PlayerPrefs.GetFloat(MusicKey, MusicDefault);
            Apply();
        }

        void OnDestroy()
        {
            if (s_current == this) s_current = null;
        }

        /// <summary>
        /// 값을 실제 소리에 민다. <b>없으면 조용히 넘어간다</b> — 소리 쪽이 아직 안 깨어났을 수도
        /// 있고, 그때 에러를 내면 콘솔만 더러워진다 (<see cref="GameAudio"/>와 같은 태도다).
        /// </summary>
        public void Apply()
        {
            if (GameAudio.Current != null) GameAudio.Current.Master = sfx;
            if (MusicPlayer.Current != null) MusicPlayer.Current.Volume = music;
        }

        /// <summary>
        /// 디스크에 쓴다. <b>슬라이더를 끄는 동안에는 안 부른다</b> —
        /// <see cref="PlayerPrefs.Save"/>는 파일을 쓰므로 매 프레임 부르면 끊긴다.
        /// 옵션 화면을 닫을 때 한 번만 부른다.
        /// </summary>
        public void Save()
        {
            PlayerPrefs.SetFloat(SfxKey, sfx);
            PlayerPrefs.SetFloat(MusicKey, music);
            PlayerPrefs.Save();
        }

        /// <summary>인스펙터에서 끌어도 소리가 따라가게 한다. 에디터에서만 돈다.</summary>
        void OnValidate()
        {
            if (Application.isPlaying) Apply();
        }
    }
}
