using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Ventana de inspeccion (IMGUI) formal, estilo panel de herramienta dentro del
/// juego: titulo, secciones con encabezado, tabla de fuerzas alineada, demanda/
/// capacidad con estado coloreado, selector de caso y graficos (curva P-M y
/// diagramas de fuerzas internas) integrados en la misma ventana.
/// </summary>
public class UIInspector : MonoBehaviour {
    public Text txtElementInfo;
    public RawImage pmPlotCanvas;
    public StructuralLoader loader;

    private bool panelVisible = true;
    private bool slabSelection = false;
    private ElementMono selected;
    private SlabMono selectedSlab;
    private Material lastHighMat;

    private Texture2D pmTex;
    private Texture2D diagTex;

    private Rect winRect = new Rect(16f, 16f, 660f, 580f);
    private string lastUICase = "__init__";

    private GUIStyle stHeader;
    private GUIStyle stBold;
    private GUIStyle stLabel;
    private GUIStyle stSection;
    private GUIStyle stValue;

    static readonly string[] CASE_IDS = { "G", "Q", "EX", "EY", "COMBO" };
    static readonly string[] CASE_LBL = { "Caso G", "Caso Q", "Caso EX", "Caso EY", "Combinacion" };
    static readonly string[] DIAG_COMPS = { "N", "Vy", "Vz", "T", "My", "Mz" };
    static readonly string[] DIAG_UNIT = { "kN", "kN", "kN", "kN-m", "kN-m", "kN-m" };
    static readonly Color[] ROW_COL = {
        new Color(0.20f, 0.35f, 0.70f, 1f),
        new Color(0.15f, 0.55f, 0.25f, 1f),
        new Color(0.85f, 0.55f, 0.10f, 1f),
        new Color(0.60f, 0.30f, 0.72f, 1f),
        new Color(0.60f, 0.38f, 0.15f, 1f),
        new Color(0.80f, 0.18f, 0.18f, 1f),
    };

    void Awake() {
        if (loader == null) loader = GetComponent<StructuralLoader>();
        InitStyles();
        // La informacion ahora se muestra en la ventana IMGUI; ocultamos los
        // controles uGUI que se crearon para el panel anterior.
        if (txtElementInfo != null) txtElementInfo.gameObject.SetActive(false);
        if (pmPlotCanvas != null) pmPlotCanvas.gameObject.SetActive(false);
    }

    void InitStyles() {
        stLabel = new GUIStyle(GUI.skin.label);
        stLabel.fontSize = 17;
        stBold = new GUIStyle(stLabel);
        stBold.fontStyle = FontStyle.Bold;
        stHeader = new GUIStyle(GUI.skin.box);
        stHeader.fontStyle = FontStyle.Bold;
        stHeader.fontSize = 19;
        stHeader.alignment = TextAnchor.MiddleLeft;
        stHeader.normal.textColor = new Color(0.08f, 0.10f, 0.25f, 1f);
        stHeader.normal.background = MakeTex(1, 1, new Color(0.80f, 0.86f, 0.95f, 1f));
        stSection = new GUIStyle(stBold);
        stSection.fontSize = 17;
        stSection.normal.textColor = new Color(0.10f, 0.30f, 0.55f, 1f);
        stValue = new GUIStyle(stLabel);
        stValue.alignment = TextAnchor.MiddleRight;
    }

    static Texture2D MakeTex(int w, int h, Color c) {
        var t = new Texture2D(w, h);
        for (int i = 0; i < w; i++)
            for (int j = 0; j < h; j++) t.SetPixel(i, j, c);
        t.Apply();
        return t;
    }

    void Update() {
        if (loader == null) loader = GetComponent<StructuralLoader>();

        if (Input.GetKeyDown(KeyCode.P)) panelVisible = !panelVisible;
        if (Input.GetKeyDown(KeyCode.L)) {
            slabSelection = !slabSelection;
            if (loader != null) loader.SetSlabSelection(slabSelection);
        }

        // Actualizar graficos de la ventana si el caso cambio por tecla C en PostProcessing.
        string curCase = loader != null && loader.activeCase != null ? loader.activeCase : "COMBO";
        if (curCase != lastUICase) { lastUICase = curCase; InvalidateTextures(); }

        // Clic fuera de la ventana -> seleccionar elemento/losa.
        if (Input.GetMouseButtonDown(0) && loader != null && !IsMouseOverWindow()) {
            Ray ray = Camera.main != null
                ? Camera.main.ScreenPointToRay(Input.mousePosition) : new Ray();
            if (Physics.Raycast(ray, out RaycastHit hit)) {
                var el = hit.collider.GetComponentInParent<ElementMono>();
                if (el != null) { SelectElement(el); return; }
                var sm = hit.collider.GetComponentInParent<SlabMono>();
                if (sm != null) { SelectSlab(sm); return; }
            }
            ClearSelection();
        }
    }

