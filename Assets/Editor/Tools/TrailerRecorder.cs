using System.IO;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 시연 영상 녹화 설정을 한 번에 맞춘다 [지시 2026-10-19].
    ///
    /// <b>왜 코드로 만드나.</b> Recorder 창을 사람이 매번 맞추면 해상도·프레임·코덱을
    /// 틀리기 쉽고, 다시 녹화할 때 지난번과 다른 설정이 된다. 한 번 눌러 맞춰 두면
    /// 플레이 버튼과 녹화 버튼만 누르면 된다.
    ///
    /// <b>Game 뷰를 찍는다.</b> 이 게임의 UI 는 전부 IMGUI(OnGUI)라 카메라만 찍으면
    /// <b>HUD 도 조작법도 타격 글자도 안 나온다</b> — 전부 Game 뷰에서만 합성된다.
    /// (같은 함정을 MCP 스크린샷에서 겪은 적이 있다.)
    ///
    /// <b>해상도를 1920x1080 으로 박는다.</b> Game 뷰 크기를 그대로 쓰면 창 크기에
    /// 따라 결과가 달라진다. 발표 자료에 넣을 것이라 고정이 낫다.
    /// </summary>
    public static class TrailerRecorder
    {
        const string SettingsPath = "Assets/_Project/Recorder/TrailerRecorder.asset";
        const string OutDir = "Recordings";

        // ── 수치 [제안·미승인 2026-10-19] ────────────────────────────────────
        const int Width = 1920;
        const int Height = 1080;
        const float Fps = 60f;

        [MenuItem("Demo/트레일러/녹화 설정 맞추기", priority = 300)]
        public static void Setup()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));

            var ctrl = AssetDatabase.LoadAssetAtPath<RecorderControllerSettings>(SettingsPath);
            if (ctrl == null)
            {
                ctrl = ScriptableObject.CreateInstance<RecorderControllerSettings>();
                AssetDatabase.CreateAsset(ctrl, SettingsPath);
            }

            // 이미 들어 있던 레코더는 비우고 새로 깐다 — 두 번 눌러도 쌓이지 않게.
            foreach (var old in new System.Collections.Generic.List<RecorderSettings>(ctrl.RecorderSettings))
                ctrl.RemoveRecorder(old);

            var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movie.name = "Rubble Rush 시연";
            movie.Enabled = true;

            // H.264 / MP4 · 높은 품질. CoreEncoder 는 유니티가 들고 오는 기본 인코더라
            // 따로 설치할 것이 없다.
            // <b>OutputFormat 은 건드리지 않는다.</b> 더 이상 쓰지 말라고 표시된 API 이고
            // (CS0618), 아래 EncoderSettings.Codec 이 같은 것을 정한다.
            movie.EncoderSettings = new CoreEncoderSettings
            {
                Codec = CoreEncoderSettings.OutputCodec.MP4,
                EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
            };

            movie.CaptureAudio = true;          // 타격음이 이 게임의 절반이다
            movie.CaptureAlpha = false;

            movie.ImageInputSettings = new GameViewInputSettings
            {
                OutputWidth = Width,
                OutputHeight = Height,
            };

            // <b>수동 모드.</b> 구간을 미리 정하지 않고, 사람이 녹화 버튼으로 시작하고
            // 멈춘다 — 한 판이 얼마나 걸릴지 모르는 상태에서 프레임 구간을 박으면
            // 중간에 잘린다.
            movie.RecordMode = RecordMode.Manual;
            movie.FrameRatePlayback = FrameRatePlayback.Constant;
            movie.FrameRate = Fps;
            movie.CapFrameRate = true;

            // <b>Path.Combine 을 쓰면 안 된다.</b> 레코더의 자리표시자 &lt;Take&gt; 에 들어 있는
            // 꺾쇠를 모노의 Path.Combine 이 불법 문자로 보고 ArgumentException 을 던진다
            // [실측 2026-10-19]. 슬래시로 직접 잇는다.
            string root = (Path.GetDirectoryName(Application.dataPath) ?? ".").Replace('\\', '/');
            movie.OutputFile = root + "/" + OutDir + "/RubbleRush_<Take>";

            AssetDatabase.AddObjectToAsset(movie, ctrl);
            ctrl.AddRecorderSettings(movie);
            ctrl.SetRecordModeToManual();
            ctrl.FrameRate = Fps;

            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var win = EditorWindow.GetWindow<RecorderWindow>();
            win.SetRecorderControllerSettings(ctrl);
            win.Show();

            Debug.Log(
                "[트레일러] 녹화 설정을 맞췄다.\n" +
                $"  해상도   {Width}x{Height} · {Fps}fps · MP4(H.264) · 소리 포함\n" +
                $"  찍는 것  Game 뷰 (UI 가 IMGUI 라 카메라만 찍으면 HUD 가 안 나온다)\n" +
                $"  나오는 곳 {OutDir}/RubbleRush_1.mp4  (다시 찍으면 _2, _3 … 으로 쌓인다)\n" +
                "  쓰는 법  Recorder 창에서 START RECORDING -> 플레이 -> 한 판 하고 STOP\n" +
                "  설정 파일 " + SettingsPath);
        }

        /// <summary>
        /// 녹화물이 저장소에 들어가지 않게 막는다. 영상은 수십~수백 MB라
        /// 깃허브에 올릴 것이 아니다.
        /// </summary>
        [MenuItem("Demo/트레일러/녹화물 git 제외하기", priority = 301)]
        public static void Ignore()
        {
            const string marker = "/Recordings/";
            string path = Path.Combine(
                Path.GetDirectoryName(Application.dataPath) ?? ".", ".gitignore");

            string text = File.Exists(path) ? File.ReadAllText(path) : "";
            if (text.Contains(marker))
            {
                Debug.Log("[트레일러] .gitignore 에 이미 들어 있다.");
                return;
            }

            File.AppendAllText(path,
                "\n# 시연 영상 — 수백 MB 라 저장소에 올리지 않는다 [2026-10-19]\n/Recordings/\n");
            Debug.Log("[트레일러] .gitignore 에 /Recordings/ 를 넣었다.");
        }
    }
}
