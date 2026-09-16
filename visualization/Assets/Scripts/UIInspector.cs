using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public class UIInspector : MonoBehaviour {
    public Text txtElementInfo;
    public RawImage pmPlotCanvas;
    public StructuralLoader loader;

    private RawImage diagramCanvas;
    private bool slabSelection = false;
    private ElementMono selected;
    private Material lastHighMat;
    private bool panelVisible = true;

    static readonly string[] DIAG_COMPS = { "N", "Vy", "Vz", "T", "My", "Mz" };
    static readonly string[] DIAG_UNIT  = { "kN", "kN", "kN", "kN-m", "kN-m", "kN-m" };
    static readonly Color[] DIAG_COL = {
        new Color(0.15f, 0.35f, 0.85f, 1f),
        new Color(0.10f, 0.60f, 0.25f, 1f),
        new Color(0.90f, 0.55f, 0.10f, 1f),
        new Color(0.65f, 0.25f, 0.75f, 1f),
        new Color(0.55f, 0.35f, 0.15f, 1f),
        new Color(0.85f, 0.15f, 0.15f, 1f),
    };

    void Awake() {
        if (loader == null) loader = GetComponent<StructuralLoader>();
        SetupPanel();
    }

    /// <summary>
    /// Panel compacto: ancho medio fijo, alto 70% de la pantalla, fuente
    /// monoespaciada grande y fondo blanco semitransparente. No tapa la
    /// estructura (queda al borde izquierdo; tecla P lo oculta/muestra).
    /// </summary>
    void SetupPanel() {
        if (txtElementInfo == null) return;
        var rt = txtElementInfo.rectTransform;
        if (rt != null) {
            float w = Mathf.Clamp(Screen.width * 0.4f, 600f, 740f);
            float h = Mathf.Clamp(Screen.height * 0.7f, 560f, 920f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(16f, -16f);
        }
        txtElementInfo.color = new Color(0.05f, 0.05f, 0.1f, 1f);
        txtElementInfo.fontSize = 22;
        txtElementInfo.lineSpacing = 1.05f;
        txtElementInfo.horizontalOverflow = HorizontalWrapMode.Wrap;
        Font mono = null;
        try { mono = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Courier New", "DejaVu Sans Mono" }, 22); }
        catch (System.Exception) { }
        if (mono != null) txtElementInfo.font = mono;
        EnsureBackground(rt);
        if (pmPlotCanvas != null) {
            pmPlotCanvas.rectTransform.sizeDelta = new Vector2(320f, 320f);
            SetupDiagramCanvas();
        }
    }

    /// <summary>Crea el canvas de diagramas de fuerzas internas, a la izquierda
    /// del grafico P-M (esquina inferior derecha).</summary>
    void SetupDiagramCanvas() {
        if (pmPlotCanvas == null || pmPlotCanvas.transform.parent == null) return;
        var parent = pmPlotCanvas.transform.parent;
        Transform existing = null;
        for (int i = 0; i < parent.childCount; i++)
            if (parent.GetChild(i).name == "Diagramas") { existing = parent.GetChild(i); break; }
        if (existing != null)
            diagramCanvas = existing.GetComponent<RawImage>();
        else {
            var go = new GameObject("Diagramas", typeof(RectTransform),
                                    typeof(CanvasRenderer), typeof(RawImage));
            go.transform.SetParent(parent, false);
            diagramCanvas = go.GetComponent<RawImage>();
        }
        var rt = diagramCanvas.rectTransform;
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(-356f, 160f);
        rt.sizeDelta = new Vector2(320f, 320f);
        diagramCanvas.raycastTarget = false;
    }

    /// <summary>Crea (o reusa) un Image blanco semitransparente detrás del texto.</summary>
    void EnsureBackground(RectTransform parent) {
        if (parent == null) return;
        Image bg = null;
        for (int i = 0; i < parent.childCount; i++) {
            var img = parent.GetChild(i).GetComponent<Image>();
            if (img != null && img.gameObject.name == "Fondo") { bg = img; break; }
        }
        if (bg == null) {
            var go = new GameObject("Fondo", typeof(RectTransform),
                                    typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            bg = go.GetComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.88f);
            bg.raycastTarget = false;
            bg.transform.SetAsFirstSibling();
        }
        bg.rectTransform.anchorMin = Vector2.zero;
        bg.rectTransform.anchorMax = Vector2.one;
        bg.rectTransform.offsetMin = Vector2.zero;
        bg.rectTransform.offsetMax = Vector2.zero;
    }

    void Update() {
        if (loader == null) loader = GetComponent<StructuralLoader>();
        if (Input.GetKeyDown(KeyCode.P)) {
            panelVisible = !panelVisible;
            if (txtElementInfo != null) txtElementInfo.gameObject.SetActive(panelVisible);
            if (pmPlotCanvas != null) pmPlotCanvas.gameObject.SetActive(panelVisible);
            if (diagramCanvas != null) diagramCanvas.gameObject.SetActive(panelVisible);
        }
        if (Input.GetKeyDown(KeyCode.L)) {
            slabSelection = !slabSelection;
            if (loader != null) loader.SetSlabSelection(slabSelection);
        }
        if (Input.GetMouseButtonDown(0) && loader != null) {
            Ray ray = Camera.main != null
                ? Camera.main.ScreenPointToRay(Input.mousePosition) : new Ray();
            if (Physics.Raycast(ray, out RaycastHit hit)) {
                var el = hit.collider.GetComponentInParent<ElementMono>();
                if (el != null) { SelectElement(el); return; }
                var sm = hit.collider.GetComponentInParent<SlabMono>();
                if (sm != null) { SelectSlab(sm); return; }
                ClearSelection();
            }
        }
    }

    void SelectElement(ElementMono el) {
        if (el == null) { ClearSelection(); return; }
        if (selected == el) { ClearSelection(); return; }
        ClearHighlight();
        selected = el;
        Highlight(el, true);
        ShowInfo(el);
        DrawPmPlot(el);
        DrawDiagrams(el);
    }

    void SelectSlab(SlabMono sm) {
        if (sm == null) { ClearSelection(); return; }
        ClearHighlight();
        selected = null;
        if (pmPlotCanvas != null) pmPlotCanvas.texture = null;
        if (diagramCanvas != null) diagramCanvas.texture = null;
        if (txtElementInfo != null) {
            string kind = sm.isVoladizo ? "VOLADIZO" : "LOSA";
            var sb = new StringBuilder();
            sb.AppendLine($"== {sm.building}  {kind}  ·  {sm.id} ==");
            sb.AppendLine($"Nivel    : {sm.lvl}");
            sb.AppendLine($"Espesor  : {sm.thickness:F2} m");
            sb.AppendLine($"Area     : {sm.area,12:F2} m2");
            sb.AppendLine($"qG       : {sm.qG,12:F3} kPa");
            sb.AppendLine($"qQ       : {sm.qQ,12:F3} kPa");
            sb.AppendLine($"G = qG·A : {sm.qG * sm.area,12:F1} kN");
            sb.AppendLine($"Q = qQ·A : {sm.qQ * sm.area,12:F1} kN");
            sb.AppendLine(line32());
            sb.AppendLine("(La losa aporta a los pesos sismicos W=C·Sigma(P);");
            sb.AppendLine("sus fuerzas se transmiten a vigas/columnas. Grafico");
            sb.AppendLine("P-M y fuerzas no aplican a la losa.)");
            sb.AppendLine("Clic: seleccionar  L: dejar de seleccionar losas  P: panel");
            txtElementInfo.text = sb.ToString();
        }
    }

    static string line32() {
        return "--------------------------------";
    }

    void ClearSelection() {
        ClearHighlight();
        selected = null;
        if (txtElementInfo != null)
            txtElementInfo.text = "Haz clic en un elemento de la estructura...";
        if (pmPlotCanvas != null) pmPlotCanvas.texture = null;
        if (diagramCanvas != null) diagramCanvas.texture = null;
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

    void ShowInfo(ElementMono el) {
        if (txtElementInfo == null) return;
        string tipo = el.elementType == "column" ? "COLUMNA"
                    : el.elementType == "wall"   ? "MURO" : "VIGA";
        string fixStr = "";
        if (loader != null && el.nodeIds != null && el.nodeIds.Count >= 2) {
            if (loader.nodeById.TryGetValue(el.nodeIds[0], out var n1)) fixStr += "i:{" + ListInt(n1.fix) + "}";
            if (loader.nodeById.TryGetValue(el.nodeIds[1], out var n2)) fixStr += "  j:{" + ListInt(n2.fix) + "}";
        }

        string comb = (loader != null && loader.data != null) ? loader.data.combinacion : "---";
        string casoLbl = (loader != null && loader.activeCase != null)
            ? $"Caso {loader.activeCase}"
            : $"Combinacion {comb}";
        const string line = "----------------------------------------------------------------";

        var sb = new StringBuilder();
        sb.AppendLine($"== {el.building}  {tipo}  ·  TAG {el.elementTag}  [{casoLbl}] ==");
        sb.AppendLine($"CAD    : {el.cadID}");
        sb.AppendLine($"Seccion: {el.sectionTag}   Material: {el.material}");
        sb.AppendLine($"Nivel  : {el.lvl}   Fase: {el.phase}   Largo: {el.length:F2} m   Orient: {el.orient}");
        sb.AppendLine($"Nodos  : {Nodelist(el)}   Restricciones: {fixStr}");
        sb.AppendLine(line);

        sb.AppendLine("CARGAS GRAVITATORIAS");
        sb.AppendLine($"  Area tributaria      : {el.tribArea,12:F3} m2");
        sb.AppendLine($"  w_G (por metro)      : {el.wG,12:F3} kN/m");
        sb.AppendLine($"  w_Q (por metro)      : {el.wQ,12:F3} kN/m");
        sb.AppendLine($"  G = w_G x L          : {el.wG * el.length,12:F2} kN");
        sb.AppendLine($"  Q = w_Q x L          : {el.wQ * el.length,12:F2} kN");
        sb.AppendLine(line);

        sb.AppendLine($"FUERZAS INTERNAS   [{casoLbl}]");
        sb.AppendLine("  Fuerza          i            j        Max|.|    Unidad");
        AppendForce(sb, "N",  el.ForceFor("N",  loader != null ? loader.activeCase : null), "kN");
        AppendForce(sb, "Vy", el.ForceFor("Vy", loader != null ? loader.activeCase : null), "kN");
        AppendForce(sb, "Vz", el.ForceFor("Vz", loader != null ? loader.activeCase : null), "kN");
        AppendForce(sb, "T",  el.ForceFor("T",  loader != null ? loader.activeCase : null), "kN-m");
        AppendForce(sb, "My", el.ForceFor("My", loader != null ? loader.activeCase : null), "kN-m");
        AppendForce(sb, "Mz", el.ForceFor("Mz", loader != null ? loader.activeCase : null), "kN-m");
        sb.AppendLine(line);

        sb.AppendLine("DEMANDA / CAPACIDAD (P-M)");
        float dP = DemandP(el);
        float dM = DemandM(el);
        sb.AppendLine($"  P (compresion +)     : {dP,12:F2} kN");
        sb.AppendLine($"  M (flexion)          : {dM,12:F2} kN-m");
        float mcap;
        float dc = ComputeDC(el, dP, dM, out mcap);
        if (!float.IsNaN(dc)) {
            sb.AppendLine($"  M capacidad en P     : {mcap,12:F2} kN-m");
            sb.AppendLine($"  D/C = M_dem / M_cap  : {dc,12:F3}   {(dc <= 1f ? "OK" : "*** EXCEDE ***")}");
        } else {
            sb.AppendLine("  (sin curva P-M: seccion de viga -> revisar Mz envolvente)");
        }
        sb.AppendLine(line);
        sb.AppendLine("Clic: seleccionar  Der: rotar  Rueda: zoom  F: encuadrar");
        sb.AppendLine("D/M/N: diagramas  C: cambiar caso  P: ocultar panel");
        txtElementInfo.text = sb.ToString();
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

    static void AppendForce(StringBuilder sb, string name, List<float> v, string unit) {
        if (v == null || v.Count < 2) return;
        float vi = v[0], vj = v[1];
        float mx = Mathf.Max(Mathf.Abs(vi), Mathf.Abs(vj));
        sb.AppendLine($"  {name,-7} {vi,12:F2} {vj,12:F2} {mx,12:F2}    {unit}");
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
        // fuera de rango: usar el extremo mas cercano
        if (p < Mathf.Min(P[0], P[P.Count - 1])) return M[P[0] < P[P.Count - 1] ? 0 : P.Count - 1];
        return M[P[0] > P[P.Count - 1] ? 0 : P.Count - 1];
    }

    void DrawPmPlot(ElementMono el) {
        if (pmPlotCanvas == null) return;
        int size = 320;
        float dP = DemandP(el);
        float dM = DemandM(el);
        var tex = new Texture2D(size, size);
        Color bg = new Color(0.95f, 0.95f, 0.98f, 1f);
        Color axCol = new Color(0.3f, 0.3f, 0.3f, 1f);
        Color capCol = new Color(0.1f, 0.35f, 0.6f, 1f);
        Color demCol = new Color(0.8f, 0.15f, 0.15f, 1f);

        for (int px = 0; px < size; px++)
            for (int py = 0; py < size; py++)
                tex.SetPixel(px, py, bg);

        // axes
        int originX = size / 2;
        int originY = 20;
        for (int i = 0; i < size; i++) {
            tex.SetPixel(originX, i, axCol);
            tex.SetPixel(i, originY, axCol);
        }

        if (loader != null && loader.data != null && loader.data.pm_capacity != null
            && (el.elementType == "column" || el.elementType == "wall")) {

            string sec = el.elementType == "column" ? "PILAR-70x70" : "M-20";
            PMCapacityEntry capEntry = null;
            foreach (var entry in loader.data.pm_capacity) {
                if (entry.section == sec || entry.section == el.sectionTag) {
                    capEntry = entry;
                    break;
                }
            }
            if (capEntry == null) {
                foreach (var entry in loader.data.pm_capacity) {
                    if (entry.section == "PILAR-70x70" && el.elementType == "column") {
                        capEntry = entry; break;
                    }
                    if (entry.section == "M-20" && el.elementType == "wall") {
                        capEntry = entry; break;
                    }
                }
            }

            if (capEntry != null) {
                float pMax = 0, pMin = 0, mMax = 0;
                foreach (float pv in capEntry.P) { if (pv > pMax) pMax = pv; if (pv < pMin) pMin = pv; }
                foreach (float mv in capEntry.M) if (Mathf.Abs(mv) > mMax) mMax = Mathf.Abs(mv);
                if (mMax < 1f) mMax = 1f;
                float pRange = pMax - pMin;
                if (pRange < 1f) pRange = 1f;
                float mScale = (size - 40f) / (2f * mMax);
                float pScale = (size - 40f) / pRange;

                for (int i = 0; i < capEntry.P.Count - 1; i++) {
                    int x1 = (int)(originX + capEntry.M[i] * mScale);
                    int y1 = (int)(originY + (capEntry.P[i] - pMin) * pScale);
                    int x2 = (int)(originX + capEntry.M[i + 1] * mScale);
                    int y2 = (int)(originY + (capEntry.P[i + 1] - pMin) * pScale);
                    DrawLine(tex, x1, y1, x2, y2, capCol, 2);
                }

                int dx = (int)(originX + dM * mScale);
                int dy = (int)(originY + (dP - pMin) * pScale);
                for (int ox = -3; ox <= 3; ox++)
                    for (int oy = -3; oy <= 3; oy++) {
                        int px = dx + ox, py = dy + oy;
                        if (px >= 0 && px < size && py >= 0 && py < size)
                            tex.SetPixel(px, py, demCol);
                    }
            }
        }

        tex.Apply();
        pmPlotCanvas.texture = tex;
    }

    /// <summary>Dibuja 6 minigraficos de fuerzas internas (N,Vy,Vz,T,My,Mz) del
    /// elemento seleccionado para el caso activo: valor en i y j con variacion
    /// lineal a lo largo del elemento, y linea de cero.</summary>
    void DrawDiagrams(ElementMono el) {
        if (diagramCanvas == null) return;
        int size = 320;
        float rowH = 44f;
        float topM = 16f;
        float xL = 52f, xR = 308f;
        var tex = new Texture2D(size, size);
        Color bg = new Color(0.97f, 0.97f, 1f, 1f);
        Color zero = new Color(0.4f, 0.4f, 0.45f, 1f);
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
            tex.SetPixel(pxPrev, pyPrev, DIAG_COL[r]);
            for (int t = 1; t <= 40; t++) {
                float ft = (float)t / 40f;
                float xCur = Mathf.Lerp(xL, xR, ft);
                float yCur = yMid + (vi + (vj - vi) * ft) * s;
                DrawLine(tex, pxPrev, pyPrev, (int)xCur, (int)yCur, DIAG_COL[r], 2);
                pxPrev = (int)xCur;
                pyPrev = (int)yCur;
            }
            int xi = (int)xL, xj = (int)xR;
            for (int ox = -2; ox <= 2; ox++)
                for (int oy = -2; oy <= 2; oy++) {
                    tex.SetPixel(xi + ox, (int)(yMid + vi * s) + oy, DIAG_COL[r]);
                    tex.SetPixel(xj + ox, (int)(yMid + vj * s) + oy, DIAG_COL[r]);
                }
        }

        tex.Apply();
        diagramCanvas.texture = tex;
    }

    /// <summary>Etiquetas de las filas del canvas de diagramas (coinciden en
    /// pantalla con las posiciones dibujadas en DrawDiagrams).</summary>
    void OnGUI() {
        if (diagramCanvas == null || !diagramCanvas.gameObject.activeInHierarchy) return;
        if (selected == null) return;
        float W = Screen.width, H = Screen.height;
        float rowH = 44f, topM = 16f;
        float left = W - 516f;
        float topY = H - 320f + topM;
        string caso = (loader != null && loader.activeCase != null)
            ? loader.activeCase : (loader != null && loader.data != null ? loader.data.combinacion : "");
        GUI.contentColor = new Color(0.1f, 0.1f, 0.2f, 1f);
        GUI.Label(new Rect(left, topY - 22f, 300f, 20f), $"Fuerzas i/j  [{caso}]");
        for (int r = 0; r < DIAG_COMPS.Length; r++) {
            GUI.color = DIAG_COL[r];
            GUI.Label(new Rect(left + 2f, topY + r * rowH + 8f, 46f, 24f), DIAG_COMPS[r]);
            GUI.color = Color.white;
            GUI.Label(new Rect(left + 48f, topY + r * rowH + 8f, 40f, 24f), DIAG_UNIT[r]);
            float yi = -180f, yj = 180f;
            List<float> v = selected.ForceFor(DIAG_COMPS[r], loader != null ? loader.activeCase : null);
            if (v != null && v.Count >= 2) { yi = v[0]; yj = v[1]; }
            GUI.color = new Color(0.15f, 0.15f, 0.25f, 1f);
            GUI.Label(new Rect(left + 122f, topY + r * rowH + 8f, 96f, 24f),
                      $"i:{yi,8:F2}");
            GUI.Label(new Rect(left + 216f, topY + r * rowH + 8f, 120f, 24f),
                      $"  j:{yj,8:F2}");
        }
        GUI.color = Color.white;
        GUI.contentColor = Color.white;
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

    static string F3(Vector3 v) => $"{v.x:F3}, {v.y:F3}, {v.z:F3}";
    static string ListInt(System.Collections.Generic.List<int> l) {
        if (l == null) return "?";
        string s = "";
        for (int i = 0; i < l.Count; i++) { if (i > 0) s += ","; s += l[i]; }
        return s;
    }
}