    bool IsMouseOverWindow() {
        if (!panelVisible) return false;
        Vector3 mp = Input.mousePosition;
        mp.y = Screen.height - mp.y;
        return winRect.Contains(mp);
    }

    void OnGUI() {
        if (!panelVisible) return;
        // Ventana panoramica pero nunca mayor que la pantalla: se puede seguir
        // interactuando con la escena fuera de ella. La rueda del raton fuera del
        // panel hace zoom (el panel NO tiene scroll para no robar la rueda).
        float w = Mathf.Clamp(Screen.width - 16f, 400f, 660f);
        float h = Mathf.Clamp(Screen.height - 24f, 400f, 580f);
        winRect.width = w;
        winRect.height = h;
        winRect.x = Mathf.Clamp(winRect.x, 0f, Mathf.Max(0f, Screen.width - w));
        winRect.y = Mathf.Clamp(winRect.y, 0f, Mathf.Max(0f, Screen.height - h));
        winRect = GUI.Window(0, winRect, WindowFunc, "INSPECCION ESTRUCTURAL  ·  P1L4 (ED1+ED2)");
    }

    void WindowFunc(int id) {
        GUI.DragWindow(new Rect(0f, 0f, winRect.width, 24f));
        if (selectedSlab != null) {
            DrawSlabInfo();
        } else if (selected == null) {
            DrawPlaceholder();
        } else {
            DrawElementInfo();
        }
        GUILayout.Space(6f);
        DrawFooter();
    }

    void DrawFooter() {
        GUILayout.Space(2f);
        var footStyle = new GUIStyle(stLabel);
        footStyle.fontSize = 14;
        var hintStyle = new GUIStyle(stLabel);
        hintStyle.fontSize = 13;
        hintStyle.normal.textColor = new Color(0.35f, 0.35f, 0.42f, 1f);
        GUILayout.Label("Click: seleccionar · L: losas · D/M/N: diagramas · C: caso · F: encuadrar · P: ocultar", footStyle);
        GUILayout.Label("Der: rotar · Rueda: zoom (panel no roba la rueda) · Arrastre esta barra para reubicar el panel", hintStyle);
    }

    void DrawPlaceholder() {
        GUILayout.Space(30f);
        GUILayout.Label("Seleccione un elemento de la estructura (clic izquierdo).", stBold);
        GUILayout.Space(4f);
        GUILayout.Label("Tambien puede seleccionar losas y voladizos con la tecla L.", stLabel);
        GUILayout.Space(10f);
        DrawFooterNotes();
    }

    void DrawFooterNotes() {
        var hintStyle = new GUIStyle(stLabel);
        hintStyle.fontSize = 13;
        hintStyle.normal.textColor = new Color(0.35f, 0.35f, 0.42f, 1f);
        GUILayout.Label("Teclas: D deformada · M momentos · N axial · C cambiar caso · F encuadrar", hintStyle);
        GUILayout.Label("Der: rotar · Rueda: zoom · Clic medio: pan · P: ocultar panel", hintStyle);
    }

