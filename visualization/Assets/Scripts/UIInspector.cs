using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public class UIInspector : MonoBehaviour {
    public Text txtElementInfo;
    public RawImage pmPlotCanvas;
    public StructuralLoader loader;

    private ElementMono selected;
    private Material lastHighMat;

    void Awake() {
        if (loader == null) loader = GetComponent<StructuralLoader>();
        SetupPanel();
    }

    /// <summary>Ancho grande + fuente monoespaciada para que la tabla numérica quede alineada.</summary>
    void SetupPanel() {
        if (txtElementInfo == null) return;
        var rt = txtElementInfo.rectTransform;
        if (rt != null) rt.sizeDelta = new Vector2(600f, 700f);
        Font mono = null;
        try { mono = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Courier New", "DejaVu Sans Mono" }, 15); }
        catch (System.Exception) { }
        if (mono != null) txtElementInfo.font = mono;
    }

    void Update() {
        if (loader == null) loader = GetComponent<StructuralLoader>();
        if (Input.GetMouseButtonDown(0) && loader != null) {
            Ray ray = Camera.main != null
                ? Camera.main.ScreenPointToRay(Input.mousePosition) : new Ray();
            if (Physics.Raycast(ray, out RaycastHit hit)) {
                var el = hit.collider.GetComponentInParent<ElementMono>();
                SelectElement(el);
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
    }

    void ClearSelection() {
        ClearHighlight();
        selected = null;
        if (txtElementInfo != null)
            txtElementInfo.text = "Haz clic en un elemento de la estructura...";
        if (pmPlotCanvas != null) pmPlotCanvas.texture = null;
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
        const string line = "----------------------------------------------------------------";

        var sb = new StringBuilder();
        sb.AppendLine($"== {el.building}  {tipo}  ·  TAG {el.elementTag} ==");
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

        sb.AppendLine($"FUERZAS INTERNAS   [{comb}]");
        sb.AppendLine("  Fuerza          i            j        Max|.|    Unidad");
        AppendForce(sb, "N",    el.N,  "kN");
        AppendForce(sb, "Vy",   el.Vy, "kN");
        AppendForce(sb, "Vz",   el.Vz, "kN");
        AppendForce(sb, "T",    el.T,  "kN-m");
        AppendForce(sb, "My",   el.My, "kN-m");
        AppendForce(sb, "Mz",   el.Mz, "kN-m");
        sb.AppendLine(line);

        sb.AppendLine("DEMANDA / CAPACIDAD (P-M)");
        sb.AppendLine($"  P (compresion +)     : {el.demandP,12:F2} kN");
        sb.AppendLine($"  M (flexion)          : {el.demandM,12:F2} kN-m");
        float mcap;
        float dc = ComputeDC(el, out mcap);
        if (!float.IsNaN(dc)) {
            sb.AppendLine($"  M capacidad en P     : {mcap,12:F2} kN-m");
            sb.AppendLine($"  D/C = M_dem / M_cap  : {dc,12:F3}   {(dc <= 1f ? "OK" : "*** EXCEDE ***")}");
        } else {
            sb.AppendLine("  (sin curva P-M: seccion de viga -> revisar Mz envolvente)");
        }
        sb.AppendLine(line);
        sb.AppendLine("Clic derecho: rotar   Rueda: zoom   F: reencuadrar");
        txtElementInfo.text = sb.ToString();
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

    /// <summary>D/C = M_demanda / M_capacidad interpolada en P=demandP. NaN si no aplica.</summary>
    float ComputeDC(ElementMono el, out float mcap) {
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
        float mc = InterpCapacityM(cap, el.demandP);
        if (mc <= 1e-6f) return float.NaN;
        mcap = mc;
        return Mathf.Abs(el.demandM) / mc;
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
        int size = 180;
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

                int dx = (int)(originX + el.demandM * mScale);
                int dy = (int)(originY + (el.demandP - pMin) * pScale);
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
