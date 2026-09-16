using System.IO;
using System.Collections.Generic;
using UnityEngine;

public class StructuralLoader : MonoBehaviour {
    private string jsonPath;

    void Start() {
        jsonPath = Path.Combine(Application.streamingAssetsPath, "structural_data.json");
        BuildStructureFromJSON();
    }

    void BuildStructureFromJSON() {
        if (!File.Exists(jsonPath)) {
            Debug.LogError($"[ERROR] No se encontró el JSON en: {jsonPath}");
            return;
        }

        Debug.Log("[OK] Cargando datos estructurales en Unity desde StreamingAssets...");

        // Generación de prueba de elementos 3D
        CreateElement3D(new Vector3(0, 0, 0), new Vector3(0, 0, 3.5f), "COL-101", 101, "column", 1);
        CreateElement3D(new Vector3(6, 0, 0), new Vector3(6, 0, 3.5f), "COL-102", 102, "column", 1);
        CreateElement3D(new Vector3(0, 0, 3.5f), new Vector3(6, 0, 3.5f), "VIG-201", 201, "beam", 1);
    }

    void CreateElement3D(Vector3 start, Vector3 end, string name, int tag, string type, int phase) {
        GameObject obj = GameObject.CreatePrimitive(type == "column" ? PrimitiveType.Cylinder : PrimitiveType.Cube);
        obj.name = name;
        obj.transform.position = (start + end) / 2.0f;

        if (type == "column") {
            obj.transform.localScale = new Vector3(0.5f, Vector3.Distance(start, end) / 2.0f, 0.5f);
        } else {
            obj.transform.localScale = new Vector3(0.3f, 0.6f, Vector3.Distance(start, end));
            obj.transform.lookAt(end);
        }

        ElementMono mono = obj.AddComponent<ElementMono>();
        mono.elementTag = tag;
        mono.elementType = type;
        mono.cadID = name;
        mono.phase = phase;
    }
}