    void DrawElementInfo() {
        string caso = (loader != null && loader.activeCase != null)
            ? case_display(loader.activeCase)
            : (loader != null && loader.data != null ? loader.data.combinacion : "---");

        DrawCaseSelector();

        string tipo = selected.elementType == "column" ? "COLUMNA"
                    : selected.elementType == "wall" ? "MURO" : "VIGA";

        GUILayout.Label($"{selected.building}  ·  {tipo}  ·  TAG {selected.elementTag}", stHeader);
        GUILayout.Label($"CAD {selected.cadID} · Sec {selected.sectionTag} · Mat {selected.material} · Nivel {selected.lvl} · Largo {selected.length:F2} m · Nodos {Nodelist(selected)}", stLabel);
        GUILayout.Space(2f);

        DrawSection("CARGAS GRAVITATORIAS");
        Row2("Area tributaria", $"{selected.tribArea:F2} m2");
        Row2("wG · wQ · G · Q",
             $"{selected.wG:F3} kN/m · {selected.wQ:F3} kN/m · G {selected.wG * selected.length:F2} kN · Q {selected.wQ * selected.length:F2} kN");
        GUILayout.Space(2f);

        DrawSection($"FUERZAS INTERNAS  [{caso}]");
        TableHeader();
        string[] comps = { "N", "Vy", "Vz", "T", "My", "Mz" };
        for (int r = 0; r < comps.Length; r++) {
            List<float> v = selected.ForceFor(comps[r], loader != null ? loader.activeCase : null);
            TableRow(comps[r], DIAG_UNIT[r], v, ROW_COL[r]);
        }
        GUILayout.Space(2f);

        DrawSection("DEMANDA / CAPACIDAD (P-M)");
        float dP = DemandP(selected);
        float dM = DemandM(selected);
        GUILayout.BeginHorizontal();
        GUILayout.Label($"P {dP:F2} kN · M {dM:F2} kN-m", stLabel);
        float mcap;
        float dc = ComputeDC(selected, dP, dM, out mcap);
        if (!float.IsNaN(dc)) {
            bool ok = dc <= 1f;
            var boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.fontSize = 15;
            boxStyle.fontStyle = FontStyle.Bold;
            boxStyle.alignment = TextAnchor.MiddleCenter;
            boxStyle.normal.textColor = Color.white;
            boxStyle.normal.background = MakeTex(1, 1, ok ? new Color(0.15f, 0.55f, 0.20f, 1f)
                                                          : new Color(0.75f, 0.15f, 0.15f, 1f));
            GUILayout.Space(10f);
            GUILayout.Box($"D/C {dc:F3}  ·  Mcap {mcap:F2}  {(ok ? "OK" : "EXCEDE")}", boxStyle, GUILayout.Width(250f));
        } else {
            Row2("D/C", "N/A (viga: revisar Mz envolvente)");
        }
        GUILayout.EndHorizontal();
        GUILayout.Space(4f);

        // Graficos: diagramas de fuerzas (izquierda) y curva P-M (derecha).
        EnsureTextures();
        GUILayout.BeginHorizontal();
        if (diagTex != null) {
            var rd = GUILayoutUtility.GetRect(300f, 150f);
            GUI.DrawTexture(rd, diagTex, ScaleMode.ScaleToFit);
        }
        if (pmTex != null) {
            var rp = GUILayoutUtility.GetRect(300f, 150f);
            GUI.DrawTexture(rp, pmTex, ScaleMode.ScaleToFit);
        }
        GUILayout.EndHorizontal();
    }

    void DrawSlabInfo() {
        string kind = selectedSlab.isVoladizo ? "VOLADIZO" : "LOSA";
        GUILayout.Label($"{selectedSlab.building}  ·  {kind}  ·  {selectedSlab.id}", stHeader);
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Nivel {selectedSlab.lvl} · Espesor {selectedSlab.thickness.ToString("F2")} m · Area {selectedSlab.area.ToString("F2")} m2", stLabel);
        GUILayout.EndHorizontal();
        GUILayout.Space(2f);

        DrawSection("CARGAS DE LA LOSA");
        Row2("qG (permanente)", selectedSlab.qG.ToString("F3") + " kPa");
        Row2("qQ (sobrecarga)", selectedSlab.qQ.ToString("F3") + " kPa");
        Row2("G = qG·A · Q = qQ·A", (selectedSlab.qG * selectedSlab.area).ToString("F1") + " kN  ·  "
                                   + (selectedSlab.qQ * selectedSlab.area).ToString("F1") + " kN");
        GUILayout.Space(4f);
        GUILayout.Label("La losa contribuye a los pesos sismicos W y transmite cargas a vigas y columnas.", stLabel);
    }

    string case_display(string c) {
        for (int i = 0; i < CASE_IDS.Length; i++)
            if (CASE_IDS[i] == c) return CASE_LBL[i];
        return c;
    }

    void DrawCaseSelector() {
        string active = loader != null && loader.activeCase != null ? loader.activeCase : "COMBO";
        GUILayout.BeginHorizontal();
        GUILayout.Label("Caso:", stBold, GUILayout.Width(66f));
        for (int i = 0; i < CASE_IDS.Length; i++) {
            bool on = CASE_IDS[i].Equals(active);
            bool pressed = GUILayout.Toggle(on, CASE_LBL[i], GUI.skin.button,
                                           GUILayout.Width(104f));
            if (pressed != on) {
                loader.activeCase = CASE_IDS[i] == "COMBO" ? null : CASE_IDS[i];
                InvalidateTextures();
                if (selected != null) {
                    RebuildTextures(selected);
                }
            }
        }
        GUILayout.EndHorizontal();
        GUILayout.Space(3f);
    }

