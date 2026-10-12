using System.Collections.Generic;
using System.Text;
using Demo.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// <b>지금 빌드하면 돌아가는가.</b> 빠진 것을 한 번에 훑는다 [2026-10-12].
    ///
    /// <b>왜 만들었나.</b> 이 프로젝트에서 조용히 빠져 있던 것들이 전부 "에러가 안 나는" 종류였다 —
    /// 빌드 씬 목록에 게임 씬이 없었고(템플릿의 빈 씬만 있었다), HUD와 결과 화면이 씬에 아예
    /// 없었고, 터치에는 부수기 버튼이 없어 폰에서 건물을 못 부쉈다. 셋 다 콘솔은 조용했다.
    /// 눈으로 찾는 대신 목록으로 센다.
    ///
    /// <b>안 보는 것</b>: 실기 성능 · 재미 · 밸런스. 그건 사람이 플레이해야 안다.
    /// </summary>
    public static class ReadyCheck
    {
        [MenuItem("Demo/23. 빠진 것 점검", priority = 77)]
        public static void Run() => Debug.Log(Report());

        public static string Report()
        {
            var sb = new StringBuilder();
            var fail = new List<string>();

            sb.AppendLine("빠진 것 점검 — 지금 빌드하면 돌아가는가");
            sb.AppendLine();

            Components(sb, fail);
            Refs(sb, fail);
            Audio(sb, fail);
            Structures(sb, fail);
            Build(sb, fail);

            sb.AppendLine();
            if (fail.Count == 0) sb.AppendLine("  빠진 것 없음.");
            else
            {
                sb.AppendLine("  막는 것 " + fail.Count + "개:");
                foreach (string f in fail) sb.AppendLine("    - " + f);
            }
            sb.AppendLine("  안 보는 것: 실기 성능 · 재미 · 밸런스");
            return sb.ToString();
        }

        /// <summary>
        /// 시작 씬에 있어야 할 것. <b>게임 쪽 컴포넌트는 여기 없는 것이 맞다</b> —
        /// 메뉴 화면에 플레이어와 구역 생성기가 있으면 그게 이상한 것이다.
        /// </summary>
        static readonly (string type, string what, bool vital)[] NeededInStart =
        {
            ("Demo.UI.TitleScreen",       "시작 메뉴",   true),
            ("Demo.Utils.VolumeSettings", "음량 설정",   true),
            ("Demo.Utils.GameAudio",      "효과음",      false),
            ("Demo.Utils.MusicPlayer",    "메뉴 음악",   false),
        };

        /// <summary>게임 씬에 있어야 할 컴포넌트. 하나라도 없으면 그 기능이 통째로 없는 것이다.</summary>
        static readonly (string type, string what, bool vital)[] Needed =
        {
            ("Demo.Core.GameConfigRef",      "설정값 출처",        true),
            ("Demo.Core.GameLoop",           "시간·할당량",        true),
            ("Demo.Core.GameModeController", "필드/파괴 전환",     true),
            ("Demo.Core.ZoneBuilder",        "구역 생성",          true),
            ("Demo.Core.PoolManager",        "풀링",               true),
            ("Demo.Player.InputRouter",      "입력",               true),
            ("Demo.Player.PlayerController", "이동",               true),
            ("Demo.Player.InteractionProbe", "건물 접근 판정",     true),
            ("Demo.Collection.CargoTow",     "견인",               true),
            ("Demo.Collection.CableController", "케이블",          true),
            ("Demo.Utils.GameAudio",         "효과음",             true),
            ("Demo.Utils.MusicPlayer",       "음악",               true),
            ("Demo.Utils.VolumeSettings",    "음량 설정",          false),
            ("Demo.UI.TouchControls",        "터치 조이스틱·버튼", true),
            ("Demo.UI.ActionPrompt",         "부수기/놓기 프롬프트", true),
            ("Demo.UI.HUD",                  "시간·할당량 표시",   true),
            ("Demo.UI.TitleScreen",          "시작 화면",          false),
            ("Demo.UI.ResultScreen",         "결과 화면",          false),
        };

        /// <summary>
        /// 열려 있는 씬에 맞는 목록으로 본다 [고침 2026-10-12].
        ///
        /// 전에는 목록이 하나뿐이라, 시작 씬을 열어 놓고 돌리면 게임 컴포넌트 열셋이
        /// 통째로 "없다"고 나왔다 — 메뉴 화면에 플레이어가 없는 것은 <b>맞는 일</b>인데
        /// 도구가 그걸 몰랐다. 씬이 둘이 된 순간 생긴 구멍이다.
        /// </summary>
        static void Components(StringBuilder sb, List<string> fail)
        {
            string scene = EditorSceneManager.GetActiveScene().name;
            bool isStart = scene == "Start";
            var needed = isStart ? NeededInStart : Needed;

            sb.AppendLine("[1] 씬에 있어야 할 것 — 열린 씬 '" + scene + "'"
                          + (isStart ? " (시작 메뉴)" : " (게임)"));
            if (!isStart && scene != "Main")
                sb.AppendLine("    * Main도 Start도 아니다. 게임 씬 기준으로 본다.");

            foreach (var n in needed)
            {
                var ty = System.Type.GetType(n.type + ", Assembly-CSharp");
                Object found = ty != null
                    ? Object.FindFirstObjectByType(ty, FindObjectsInactive.Include) : null;

                if (found != null) continue;
                string line = n.what + " (" + n.type.Substring(n.type.LastIndexOf('.') + 1) + ")";
                sb.AppendLine("    없음: " + line);
                if (n.vital) fail.Add("씬에 " + line + "이 없다");
            }
            sb.AppendLine("    확인한 것 " + needed.Length + "개");
        }

        /// <summary>
        /// 비어 있는 직렬화 참조. <b>런타임에야 터지는 종류라 제일 비싸다.</b>
        /// 스스로 찾아 채우는 필드(대부분 Awake에서 FindFirstObjectByType)는 비어 있어도 되므로
        /// 세기만 하고 막지는 않는다 — 어느 쪽인지는 코드를 봐야 알 수 있다.
        /// </summary>
        static void Refs(StringBuilder sb, List<string> fail)
        {
            sb.AppendLine();
            sb.AppendLine("[2] 비어 있는 참조");

            int empty = 0;
            var lines = new List<string>();
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,
                                                                      FindObjectsSortMode.None))
            {
                if (mb == null || !mb.GetType().FullName.StartsWith("Demo.")) continue;

                var so = new SerializedObject(mb);
                SerializedProperty p = so.GetIterator();
                while (p.NextVisible(true))
                {
                    if (p.propertyType != SerializedPropertyType.ObjectReference) continue;
                    if (p.objectReferenceValue != null) continue;
                    empty++;
                    if (lines.Count < 12)
                        lines.Add(mb.GetType().Name + "." + p.name);
                }
            }

            foreach (string l in lines) sb.AppendLine("    " + l);
            if (empty > lines.Count) sb.AppendLine("    ... 모두 " + empty + "개");
            sb.AppendLine("    * 대부분 Awake에서 스스로 찾는다. 터지면 그때 이 목록을 본다.");
        }

        static void Audio(StringBuilder sb, List<string> fail)
        {
            sb.AppendLine();
            sb.AppendLine("[3] 소리");

            string[] guids = AssetDatabase.FindAssets("t:SoundBank");
            if (guids.Length == 0) { sb.AppendLine("    SoundBank 에셋이 없다"); fail.Add("SoundBank가 없다"); return; }

            var bank = AssetDatabase.LoadAssetAtPath<SoundBank>(AssetDatabase.GUIDToAssetPath(guids[0]));
            int wired = 0, blank = 0;
            foreach (SoundBank.Entry e in bank.Entries)
            {
                bool has = e.clips != null && e.clips.Length > 0 && e.clips[0] != null;
                if (has) wired++; else { blank++; sb.AppendLine("    음원 없음: " + e.id); }
            }
            sb.AppendLine("    연결된 사건 " + wired + " / " + (wired + blank));

            var ga = Object.FindFirstObjectByType<Demo.Utils.GameAudio>(FindObjectsInactive.Include);
            if (ga != null && new SerializedObject(ga).FindProperty("bank").objectReferenceValue == null)
                fail.Add("GameAudio에 SoundBank가 안 물려 있다 — 소리가 하나도 안 난다");

            var mp = Object.FindFirstObjectByType<Demo.Utils.MusicPlayer>(FindObjectsInactive.Include);
            if (mp != null)
            {
                var so = new SerializedObject(mp);
                if (so.FindProperty("field").objectReferenceValue == null)
                    sb.AppendLine("    음악: 필드 곡이 안 물려 있다 — 조용하다");
            }
        }

        static void Structures(StringBuilder sb, List<string> fail)
        {
            sb.AppendLine();
            sb.AppendLine("[4] 건물 데이터");

            int ok = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:StructureData"))
            {
                var d = AssetDatabase.LoadAssetAtPath<StructureData>(AssetDatabase.GUIDToAssetPath(guid));
                if (d == null || string.IsNullOrEmpty(d.id)) continue;

                string bad = d.Validate();
                if (bad == null) { ok++; continue; }
                sb.AppendLine("    " + bad);
                fail.Add(d.id + "의 값이 비어 있다");
            }
            sb.AppendLine("    통과한 종류 " + ok + "개");

            // 시작 씬에는 ZoneBuilder가 없는 것이 맞다. 없다고 막지 않는다.
            var zone = Object.FindFirstObjectByType<Demo.Core.ZoneBuilder>(FindObjectsInactive.Include);
            if (zone == null) return;
            var zp = new SerializedObject(zone).FindProperty("zone");
            if (zp != null && zp.objectReferenceValue == null)
                fail.Add("ZoneBuilder에 ZoneData가 안 물려 있다 — 건물이 하나도 안 생긴다");
        }

        static void Build(StringBuilder sb, List<string> fail)
        {
            sb.AppendLine();
            sb.AppendLine("[5] 빌드 설정");

            var scenes = EditorBuildSettings.scenes;
            bool hasMain = false, hasStart = false;
            foreach (var s in scenes)
            {
                if (!s.enabled) continue;
                if (s.path.EndsWith("Main.unity")) hasMain = true;
                if (s.path.EndsWith("Start.unity")) hasStart = true;
            }

            sb.AppendLine("    빌드 씬 " + scenes.Length + "개");
            for (int i = 0; i < scenes.Length; i++)
                sb.AppendLine("      " + i + ". " + scenes[i].path + (scenes[i].enabled ? "" : "  [꺼짐]"));

            if (!hasMain) fail.Add("빌드 씬 목록에 Main.unity가 없다 — 빌드하면 게임이 안 들어간다");
            if (!hasStart) fail.Add("빌드 씬 목록에 Start.unity가 없다 — 시작 메뉴가 안 들어간다");

            // 0번이 앱이 여는 씬이다. 순서가 뒤집히면 메뉴 없이 바로 게임이 뜬다.
            if (hasStart && scenes.Length > 0 && !scenes[0].path.EndsWith("Start.unity"))
                fail.Add("빌드 씬 0번이 Start.unity가 아니다 (" + scenes[0].path + ") — 앱이 여는 씬은 0번이다");

            if (PlayerSettings.allowedAutorotateToPortrait)
                sb.AppendLine("    세로 회전이 허용돼 있다 — 폰을 세우면 버튼이 겹친다");

            string id = PlayerSettings.GetApplicationIdentifier(
                UnityEditor.Build.NamedBuildTarget.Android);
            if (id.Contains("UnityTechnologies") || id.Contains("DefaultCompany"))
                fail.Add("패키지 이름이 템플릿 기본값이다 (" + id + ")");

            if (string.IsNullOrEmpty(PlayerSettings.Android.keystoreName))
                sb.AppendLine("    키스토어 없음 — 서명 빌드를 하려면 사람이 만들어야 한다");

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                sb.AppendLine("    활성 타깃이 " + EditorUserBuildSettings.activeBuildTarget
                              + " 다 — 'Demo/빌드/3'으로 전환한다 (오래 걸린다)");
        }
    }
}
