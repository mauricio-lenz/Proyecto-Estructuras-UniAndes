using System.Collections.Generic;
using UnityEngine;

public class PostProcessing : MonoBehaviour {
    public StructuralLoader loader;

    [Header("Settings")]
    public float deformationScale = 30f;
    public float momentScale = 0.003f;
    public float shearScale = 0.001f;

    private bool showDeformed = false;
    private bool showMoment = false;
    private bool showAxial = false;

    private List<GameObject> deformGroup;
    private List<GameObject> momentGroup;
    private List<GameObject> axialGroup;

    private Dictionary<string, Vector3> worldStartOrig = new Dictionary<string, Vector3>();
    private Dictionary<string, Vector3> worldEndOrig = new Dictionary<string, Vector3>();

    static readonly string[] CASE_IDS = { "G", "Q", "EX", "EY", "COMBO" };
    static readonly string[] CASE_LBL = { "Caso G", "Caso Q", "Caso EX", "Caso EY", "Combinacion" };

    void Start() {
        if (loader == null) loader = GetComponent<StructuralLoader>();
    }

    void Update() {
        if (loader == null || loader.elementMonos == null) return;

        if (Input.GetKeyDown(KeyCode.D)) ToggleDeformed();
        if (Input.GetKeyDown(KeyCode.M)) ToggleMoment();
        if (Input.GetKeyDown(KeyCode.N)) ToggleAxial();
        if (Input.GetKeyDown(KeyCode.C)) CycleCase();
    }

    /// <summary>Cicla el caso activo: COMBO -> G -> Q -> EX -> EY -> COMBO.
    /// Al cambiar se quedan accionados solo los diagramas que siguen siendo
    /// validos (el axial se conserva por depender de cada caso). Los diagramas
    /// ya creados se regeneran con el nuevo caso.</summary>
    void CycleCase() {
        int i = System.Array.IndexOf(CASE_IDS, loader.activeCase ?? "COMBO");
        i = (i + 1) % CASE_IDS.Length;
        loader.activeCase = CASE_IDS[i] == "COMBO" ? null : CASE_IDS[i];
        if (showDeformed) { RefreshDeformed(); }
        if (showAxial) { RefreshAxial(); }
        if (showMoment) { RefreshMoment(); }
    }

    void OnGUI() {
        if (loader == null) return;
        string cur = loader.activeCase ?? "COMBO";
        GUI.Box(new Rect(320, Screen.height - 46, 300, 40), "Caso activo: " + cur);
        for (int i = 0; i < CASE_IDS.Length; i++) {
            bool sel = CASE_IDS[i].Equals(cur);
            GUI.backgroundColor = sel ? new Color(0.7f, 0.9f, 0.7f) : Color.white;
            if (GUI.Button(new Rect(320 + i * 80, Screen.height - 42, 76, 30),
                           CASE_LBL[i])) {
                loader.activeCase = CASE_IDS[i] == "COMBO" ? null : CASE_IDS[i];
                if (showDeformed) RefreshDeformed();
                if (showAxial) RefreshAxial();
                if (showMoment) RefreshMoment();
            }
        }
        if (showDeformed || showAxial || showMoment)
            GUI.Label(new Rect(820, Screen.height - 44, 400, 30),
                      "D/M/N: toggle  C: cambiar caso");
    }

    void RefreshDeformed() {
        DestroyGroup(deformGroup);
        BuildDeformed();
    }

    void RefreshAxial() {
        DestroyGroup(axialGroup);
        BuildAxial();
    }

    void RefreshMoment() {
        DestroyGroup(momentGroup);
        BuildMoment();
    }

    void ToggleDeformed() {
        if (showDeformed) { DestroyGroup(deformGroup); showDeformed = false; return; }
        showDeformed = true;
        BuildDeformed();
    }