    void DrawSection(string title) {
        GUILayout.BeginHorizontal();
        GUILayout.Space(2f);
        GUILayout.Label(title, stSection);
        var line = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
        GUI.Box(line, GUIContent.none);
        GUILayout.EndHorizontal();
    }

    void TableHeader() {
        GUILayout.BeginHorizontal();
        string[] h = { "Fuerza", "i", "j", "Max|.|", "Unidad" };
        float[] w = { 96f, 86f, 86f, 92f, 92f };
        GUILayout.Label(h[0], stBold, GUILayout.Width(w[0]));
        for (int c = 1; c < h.Length; c++)
            GUILayout.Label(h[c], stBold, GUILayout.Width(w[c]));
        GUILayout.EndHorizontal();
    }

    void TableRow(string name, string unit, List<float> v, Color col) {
        GUILayout.BeginHorizontal();
        var lname = new GUIStyle(stLabel);
        lname.normal.textColor = col;
        lname.fontStyle = FontStyle.Bold;
        GUILayout.Label(name, lname, GUILayout.Width(96f));
        if (v == null || v.Count < 2) {
            GUILayout.Label("-", stLabel, GUILayout.Width(86f));
            GUILayout.Label("-", stLabel, GUILayout.Width(86f));
            GUILayout.Label("-", stLabel, GUILayout.Width(92f));
        } else {
            float vi = v[0], vj = v[1];
            float mx = Mathf.Max(Mathf.Abs(vi), Mathf.Abs(vj));
            GUILayout.Label(vi.ToString("F2"), stValue, GUILayout.Width(86f));
            GUILayout.Label(vj.ToString("F2"), stValue, GUILayout.Width(86f));
            GUILayout.Label(mx.ToString("F2"), stValue, GUILayout.Width(92f));
        }
        GUILayout.Label(unit, stLabel, GUILayout.Width(92f));
        GUILayout.EndHorizontal();
    }

    void Row2(string name, string value) {
        GUILayout.BeginHorizontal();
        GUILayout.Label(name, stLabel, GUILayout.Width(190f));
        GUILayout.Label(value, stBold, GUILayout.Width(260f));
        GUILayout.EndHorizontal();
    }

    string fixStr() {
        if (loader == null || selected == null || selected.nodeIds == null) return "-";
        var sb = new StringBuilder();
        for (int k = 0; k < selected.nodeIds.Count && k < 2; k++) {
            if (loader.nodeById.TryGetValue(selected.nodeIds[k], out var n))
                sb.Append((k == 0 ? "i:{" : " j:{") + ListInt(n.fix) + "}");
        }
        return sb.ToString().Length == 0 ? "-" : sb.ToString();
    }

    void SelectElement(ElementMono el) {
        if (el == null) { ClearSelection(); return; }
        if (selected == el) { ClearSelection(); return; }
        ClearHighlight();
        selected = el;
        selectedSlab = null;
        Highlight(el, true);
        RebuildTextures(el);
    }

    void SelectSlab(SlabMono sm) {
        ClearHighlight();
        selected = null;
        selectedSlab = sm;
        pmTex = null;
        diagTex = null;
    }

    void ClearSelection() {
        ClearHighlight();
        selected = null;
        selectedSlab = null;
        pmTex = null;
        diagTex = null;
    }

    void InvalidateTextures() {
        if (pmTex != null) { Destroy(pmTex); pmTex = null; }
        if (diagTex != null) { Destroy(diagTex); diagTex = null; }
    }

    void RebuildTextures(ElementMono el) {
        var np = BuildPmTexture(el);
        var nd = BuildDiagrams(el);
        if (pmTex != null) Destroy(pmTex);
        if (diagTex != null) Destroy(diagTex);
        pmTex = np;
        diagTex = nd;
    }

    void EnsureTextures() {
        if ((pmTex == null || diagTex == null) && selected != null)
            RebuildTextures(selected);
    }

    void ClearHighlight() {
        if (selected != null && lastHighMat != null) {
            var r = selected.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = lastHighMat;
            lastHighMat = null;
        }
    }

    void Highlight(ElementMono el, bool on) {
        if (el == null) return;
        var r = el.GetComponent<Renderer>();
        if (r == null) return;
        if (on) {
            lastHighMat = r.sharedMaterial;
            var m = new Material(Shader.Find("Standard"));
            if (m == null || m.shader == null)
                m = new Material(Shader.Find("Legacy Shaders/Diffuse"));
            m.color = new Color(1f, 1f, 0.2f, 1f);
            r.sharedMaterial = m;
        } else {
            r.sharedMaterial = lastHighMat;
        }
    }

