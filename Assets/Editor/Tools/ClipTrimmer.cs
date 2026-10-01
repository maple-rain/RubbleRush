using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// VARCO 클립의 <b>정지 꼬리를 자르고 이음매가 맞는 한 사이클을 찾는다</b> (CLAUDE.md 규칙 8).
    ///
    /// <b>왜 필요한가.</b> VARCO의 Animate 노드는 길이를 넉넉히 뽑고 뒤를 정지로 채운다.
    /// Walk를 실측했더니 82프레임 중 <b>68~80이 움직임 0.0도</b>였고, 마지막 한 프레임에서
    /// 루프 시작점으로 <b>158도를 튀었다</b>. 3배속으로 돌리면 0.17초 미끄러지다 툭 튄다 —
    /// 화면에서는 "걷다가 스키딩하는" 것으로 보인다 [지적 2026-09-28].
    ///
    /// <b>이음매를 어떻게 고르는가.</b> 자세만 맞추면 보폭 위상은 같아도 가속도가 어긋나
    /// 한 박자 절뚝인다. 그래서 프레임 i와 i+p를 볼 때 <b>다음 프레임까지 같이</b> 비교한다 —
    /// 자세와 속도를 동시에 맞추는 값싼 방법이다.
    ///
    /// <b>이 도구가 검증하지 않는 것</b> (CLAUDE.md 역할 분담):
    /// <list type="bullet">
    /// <item>고른 사이클이 <b>보기 좋은지</b>. 이음매 오차만 재지 연기의 질은 모른다</item>
    /// <item>클립이 <b>루프해야 하는지</b>. 이미 켜져 있는 loopTime을 신호로 쓸 뿐이다</item>
    /// <item>남은 <b>발 미끄러짐</b>. 그건 이동 속도와의 관계라 여기서 잴 수 없다</item>
    /// </list>
    /// </summary>
    public static partial class ClipTrimmer
    {
        const string Folder = "Assets/_Project/Art/Models/Robot";
        const string BaseModel = Folder + "/R_Demolition.fbx";

        /// <summary>움직임이 중앙값의 이 비율 아래면 '죽은' 프레임으로 본다.</summary>
        const float DeadRatio = 0.12f;

        /// <summary>이보다 짧은 주기는 사이클이 아니라 잡음으로 본다.</summary>
        const int MinPeriod = 8;

        /// <summary>
        /// 마지막 보고문 전문. 콘솔은 여러 줄 로그의 첫 줄만 보여 줄 때가 있어서 따로 남긴다 —
        /// 자동화(MCP)로 돌릴 때는 이쪽을 읽는 편이 확실하다.
        /// </summary>
        public static string LastReport { get; private set; }

        [MenuItem("Demo/8. 애니메이션 클립 분석 (보고만)", priority = 50)]
        public static void Report() { Run(false); }

        [MenuItem("Demo/8b. 애니메이션 클립 다듬기 (적용)", priority = 51)]
        public static void Apply() { Run(true); }

        struct Cut
        {
            public string Fbx;
            public string Clip;
            public int First;       // 원본 테이크 기준 프레임
            public int Last;
            public int DeadHead;
            public int DeadTail;
            public float SeamBefore;
            public float SeamAfter;
            public bool Loop;
            public int Bones;           // 이음매 오차를 뼈당으로 환산할 때 쓴다
            public float Median;        // 이 클립의 프레임당 움직임 중앙값. 이음매의 잣대다
        }

        static void Run(bool apply)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(BaseModel);
            if (model == null)
            {
                Debug.LogError("[ClipTrimmer] 기본 모델을 못 찾음: " + BaseModel);
                return;
            }

            // 클립을 얹어 볼 뼈대가 필요하다. 씬에 잠깐 세웠다 지운다.
            GameObject rig = Object.Instantiate(model);
            rig.hideFlags = HideFlags.HideAndDontSave;
            Transform[] bones = rig.GetComponentsInChildren<Transform>(true);

            var cuts = new List<Cut>();
            AnimationMode.StartAnimationMode();
            try
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { Folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.Contains("@")) continue;      // 기본 모델에는 클립이 없다
                    Scan(path, rig, bones, cuts);
                }
            }
            finally
            {
                // 재임포트는 반드시 샘플링을 끝낸 뒤에 한다. 도중에 하면 들고 있는
                // 클립 참조가 죽어 MissingReferenceException이 난다.
                AnimationMode.StopAnimationMode();
                Object.DestroyImmediate(rig);
            }

            LastReport = Format(cuts, apply);
            Debug.Log(LastReport);
            if (apply) ApplyCuts(cuts);
        }
    }
}
