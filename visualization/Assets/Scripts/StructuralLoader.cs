using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

public class StructuralLoader : MonoBehaviour {
    public Material matColumn;
    public Material matBeam;
    public Material matWall;
    public Material matColumnPhase2;
    public Material matBeamPhase2;
    public Material matWallPhase2;

    private StructuralDataset data;

    void Start() {
        string jsonPath = Path.Combine(Application.streamingAssetsPath, "structural_data.json");
        BuildStructureFromJSON(jsonPath);
    }

    Vector3 ModelToWorld(float x, float y, float z) {
        // Modelo: x,y en planta, z = elevacion. Unity: x,z'=elev,y'=plan
        return new Vector3(x, z, y);
    }

    Material MaterialFor(string type, int phase) {
        bool p2 = phase >= 2;
        if (type == "column") return p2 ? matColumnPhase2 : matColumn;
        if (type == "wall") return p2 ? matWallPhase2 : matWall;
        return p2 ? matBeamPhase2 : matBeam;
    }

    void EnsureMaterials() {
        matColumn = MakeMaterial(new Color(0.2f, 0.5f, 1f, 1f));      // azul
        matBeam = MakeMaterial(new Color(1f, 0.55f, 0.1f, 1f));       // naranja
        matWall = MakeMaterial(new Color(0.2f, 0.8f, 0.5f, 1f));      // verde
        matColumnPhase2 = MakeMaterial(new Color(0.1f, 0.3f, 0.7f, 1f));
        matBeamPhase2 = MakeMaterial(new Color(0.7f, 0.35f, 0.05f, 1f));
        matWallPhase2 = MakeMaterial(new Color(0.1f, 0.5f, 0.3f, 1f));
    }

    Material MakeMaterial(Color c) {
        var m = new Material(Shader.Find("Standard"));
        if (m == null || m.shader == null) m = new Material(Shader.Find("Legacy Shaders/Diffuse"));
        m.color = c;
        return m;
    }

    GameObject CreateBox(Vector3 start, Vector3 end, Material mat, Vector3 section) {
        Vector3 dir = end - start;
        float len = dir.magnitude;
        if (len < 1e-6) return null;
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = "element";
        obj.transform.position = (start + end) / 2f;
        obj.transform.localScale = new Vector3(len, section.y, section.x);
        obj.transform.rotation = Quaternion.FromToRotation(Vector3.right, dir.normalized);
        obj.GetComponent<Collider>().enabled = true;
        SetMaterial(obj, mat);
        return obj;
    }

    GameObject CreateColumn(Vector3 start, Vector3 end, Material mat, float dia) {
        Vector3 dir = end - start;
        float len = dir.magnitude;
        if (len < 1e-6) return null;
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        obj.name = "element";
        obj.transform.position = (start + end) / 2f;
        obj.transform.localScale = new Vector3(dia, len / 2f, dia);
        obj.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
        obj.GetComponent<Collider>().enabled = true;
        SetMaterial(obj, mat);
        return obj;
    }

    void SetMaterial(GameObject obj, Material mat) {
        var r = obj.GetComponent<Renderer>();
        if (r != null && mat != null) r.sharedMaterial = mat;
    }

    void BuildStructureFromJSON(string jsonPath) {
        if (!File.Exists(jsonPath)) {
            Debug.LogError($"[ERROR] No se encontró el JSON en: {jsonPath}");
            return;
        }
        Debug.Log("[OK] Cargando structural_data.json...");
        data = JsonUtility.FromJson<StructuralDataset>(File.ReadAllText(jsonPath));
        if (data?.elements == null) {
            Debug.LogError("[ERROR] JSON inválido o vacío (elements nulo).");
            return;
        }

        EnsureMaterials();

        // Índice de nodos y resultados por id
        var nodeById = new Dictionary<string, NodeData>();
        foreach (var nd in data.nodes) nodeById[nd.id] = nd;
        var resById = new Dictionary<string, ElementResult>();
        foreach (var r in data.results) resById[r.id] = r;

        GameObject parent = new GameObject("Estructura");
        GameObject baseGroup = new GameObject("Apoyos");
        baseGroup.transform.SetParent(parent.transform, false);
        Material matApoyo = MakeMaterial(new Color(0.2f, 0.2f, 0.2f, 1f));
        foreach (NodeData nd in data.nodes) {
            if (nd.fix == null || nd.fix.Count < 1 || nd.fix[0] != 1) continue;
            GameObject s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            s.name = "APOYO-" + nd.id;
            s.transform.position = ModelToWorld(nd.x, nd.y, nd.z);
            s.transform.localScale = Vector3.one * 0.8f;
            s.transform.SetParent(baseGroup.transform, false);
            SetMaterial(s, matApoyo);
        }

        foreach (var ed in data.elements) {
            if (ed.nodes == null || ed.nodes.Count < 2) continue;
            if (!nodeById.TryGetValue(ed.nodes[0], out var n1) ||
                !nodeById.TryGetValue(ed.nodes[1], out var n2)) continue;

            Vector3 p1 = ModelToWorld(n1.x, n1.y, n1.z);
            Vector3 p2 = ModelToWorld(n2.x, n2.y, n2.z);
            Material mat = MaterialFor(ed.type, ed.phase);

            GameObject go;
            if (ed.type == "column") {
                go = CreateColumn(p1, p2, mat, 0.7f);
            } else if (ed.type == "wall") {
                go = CreateBox(p1, p2, mat, new Vector3(0.2f, 1.0f, 0.2f));
            } else {
                go = CreateBox(p1, p2, mat, new Vector3(0.6f, 0.8f, 0.8f));
            }
            if (go == null) continue;

            go.transform.SetParent(parent.transform, true);
            go.name = LoaderNameFromCad(ed.cad_id, ed.id);

            ElementMono mono = go.AddComponent<ElementMono>();
            mono.elementTag = int.Parse(ed.id);
            mono.building = ed.building;
            mono.elementType = ed.type;
            mono.cadID = ed.cad_id;
            mono.sectionTag = ed.sectionTag;
            mono.floor = n1.floor;
            mono.lvl = ed.lvl;
            mono.orient = ed.orient;
            mono.phase = ed.phase;
            mono.tribArea = ed.trib_area;
            mono.wG = ed.w_G;
            mono.wQ = ed.w_Q;
            mono.length = ed.length;

            if (resById.TryGetValue(ed.id, out var rr)) {
                mono.N = rr.N;
                mono.Vy = rr.Vy;
                mono.Mz = rr.Mz;
            }
        }

        Debug.Log($"[OK] Estructura construida: {data.elements.Count} elementos, "
                  + $"{data.nodes.Count} nodos.");
    }

    string LoaderNameFromCad(string cad, string id) {
        return string.IsNullOrEmpty(cad) ? "ELEM-" + id : cad;
    }
}