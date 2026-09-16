using UnityEngine;
using UnityEngine.UI;

public class UIInspector : MonoBehaviour {
    public Text txtElementInfo;
    public RawImage pmPlotCanvas;

    private ElementMono selected;

    void Update() {
        if (Input.GetMouseButtonDown(0)) {
            Ray ray = Camera.main != null ? Camera.main.ScreenPointToRay(Input.mousePosition) : new Ray();
            if (Physics.Raycast(ray, out RaycastHit hit)) {
                ElementMono element = hit.collider.GetComponentInParent<ElementMono>();
                if (element != null) {
                    selected = element;
                    ShowElementInfo(element);
                }
            }
        }
    }

    void ShowElementInfo(ElementMono element) {
        if (txtElementInfo == null) return;

        string info = $"ELEMENTO: {element.cadID}\n" +
                      $"TAG: {element.elementTag}  |  TIPO: {element.elementType}\n" +
                      $"FASE: {element.phase}  |  AREA TRIB.: {element.tribArea} m2\n\n";

        if (element.N != null && element.N.Count >= 2) {
            info += $"N: {element.N[0]} / {element.N[1]} kN\n";
        }
        if (element.Vy != null && element.Vy.Count >= 2) {
            info += $"Vy: {element.Vy[0]} / {element.Vy[1]} kN\n";
        }
        if (element.Mz != null && element.Mz.Count >= 2) {
            info += $"Mz: {element.Mz[0]} / {element.Mz[1]} kN-m";
        }

        txtElementInfo.text = info;

        if (pmPlotCanvas != null) {
            Color c = element.elementType == "column" ? new Color(0.2f, 0.6f, 1f, 1f) : new Color(0.9f, 0.4f, 0.2f, 1f);
            pmPlotCanvas.color = c;
        }
    }
}