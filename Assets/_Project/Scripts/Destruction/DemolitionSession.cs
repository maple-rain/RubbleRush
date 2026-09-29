using Demo.Core;
using Demo.Data;
using UnityEngine;

namespace Demo.Destruction
{
    /// <summary>
    /// 파괴 화면 한 판 [지시 2026-09-22].
    ///
    /// 건물에 다가가 상호작용하면 여기로 넘어온다. 이동과 케이블이 멈추고,
    /// <b>화면을 두드리는 것만</b>으로 게이지가 오른다. 게이지가 차면 건물이 무너지고 필드로 돌아간다.
    ///
    /// 파괴를 별도 화면으로 뺀 이유는 성능이다 — 조각 파쇄와 연결 그래프는 건물 하나에
    /// 조각 수십 개와 인접 관계 수십 쌍을 만든다. 필드의 구조물 60개에 동시에 돌릴 수 없지만
    /// <b>한 번에 하나면</b> 감당할 수 있다.
    ///
    /// <b>지금 구현된 것</b>: 모드 전환, 탭 입력, 게이지, 게이지에 연동된 3단계 메시 교체,
    /// 완료 시 잔해 생성과 필드 복귀.
    ///
    /// <b>아직 아닌 것</b>: 조각 파쇄와 연결 그래프. 참조 영상(@데브조생 "게임에서 벽은 어떻게 부술까")의
    /// 기법 — 파쇄 무늬를 미리 만들고, 맞는 순간 충격점 근처만 자르고, 떼어낸 조각만 물리로 두고,
    /// 남은 조각은 인접 관계로 붙여 둔 뒤 바닥에서 연결 탐색해 끊어진 덩어리만 떨어뜨린다.
    /// 조각을 격자로 할지 보로노이로 할지가 미결이라(HANDOFF) 여기서는 자리만 비워 뒀다.
    /// </summary>
    public sealed class DemolitionSession : MonoBehaviour
    {
        [SerializeField] Player.InputRouter input;
        [SerializeField] DebrisSpawner spawner;
        [SerializeField] DestructionFX fx;

        /// <summary>0~1. 화면을 두드릴 때마다 오른다.</summary>
        public float Gauge { get; private set; }

        /// <summary>지금 부수고 있는 대상. 필드에서는 null.</summary>
        public Destructible Target { get; private set; }

        /// <summary>이번 판에 두드린 횟수. 연출과 결과 화면이 읽는다.</summary>
        public int TapCount { get; private set; }

        Vector3 targetPoint;
        GameObject root;          // FractureProxy — 겹과 속의 부모
        WallFracture[] layers;
        Material innerMat;
        Renderer[] hiddenOriginals;

        /// <summary>
        /// 파사드를 입히는 머티리얼. <b>한 개를 재사용한다.</b>
        /// 파괴 화면은 한 번에 하나뿐이라 인스턴스가 하나면 충분하고,
        /// 들어갈 때마다 만들면 나갈 때마다 버려야 하는 쓰레기가 된다 (규칙 6).
        /// </summary>
        Material facadeMat;

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

        [SerializeField, Range(0.2f, 3f),
         Tooltip("마지막 쾅 뒤에 쏟아지는 것을 보여 줄 최대 시간(초). " +
                 "낙하와 내려앉기가 먼저 끝나면 그때 바로 닫는다 — 이 값은 안전장치다")]
        float finishMaxSeconds = 1.2f;

        [SerializeField, Tooltip("건물 속 어둠. 완전 검정으로 두면 얇은 금이 잉크선처럼 보인다")]
        Color interiorColor = new Color(0.11f, 0.10f, 0.09f, 1f);

