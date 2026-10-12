using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 모바일 빌드 설정을 맞춘다 [2026-10-12]. <b>먼저 점검하고, 그 다음 적용한다.</b>
    ///
    /// <b>왜 급한가.</b> 마감 산출물이 모바일 빌드인데 2026-10-12까지 빌드를 한 번도 안 만들었고,
    /// 빌드 씬 목록에는 유니티 템플릿의 <c>SampleScene.unity</c>만 들어 있었다 —
    /// <b>그대로 빌드하면 게임이 아니라 빈 씬이 나온다.</b> 이건 눈으로 안 보이는 종류의 사고라
    /// 도구로 고정해 둔다.
    ///
    /// <b>플랫폼 전환은 여기서 안 한다.</b> 안드로이드로 바꾸면 에셋 전체가 재임포트돼
    /// 에디터가 오래 잠긴다. 언제 치를지는 사람이 정할 일이라 메뉴를 따로 뒀다.
    ///
    /// <b>안 하는 것</b>: 키스토어 생성·서명. 비밀번호가 필요하고 키 파일은 커밋 금지 대상이라
    /// (CLAUDE.md) 사람이 직접 만든다.
    /// </summary>
    public static class MobileSetup
    {
        /// <summary>
        /// 빌드에 들어가야 할 씬. <b>순서가 뜻을 갖는다</b> — 0번이 앱이 여는 씬이다.
        /// 시작 메뉴를 Start.unity로 뗀 뒤로는 그것이 0번이어야 한다 [2026-10-12].
        /// </summary>
        static readonly string[] Scenes =
        {
            "Assets/_Project/Scenes/Start.unity",
            "Assets/_Project/Scenes/Main.unity",
        };

        /// <summary>자리표시자다. 기획자가 정하면 바꾼다 — 한 번 올리면 못 바꾸는 값이다.</summary>
        const string Package = "com.juneseo.fp";

        const string Company = "juneseo";

        [MenuItem("Demo/빌드/1. 모바일 설정 점검", priority = 190)]
        public static void Check() => Debug.Log(Report());

        public static string Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine("모바일 빌드 설정 점검");
            sb.AppendLine();

            var a = NamedBuildTarget.Android;
            Line(sb, "빌드 씬", SceneList(), string.Join(" + ", Scenes));
            Line(sb, "활성 타깃", EditorUserBuildSettings.activeBuildTarget.ToString(), "Android");
            Line(sb, "스크립팅", PlayerSettings.GetScriptingBackend(a).ToString(), "IL2CPP");
            Line(sb, "아키텍처", PlayerSettings.Android.targetArchitectures.ToString(), "ARM64");
            Line(sb, "색 공간", PlayerSettings.colorSpace.ToString(), "Linear");
            Line(sb, "화면 방향", Orientation(), "가로 고정");
            Line(sb, "패키지 이름", PlayerSettings.GetApplicationIdentifier(a), Package);
            Line(sb, "회사", PlayerSettings.companyName, Company);
            Line(sb, "렌더 파이프라인", Pipe(), "Mobile_RPAsset");
            Line(sb, "키스토어", string.IsNullOrEmpty(PlayerSettings.Android.keystoreName) ? "없음" : "있음",
                 "있음 (사람이 만든다)");

            sb.AppendLine();
            sb.AppendLine("  고치려면 'Demo/빌드/2. 모바일 설정 적용'.");
            sb.AppendLine("  플랫폼 전환은 'Demo/빌드/3. 안드로이드로 전환'에서 따로 — 에디터가 오래 잠긴다.");
            sb.AppendLine("  안 하는 것: 키스토어 생성 · 서명 · 실기 성능 (폰이 있어야 안다)");
            return sb.ToString();
        }

        [MenuItem("Demo/빌드/2. 모바일 설정 적용", priority = 191)]
        public static void Apply()
        {
            var sb = new StringBuilder();
            sb.AppendLine("모바일 빌드 설정 적용");
            sb.AppendLine();

            // ── 빌드 씬. 제일 중요하다 — 틀리면 빈 씬이 나간다 ────────────────
            // 순서까지 중요하다. 0번이 앱이 여는 씬이라 시작 메뉴가 먼저 와야 한다.
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>();
            foreach (string path in Scenes)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                {
                    Debug.LogError("[모바일 설정] " + path + " 를 못 찾았다. 중단한다.");
                    return;
                }
                list.Add(new EditorBuildSettingsScene(path, true));
            }
            string was = SceneList();
            EditorBuildSettings.scenes = list.ToArray();
            sb.AppendLine("  빌드 씬      " + was + "  ->  " + SceneList());

            // ── 가로 고정. 세로로 돌면 조이스틱과 버튼이 겹친다 ────────────────
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            sb.AppendLine("  화면 방향    가로 둘만 허용 (세로 금지)");

            // ── 이름. 템플릿 기본값이면 스토어에 못 올린다 ─────────────────────
            var a = NamedBuildTarget.Android;
            string oldId = PlayerSettings.GetApplicationIdentifier(a);
            PlayerSettings.companyName = Company;
            PlayerSettings.SetApplicationIdentifier(a, Package);
            sb.AppendLine("  패키지 이름  " + oldId + "  ->  " + Package + "   [자리표시자 — 기획자가 정할 것]");

            // ── 모바일 렌더 파이프라인 ─────────────────────────────────────────
            sb.Append(UseMobilePipeline());

            AssetDatabase.SaveAssets();
            sb.AppendLine();
            sb.AppendLine("  남은 것: 플랫폼 전환(메뉴 3) · 키스토어(사람) · 실기 성능(폰 필요)");
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// 안드로이드 품질 레벨이 <c>Mobile_RPAsset</c>을 쓰게 한다.
        ///
        /// 지금은 <c>PC_RPAsset</c>(2048 소프트 그림자)으로 돌고 있다. 그대로 폰에 올리면
        /// 그림자 맵 하나에 16MB를 쓰고, CLAUDE.md 예산은 애초에 "실시간 그림자 사용 안 함"이다.
        /// <b>다만 여기서 그림자를 끄지는 않는다</b> — 블롭 섀도우 대체가 없어서 그냥 끄면
        /// 물체가 떠 보인다. 모바일 에셋이 무엇을 담을지는 그 에셋의 값이 정한다.
        /// </summary>
        static string UseMobilePipeline()
        {
            var sb = new StringBuilder();
            var mobile = Find("Mobile_RPAsset");
            if (mobile == null) { sb.AppendLine("  파이프라인   Mobile_RPAsset을 못 찾음 — 건너뜀"); return sb.ToString(); }

            string[] names = QualitySettings.names;
            int moved = 0;
            for (int i = 0; i < names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                if (QualitySettings.renderPipeline == mobile) continue;
                QualitySettings.renderPipeline = mobile;
                moved++;
            }
            sb.AppendLine("  파이프라인   품질 레벨 " + moved + "개를 Mobile_RPAsset으로 (" +
                          string.Join(", ", names) + ")");
            return sb.ToString();
        }

        [MenuItem("Demo/빌드/3. 안드로이드로 전환 (오래 걸림)", priority = 192)]
        public static void SwitchPlatform()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
            {
                Debug.Log("[모바일 설정] 이미 안드로이드다.");
                return;
            }
            if (!EditorUtility.DisplayDialog("안드로이드로 전환",
                    "에셋 전체가 다시 임포트된다. 텍스처가 42장이라 10분 이상 걸릴 수 있고 " +
                    "그동안 에디터가 잠긴다.\n\n진행할까?", "전환한다", "그만둔다"))
                return;

            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        }

        // ── 읽기 ────────────────────────────────────────────────────────────────

        static void Line(StringBuilder sb, string name, string now, string want)
        {
            bool ok = now.Contains(want) || want.Contains(now);
            sb.AppendLine(string.Format("  {0,-14} {1,-46} {2}", name, now, ok ? "OK" : "-> " + want));
        }

        static string SceneList()
        {
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Length == 0) return "(비어 있음)";
            var parts = new string[scenes.Length];
            for (int i = 0; i < scenes.Length; i++)
                parts[i] = (scenes[i].enabled ? "" : "[꺼짐] ") + scenes[i].path;
            return string.Join(" + ", parts);
        }

        static string Orientation()
        {
            if (PlayerSettings.defaultInterfaceOrientation != UIOrientation.AutoRotation)
                return PlayerSettings.defaultInterfaceOrientation.ToString();
            bool portrait = PlayerSettings.allowedAutorotateToPortrait
                         || PlayerSettings.allowedAutorotateToPortraitUpsideDown;
            return portrait ? "AutoRotation (세로 허용)" : "가로 고정";
        }

        static string Pipe()
        {
            var rp = QualitySettings.renderPipeline ?? UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
            return rp != null ? rp.name : "(없음)";
        }

        static UnityEngine.Rendering.RenderPipelineAsset Find(string name)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:RenderPipelineAsset"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == name)
                    return AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.RenderPipelineAsset>(p);
            }
            return null;
        }
    }
}
