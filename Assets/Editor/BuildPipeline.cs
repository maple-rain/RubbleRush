using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 빌드를 낸다 [구현 2026-10-15. 그전에는 스텁이었다].
    ///
    /// | 플랫폼 | 설정 |
    /// |---|---|
    /// | Android | AAB · IL2CPP · ARM64 · 가로 고정 |
    /// | WebGL | Brotli · 예외 처리 끄기 |
    ///
    /// <b>왜 이제야.</b> SETUP.md는 D5·D10·D14·D18에 빌드를 올리라고 적어 뒀지만
    /// 이 파일은 경고만 찍는 스텁이었고 <b>한 번도 빌드가 나온 적이 없다.</b>
    /// 목표에 itch.io 업로드가 있는데 수단이 없던 상태다.
    ///
    /// <b>설정을 여기서 다시 맞추지 않는다.</b> 실측하니 Player Settings는 이미
    /// IL2CPP · ARM64 · Landscape · Linear · Input System 으로 맞춰져 있었다
    /// (CLAUDE.md의 "전부 기본값"은 낡은 기록이다). 빌드 때마다 코드가 덮어쓰면
    /// 사람이 에디터에서 바꾼 것이 조용히 되돌아간다. 여기서는 <b>플랫폼별로
    /// 빌드 산출물의 형태를 정하는 것</b>(AAB인가 APK인가, 압축을 뭘로 하는가)만 만진다.
    ///
    /// <b>오래 걸린다.</b> 실측 기준 Assets 345MB · 텍스처 686장이라, 플랫폼을 처음
    /// 바꾸면 전부 재압축한다. 첫 빌드는 30~90분을 본다. 그래서
    /// <see cref="BuildCheck"/>를 먼저 돌려 초 단위로 알 수 있는 것을 먼저 거른다.
    ///
    /// <b>배치 모드</b>로도 돈다 — 에디터를 열지 않고 명령줄에서 돌리면 그동안
    /// 유니티를 다른 일에 쓸 수 있다. <see cref="Batch"/> 참조.
    /// </summary>
    public static class BuildPipelineTool
    {
        /// <summary>산출물이 쌓이는 곳. 저장소 밖이다 — .gitignore를 믿지 않는다.</summary>
        const string OutRoot = "Builds";

        // ── 메뉴 ─────────────────────────────────────────────────────────────

        [MenuItem("Demo/빌드/Android AAB", priority = 200)]
        public static void BuildAndroid() => Run(Target.Android, true);

        [MenuItem("Demo/빌드/Android APK (기기 설치용)", priority = 201)]
        public static void BuildAndroidApk() => Run(Target.Android, false);

        [MenuItem("Demo/빌드/WebGL", priority = 202)]
        public static void BuildWebGL() => Run(Target.WebGL, false);

        enum Target { Android, WebGL }

        // ── 배치 모드 ────────────────────────────────────────────────────────

        /// <summary>
        /// 명령줄에서 부른다. 에디터를 닫아 두고 돌릴 수 있어서, 빌드가 도는 30~90분 동안
        /// 유니티를 다른 일에 쓸 수 있다.
        ///
        /// <code>
        /// Unity.exe -quit -batchmode -nographics -projectPath "C:\...\FP" ^
        ///           -executeMethod Demo.EditorTools.BuildPipelineTool.Batch ^
        ///           -buildTarget Android -logFile build.log
        /// </code>
        ///
        /// 인자 <c>-demoTarget android|androidApk|webgl</c> 로 무엇을 낼지 고른다.
        /// 없으면 Android AAB다.
        ///
        /// <b>실패하면 종료 코드가 1이다</b> — 스크립트로 엮을 수 있어야 한다.
        /// </summary>
        public static void Batch()
        {
            string want = Arg("-demoTarget") ?? "android";
            bool ok;

            switch (want.ToLowerInvariant())
            {
                case "webgl": ok = Run(Target.WebGL, false); break;
                case "androidapk": ok = Run(Target.Android, false); break;
                default: ok = Run(Target.Android, true); break;
            }

            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static string Arg(string name)
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++)
                if (string.Equals(a[i], name, StringComparison.OrdinalIgnoreCase)) return a[i + 1];
            return null;
        }

        // ── 본체 ─────────────────────────────────────────────────────────────

        static bool Run(Target target, bool appBundle)
        {
            // 한 시간을 기다린 끝에 "ID가 비었다"로 실패하지 않도록 먼저 본다.
            List<BuildCheck.Note> notes = BuildCheck.Collect();
            Debug.Log(BuildCheck.Format(notes));
            if (BuildCheck.Blocked(notes))
            {
                Debug.LogError("[빌드] 막는 것이 있어 시작하지 않았다. 위 목록을 볼 것.");
                return false;
            }

            string[] scenes = EnabledScenes();
            if (scenes.Length == 0) { Debug.LogError("[빌드] 켜진 씬이 없다"); return false; }

            BuildTarget bt = target == Target.Android ? BuildTarget.Android : BuildTarget.WebGL;
            BuildTargetGroup grp = target == Target.Android
                                 ? BuildTargetGroup.Android : BuildTargetGroup.WebGL;

            // <b>플랫폼 전환은 이 메서드를 죽일 수 있다.</b>
            //
            // 바꾸면 UNITY_ANDROID 같은 정의가 달라져 스크립트가 재컴파일되고,
            // 그러면 <b>도메인이 리로드되면서 실행 중인 static 메서드가 통째로 사라진다.</b>
            // 비동기(SwitchActiveBuildTargetAsync)는 더 나쁘다 — 즉시 반환하므로
            // 전환이 끝나기도 전에 BuildPlayer가 돈다.
            //
            // 그래서 동기로 바꾸고, 살아남았는지 <b>직접 확인</b>한다. 못 살아남으면
            // 한 번 더 부르면 된다 — 그때는 이미 그 플랫폼이라 바로 빌드로 간다.
            // 텍스처 686장을 전부 재압축하므로 여기서 몇십 분이 간다.
            if (EditorUserBuildSettings.activeBuildTarget != bt)
            {
                Debug.Log("[빌드] 플랫폼을 " + bt + " 로 바꾼다 — 텍스처를 전부 다시 "
                        + "임포트하므로 오래 걸린다. 재컴파일로 이 작업이 끊기면 "
                        + "같은 메뉴를 한 번 더 누르면 된다.");

                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(grp, bt))
                {
                    Debug.LogError("[빌드] 플랫폼 전환 실패 — " + bt
                                 + " 모듈이 설치돼 있는가? (Unity Hub > Add modules)");
                    return false;
                }

                if (EditorUserBuildSettings.activeBuildTarget != bt)
                {
                    Debug.LogError("[빌드] 전환을 요청했는데 아직 "
                                 + EditorUserBuildSettings.activeBuildTarget
                                 + " 이다. 전환이 끝난 뒤 같은 메뉴를 다시 누를 것.");
                    return false;
                }
            }

            Shape(target, appBundle);

            string path = OutPath(target, appBundle);
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            var opt = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = path,
                target = bt,
                targetGroup = grp,
                options = BuildOptions.None,
            };

            Debug.Log("[빌드] 시작 — " + bt + (appBundle ? " (AAB)" : "")
                    + " v" + PlayerSettings.bundleVersion + "\n  -> " + path);

            BuildReport report = UnityEditor.BuildPipeline.BuildPlayer(opt);
            Debug.Log(BuildSize.Format(report, target == Target.Android));

            return report.summary.result == BuildResult.Succeeded;
        }

        /// <summary>
        /// 산출물의 <b>형태</b>만 정한다. Player Settings는 안 건드린다 —
        /// 사람이 에디터에서 바꾼 것이 빌드 때마다 되돌아가면 안 된다.
        /// </summary>
        static void Shape(Target target, bool appBundle)
        {
            if (target == Target.Android)
            {
                EditorUserBuildSettings.buildAppBundle = appBundle;
                return;
            }

            // WebGL: 예산이 100MB다. Brotli가 가장 작고, 그만큼 압축이 오래 걸린다.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;

            // 예외 처리를 끄면 코드가 작아지고 빨라진다. 대신 스택트레이스가 안 나온다 —
            // 디버그가 필요하면 ExplicitlyThrownExceptionsOnly 로 올릴 것.
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
        }

        static string[] EnabledScenes()
        {
            var list = new List<string>();
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
                if (s.enabled && !string.IsNullOrEmpty(s.path) && File.Exists(s.path))
                    list.Add(s.path);
            return list.ToArray();
        }

        /// <summary>
        /// 어디에 낼 것인가. <b>버전을 파일 이름에 박는다</b> — 어느 빌드를 올렸는지
        /// 나중에 알 수 없으면 스토어 업로드에서 반드시 헷갈린다.
        /// </summary>
        static string OutPath(Target target, bool appBundle)
        {
            string name = Slug(PlayerSettings.productName) + "_v" + PlayerSettings.bundleVersion;
            string root = Path.Combine(Directory.GetParent(Application.dataPath).FullName, OutRoot);

            if (target == Target.WebGL) return Path.Combine(root, "WebGL", name);
            return Path.Combine(root, "Android", name + (appBundle ? ".aab" : ".apk"));
        }

        static string Slug(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s)
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.Length == 0 ? "Build" : sb.ToString();
        }
    }
}
