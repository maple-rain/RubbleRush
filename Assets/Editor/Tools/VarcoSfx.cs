using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// VARCO Sound로 효과음을 <b>일괄 생성</b>해 프로젝트에 떨군다 (CLAUDE.md 규칙 8).
    ///
    /// <b>왜 콜백으로 저장하지 않는가.</b> 처음에는 Task가 끝나면 <c>EditorApplication.delayCall</c>로
    /// 저장하게 만들었는데, 한 개도 안 떨어졌다. 유니티 <b>에디터는 포커스가 없으면 틱을 안 돈다</b> —
    /// 생성은 끝났는데 저장 콜백이 실행될 기회를 영영 못 받는다. 창을 눌러 두면 되지만,
    /// 그러면 이 툴이 "사람이 지켜봐야 도는 툴"이 된다.
    ///
    /// 그래서 완료 여부를 <b>물어보는</b> 구조로 바꿨다. 요청은 <see cref="pending"/>에 쌓아 두고,
    /// <see cref="Collect"/>를 부를 때 끝난 것만 거둬 저장한다. 메뉴로 눌러도 되고 외부에서
    /// 호출해도 되므로 포커스와 무관하다.
    ///
    /// <b>리플렉션을 쓰는 이유.</b> VARCOSound.Editor.dll을 직접 참조하면 그 에셋을 지우는
    /// 순간 이 파일이 컴파일 에러를 내고 게임 코드 전체가 같이 멈춘다.
    ///
    /// <b>주의:</b> 생성이 도는 중에 스크립트를 고쳐 재컴파일하면 AppDomain이 갈리면서
    /// 대기 중이던 Task가 통째로 사라진다. 실패 로그도 안 남아 "느린 것"과 구분이 안 된다.
    /// </summary>
    public static class VarcoSfx
    {
        const string OutputFolder = "Assets/_Project/Audio/SFX";

        /// <summary>
        /// 만들 소리. 이름이 곧 파일명, 숫자는 <b>변형 개수</b>다.
        /// 타격음만 변형을 여럿 두는 이유는 초당 다섯 번 울리기 때문이다 —
        /// 한 가지 소리를 그 속도로 반복하면 기계 소리로 들린다.
        /// </summary>
        static readonly (string name, string prompt, int count)[] Wanted =
        {
            ("Strike",
             "A heavy metal robot fist slamming into a solid concrete wall. One single short dry impact, " +
             "deep thud with a sharp crack, small bits of concrete crumbling away. No music, no reverb tail.",
             4),

            ("FloorCollapse",
             "An entire concrete floor of a building giving way at once. A deep heavy crunch followed by " +
             "a short avalanche of falling concrete slabs and rubble. Powerful and low. No music.",
             2),

            ("Broken",
             "A derelict concrete building finally collapsing into a pile of rubble. A huge low boom, " +
             "a long crumbling roar of falling debris, settling dust. Heavy and final. No music.",
             2),

            // ── 아래 아홉은 아직 안 만들었다 [2026-10-08]. 이름은 Demo.Data.Sfx와 같아야
            //    Demo/16이 알아서 꽂는다 (SFX_<이름>_NN.wav 규칙).
            ("Punch",
             "A large hydraulic robot arm swinging fast through the air. A short heavy servo whirr " +
             "and a low air whoosh, metal joints under load. No impact at the end. No music.",
             3),

            ("CableOpen",
             "A steel cable spool releasing. A short mechanical clack followed by wire unspooling " +
             "quickly, metallic and taut. Industrial. No music.",
             2),

            ("CableMiss",
             "A steel cable loop closing on empty ground. A dull slack snap, a brief scrape of wire " +
             "on dirt, no tension. Disappointing and short. No music.",
             2),

            ("Capture",
             "A steel net cinching tight around a pile of concrete rubble. A sharp wire tightening snap " +
             "followed by rubble shifting and settling against the mesh. Satisfying. No music.",
             3),

            ("Snag",
             "Heavy rubble being dragged catches hard on a concrete edge. A jarring scrape of stone on " +
             "stone with a metallic cable strain. Short and rough. No music.",
             2),

            ("Drop",
             "A load of concrete rubble released from a net onto bare ground. A dull collective thud and " +
             "stones tumbling apart. Short. No music.",
             2),

            ("Swallow",
             "A single chunk of concrete falling into a deep pit. A short tumble on the rim, then a " +
             "distant hollow thud from below. Dry, no reverb tail. No music.",
             3),

            ("HoleLevelUp",
             "A pit in the ground widening. A deep earthy rumble of soil and concrete giving way, " +
             "rising into a short bright tonal swell that feels like a reward. No voice.",
             2),

            ("QuotaDone",
             "A short triumphant industrial fanfare. Heavy machinery stamping a completion, a bright " +
             "metallic chime over a low mechanical thump. Celebratory but brief. No voice.",
             2),
        };

        static readonly List<(string name, Task task)> pending = new List<(string, Task)>();

        /// <summary>아직 안 거둔 요청 수. 외부에서 진행 상황을 묻는 창구다.</summary>
        public static int Pending => pending.Count;

        [MenuItem("Demo/6 VARCO 효과음 — 생성 요청")]
        public static void Request()
        {
            MethodInfo generate = FindGenerateMethod();
            if (generate == null)
            {
                Debug.LogError("[VarcoSfx] ApiClient.GenerateAudioFromText를 못 찾았다. " +
                               "VARCO Sound 에셋과 로그인 상태를 확인할 것.");
                return;
            }

            Directory.CreateDirectory(OutputFolder);

            foreach (var item in Wanted)
            {
                // <b>이미 있는 것은 다시 안 만든다</b> [2026-10-08]. 생성은 크레딧을 쓴다 —
                // 아홉 개를 더 받으려고 눌렀는데 이미 만든 셋까지 같이 태우면 안 된다.
                // 다시 만들고 싶으면 그 파일을 지우고 누르면 된다.
                if (File.Exists(Path.Combine(OutputFolder, $"SFX_{item.name}_01.wav")))
                {
                    Debug.Log($"[VarcoSfx] '{item.name}'은 이미 있다 — 건너뛴다.");
                    continue;
                }

                try
                {
                    var t = generate.Invoke(null, new object[] { item.prompt, item.count }) as Task;
                    if (t == null) { Debug.LogError($"[VarcoSfx] '{item.name}' 반환값이 Task가 아니다."); continue; }
                    pending.Add((item.name, t));
                }
                catch (Exception e)
                {
                    Debug.LogError($"[VarcoSfx] '{item.name}' 요청 실패: {e.GetBaseException().Message}");
                }
            }

            Debug.Log($"[VarcoSfx] {pending.Count}건 요청. 끝나면 'Demo/7 VARCO 효과음 — 결과 거두기'를 누를 것.");
        }

        /// <summary>
        /// 끝난 것만 저장하고 목록에서 뺀다. 아직 도는 것은 그대로 둔다 —
        /// 몇 번을 눌러도 안전하다.
        /// </summary>
        [MenuItem("Demo/7 VARCO 효과음 — 결과 거두기")]
        public static void CollectMenu() => Debug.Log(Collect());

        public static string Collect()
        {
            if (pending.Count == 0) return "[VarcoSfx] 거둘 요청이 없다.";

            int saved = 0, stillRunning = 0, failed = 0;

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                (string name, Task task) = pending[i];

                if (!task.IsCompleted) { stillRunning++; continue; }
                pending.RemoveAt(i);

                if (task.Exception != null)
                {
                    Debug.LogError($"[VarcoSfx] '{name}' 생성 실패: {task.Exception.GetBaseException().Message}");
                    failed++;
                    continue;
                }

                saved += Save(name, task.GetType().GetProperty("Result")?.GetValue(task));
            }

            if (saved > 0) AssetDatabase.Refresh();
            return $"[VarcoSfx] 저장 {saved}개 · 진행중 {stillRunning}건 · 실패 {failed}건";
        }

        static int Save(string name, object result)
        {
            var clips = new List<AudioClip>();
            if (result is AudioClip one) clips.Add(one);
            else if (result is IEnumerable<AudioClip> many) clips.AddRange(many);

            if (clips.Count == 0)
            {
                Debug.LogWarning($"[VarcoSfx] '{name}' 결과에 클립이 없다 ({result?.GetType().Name ?? "null"}).");
                return 0;
            }

            int saved = 0;
            for (int i = 0; i < clips.Count; i++)
            {
                if (clips[i] == null) continue;

                string file = clips.Count > 1 ? $"SFX_{name}_{i + 1:00}.wav" : $"SFX_{name}.wav";
                string path = $"{OutputFolder}/{file}";

                try { File.WriteAllBytes(path, EncodeWav(clips[i])); saved++; }
                catch (Exception e) { Debug.LogError($"[VarcoSfx] '{file}' 저장 실패: {e.Message}"); }
            }
            return saved;
        }

        static MethodInfo FindGenerateMethod()
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name != "VARCOSound.Editor") continue;
                return a.GetType("VARCOSound.Editor.API.ApiClient")?.GetMethod(
                    "GenerateAudioFromText",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            }
            return null;
        }

        /// <summary>
        /// AudioClip을 16비트 PCM WAV 한 덩어리로 포장한다. 유니티에는 AudioClip을 파일로
        /// 쓰는 기능이 없다 — 재생만 되고 저장이 안 되므로 PCM을 직접 RIFF로 싼다.
        ///
        /// 32767이 아니라 <b>32766</b>을 곱하는 이유는, 정확히 1.0인 샘플이 넘쳐서
        /// 반대 부호로 감기는 것을 막기 위해서다. 그 한 샘플이 딱 소리로 들린다.
        /// </summary>
        static byte[] EncodeWav(AudioClip clip)
        {
            var samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);

            int dataBytes = samples.Length * 2;
            using (var mem = new MemoryStream(44 + dataBytes))
            using (var w = new BinaryWriter(mem))
            {
                w.Write(new[] { 'R', 'I', 'F', 'F' });
                w.Write(36 + dataBytes);
                w.Write(new[] { 'W', 'A', 'V', 'E' });

                w.Write(new[] { 'f', 'm', 't', ' ' });
                w.Write(16);
                w.Write((short)1);                             // 1 = 무압축 PCM
                w.Write((short)clip.channels);
                w.Write(clip.frequency);
                w.Write(clip.frequency * clip.channels * 2);
                w.Write((short)(clip.channels * 2));
                w.Write((short)16);

                w.Write(new[] { 'd', 'a', 't', 'a' });
                w.Write(dataBytes);

                for (int i = 0; i < samples.Length; i++)
                    w.Write((short)(Mathf.Clamp(samples[i], -1f, 1f) * 32766f));

                w.Flush();
                return mem.ToArray();
            }
        }
    }
}
