using UnityEngine;

namespace Demo.Destruction
{
    /// <summary>
    /// 파괴 화면에서 건물을 대신하는 <b>조각 판 더미</b> [분리 2026-10-01].
    ///
    /// 필드의 VARCO 메시는 통짜라 런타임 절단이 비싸다. 대신 벽 앞에 조각으로 나뉜 판을
    /// 깊이 방향으로 겹쳐 세우고 원본은 숨긴다. 한 번에 한 채만 세우므로 조각이 수백 개여도 감당된다.
    ///
    /// <b>왜 떼어냈나.</b> <see cref="DemolitionSession"/>이 550줄이었다 (CLAUDE.md 규칙 3).
    /// 그 안에 섞여 있던 두 가지 일 — '프록시를 세우고 치우는 것'과 '한 판의 진행(게이지·탭·마무리)' —
    /// 은 공유 상태가 거의 없다. 세션은 이제 Build/Show/SetGauge/Clear만 부른다.
    ///
    /// MonoBehaviour가 아닌 이유는 <see cref="Player.PunchRush"/>·<see cref="FractureFall"/>와 같다 —
    /// 수명주기도 코루틴도 필요 없고, 컴포넌트로 만들면 배선과 실행 순서 문제만 새로 생긴다.
    /// </summary>
    [System.Serializable]
    public sealed class FractureProxy
    {
        [Header("두께 — 깊이 방향으로 겹을 쌓는다 [지시 2026-09-23]")]
        [SerializeField, Range(1, 8),
         Tooltip("벽을 깊이 방향으로 몇 겹으로 <b>따로</b> 깎을 것인가 [지시 2026-09-23].\n" +
                 "1이면 파편 하나가 벽 두께를 통째로 갖는다 — 두께가 곧 파편의 깊이라\n" +
                 "부서진 자리에 단면이 그대로 드러나고, 앞뒤가 언제나 같이 무너진다.\n" +
                 "2 이상은 depthLag와 함께 써서 '앞에서부터 파먹어 들어가는' 연출을 만들 때만\n" +
                 "의미가 있다. 겹마다 정점을 한 벌씩 더 쓰므로 그만큼 파편이 굵어진다")]
        int depthLayers = 3;

        [SerializeField, Range(0f, 3f),
         Tooltip("겹 두께를 <b>앞에서 뒤로 갈수록 두껍게</b> 하는 정도 [지시 2026-09-23].\n" +
                 "겹 i의 두께가 (i+1)의 이 제곱에 비례한다. 0이면 모든 겹이 같은 두께다.\n" +
                 "2면 세 겹이 1:4:9로 나뉘어, 1.8m 벽이 0.13 / 0.51 / 1.16m가 된다 — " +
                 "앞은 얇은 겉껍질, 뒤로 갈수록 두꺼운 속살이라 부술수록 <b>더 단단한 것이 " +
                 "나오는</b> 그림이 된다.\n" +
                 "배열이 아니라 지수인 이유는 depthLayers를 바꿔도 모양이 유지되기 때문이다")]
        float depthRamp = 2f;

        [SerializeField, Range(0f, 0.9f),
         Tooltip("뒷겹이 앞겹보다 얼마나 늦게 깎이는가. 한 층 분량 대비 비율이다. " +
                 "<b>0이면 모든 겹이 동시에</b> 무너진다 — 벽 하나가 통째로 가는 그림이고, " +
                 "겹이 하나일 때는 아무 의미가 없다")]
        float depthLag = 0f;

        [SerializeField, Range(20000, 200000),
         Tooltip("파괴 화면 전체가 쓸 정점 수 [지시 2026-09-23]. 겹 수로 나눠 겹마다 배분된다. " +
                 "삼각기둥은 칸당 15정점이라 겹을 늘리면 파편이 굵어질 수밖에 없다 — " +
                 "예산을 고정해 두면 겹을 바꿔도 알아서 균형이 잡힌다. " +
                 "CLAUDE.md 성능 예산: 프레임당 삼각형 15만")]
        int vertexBudget = 120000;

        [SerializeField, Range(0.05f, 3f),
         Tooltip("<b>바라는</b> 벽 두께(m) [지시 2026-09-23]. 겹이 이 값을 n분의 1씩 나눠 갖는다. " +
                 "겹이 하나면 파편 하나가 이 두께를 통째로 가지므로, 부서진 자리에 " +
                 "그만한 단면이 그대로 드러난다 — 입체감은 거의 전부 여기서 나온다. " +
                 "<b>바라는</b> 값인 이유는 건물이 이보다 얇을 수 있기 때문이다 — " +
                 "실제 두께는 maxThicknessRatio가 건물 깊이에 맞춰 깎는다")]
        float wallThickness = 1.8f;