        void Awake()
        {
            if (input == null) input = FindFirstObjectByType<Player.InputRouter>();
            if (spawner == null) spawner = FindFirstObjectByType<DebrisSpawner>();

            // 연출 컴포넌트는 <b>스스로 붙인다</b> [2026-09-28].
            //
            // 씬에 DestructionFX가 한 번도 없었고, 그래서 fx가 null이라 OnStrike/OnBroken
            // 호출이 전부 조용히 무시되고 있었다 — 연출을 구현해도 아무 일이 안 일어난다.
            // 씬 와이어링은 사람이 하는 것이 원칙이지만(CLAUDE.md 규칙 1),
            // <b>와이어링이 필요 없게 만드는 것</b>이 그 원칙에 어긋나지 않는다.
            // 인스펙터에서 다른 것을 꽂아 두면 그쪽이 이긴다.
            if (fx == null) fx = GetComponent<DestructionFX>();
            if (fx == null) fx = gameObject.AddComponent<DestructionFX>();
        }

        void OnEnable()
        {
            if (GameModeController.Instance != null)
                GameModeController.Instance.Changed += OnModeChanged;
        }

        void OnDisable()
        {
            if (GameModeController.Instance != null)
                GameModeController.Instance.Changed -= OnModeChanged;
        }

        void OnModeChanged(Mode prev, Mode next)
        {
            if (next == Mode.Demolition) Begin(GameModeController.Instance.Target);
            else if (prev == Mode.Demolition) End();
        }

        void Begin(Destructible target)
        {
            Target = target;
            Gauge = 0f;
            TapCount = 0;
            targetPoint = target != null ? target.transform.position : Vector3.zero;

            BuildFractureProxy(target);
        }

        /// <summary>
        /// 파괴 화면 전용 조각 프록시를 세운다.
        ///
        /// 필드의 VARCO 메시는 통짜라 런타임 절단이 비싸다. 대신 벽 앞에 조각으로 나뉜 판을
        /// 겹쳐 세우고 원본은 숨긴다 — 화면 전환이 있어 바뀌는 순간이 안 보인다.
        /// 한 번에 한 채만 세우므로 조각이 수백 개여도 감당된다.
        /// </summary>
        void BuildFractureProxy(Destructible target)
        {
            ClearProxy();
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
                                i == 0 ? FacadeMaterial(target.Data, face, mat) : InnerMaterial(),
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

            // 원본은 전부 숨긴다. 조각 판이 그 자리를 대신한다.
            for (int i = 0; i < all.Length; i++) all[i].enabled = false;
            hiddenOriginals = all;
        }

