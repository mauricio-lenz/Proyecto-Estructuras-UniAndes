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
        if (loader != null) {
            if (el.nodeIds != null && el.nodeIds.Count >= 2) {
                if (loader.nodeById.TryGetValue(el.nodeIds[0], out var n1)) fixStr += "i:{" + ListInt(n1.fix) + "}";
                if (loader.nodeById.TryGetValue(el.nodeIds[1], out var n2)) fixStr += "  j:{" + ListInt(n2.fix) + "}";
            }
        }
        string info = "";
        info += $"EDIFICIO: {el.building}\n";
        info += $"TAG: {el.elementTag}\n";
        info += $"NOMBRE: {el.cadID}\n";
        info += $"TIPO: {tipo}   SECCION: {el.sectionTag}\n";
        info += $"MATERIAL: {el.material}\n";
        info += $"NIVEL: {el.lvl}   FASE: {el.phase}\n";
        info += $"ORIENTACION: {el.orient}   LARGO: {el.length:F2} m\n";
        info += $"AREA TRIBUTARIA: {el.tribArea:F2} m2\n";
        info += $"w_G: {el.wG:F2} kN/m   w_Q: {el.wQ:F2} kN/m\n";
        info += $"RESTRICCIONES: {fixStr}\n";
        info += $"EJE LOCAL L1: ({F3(el.localL1)})\n";
        info += $"EJE LOCAL L2: ({F3(el.localL2)})\n";
        info += $"EJE LOCAL L3: ({F3(el.localL3)})\n\n";
        info += $"--- COMBINACION: {(loader != null && loader.data != null ? loader.data.combinacion : "---")} ---\n";
        if (el.N != null && el.N.Count >= 2)
            info += $"N  : {el.N[0]:F2}  /  {el.N[1]:F2} kN\n";
        if (el.Vy != null && el.Vy.Count >= 2)
            info += $"Vy : {el.Vy[0]:F2}  /  {el.Vy[1]:F2} kN\n";
        if (el.Vz != null && el.Vz.Count >= 2)
            info += $"Vz : {el.Vz[0]:F2}  /  {el.Vz[1]:F2} kN\n";
        if (el.T != null && el.T.Count >= 2)
            info += $"T  : {el.T[0]:F2}  /  {el.T[1]:F2} kN-m\n";
        if (el.My != null && el.My.Count >= 2)
            info += $"My : {el.My[0]:F2}  /  {el.My[1]:F2} kN-m\n";
        if (el.Mz != null && el.Mz.Count >= 2)
            info += $"Mz : {el.Mz[0]:F2}  /  {el.Mz[1]:F2} kN-m\n\n";
        info += $"DEMANDA P (compresion +): {el.demandP:F2} kN\n";
        info += $"DEMANDA M (flexion): {el.demandM:F2} kN-m\n";
        info += $"OpenSees elementTag: {el.elementTag}";
        txtElementInfo.text = info;
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
