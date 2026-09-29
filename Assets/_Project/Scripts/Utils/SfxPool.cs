using UnityEngine;

namespace Demo.Utils
{
    /// <summary>
    /// 한 번 울리는 효과음을 <b>돌려 쓰는</b> 재생기 (CLAUDE.md 규칙 6).
    ///
    /// <see cref="AudioSource.PlayClipAtPoint"/>는 쓰지 않는다. 그 함수는 호출할 때마다
    /// GameObject를 하나 만들고 끝나면 버린다 — 초당 다섯 번 두드리는 이 게임에서는
    /// 그게 곧 <b>초당 다섯 번의 할당과 파괴</b>라, 모바일에서 GC가 튀는 전형적인 자리다.
    ///
    /// 대신 AudioSource를 미리 줄지어 두고 돌아가며 쓴다. 다 쓰면 <b>가장 오래 울린 것을
    /// 뺏는다</b> — 소리가 겹칠 때 새 타격음이 안 나는 것보다, 끝나가던 잔향이 끊기는 쪽이
    /// 덜 눈에 띈다.
    /// </summary>
    public sealed class SfxPool : MonoBehaviour
    {
        AudioSource[] voices;
        float[] startedAt;
        int next;

        /// <summary>
        /// 이 부모 밑에 풀을 만들어 둔다. 이미 있으면 그것을 돌려준다 —
        /// 부르는 쪽이 있는지 없는지 신경 쓰지 않아도 된다.
        /// </summary>
        public static SfxPool Ensure(Transform parent, int voiceCount)
        {
            var found = parent.GetComponentInChildren<SfxPool>();
            if (found != null) return found;

            var go = new GameObject("SfxPool");
            go.transform.SetParent(parent, false);

            var pool = go.AddComponent<SfxPool>();
            pool.Build(Mathf.Max(1, voiceCount));
            return pool;
        }

        void Build(int count)
        {
            voices = new AudioSource[count];
            startedAt = new float[count];

            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Voice" + i);
                go.transform.SetParent(transform, false);

                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                // 거리에 따라 줄어야 한다. 파괴 화면은 벽에 바짝 붙어 있고 필드는 멀다.
                src.spatialBlend = 1f;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 3f;
                src.maxDistance = 40f;

                voices[i] = src;
                startedAt[i] = float.NegativeInfinity;
            }
        }

        /// <summary>
        /// 한 발 울린다. <paramref name="clip"/>이 비어 있으면 조용히 넘어간다 —
        /// 기획자가 아직 음원을 안 꽂았을 때 로그가 도배되지 않도록.
        /// </summary>
        public void Play(AudioClip clip, Vector3 at, float volume = 1f, float pitch = 1f)
        {
            if (clip == null || voices == null) return;

            AudioSource src = Take();
            src.transform.position = at;
            src.clip = clip;
            src.volume = Mathf.Clamp01(volume);
            src.pitch = Mathf.Max(0.01f, pitch);
            src.Play();
        }

        /// <summary>여러 개 중 하나를 무작위로. 같은 소리가 반복되면 기계처럼 들린다.</summary>
        public void PlayAny(AudioClip[] clips, Vector3 at, float volume = 1f, float pitch = 1f)
        {
            if (clips == null || clips.Length == 0) return;
            Play(clips[Random.Range(0, clips.Length)], at, volume, pitch);
        }

        /// <summary>
        /// 쉬고 있는 것을 먼저 주고, 없으면 <b>가장 오래전에 시작한 것</b>을 뺏는다.
        /// 단순한 순번 돌리기로 하면 방금 시작한 소리를 끊어 먹는 일이 생긴다.
        /// </summary>
        AudioSource Take()
        {
            for (int i = 0; i < voices.Length; i++)
            {
                int k = (next + i) % voices.Length;
                if (voices[k].isPlaying) continue;

                next = (k + 1) % voices.Length;
                startedAt[k] = Time.unscaledTime;
                return voices[k];
            }

            int oldest = 0;
            for (int i = 1; i < voices.Length; i++)
                if (startedAt[i] < startedAt[oldest]) oldest = i;

            startedAt[oldest] = Time.unscaledTime;
            return voices[oldest];
        }
    }
}