        [SerializeField, Range(0.2f, 0.8f),
         Tooltip("벽 두께가 <b>건물 반깊이</b>의 몇 할을 넘지 못하게 할 것인가 [제안 2026-09-23]. " +
                 "wallThickness가 절대 미터값이라 그냥 두면 얇은 건물에서 벽이 뒤로 뚫고 나온다 — " +
                 "건물 앞뒤 깊이가 1.0m(경량)부터 12m(견고)까지 12배 차이라, " +
                 "한 값이 모두에게 맞을 수가 없다. " +
                 "0.6이면 남은 4할이 속(어둠)으로 남아, 벽을 뚫었을 때 뒤가 비는 것이 보인다")]
        float maxThicknessRatio = 0.6f;

        [SerializeField, Tooltip("벽 단면(속살) 색. 건물 속보다 <b>밝아야</b> 한다 — " +
                                 "단면과 속이 같은 색이면 구멍이 다시 평면으로 읽힌다")]
        Color wallInnerColor = new Color(0.26f, 0.25f, 0.23f, 1f);

        [SerializeField, Tooltip("건물 속 벽까지의 <b>최소</b> 깊이(m). " +
                                 "속은 언제나 벽 뒤에 있어야 하므로, 벽이 이보다 두꺼우면 " +
                                 "속이 알아서 더 물러난다 — 두 값을 손으로 맞춰 둘 필요가 없다")]
        float interiorDepth = 1.0f;

        [SerializeField, Tooltip("건물 속 어둠. 완전 검정으로 두면 얇은 금이 잉크선처럼 보인다")]
        Color interiorColor = new Color(0.06f, 0.06f, 0.07f, 1f);

        GameObject root;                 // FractureProxy — 겹과 속의 부모
        WallFracture[] layers;
        Renderer[] hiddenOriginals;
        Renderer[] proxyRenderers;
        bool shown;
        readonly WallMaterials materials = new WallMaterials();

        /// <summary>원본 대신 조각 판이 보이고 있는가. 세션이 교체 시점을 판단한다.</summary>
        public bool Shown => shown;

        /// <summary>
        /// 아직 쏟아지거나 내려앉는 중인 겹이 있는가.
        /// 세션이 <b>화면을 언제 닫을지</b> 이것으로 판단한다 — 다 쏟아진 뒤에 닫아야
        /// 마지막 탭이 곧 마지막 쾅이 된다.
        /// </summary>
        public bool Animating
        {
            get
            {
                if (layers == null) return false;
                for (int i = 0; i < layers.Length; i++)
                    if (layers[i] != null && layers[i].Animating) return true;
                return false;
            }
        }

