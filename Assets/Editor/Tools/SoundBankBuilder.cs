using System.Collections.Generic;
using System.IO;
using System.Text;
using Demo.Data;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 음원 파일 이름을 보고 <see cref="SoundBank"/>를 채운다 [2026-10-08].
    ///
    /// <b>왜 손으로 안 끄는가.</b> 소리 이름이 열두 개이고 음원은 더 늘어난다.
    /// 인스펙터에서 끌어다 놓으면 음원을 새로 받을 때마다 다시 해야 하고, 빠뜨려도
    /// <b>아무 에러가 안 난다</b> — 그냥 그 소리만 조용하다. 회수 기간이 하루도 안 된다
    /// (CLAUDE.md 규칙 8).
    ///
    /// <b>규칙</b>: <c>Assets/_Project/Audio/SFX/SFX_&lt;이름&gt;_NN.wav</c>에서 &lt;이름&gt;이
    /// <see cref="Sfx"/>의 항목과 같으면(대소문자 무시) 그 자리에 들어간다.
    /// 번호는 변형이고, 재생할 때 그중 하나가 무작위로 뽑힌다.
    ///
    /// <b>이 도구는 음량·음높이를 안 건드린다.</b> 한 번 정한 값은 다시 돌려도 남는다 —
    /// 기획자가 맞춘 것을 자동화가 쓸어버리면 안 된다. 새로 생긴 항목에만 기본값을 넣는다.
    /// </summary>
    public static class SoundBankBuilder
    {
        const string SfxDir = "Assets/_Project/Audio/SFX";
        const string BankPath = "Assets/_Project/Data/Config/SoundBank.asset";

        [MenuItem("Demo/16. 효과음 연결 — 파일 이름으로", priority = 70)]
        public static void Build()
        {
            SoundBank bank = AssetDatabase.LoadAssetAtPath<SoundBank>(BankPath);
            if (bank == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(BankPath));
                bank = ScriptableObject.CreateInstance<SoundBank>();
                AssetDatabase.CreateAsset(bank, BankPath);
                Debug.Log($"[효과음] {BankPath}를 새로 만들었다.");
            }

            var byId = new Dictionary<Sfx, List<AudioClip>>();
            var orphans = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { SfxDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) continue;

                if (!TryName(clip.name, out Sfx id)) { orphans.Add(clip.name); continue; }
                if (!byId.TryGetValue(id, out var list)) byId[id] = list = new List<AudioClip>();
                list.Add(clip);
            }

            Write(bank, byId);
            Report(bank, byId, orphans);
        }

        /// <summary><c>SFX_Strike_02</c> → <see cref="Sfx.Strike"/>. 못 읽으면 false.</summary>
        static bool TryName(string fileName, out Sfx id)
        {
            id = Sfx.None;
            string n = fileName;
            if (n.StartsWith("SFX_", System.StringComparison.OrdinalIgnoreCase)) n = n.Substring(4);

            // 끝의 _01 같은 변형 번호를 뗀다
            int tail = n.LastIndexOf('_');
            if (tail > 0 && int.TryParse(n.Substring(tail + 1), out _)) n = n.Substring(0, tail);

            foreach (Sfx s in System.Enum.GetValues(typeof(Sfx)))
            {
                if (s == Sfx.None) continue;
                if (string.Equals(s.ToString(), n, System.StringComparison.OrdinalIgnoreCase))
                { id = s; return true; }
            }
            return false;
        }

        /// <summary>
        /// 음원만 갈아 끼운다. 기존 항목의 음량·음높이·간격은 그대로 둔다 —
        /// 자동화가 기획자의 조정을 지우면 다시는 안 쓰게 된다.
        /// </summary>
        static void Write(SoundBank bank, Dictionary<Sfx, List<AudioClip>> byId)
        {
            var so = new SerializedObject(bank);
            SerializedProperty arr = so.FindProperty("entries");

            // <b>enumValueIndex가 아니라 intValue를 쓴다</b> [고침 2026-10-08].
            // enumValueIndex는 <b>선언 순서</b>지 enum 값이 아니다. Sfx는 값을 10·20으로
            // 띄워 놓아서(묶음을 가르려고) 둘이 어긋난다 — 처음에 그걸로 썼다가
            // CableOpen이 Swallow 자리에 들어가고 뒤쪽이 전부 QuotaDone이 됐다.
            var seen = new Dictionary<Sfx, int>();
            for (int i = arr.arraySize - 1; i >= 0; i--)
            {
                var id0 = (Sfx)arr.GetArrayElementAtIndex(i).FindPropertyRelative("id").intValue;
                if (!System.Enum.IsDefined(typeof(Sfx), id0) || id0 == Sfx.None || seen.ContainsKey(id0))
                { arr.DeleteArrayElementAtIndex(i); continue; }   // 모르는 값과 중복은 버린다
                seen[id0] = i;
            }
            seen.Clear();
            for (int i = 0; i < arr.arraySize; i++)
                seen[(Sfx)arr.GetArrayElementAtIndex(i).FindPropertyRelative("id").intValue] = i;

            foreach (Sfx id in System.Enum.GetValues(typeof(Sfx)))
            {
                if (id == Sfx.None) continue;

                if (!seen.TryGetValue(id, out int at))
                {
                    at = arr.arraySize;
                    arr.InsertArrayElementAtIndex(at);
                    SerializedProperty fresh = arr.GetArrayElementAtIndex(at);
                    fresh.FindPropertyRelative("id").intValue = (int)id;
                    fresh.FindPropertyRelative("clips").arraySize = 0;
                    fresh.FindPropertyRelative("volume").floatValue = 1f;
                    fresh.FindPropertyRelative("pitchJitter").floatValue = Jitter(id);
                    fresh.FindPropertyRelative("minGap").floatValue = Gap(id);
                    fresh.FindPropertyRelative("flat").boolValue = Flat(id);
                    seen[id] = at;
                }

                SerializedProperty clips = arr.GetArrayElementAtIndex(at).FindPropertyRelative("clips");
                byId.TryGetValue(id, out List<AudioClip> found);
                clips.arraySize = found != null ? found.Count : 0;
                for (int i = 0; i < clips.arraySize; i++)
                    clips.GetArrayElementAtIndex(i).objectReferenceValue = found[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();
        }

        // 아래 셋은 <b>[제안·미승인]</b>이다. 새 항목이 생길 때의 출발값일 뿐,
        // 기획자가 한 번 고치면 이 도구는 다시 안 덮는다.
        static float Jitter(Sfx id) => id == Sfx.Strike ? 0.12f : 0.06f;
        static float Gap(Sfx id) => id == Sfx.Swallow ? 0.05f : id == Sfx.Strike ? 0.03f : 0f;
        static bool Flat(Sfx id) => id == Sfx.HoleLevelUp || id == Sfx.QuotaDone;

        static void Report(SoundBank bank, Dictionary<Sfx, List<AudioClip>> byId, List<string> orphans)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"효과음 연결 — {BankPath}");
            sb.AppendLine();

            int have = 0, missing = 0;
            sb.AppendLine("  소리 이름            음원  상태");
            foreach (Sfx id in System.Enum.GetValues(typeof(Sfx)))
            {
                if (id == Sfx.None) continue;
                byId.TryGetValue(id, out List<AudioClip> list);
                int n = list != null ? list.Count : 0;
                if (n > 0) have++; else missing++;
                sb.AppendLine(string.Format("  {0,-20} {1,3}개  {2}", id, n,
                    n > 0 ? "O" : "X — SFX_" + id + "_01.wav 가 없다"));
            }

            sb.AppendLine();
            sb.AppendLine($"연결됨 {have} · 비어 있음 {missing}. " +
                          "비어 있는 것은 그냥 조용하다 — 에러가 안 나므로 이 표로만 보인다.");
            if (orphans.Count > 0)
                sb.AppendLine("이름을 못 읽은 파일: " + string.Join(", ", orphans) +
                              "  (SFX_<Sfx이름>_01.wav 꼴이어야 한다)");

            if (missing > 0) Debug.LogWarning(sb.ToString()); else Debug.Log(sb.ToString());
        }
    }
}
