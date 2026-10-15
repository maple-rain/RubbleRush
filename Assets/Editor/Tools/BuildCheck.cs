using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace Demo.EditorTools
{
    /// <summary>
    /// 빌드 전에 <b>막을 것과 알릴 것</b>을 가른다 [신설 2026-10-15].
    ///
    /// <b>왜 따로 두는가.</b> 빌드는 30~90분짜리다 (실측: Assets 345MB · 텍스처 686장 ·
    /// 플랫폼 전환이 전부 재압축). 한 시간을 기다린 끝에 "애플리케이션 ID가 비었다"로
    /// 실패하면 그 한 시간이 통째로 날아간다. <b>초 단위로 알 수 있는 것은 먼저 본다.</b>
    ///
    /// <b>막는 것과 알리는 것을 나눈다.</b> 막는 것은 빌드가 실패하거나 못 쓸 산출물이
    /// 나오는 경우뿐이다. 나머지는 알리기만 하고 빌드는 진행한다 — 마감 나흘 전에
    /// 경고 하나로 빌드를 멈추면 그게 더 비싸다.
    ///
    /// <b>값을 정하지 않는다.</b> 애플리케이션 ID처럼 한 번 정하면 못 바꾸는 것은
    /// 제안만 하고 사람이 누른다 (<see cref="ApplyIdentifier"/>).
    /// </summary>
    public static class BuildCheck
    {
        /// <summary>한 줄. 막는 것인가 알리는 것인가.</summary>
        public struct Note
        {
            public bool Blocking;
            public string Text;
            public string Fix;      // 어떻게 고치는가. 없으면 null
        }

        [MenuItem("Demo/빌드/0. 빌드 전 검사", priority = 190)]
        public static void Run()
        {
            List<Note> notes = Collect();
            Debug.Log(Format(notes));
        }

        /// <summary>
        /// 전부 본다. 막는 것이 하나도 없으면 빌드해도 된다.
        /// </summary>
        public static List<Note> Collect()
        {
            var n = new List<Note>();

            Scenes(n);
            Identifier(n);
            Shadows(n);
            Resources(n);
            Keystore(n);
            Config(n);

            return n;
        }

        /// <summary>막는 것이 있는가.</summary>
        public static bool Blocked(List<Note> notes)
        {
            foreach (Note x in notes) if (x.Blocking) return true;
            return false;
        }

        // ── 개별 검사 ────────────────────────────────────────────────────────

        /// <summary>
        /// 씬이 빌드에 들어 있는가. <b>이게 비면 빈 화면이 나온다</b> —
        /// 빌드는 성공하고 실행하면 아무것도 없다. 전에 한 번 겪은 사고다 (커밋 b4e5119).
        /// </summary>
        static void Scenes(List<Note> n)
        {
            int on = 0;
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
                if (s.enabled && !string.IsNullOrEmpty(s.path) && File.Exists(s.path)) on++;

            if (on == 0)
                n.Add(new Note
                {
                    Blocking = true,
                    Text = "빌드에 켜진 씬이 0개다 — 빌드는 성공하고 실행하면 빈 화면이 나온다",
                    Fix = "File > Build Profiles 에서 Start.unity 와 Main.unity 를 켤 것",
                });
            else
                n.Add(new Note { Text = "씬 " + on + "개가 빌드에 들어간다" });
        }

        /// <summary>
        /// 애플리케이션 ID. <b>비면 안드로이드 빌드가 실패한다.</b>
        ///
        /// 값을 여기서 정하지 않는다 — 스토어에 한 번 올리면 <b>영원히 못 바꾸는</b>
        /// 이름이라 사람이 눌러야 한다. 제안만 만든다.
        /// </summary>
        static void Identifier(List<Note> n)
        {
            string id = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            if (!string.IsNullOrEmpty(id) && !id.StartsWith("com.DefaultCompany"))
            {
                n.Add(new Note { Text = "애플리케이션 ID: " + id });
                return;
            }

            n.Add(new Note
            {
                Blocking = true,
                Text = "애플리케이션 ID가 비어 있다 — 안드로이드 빌드가 실패한다",
                Fix = "메뉴 'Demo/빌드/애플리케이션 ID 맞추기 (" + Suggest() + ")' 를 누를 것. "
                    + "스토어에 올린 뒤에는 영원히 못 바꾸므로 한 번 보고 누르시라",
            });
        }

        /// <summary>회사·제품 이름에서 만든 제안. 유니티의 기본 규칙과 같다.</summary>
        public static string Suggest()
        {
            return "com." + Slug(PlayerSettings.companyName) + "." + Slug(PlayerSettings.productName);
        }

        static string Slug(string s)
        {
            if (string.IsNullOrEmpty(s)) return "app";
            var sb = new System.Text.StringBuilder();
            foreach (char c in s)
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.Length == 0 ? "app" : sb.ToString();
        }

        [MenuItem("Demo/빌드/애플리케이션 ID 맞추기", priority = 191)]
        public static void ApplyIdentifier()
        {
            string id = Suggest();
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, id);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.WebGL, id);
            AssetDatabase.SaveAssets();
            Debug.Log("[빌드] 애플리케이션 ID를 " + id + " 로 맞췄다. "
                    + "스토어에 올린 뒤에는 바꿀 수 없다.");
        }

        /// <summary>
        /// 실시간 그림자. CLAUDE.md 성능 예산은 "사용 안 함"이다.
        /// 막지는 않는다 — 그림자가 켜져 있어도 빌드는 나온다.
        /// </summary>
        static void Shadows(List<Note> n)
        {
            RenderPipelineAsset rp = GraphicsSettings.defaultRenderPipeline;
            if (rp == null)
            {
                n.Add(new Note { Blocking = true, Text = "렌더 파이프라인 에셋이 없다" });
                return;
            }

            var so = new SerializedObject(rp);
            SerializedProperty main = so.FindProperty("m_MainLightShadowsSupported");
            string path = AssetDatabase.GetAssetPath(rp);

            if (main != null && main.boolValue)
                n.Add(new Note
                {
                    Text = "실시간 그림자가 켜져 있다 (" + Path.GetFileName(path)
                         + ") — CLAUDE.md 예산은 '사용 안 함'이다",
                    Fix = "메뉴 'Demo/19. 그림자 지원 끄기 (URP)'",
                });
            else
                n.Add(new Note { Text = "그림자 지원 꺼짐 (" + Path.GetFileName(path) + ")" });
        }

        /// <summary>
        /// <c>Resources</c>는 <b>참조되지 않아도 통째로</b> 빌드에 들어간다.
        /// 유료 에셋이 거기 들어가 있으면 빌드가 수십 MB씩 불어난다.
        /// <c>Editor</c> 아래의 것은 빌드에서 빠지므로 세지 않는다.
        /// </summary>
        static void Resources(List<Note> n)
        {
            long bytes = 0;
            int files = 0;
            foreach (string dir in Directory.GetDirectories("Assets", "Resources",
                                                            SearchOption.AllDirectories))
            {
                if (dir.Replace('\\', '/').Contains("/Editor/")) continue;
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    if (f.EndsWith(".meta")) continue;
                    bytes += new FileInfo(f).Length;
                    files++;
                }
            }

            string size = (bytes / 1024f / 1024f).ToString("0.0") + "MB";
            if (bytes > 20L * 1024 * 1024)
                n.Add(new Note
                {
                    Text = "Resources 가 " + size + " (" + files + "개) — 참조와 상관없이 전부 빌드에 들어간다",
                    Fix = "쓰지 않는 것을 Resources 밖으로 옮길 것",
                });
            else
                n.Add(new Note { Text = "Resources " + size + " (" + files + "개)" });
        }

        /// <summary>
        /// 키스토어. <b>만들고 서명하는 것은 사람 몫이다</b> — 비밀번호를 코드가 들고 있으면
        /// 그대로 저장소에 올라간다. 여기서는 비었는지만 알린다.
        /// </summary>
        static void Keystore(List<Note> n)
        {
            if (string.IsNullOrEmpty(PlayerSettings.Android.keystoreName))
                n.Add(new Note
                {
                    Text = "키스토어가 없다 — 디버그 키로 서명된다. 테스트는 되고 스토어 업로드는 안 된다",
                    Fix = "스토어에 올릴 때 사람이 직접 만들 것 (비밀번호는 저장소에 올리지 않는다)",
                });
        }

        /// <summary>GameConfig가 스스로 유효하다고 하는가.</summary>
        static void Config(List<Note> n)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:GameConfig"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                var c = AssetDatabase.LoadAssetAtPath<Data.GameConfig>(p);
                if (c == null) continue;
                string bad = c.Validate();
                if (!string.IsNullOrEmpty(bad))
                    n.Add(new Note { Blocking = true, Text = Path.GetFileName(p) + ": " + bad });
            }
        }

        // ── 보고 ─────────────────────────────────────────────────────────────

        public static string Format(List<Note> notes)
        {
            var sb = new System.Text.StringBuilder("[빌드 전 검사]\n");
            int block = 0;

            foreach (Note x in notes)
            {
                if (x.Blocking) block++;
                sb.AppendLine("  " + (x.Blocking ? "[막힘] " : "[  ] ") + x.Text);
                if (!string.IsNullOrEmpty(x.Fix)) sb.AppendLine("         -> " + x.Fix);
            }

            sb.Append(block == 0
                ? "  빌드해도 된다."
                : "  막는 것이 " + block + "개다. 고치고 다시 볼 것.");
            return sb.ToString();
        }
    }
}