    void BuildDeformed() {
        deformGroup = new List<GameObject>();
        Material mat = MakeMat(new Color(1f, 0.2f, 0.2f, 0.6f));

        foreach (var el in loader.elementMonos) {
            if (el.nodeIds == null || el.nodeIds.Count < 2) continue;
            if (!loader.nodeById.TryGetValue(el.nodeIds[0], out var nd1) ||
                !loader.nodeById.TryGetValue(el.nodeIds[1], out var nd2)) continue;

            Vector3 p1d = loader.NodePosDeformed(nd1, deformationScale);
            Vector3 p2d = loader.NodePosDeformed(nd2, deformationScale);

            float dist = Vector3.Distance(p1d, p2d);
            if (dist < 1e-6f) continue;

            var go = GameObject.CreatePrimitive(el.elementType == "column" ? PrimitiveType.Cylinder : PrimitiveType.Cube);
            go.name = $"Deform-{el.elementTag}";
            go.transform.position = (p1d + p2d) / 2f;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, (p2d - p1d).normalized);
            if (el.elementType == "column")
                go.transform.localScale = new Vector3(0.5f, dist / 2f, 0.5f);
            else
                go.transform.localScale = new Vector3(dist, 0.2f, 0.2f);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.GetComponent<Collider>().enabled = false;
            deformGroup.Add(go);
        }
    }

    void ToggleMoment() {
        if (showMoment) { DestroyGroup(momentGroup); showMoment = false; return; }
        showMoment = true;
        BuildMoment();
    }

    void BuildMoment() {
        momentGroup = new List<GameObject>();
        Material matPos = MakeMat(new Color(0.9f, 0.2f, 0.1f, 0.7f));
        Material matNeg = MakeMat(new Color(0.1f, 0.3f, 0.9f, 0.7f));

        foreach (var el in loader.elementMonos) {
            List<float> MzL = el.ForceFor("Mz", loader.activeCase);
            if (MzL == null || MzL.Count < 2) continue;
            if (el.length < 0.1f) continue;
            float M1 = MzL[0], M2 = MzL[1];

            Vector3 L1 = (el.worldEnd - el.worldStart).normalized;
            Vector3 up = Vector3.up;
            Vector3 perp = Vector3.Cross(up, L1).normalized;
            if (perp.sqrMagnitude < 1e-6f)
                perp = Vector3.Cross(Vector3.right, L1).normalized;

            float maxM = Mathf.Max(Mathf.Abs(M1), Mathf.Abs(M2));
            if (maxM < 0.01f) continue;

            float L = el.length;
            int nPts = 9;
            for (int i = 0; i < nPts - 1; i++) {
                float t0 = (float)i / (nPts - 1);
                float t1 = (float)(i + 1) / (nPts - 1);
                float mx0 = Mathf.Lerp(M1, M2, t0);
                float mx1 = Mathf.Lerp(M1, M2, t1);
                Material mat = mx0 >= 0 ? matPos : matNeg;
                Vector3 base0 = el.worldStart + L1 * L * t0;
                Vector3 base1 = el.worldStart + L1 * L * t1;
                Vector3 pt0 = base0 + perp * (mx0 * momentScale);
                Vector3 pt1 = base1 + perp * (mx1 * momentScale);

                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"Mom-{el.elementTag}-s{i}";
                go.transform.position = (pt0 + pt1) / 2f;
                float segLen = Vector3.Distance(pt0, pt1);
                go.transform.localScale = new Vector3(segLen, 0.06f, 0.06f);
                go.transform.rotation = Quaternion.FromToRotation(Vector3.right, (pt1 - pt0).normalized);
                go.GetComponent<Renderer>().sharedMaterial = mat;
                go.GetComponent<Collider>().enabled = false;
                momentGroup.Add(go);
            }

            string label = $"M={Mathf.Abs(M1):F0}";
            var lblGo = new GameObject($"LblM-{el.elementTag}");
            lblGo.transform.position = el.worldStart + perp * (M1 * momentScale) + Vector3.up * 0.4f;
            var tm = lblGo.AddComponent<TextMesh>();
            tm.text = label;
            tm.characterSize = 0.015f;
            tm.fontSize = 50;
            tm.color = new Color(0.15f, 0.15f, 0.15f);
            lblGo.transform.localScale = Vector3.one * 0.3f;
            momentGroup.Add(lblGo);
        }
    }

    void ToggleAxial() {
        if (showAxial) { DestroyGroup(axialGroup); showAxial = false; return; }
        showAxial = true;
        BuildAxial();
    }

    void BuildAxial() {
        axialGroup = new List<GameObject>();
        Material matC = MakeMat(new Color(0.1f, 0.1f, 0.8f, 0.7f));
        Material matT = MakeMat(new Color(0.8f, 0.1f, 0.1f, 0.7f));

        foreach (var el in loader.elementMonos) {
            List<float> NL = el.ForceFor("N", loader.activeCase);
            if (NL == null || NL.Count < 2) continue;
            float N = (NL[0] + NL[1]) / 2f;
            if (Mathf.Abs(N) < 0.5f) continue;

            Vector3 L1 = (el.worldEnd - el.worldStart).normalized;
            Vector3 up = Vector3.up;
            Vector3 horz = Vector3.Cross(L1, up).normalized;
            if (horz.sqrMagnitude < 1e-6f)
                horz = Vector3.Cross(Vector3.right, L1).normalized;

            float nScale = 0.0004f;
            float width = Mathf.Abs(N) * nScale;
            Material mat = N < 0 ? matC : matT;  // compression < 0

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Axial-{el.elementTag}";
            go.transform.position = (el.worldStart + el.worldEnd) / 2f + horz * (N * nScale * 0.5f);
            go.transform.localScale = new Vector3(el.length, width, width * 0.5f);
            go.transform.rotation = Quaternion.FromToRotation(Vector3.right, L1);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.GetComponent<Collider>().enabled = false;
            axialGroup.Add(go);
        }
    }

    static Material MakeMat(Color c) {
        var m = new Material(Shader.Find("Standard"));
        if (m == null || m.shader == null)
            m = new Material(Shader.Find("Legacy Shaders/Diffuse"));
        m.SetFloat("_Mode", 3);
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON");
        m.EnableKeyword("_ALPHABLEND_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = 3000;
        m.color = c;
        return m;
    }

    void DestroyGroup(List<GameObject> group) {
        if (group == null) return;
        foreach (var go in group) if (go != null) Destroy(go);
        group.Clear();
    }
}