    float DemandP(ElementMono el) {
        var v = el.ForceFor("P", loader != null ? loader.activeCase : null);
        if (v != null && v.Count >= 1) return v[0];
        return el.demandP;
    }

    float DemandM(ElementMono el) {
        var v = el.ForceFor("M", loader != null ? loader.activeCase : null);
        if (v != null && v.Count >= 1) return v[0];
        return el.demandM;
    }

    static string Nodelist(ElementMono el) {
        if (el.nodeIds == null || el.nodeIds.Count < 2) return "-";
        return $"{el.nodeIds[0]} -> {el.nodeIds[1]}";
    }

    /// <summary>D/C = M_demanda / M_capacidad interpolada en P. NaN si no aplica.</summary>
    float ComputeDC(ElementMono el, float dP, float dM, out float mcap) {
        mcap = float.NaN;
        if (loader == null || loader.data == null || loader.data.pm_capacity == null) return float.NaN;
        if (el.elementType != "column" && el.elementType != "wall") return float.NaN;
        string fallback = el.elementType == "column" ? "PILAR-70x70" : "M-20";
        PMCapacityEntry cap = null;
        foreach (var e in loader.data.pm_capacity) {
            if (e.section == el.sectionTag) { cap = e; break; }
        }
        if (cap == null) {
            foreach (var e in loader.data.pm_capacity) {
                if (e.section == fallback) { cap = e; break; }
            }
        }
        if (cap == null || cap.P == null || cap.M == null || cap.P.Count < 2) return float.NaN;
        float mc = InterpCapacityM(cap, dP);
        if (mc <= 1e-6f) return float.NaN;
        mcap = mc;
        return Mathf.Abs(dM) / mc;
    }

    static float InterpCapacityM(PMCapacityEntry cap, float p) {
        var P = cap.P;
        var M = cap.M;
        for (int i = 0; i < P.Count - 1; i++) {
            float p0 = P[i], p1 = P[i + 1];
            float lo = Mathf.Min(p0, p1), hi = Mathf.Max(p0, p1);
            if (p >= lo && p <= hi) {
                float t = Mathf.Abs(p1 - p0) < 1e-6f ? 0f : (p - p0) / (p1 - p0);
                return Mathf.Lerp(M[i], M[i + 1], t);
            }
        }
        if (p < Mathf.Min(P[0], P[P.Count - 1])) return M[P[0] < P[P.Count - 1] ? 0 : P.Count - 1];
        return M[P[0] > P[P.Count - 1] ? 0 : P.Count - 1];
    }

    /// <summary>Curva P-M (capacidad) con el punto de demanda del caso activo.</summary>
    Texture2D BuildPmTexture(ElementMono el) {
        int size = 300;
        var tex = new Texture2D(size, size);
        Color bg = new Color(0.97f, 0.98f, 1f, 1f);
        Color axCol = new Color(0.35f, 0.35f, 0.4f, 1f);
        Color capCol = new Color(0.1f, 0.35f, 0.6f, 1f);
        Color demCol = new Color(0.8f, 0.18f, 0.18f, 1f);
        for (int px = 0; px < size; px++)
            for (int py = 0; py < size; py++)
                tex.SetPixel(px, py, bg);

        int originX = size / 2;
        int originY = 18;
        for (int i = 0; i < size; i++) {
            tex.SetPixel(originX, i, axCol);
            tex.SetPixel(i, originY, axCol);
        }

        float dP = DemandP(el);
        float dM = DemandM(el);

        if (loader != null && loader.data != null && loader.data.pm_capacity != null
            && (el.elementType == "column" || el.elementType == "wall")) {
            string sec = el.elementType == "column" ? "PILAR-70x70" : "M-20";
            PMCapacityEntry capEntry = null;
            foreach (var entry in loader.data.pm_capacity) {
                if (entry.section == sec || entry.section == el.sectionTag) { capEntry = entry; break; }
            }
            if (capEntry == null) {
                foreach (var entry in loader.data.pm_capacity) {
                    if (entry.section == "PILAR-70x70" && el.elementType == "column") { capEntry = entry; break; }
                    if (entry.section == "M-20" && el.elementType == "wall") { capEntry = entry; break; }
                }
            }
            if (capEntry != null) {
                float pMax = 0, pMin = 0, mMax = 0;
                foreach (float pv in capEntry.P) { if (pv > pMax) pMax = pv; if (pv < pMin) pMin = pv; }
                foreach (float mv in capEntry.M) if (Mathf.Abs(mv) > mMax) mMax = Mathf.Abs(mv);
                if (mMax < 1f) mMax = 1f;
                float pRange = pMax - pMin;
                if (pRange < 1f) pRange = 1f;
                float mScale = (size - 36f) / (2f * mMax);
                float pScale = (size - 36f) / pRange;
                for (int i = 0; i < capEntry.P.Count - 1; i++) {
                    int x1 = (int)(originX + capEntry.M[i] * mScale);
                    int y1 = (int)(originY + (capEntry.P[i] - pMin) * pScale);
                    int x2 = (int)(originX + capEntry.M[i + 1] * mScale);
                    int y2 = (int)(originY + (capEntry.P[i + 1] - pMin) * pScale);
                    DrawLine(tex, x1, y1, x2, y2, capCol, 2);
                }
                int dx = (int)(originX + dM * mScale);
                int dy = (int)(originY + (dP - pMin) * pScale);
                for (int ox = -4; ox <= 4; ox++)
                    for (int oy = -4; oy <= 4; oy++) {
                        int px = dx + ox, py = dy + oy;
                        if (px >= 0 && px < size && py >= 0 && py < size)
                            tex.SetPixel(px, py, demCol);
                    }
            }
        }
        tex.Apply();
        return tex;
    }

