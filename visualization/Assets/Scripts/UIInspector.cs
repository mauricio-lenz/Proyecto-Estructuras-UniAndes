using UnityEngine;
using UnityEngine.UI;

public class UIInspector : MonoBehaviour {
    public Text txtElementInfo;
    public RawImage pmPlotCanvas;

    private ElementMono selected;
    private MeshRenderer lastRenderer;

    void Update() {
        if (Input.GetMouseButtonDown(0)) {
            Ray ray = Camera.main != null ? Camera.main.ScreenPointToRay(Input.mousePosition) : new Ray();
            if (Physics.Raycast(ray, out RaycastHit hit)) {
                ElementMono element = hit.collider.GetComponentInParent<ElementMono>();
                Select(element);
            }
        }
    }

    void Select(ElementMono element) {
        if (element == null || element == selected) {
            Deselect();
            return;
        }
        DeselectCurrent();
        selected = element;
        ShowElementInfo(element);
        Highlight(element, true);

        if (pmPlotCanvas != null) {
            Color c = element.elementType == "column" ? new Color(0.2f, 0.6f, 1f, 1f)
                     : element.elementType == "wall" ? new Color(0.2f, 0.8f, 0.4f, 1f)
                     : new Color(0.9f, 0.4f, 0.2f, 1f);
            pmPlotCanvas.color = c;
        }
    }

    void Deselect() {
        DeselectCurrent();
        selected = null;
        if (txtElementInfo != null) txtElementInfo.text = "Haz clic en un elemento de la estructura...";
    }

    void DeselectCurrent() {
        if (selected != null) Highlight(selected, false);
    }

    void Highlight(ElementMono element, bool on) {
        var r = element.GetComponent<Renderer>();
        if (r == null) return;
        if (on) {
            element.originalMaterial = r.sharedMaterial;
            var selMat = new Material(Shader.Find("Standard"));
            if (selMat == null || selMat.shader == null) selMat = new Material(Shader.Find("Legacy Shaders/Diffuse"));
            selMat.color = new Color(1f, 1f, 0.2f, 1f);  // amarillo de selección
            r.sharedMaterial = selMat;
        } else if (element.originalMaterial != null) {
            r.sharedMaterial = element.originalMaterial;
        }
    }

    void ShowElementInfo(ElementMono element) {
        if (txtElementInfo == null) return;

        string tipo = element.elementType == "column" ? "COLUMNA"
                    : element.elementType == "wall" ? "MURO" : "VIGA";

        string buildingLabel = string.IsNullOrEmpty(element.building) ? "" : element.building + "";
        string info  = $"EDIFICIO: {buildingLabel}\n";
        info        += $"TAG: {element.elementTag}\n";
        info        += $"NOMBRE: {element.cadID}\n";
        info        += $"TIPO: {tipo}  |  SECCION: {element.sectionTag}\n";
        info        += $"NIVEL: {element.lvl}  |  FASE: {element.phase}\n";
        info        += $"ORIENTACION: {element.orient}  |  LARGO: {element.length:F2} m\n";
        info        += $"AREA TRIBUTARIA: {element.tribArea:F2} m2\n";
        info        += $"w_G: {element.wG:F2} kN/m  |  w_Q: {element.wQ:F2} kN/m\n\n";

        info += "--- FUERZAS INTERNAS (superposicion) ---\n";
        if (element.N != null && element.N.Count >= 2)
            info += $"N : {element.N[0]:F2} / {element.N[1]:F2} kN\n";
        if (element.Vy != null && element.Vy.Count >= 2)
            info += $"Vy: {element.Vy[0]:F2} / {element.Vy[1]:F2} kN\n";
        if (element.Mz != null && element.Mz.Count >= 2)
            info += $"Mz: {element.Mz[0]:F2} / {element.Mz[1]:F2} kN-m";

        txtElementInfo.text = info;
    }
}