using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 펀치 타이밍을 CSV 한 장으로 모아 조절한다 [지시 2026-09-30].
    ///
    /// <b>왜 필요한가.</b> 펀치 타이밍이 <b>세 군데</b>에 흩어져 있다 —
    /// 컨트롤러(재생 배속·전이 시간), <c>PlayerAnimator</c>(정점 위치·감속·러쉬),
    /// <c>DestructionFX</c>(정지 배수·기다림). 한 박자를 바꾸려면 세 곳을 다 찾아야 했고,
    /// 서로 물려 있어서 한쪽만 고치면 주먹과 연출이 어긋난다.
    ///
    /// <b>값이 서로 어떻게 물려 있는가</b> — 이 표가 곧 이 툴을 만든 이유다.
    /// <list type="bullet">
    /// <item>한 방 길이 = 클립 0.458초 / 재생배속. 3.08이면 149ms</item>
    /// <item>정점 도달 = 한 방 길이 × 정점위치(0.36). 3.08이면 53ms</item>
    /// <item>러쉬 배속은 <b>한 방이 탭 간격 안에 끝나게</b> 하는 값이다.
    ///       모자라면 주먹이 벽에 닿기 전에 회수된다 (실측 8연타 중 도달 0회)</item>
    /// <item>정지 시간 = GameConfig.hitStopSeconds × 배수.
    ///       탭 간격(초당 5탭 = 200ms)의 20%를 넘으면 렉으로 읽힌다</item>
    /// </list>
    ///
    /// <b>연출 타이밍은 여기 없다.</b> 예전에는 고정 지연으로 맞췄는데 프레임 지터에
    /// 걸려 같은 코드가 뻗음 97%와 21%로 갈렸다. 지금은 주먹이 정점에 닿는 것을
    /// <b>보고</b> 쏘므로 맞출 값이 아니다 — 남은 것은 못 기다릴 때의 상한뿐이다.
    /// </summary>
    public static class PunchTuningIO
    {
        const string CsvPath = "Tools/balance/punch.csv";
        const string ControllerPath = "Assets/_Project/Art/Models/Robot/AC_Demolition.controller";

        /// <summary>키 -> (읽기, 쓰기). 표 하나로 내보내기와 불러오기를 같이 쓴다.</summary>
        sealed class Knob
        {
            public string Key;
            public string Note;
            public System.Func<Ctx, float> Get;
            public System.Action<Ctx, float> Set;
        }

        /// <summary>값이 사는 세 곳을 한 번만 찾아 들고 다닌다.</summary>
        sealed class Ctx
        {
            public AnimatorController Ac;
            public SerializedObject Anim;    // PlayerAnimator
            public SerializedObject Fx;      // DestructionFX

            public SerializedProperty A(string path) => Anim != null ? Anim.FindProperty(path) : null;
            public SerializedProperty F(string path) => Fx != null ? Fx.FindProperty(path) : null;
        }

        static float ReadA(Ctx c, string p) { var x = c.A(p); return x != null ? x.floatValue : 0f; }
        static void WriteA(Ctx c, string p, float v) { var x = c.A(p); if (x != null) x.floatValue = v; }
        static float ReadF(Ctx c, string p) { var x = c.F(p); return x != null ? x.floatValue : 0f; }
        static void WriteF(Ctx c, string p, float v) { var x = c.F(p); if (x != null) x.floatValue = v; }

        static List<Knob> Table()
        {
            return new List<Knob>
            {
                new Knob { Key = "clip.speed", Note = "펀치 클립 재생 배속. 한 방 = 0.458초 / 이 값",
                           Get = c => PunchStateSpeed(c.Ac), Set = (c, v) => SetPunchStateSpeed(c.Ac, v) },
                new Knob { Key = "clip.transition", Note = "펀치끼리 전이 시간(초). 길면 연타가 계속 섞여 팔이 안 뻗는다",
                           Get = c => PunchTransition(c.Ac), Set = (c, v) => SetPunchTransition(c.Ac, v) },

                new Knob { Key = "peak.at", Note = "주먹이 최대로 뻗는 재생 진행도(0~1). 실측 0.36",
                           Get = c => ReadA(c, "holdAt"), Set = (c, v) => WriteA(c, "holdAt", v) },
                new Knob { Key = "peak.width", Note = "정점으로 치는 구간의 폭(진행도)",
                           Get = c => ReadA(c, "holdWidth"), Set = (c, v) => WriteA(c, "holdWidth", v) },
                new Knob { Key = "peak.slowRate", Note = "정점에서 늦추는 배율. 작을수록 오래 버틴다",
                           Get = c => ReadA(c, "holdRate"), Set = (c, v) => WriteA(c, "holdRate", v) },

                new Knob { Key = "rush.fastGap", Note = "탭 간격이 이보다 짧으면 러쉬 최대(초)",
                           Get = c => ReadA(c, "rush.fastGap"), Set = (c, v) => WriteA(c, "rush.fastGap", v) },
                new Knob { Key = "rush.slowGap", Note = "탭 간격이 이보다 길면 러쉬 아님(초)",
                           Get = c => ReadA(c, "rush.slowGap"), Set = (c, v) => WriteA(c, "rush.slowGap", v) },
                new Knob { Key = "rush.rate", Note = "러쉬 배속. 한 방이 탭 간격 안에 끝나야 주먹이 벽까지 간다",
                           Get = c => ReadA(c, "rush.punchRate"), Set = (c, v) => WriteA(c, "rush.punchRate", v) },
                new Knob { Key = "rush.fade", Note = "안 때리면 러쉬가 빠지는 속도(초당)",
                           Get = c => ReadA(c, "rush.fade"), Set = (c, v) => WriteA(c, "rush.fade", v) },

                new Knob { Key = "stop.strike", Note = "보통 두드림 정지 배수 (x GameConfig.hitStopSeconds)",
                           Get = c => ReadF(c, "strikeHitStop"), Set = (c, v) => WriteF(c, "strikeHitStop", v) },
                new Knob { Key = "stop.collapse", Note = "층 붕괴 정지 배수",
                           Get = c => ReadF(c, "collapseHitStop"), Set = (c, v) => WriteF(c, "collapseHitStop", v) },
                new Knob { Key = "stop.broken", Note = "완파 정지 배수",
                           Get = c => ReadF(c, "brokenHitStop"), Set = (c, v) => WriteF(c, "brokenHitStop", v) },
                new Knob { Key = "stop.rushFinish", Note = "러쉬 마무리 정지 배수",
                           Get = c => ReadF(c, "rushFinishHitStop"), Set = (c, v) => WriteF(c, "rushFinishHitStop", v) },
                new Knob { Key = "stop.rushSuppressAbove", Note = "러쉬가 이 값을 넘으면 정지를 걸지 않는다",
                           Get = c => ReadF(c, "rushSuppressAbove"), Set = (c, v) => WriteF(c, "rushSuppressAbove", v) },
                new Knob { Key = "stop.peakTimeout", Note = "정점을 이만큼(초) 기다리다 못 만나면 그냥 쏜다",
                           Get = c => ReadF(c, "peakTimeout"), Set = (c, v) => WriteF(c, "peakTimeout", v) },

                new Knob { Key = "shake.strike", Note = "두드림 셰이크 세기(m)",
                           Get = c => ReadF(c, "strikeShake"), Set = (c, v) => WriteF(c, "strikeShake", v) },
                new Knob { Key = "shake.strikeMax", Note = "진행도가 오를수록 커지는 상한(m)",
                           Get = c => ReadF(c, "strikeShakeMax"), Set = (c, v) => WriteF(c, "strikeShakeMax", v) },
                new Knob { Key = "shake.collapse", Note = "층 붕괴 셰이크(m)",
                           Get = c => ReadF(c, "collapseShake"), Set = (c, v) => WriteF(c, "collapseShake", v) },
                new Knob { Key = "shake.broken", Note = "완파 셰이크(m)",
                           Get = c => ReadF(c, "brokenShake"), Set = (c, v) => WriteF(c, "brokenShake", v) },
                new Knob { Key = "shake.rushFinish", Note = "러쉬 마무리 셰이크(m). 층 붕괴급을 쓰면 너무 심하다",
                           Get = c => ReadF(c, "rushFinishShake"), Set = (c, v) => WriteF(c, "rushFinishShake", v) },
            };
        }

        // ── 컨트롤러 쪽 읽고 쓰기 ────────────────────────────────────────────

        static float PunchStateSpeed(AnimatorController ac)
        {
            foreach (var st in ac.layers[1].stateMachine.states)
                if (st.state.name.Contains("Punch")) return st.state.speed;
            return 0f;
        }

        static void SetPunchStateSpeed(AnimatorController ac, float v)
        {
            foreach (var st in ac.layers[1].stateMachine.states)
                if (st.state.name.Contains("Punch")) st.state.speed = v;
        }

        static float PunchTransition(AnimatorController ac)
        {
            foreach (var st in ac.layers[1].stateMachine.states)
                foreach (var t in st.state.transitions)
                    if (t.destinationState != null && t.destinationState.name.Contains("Punch"))
                        return t.duration;
            return 0f;
        }

        static void SetPunchTransition(AnimatorController ac, float v)
        {
            foreach (var st in ac.layers[1].stateMachine.states)
                foreach (var t in st.state.transitions)
                    if (t.destinationState != null && t.destinationState.name.Contains("Punch"))
                        t.duration = v;
        }

        // ── 컨텍스트 ────────────────────────────────────────────────────────

        static Ctx Open()
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (ac == null) { Debug.LogError($"[PunchTuningIO] 컨트롤러가 없다: {ControllerPath}"); return null; }

            var anim = Object.FindFirstObjectByType<Demo.Player.PlayerAnimator>(FindObjectsInactive.Include);
            var fx = Object.FindFirstObjectByType<Demo.Destruction.DestructionFX>(FindObjectsInactive.Include);
            if (anim == null) Debug.LogWarning("[PunchTuningIO] 씬에 PlayerAnimator가 없다 — 그 값들은 건너뛴다.");
            if (fx == null)
                Debug.LogWarning("[PunchTuningIO] 씬에 DestructionFX가 없다 — 그 값들은 건너뛴다. " +
                                 "[Bootstrap]에 컴포넌트로 올라가 있어야 한다.");

            return new Ctx
            {
                Ac = ac,
                Anim = anim != null ? new SerializedObject(anim) : null,
                Fx = fx != null ? new SerializedObject(fx) : null,
            };
        }

        static string Full(string rel) =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, rel);

        // ── 내보내기 ────────────────────────────────────────────────────────

        [MenuItem("Demo/11. 펀치 타이밍 CSV 내보내기", priority = 60)]
        public static void Export()
        {
            Ctx c = Open();
            if (c == null) return;

            float clip = 0.458f;
            float speed = Mathf.Max(0.01f, PunchStateSpeed(c.Ac));
            float peak = ReadA(c, "holdAt");

            var sb = new StringBuilder();
            sb.AppendLine("# 펀치 타이밍. value만 고치고 'Demo/11b. 펀치 타이밍 CSV 불러오기'를 실행한다.");
            sb.AppendLine("# 값이 세 군데(컨트롤러·PlayerAnimator·DestructionFX)에 흩어져 있어 여기로 모았다.");
            sb.AppendLine("#");
            sb.AppendLine($"# 지금 기준:  한 방 {clip / speed * 1000f:F0}ms   " +
                          $"정점 도달 {clip * peak / speed * 1000f:F0}ms   " +
                          $"러쉬 한 방 {clip / speed / Mathf.Max(0.01f, ReadA(c, "rush.punchRate")) * 1000f:F0}ms");
            sb.AppendLine("# 탭 간격은 GameConfig.tapsPerSecond가 정한다 (초당 5탭이면 200ms).");
            sb.AppendLine("# 러쉬 한 방이 탭 간격보다 길면 주먹이 벽에 닿기 전에 회수된다.");
            sb.AppendLine("#");
            sb.AppendLine("key,value,설명");

            foreach (Knob k in Table())
                sb.AppendLine($"{k.Key},{k.Get(c).ToString("0.####", CultureInfo.InvariantCulture)},{k.Note}");

            string path = Full(CsvPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            Debug.Log($"[PunchTuningIO] {CsvPath} 에 내보냈다. 고친 뒤 'Demo/11b'로 되돌린다.");
        }

        // ── 불러오기 ────────────────────────────────────────────────────────

        [MenuItem("Demo/11b. 펀치 타이밍 CSV 불러오기", priority = 61)]
        public static void Import()
        {
            string path = Full(CsvPath);
            if (!File.Exists(path))
            {
                Debug.LogError($"[PunchTuningIO] {CsvPath} 가 없다. 먼저 'Demo/11'을 실행할 것.");
                return;
            }

            Ctx c = Open();
            if (c == null) return;

            var byKey = new Dictionary<string, Knob>();
            foreach (Knob k in Table()) byKey[k.Key] = k;

            int applied = 0;
            var unknown = new List<string>();
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("key,")) continue;

                string[] cell = line.Split(',');
                if (cell.Length < 2) continue;

                string key = cell[0].Trim();
                if (!byKey.TryGetValue(key, out Knob knob)) { unknown.Add(key); continue; }
                if (!float.TryParse(cell[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                {
                    Debug.LogWarning($"[PunchTuningIO] '{key}'의 값을 못 읽었다: '{cell[1]}'");
                    continue;
                }
                knob.Set(c, v);
                applied++;
            }

            if (c.Anim != null) c.Anim.ApplyModifiedProperties();
            if (c.Fx != null) c.Fx.ApplyModifiedProperties();
            EditorUtility.SetDirty(c.Ac);
            AssetDatabase.SaveAssets();

            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);

            if (unknown.Count > 0)
                Debug.LogWarning($"[PunchTuningIO] 모르는 키 {unknown.Count}개는 건너뛰었다: {string.Join(", ", unknown)}");
            Debug.LogWarning($"[PunchTuningIO] {applied}개 값을 반영하고 씬을 저장했다. " +
                             "값은 여전히 [제안·미승인]이다.");
        }
    }
}