    /// <summary>Diagramas de fuerzas internas (N,Vy,Vz,T,My,Mz) con valores i/j.</summary>
    Texture2D BuildDiagrams(ElementMono el) {
        int size = 300;
        float rowH = 40f;
        float topM = 14f;
        float xL = 52f, xR = 292f;
        var tex = new Texture2D(size, size);
        Color bg = new Color(0.97f, 0.97f, 1f, 1f);
        Color zero = new Color(0.42f, 0.42f, 0.48f, 1f);
        for (int px = 0; px < size; px++)
            for (int py = 0; py < size; py++)
                tex.SetPixel(px, py, bg);

        for (int r = 0; r < DIAG_COMPS.Length; r++) {
            float yTop = size - topM - r * rowH;
            float yBot = yTop - rowH;
            float yMid = (yTop + yBot) / 2f;

            DrawLine(tex, (int)xL, (int)yMid, (int)xR, (int)yMid, zero, 1);

            List<float> v = el.ForceFor(DIAG_COMPS[r], loader != null ? loader.activeCase : null);
            if (v == null || v.Count < 2) continue;
            float vi = v[0], vj = v[1];
            float maxAbs = Mathf.Max(Mathf.Abs(vi), Mathf.Abs(vj), 1e-3f);
            float s = (rowH * 0.36f) / maxAbs;

            int pxPrev = (int)xL;
            int pyPrev = (int)(yMid + vi * s);
            for (int t = 1; t <= 40; t++) {
                float ft = (float)t / 40f;
                float xCur = Mathf.Lerp(xL, xR, ft);
                float yCur = yMid + (vi + (vj - vi) * ft) * s;
                DrawLine(tex, pxPrev, pyPrev, (int)xCur, (int)yCur, ROW_COL[r], 2);
                pxPrev = (int)xCur;
                pyPrev = (int)yCur;
            }
            int xi = (int)xL, xj = (int)xR;
            for (int ox = -2; ox <= 2; ox++)
                for (int oy = -2; oy <= 2; oy++) {
                    tex.SetPixel(xi + ox, (int)(yMid + vi * s) + oy, ROW_COL[r]);
                    tex.SetPixel(xj + ox, (int)(yMid + vj * s) + oy, ROW_COL[r]);
                }
        }
        tex.Apply();
        return tex;
    }

    static void DrawLine(Texture2D tex, int x0, int y0, int x1, int y1, Color col, int thick) {
        int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        int maxIter = 10000;
        while (maxIter-- > 0) {
            for (int tx = 0; tx < thick; tx++)
                for (int ty = 0; ty < thick; ty++) {
                    int px = x0 + tx, py = y0 + ty;
                    if (px >= 0 && px < tex.width && py >= 0 && py < tex.height)
                        tex.SetPixel(px, py, col);
                }
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    static string ListInt(List<int> l) {
        if (l == null) return "?";
        string s = "";
        for (int i = 0; i < l.Count; i++) { if (i > 0) s += ","; s += l[i]; }
        return s;
    }
}