        public void Build(Destructible target)
        {
            Clear();
            if (target == null || target.Data == null) return;

            // 비활성 자식까지 전부 모은다. Destructible은 Intact/Damaged/Rubble 셋을 자식으로
            // 달아두고 켜고 끄는 구조라, 지금 켜져 있는 하나만 숨기면 나중에 단계가 바뀌는 순간
            // 꺼져 있던 메시가 조각 판 뒤에서 되살아난다 — 그게 "원본이 다시 보인다"였다.
            Renderer[] all = target.GetComponentsInChildren<Renderer>(true);
            Renderer src = null;
            for (int i = 0; i < all.Length; i++)
                if (all[i].gameObject.activeInHierarchy) { src = all[i]; break; }
            Material mat = src != null ? src.sharedMaterial : null;

            var go = new GameObject("FractureProxy");
            root = go;
            go.transform.SetParent(target.transform, false);

            // 네 면 중 하나에 <b>딱 맞춰</b> 세운다 [지시 2026-09-23].
            // 예전에는 플레이어 방향을 그대로 따라 비스듬히 섰는데, 파사드 그림을 입히는
            // 지금은 그러면 안 된다 — 면에 안 맞으면 입구가 모서리에 걸쳐 잘린다.
            // 면은 GameModeController가 상호작용 순간에 확정해 둔 값을 그대로 쓴다.
            Core.GameModeController mode = Core.GameModeController.Instance;
            int face = mode != null ? mode.TargetFace : StructureFace.Front;

            Vector3 size = target.Data.colliderSize;
            Vector3 outward = StructureFace.Normal(target.transform, face);
            float width = StructureFace.Width(size, face);

            go.transform.position = target.transform.position
                                  + outward * (StructureFace.Offset(size, face) + 0.02f);
            go.transform.rotation = Quaternion.LookRotation(-outward, Vector3.up);

            // 깊이 방향으로 겹을 쌓는다 [지시 2026-09-23].
            //
            // 한 겹이면 조각이 지워질 때 벽 두께가 통째로 사라진다 — 주먹 한 방에 앞에서
            // 뒤까지 뚫리는 그림이고, 그게 "평면 같다"의 정체였다. 겹마다 <b>자기 진행도</b>를
            // 주면 그때부터 두께가 그림이 아니라 <b>재료</b>가 된다.
            //
            // 앞겹만 텍스처를 입는다. 뒷겹은 벽의 속살이라 그림이 없는 것이 맞다 —
            // 같은 파사드를 뒤에도 붙이면 창문이 두 겹으로 겹쳐 보인다.
            int floors = Mathf.Max(1, target.Data.floors);
            int n = Mathf.Max(1, depthLayers);

            // 겹이 <b>같은 무늬</b>를 쓰게 하는 씨앗. 건물마다 달라야 매번 같은 모양으로
            // 부서지지 않고, 겹끼리는 같아야 조각이 깊이로 맞물린다.
            int layoutSeed = Random.Range(int.MinValue, int.MaxValue);

            // 벽은 <b>건물보다 두꺼울 수 없다</b> [지시 2026-09-23].
            //
            // wallThickness가 절대 미터값이라 그냥 쓰면 얇은 건물에서 벽이 뒤로 뚫고 나온다.
            // 경량 건물은 앞뒤가 1m뿐이라 1.8m 벽이면 건물의 1.8배다. 금 두께를 미터로 줬다가
            // 작은 파편이 통째로 사라졌던 것과 같은 함정이다 - 크기가 12배 벌어지는 값에
            // 절대값 하나를 물리면 어느 한쪽이 반드시 깨진다.
            float half = StructureFace.Offset(size, face);
            float thick = Mathf.Min(wallThickness, half * maxThicknessRatio);

            // 겹마다 <b>다른 두께</b>를 갖는다 [지시 2026-09-23].
            // 앞은 얇은 껍질, 뒤로 갈수록 두꺼운 속살이다. 합은 언제나 벽 두께와 같다.
            float[] band = new float[n];
            float wsum = 0f;
            for (int i = 0; i < n; i++)
            {
                band[i] = Mathf.Pow(i + 1, depthRamp);
                wsum += band[i];
            }
            for (int i = 0; i < n; i++) band[i] = thick * band[i] / wsum;

            layers = new WallFracture[n];
            float zAt = 0f;
            for (int i = 0; i < n; i++)
            {
                var lgo = new GameObject("FractureLayer" + i);
                lgo.transform.SetParent(go.transform, false);
                lgo.transform.localPosition = new Vector3(0f, 0f, zAt);   // +Z가 건물 안쪽

                lgo.AddComponent<MeshFilter>();
                lgo.AddComponent<MeshRenderer>();
                layers[i] = lgo.AddComponent<WallFracture>();

                // 겹마다 <b>다른</b> 무늬를 뽑는다 [지시 2026-09-23].
                //
                // 예전에는 겹끼리 똑같이 맞췄다. 앞뒤가 따로 무너지는 것을 막으려던 것인데,
                // 무늬까지 같을 필요는 없었다 — 따로 무너지던 진짜 원인은 겹마다 진행도를
                // 어긋나게 주던 depthLag였고 그건 0으로 껐다.
                //
                // 무늬가 같으면 파편이 깊이로 <b>곧은 기둥</b>이 되어, 부서진 자리 단면이
                // 격자처럼 가지런하다. 겹마다 다르게 뽑으면 앞 파편의 경계와 뒤 파편의 경계가
                // 어긋나 단면이 켜켜이 물린다 — 깊이에도 보로노이가 한 번 더 걸리는 셈이다.
                layers[i].Build(new Vector2(width, size.y), floors,
                                i == 0 ? materials.Facade(target.Data, face, mat)
                                       : materials.Inner(wallInnerColor),
                                band[i], layoutSeed + i * 7919, vertexBudget / n, i == 0, thick);
                zAt += band[i];
            }

            // 맨 뒤 겹까지 뚫렸을 때 보이는 건물 속.
            //
            // 속은 <b>반드시 벽 뒤</b>에 있어야 한다. 앞에 오면 벽이 어둠을 뚫고 튀어나와,
            // 부순 자리에 구멍이 아니라 벽 단면만 남는다. 그래서 두 값을 손으로 맞추는 대신
            // 벽 두께에서 끌어올린다 - 두께를 얼마로 바꾸든 이 관계가 깨지지 않는다.
            float depth = Mathf.Min(Mathf.Max(interiorDepth, thick * 1.25f), half * 0.95f);
            var shell = go.AddComponent<FractureShell>();
            shell.Build(width, size.y, depth, interiorColor);
            shell.Follow(layers[0]);

            // <b>아직 바꾸지 않는다</b> [고침 2026-10-01].
            //
            // 전환은 컷이 아니라 <b>카메라 비행</b>(FollowCamera.modeBlendSeconds 0.35초)이라,
            // 그 동안 플레이어는 이 더미를 <b>비스듬히</b> 보게 된다 — 실측 63도에서 시작한다.
            // 그러면 깊이가 다른 네 장이 각각 다른 자리로 투영돼 화면에서 115~172px 어긋나
            // 미끄러진다. 같은 실루엣 네 개가 겹쳐 보이는 것이 그것이다 [지적 2026-10-01].
            // 그래서 정면에 설 때까지 원본을 그대로 두고 이쪽을 숨긴다 (Show 참조).
            proxyRenderers = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < proxyRenderers.Length; i++) proxyRenderers[i].enabled = false;
            hiddenOriginals = all;
            shown = false;
        }