        void ClearProxy()
        {
            if (hiddenOriginals != null)
            {
                for (int i = 0; i < hiddenOriginals.Length; i++)
                    if (hiddenOriginals[i] != null) hiddenOriginals[i].enabled = true;
                hiddenOriginals = null;
            }

            // 겹이 아니라 <b>껍데기부터</b> 지운다. 겹은 껍데기의 자식이라 같이 딸려 간다.
            // 겹만 지우면 빈 FractureProxy가 씬에 계속 쌓인다 — 실측으로 3개까지 남았다.
            if (root != null) Destroy(root);
            root = null;
            layers = null;

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
        void PushProgress()
        {
            if (layers == null) return;

            int floors = Target != null && Target.Data != null
                ? Mathf.Max(1, Target.Data.floors) : 1;

            // depthLag는 <b>맨 앞겹과 맨 뒷겹 사이의 총 간격</b>이다. 겹 수로 한 번 더
            // 나눠야 겹을 늘려도 그 총량이 유지된다. 안 나누면 4겹일 때 뒷겹이 0.9층이나
            // 뒤처져서, 1층 앞면을 다 깎았는데 뒷겹은 시작도 안 한 꼴이 된다.
            float lag = depthLag / floors / Mathf.Max(1, layers.Length - 1);

            // 마지막 겹도 게이지 1에서는 다 깎여야 한다. 그래서 남은 구간에 맞춰 늘린다.
            float span = Mathf.Max(0.01f, 1f - lag * (layers.Length - 1));

            for (int i = 0; i < layers.Length; i++)
                if (layers[i] != null)
                    layers[i].SetProgress(Mathf.Clamp01((Gauge - i * lag) / span));
        }

        /// <summary>벽의 속살. 그림 없는 콘크리트 한 색이다 — 파사드를 뒤에도 붙이면 창문이 겹쳐 보인다.</summary>
        Material InnerMaterial()
        {
            if (innerMat != null) return innerMat;

            Shader sh = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Universal Render Pipeline/Unlit");
            innerMat = new Material(sh) { name = "WallInner (instance)" };
            if (innerMat.HasProperty(BaseColorId)) innerMat.SetColor(BaseColorId, wallInnerColor);
            if (innerMat.HasProperty(ColorId))     innerMat.SetColor(ColorId, wallInnerColor);
            return innerMat;
        }

        static readonly int BaseMapId   = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId     = Shader.PropertyToID("_Color");

        /// <summary>
        /// 그 면의 그림을 입힌 머티리얼. <b>그림이 아직 없으면 건물 머티리얼을 그대로 돌려준다</b> —
        /// 5종을 한꺼번에 뽑지 못하므로, 뽑힌 종부터 차례로 켜지게 두는 편이 낫다.
        ///
        /// 건물 머티리얼을 <b>복제하지 않고 셰이더만</b> 가져다 새로 만든다. 복제하면 회색 틴트까지
        /// 따라와서 그림이 그만큼 어두워진다. 셰이더를 맞추는 이유는 따로 있다 — 파괴 화면만
        /// 다른 셰이더를 쓰면 그 화면에서만 조명이 달라 보인다.
        /// </summary>
        Material FacadeMaterial(StructureData data, int face, Material fallback)
        {
            Texture2D tex = data != null ? data.Facade(face) : null;
            if (tex == null) return fallback;

            if (facadeMat == null)
            {
                Shader sh = fallback != null ? fallback.shader
                                             : Shader.Find("Universal Render Pipeline/Lit");
                facadeMat = new Material(sh) { name = "FacadeProxy (instance)" };
            }

            if (facadeMat.HasProperty(BaseColorId)) facadeMat.SetColor(BaseColorId, Color.white);
            if (facadeMat.HasProperty(ColorId))     facadeMat.SetColor(ColorId, Color.white);

            if (facadeMat.HasProperty(BaseMapId)) facadeMat.SetTexture(BaseMapId, tex);
            else facadeMat.mainTexture = tex;

            return facadeMat;
        }

        void OnDestroy()
        {
            if (facadeMat != null) Destroy(facadeMat);
            facadeMat = null;
            if (innerMat != null) Destroy(innerMat);
            innerMat = null;
        }

        void End()
        {
            ClearProxy();
            Target = null;
            Gauge = 0f;
        }

        void Update()
        {
            GameModeController mode = GameModeController.Instance;
            if (mode == null || !mode.IsDemolition || Target == null) return;

            // 마지막 쾅 뒤에는 입력을 안 받는다. 쏟아지는 것을 보여 주고 나서 닫는다.
            if (finishing) { TickFinish(); return; }

            if (input == null || !input.ActionPressedThisFrame) return;

            Tap();
        }

        bool finishing;
        float finishElapsed;

        /// <summary>
        /// 마지막 층이 쏟아지는 <b>동안</b>을 기다린다 [지시 2026-09-23].
        ///
        /// 예전에는 게이지가 1이 되는 순간 바로 닫았다. 그래서 shatterAt이 1보다 작아야만
        /// 했다 — 마지막 층을 미리 터뜨려 두고, 남은 빈 탭 동안 쏟아지는 것을 보여 준 것이다.
        /// 그 대가로 <b>다 부순 벽을 몇 번 더 두드려야</b> 끝났다.
        /// 기다리는 일을 여기로 옮기면 마지막 탭이 곧 마지막 쾅이 된다.
        /// </summary>
        void TickFinish()
        {
            finishElapsed += Time.deltaTime;

            bool moving = false;
            if (layers != null)
                for (int i = 0; i < layers.Length; i++)
                    if (layers[i] != null && layers[i].Animating) { moving = true; break; }

            // 멈춰 서 있는데 안 끝나는 일이 없도록 상한을 둔다.
            if (moving && finishElapsed < finishMaxSeconds) return;

            finishing = false;
            CloseOut();
        }

        /// <summary>화면을 한 번 두드렸다.</summary>
        void Tap()
        {
            GameConfig cfg = GameConfigRef.Current;
            if (cfg == null || Target.Data == null) return;

            TapCount++;

            // 탭 하나가 올리는 양은 breakTime에서 유도한다 —
            // '몇 초 걸리는가'는 기획자가 채운 값이고, '초당 몇 번 두드리는가'는 손의 특성이라
            // 대상별이 아니라 전역이다. 새 수치는 tapsPerSecond 하나만 늘어난다.
            float taps = Mathf.Max(1f, Target.Data.breakTime * cfg.tapsPerSecond);
            float before = Gauge;

            // <b>세어서 나눈다. 더해서 쌓지 않는다.</b>
            //
            // 예전에는 <c>Gauge += 1f / taps</c>였는데, 1/40 = 0.025 같은 값은 이진
            // 부동소수로 정확히 표현되지 않아 40번을 더해도 1.0에 닿지 못했다.
            // 그래서 견고 건물이 <b>40탭이 아니라 41탭</b>에 부서졌다 —
            // 1~80 중 <b>31개</b> 값(12·19·23·27·28·31·33·37·40…)이 같은 증상이었다
            // [리스크_설계.md §4에서 지적, 2026-09-29 재현·수정].
            //
            // 횟수를 세어 한 번만 나누면 오차가 쌓이지 않는다 — taps가 정수일 때
            // TapCount == taps 순간의 몫이 정확히 1.0이 된다.
            Gauge = Mathf.Clamp01(TapCount / taps);

            // 층이 <b>방금 쏟아졌는가</b>. 이 게임에서 가장 자주 오는 큰 박자다.
            //
            // WallFracture가 알려 주게 할 수도 있지만, 여기서 게이지만 보면 된다 —
            // 층 f는 진행도가 (f+1)/층수를 넘는 순간 통째로 간다(shatterAt=1).
            // 그 경계를 넘었는지는 내림한 값이 바뀌었는지로 알 수 있다.
            int floorsOf = Mathf.Max(1, Target.Data.floors);
            bool floorJustBlew = Mathf.FloorToInt(Gauge * floorsOf)
                               > Mathf.FloorToInt(before * floorsOf);

            // 게이지가 곧 파괴 진행도다. 파괴 화면에서는 <b>조각 판만</b> 이 값을 본다.
            //
            // Destructible을 같이 굴리지 않는 이유 [지시 2026-09-22]:
            // 3단계 메시 교체는 structureDamagedAt01을 넘는 순간 Damaged 메시를 켜는데,
            // 그 메시가 조각 판 뒤에서 통짜로 나타나 "부서지다 말고 원본이 되살아난" 것처럼 보인다.
            // 어차피 이 화면을 나가면 잔해가 되므로, 중간 단계를 보여줄 이유가 없다.
            PushProgress();

            if (fx != null)
            {
                // 마지막 층은 완파와 같은 탭에 터진다. 둘을 같이 울리면 셰이크가 겹쳐
                // 한 번의 큰 흔들림으로 뭉개지므로, 끝나는 탭에서는 완파 쪽만 쓴다.
                // 주먹은 <b>먼저, 조건 없이</b>. 어느 박자가 울리든 플레이어는 쳤다.
                fx.OnPunch(input != null && input.StrikeRightArm);

                if (floorJustBlew && Gauge < 1f) fx.OnFloorCollapse(targetPoint);
                else if (!floorJustBlew) fx.OnStrike(Gauge, targetPoint);
            }

            if (Gauge >= 1f) Finish();
        }

        /// <summary>마지막 쾅. 닫는 것은 다 쏟아진 뒤다.</summary>
        void Finish()
        {
            finishing = true;
            finishElapsed = 0f;
        }

        void CloseOut()
        {
            // 여기서 한 번에 잔해로 넘긴다. 중간 단계를 건너뛰므로 Damaged 메시는 아예 안 나온다.
            Target.ForceRubble();

            if (spawner != null && Target.Data != null)
                spawner.SpawnFor(Target.Data, Target.transform.position);

            if (fx != null) fx.OnBroken(targetPoint);

            Debug.Log($"[DemolitionSession] {Target.Data?.id} 파괴 — 탭 {TapCount}회");
            GameModeController.Instance.ExitDemolition();
        }
    }
}