        /// <summary>
        /// 원본 ↔ 조각 판을 바꾼다. 비행이 끝났거나(정면에 섰다) 플레이어가 먼저 두드렸을 때.
        /// 바꿔치기가 안 보인다는 것은 쟀다 — 정면에서 두 그림의 최대 픽셀 차이가 2/255다.
        /// </summary>
        public void Show()
        {
            if (shown || proxyRenderers == null) return;
            shown = true;
            for (int i = 0; i < proxyRenderers.Length; i++)
                if (proxyRenderers[i] != null) proxyRenderers[i].enabled = true;
            if (hiddenOriginals != null)
                for (int i = 0; i < hiddenOriginals.Length; i++)
                    if (hiddenOriginals[i] != null) hiddenOriginals[i].enabled = false;
        }

        public void Clear()
        {
            if (hiddenOriginals != null)
            {
                for (int i = 0; i < hiddenOriginals.Length; i++)
                    if (hiddenOriginals[i] != null) hiddenOriginals[i].enabled = true;
                hiddenOriginals = null;
            }

            // 겹이 아니라 <b>껍데기부터</b> 지운다. 겹은 껍데기의 자식이라 같이 딸려 간다.
            // 겹만 지우면 빈 FractureProxy가 씬에 계속 쌓인다 — 실측으로 3개까지 남았다.
            if (root != null) Object.Destroy(root);
            root = null;
            layers = null;
            proxyRenderers = null;
            shown = false;
        }

        /// <summary>
        /// 게이지를 겹마다 <b>다른 진행도</b>로 나눠 먹인다 [지시 2026-09-23].
        ///
        /// 뒷겹이 늦게 깎여야 "앞을 떼어내니 속살이 나왔다"가 된다. 전부 같은 값을 주면
        /// 겹을 아무리 쌓아도 동시에 뚫려서 한 겹과 구별되지 않는다.
        ///
        /// 지연을 <b>한 층 분량으로 환산</b>하는 것이 중요하다. 진행도는 건물 전체를 0~1로
        /// 재므로, 0.3을 그대로 빼면 6층 건물에서 앞겹이 <b>1.8층</b>을 앞서간다 —
        /// 깊이가 아니라 층이 어긋나서, 1층을 부수는데 뒷겹은 아직 시작도 안 한 꼴이 된다.
        /// </summary>
        public void SetGauge(float gauge, int floors)
        {
            if (layers == null) return;
            floors = Mathf.Max(1, floors);

            // depthLag는 <b>맨 앞겹과 맨 뒷겹 사이의 총 간격</b>이다. 겹 수로 한 번 더
            // 나눠야 겹을 늘려도 그 총량이 유지된다. 안 나누면 4겹일 때 뒷겹이 0.9층이나
            // 뒤처져서, 1층 앞면을 다 깎았는데 뒷겹은 시작도 안 한 꼴이 된다.
            float lag = depthLag / floors / Mathf.Max(1, layers.Length - 1);

            // 마지막 겹도 게이지 1에서는 다 깎여야 한다. 그래서 남은 구간에 맞춰 늘린다.
            float span = Mathf.Max(0.01f, 1f - lag * (layers.Length - 1));

            for (int i = 0; i < layers.Length; i++)
                if (layers[i] != null)
                    layers[i].SetProgress(Mathf.Clamp01((gauge - i * lag) / span));
        }

        /// <summary>머티리얼을 거둔다. 세션이 사라질 때 부른다.</summary>
        public void Dispose() => materials.Dispose();
    }
}
