using System.IO;
using System.Collections.Generic;
using UnityEngine;

public class StructuralLoader : MonoBehaviour {
    public Material matColumn;
    public Material matBeam;
    public Material matWall;
    public Material matColumnPhase2;
    public Material matBeamPhase2;
    public Material matWallPhase2;
    public Material matSlab;
    public Material matVol;
    public Material matApoyo;

    public bool unirEdificios = true;

    public StructuralDataset data { get; private set; }
    public Dictionary<string, NodeData> nodeById { get; private set; }
    public Dictionary<string, ElementResult> resById { get; private set; }
    public List<ElementMono> elementMonos { get; private set; }

    readonly Dictionary<string, Vector2> planOff = new Dictionary<string, Vector2>();
    readonly Dictionary<string, float> elevOff = new Dictionary<string, float>();

    void Start() {
        string jsonPath = Path.Combine(Application.streamingAssetsPath, "structural_data.json");
        BuildStructureFromJSON(jsonPath);
    }

    // Réplica de P1L2ModelBuilder.ComputePlanOffsets del viewer P1L2 (unirEdificios):
    // si hay 2 edificios, elimina el hueco uniéndolos por su lado angosto (cara
    // corta), alinea los rangos en planta y sube el más bajo hasta igualar el
    // nivel superior del más alto (techos a la misma altura).
    void ComputePlanOffsets() {
        planOff.Clear();
        elevOff.Clear();
        if (data == null || data.nodes == null || data.nodes.Count == 0) return;

        var count = new Dictionary<string, int>();
        var mins = new Dictionary<string, Vector2>();
        var maxs = new Dictionary<string, Vector2>();
        var maxZ = new Dictionary<string, float>();

        foreach (var n in data.nodes) {
            string b = string.IsNullOrEmpty(n.building) ? "ED1" : n.building;
            if (!mins.ContainsKey(b)) {
                count[b] = 0;
                mins[b] = new Vector2(float.MaxValue, float.MaxValue);
                maxs[b] = new Vector2(float.MinValue, float.MinValue);
                maxZ[b] = float.MinValue;
            }
            count[b]++;
            mins[b] = new Vector2(Mathf.Min(mins[b].x, n.x), Mathf.Min(mins[b].y, n.y));
            maxs[b] = new Vector2(Mathf.Max(maxs[b].x, n.x), Mathf.Max(maxs[b].y, n.y));
            maxZ[b] = Mathf.Max(maxZ[b], n.z);
        }

        var keys = new List<string>(mins.Keys);
        foreach (var k in keys) { planOff[k] = Vector2.zero; elevOff[k] = 0f; }

        if (!unirEdificios || keys.Count < 2) return;

        keys.Sort();
        string A = count[keys[0]] >= count[keys[1]] ? keys[0] : keys[1];
        string B = A == keys[0] ? keys[1] : keys[0];

        var minA = mins[A]; var maxA = maxs[A];
        var minB = mins[B]; var maxB = maxs[B];
        float dxA = maxA.x - minA.x, dyA = maxA.y - minA.y;
        float dxB = maxB.x - minB.x, dyB = maxB.y - minB.y;

        Vector2 offB;
        if (dxA >= dyA && dxB >= dyB) {
            offB.y = minA.y - minB.y;
            offB.x = minA.x - maxB.x;
        } else {
            offB.x = minA.x - minB.x;
            offB.y = maxA.y - minB.y;
        }
        planOff[B] = offB;

        float top = Mathf.Max(maxZ[A], maxZ[B]);
        elevOff[A] = top - maxZ[A];
        elevOff[B] = top - maxZ[B];

        Debug.Log($"[P1L4] unirEdificios: A={A} B={B} planOff[B]={offB} " +
                  $"elevOff A={elevOff[A]} B={elevOff[B]}");
    }

    Vector3 ModelToWorld(string building, float x, float y, float z) {
        Vector2 off = Vector2.zero;
        if (building != null && planOff.TryGetValue(building, out var po)) off = po;
        float eo = 0f;
        if (building != null && elevOff.TryGetValue(building, out var el)) eo = el;
        return new Vector3(x + off.x, z + eo, y + off.y);
    }

    Material MakeMaterial(Color c) {
        var m = new Material(Shader.Find("Standard"));
        if (m == null || m.shader == null)
            m = new Material(Shader.Find("Legacy Shaders/Diffuse"));
        m.color = c;
        return m;
    }

    Material MakeTransparent(Color c, float alpha) {
        var m = MakeMaterial(new Color(c.r, c.g, c.b, alpha));
        m.SetFloat("_Mode", 3);
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON");
        m.EnableKeyword("_ALPHABLEND_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = 3000;
        m.color = new Color(c.r, c.g, c.b, alpha);
        return m;
    }

    Material MaterialFor(string type, int phase) {
        bool p2 = phase >= 2;
        if (type == "column") return p2 ? matColumnPhase2 : matColumn;
        if (type == "wall")   return p2 ? matWallPhase2 : matWall;
        return p2 ? matBeamPhase2 : matBeam;
    }

    void EnsureMaterials() {
        matColumn = MakeMaterial(new Color(0.2f, 0.5f, 1f, 1f));
        matBeam   = MakeMaterial(new Color(1f, 0.55f, 0.1f, 1f));
        matWall   = MakeMaterial(new Color(0.2f, 0.8f, 0.5f, 1f));
        matColumnPhase2 = MakeMaterial(new Color(0.1f, 0.3f, 0.7f, 1f));
        matBeamPhase2   = MakeMaterial(new Color(0.7f, 0.35f, 0.05f, 1f));
        matWallPhase2   = MakeMaterial(new Color(0.1f, 0.5f, 0.3f, 1f));
        matSlab  = MakeTransparent(new Color(0.6f, 0.6f, 0.65f), 0.25f);
        matVol   = MakeTransparent(new Color(0.86f, 0.71f, 0.2f), 0.35f);
        matApoyo = MakeMaterial(new Color(0.1f, 0.6f, 0.2f, 1f));
    }

    GameObject CreateBox(Vector3 start, Vector3 end, Material mat, Vector3 section) {
        Vector3 dir = end - start;
        float len = dir.magnitude;
        if (len < 1e-6f) return null;
        var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = "element";
        obj.transform.position = (start + end) / 2f;
        obj.transform.localScale = new Vector3(len, section.y, section.x);
        obj.transform.rotation = Quaternion.FromToRotation(Vector3.right, dir.normalized);
        obj.GetComponent<Collider>().enabled = true;
        obj.GetComponent<Renderer>().sharedMaterial = mat;
        return obj;
    }

    GameObject CreateColumn(Vector3 start, Vector3 end, Material mat, float dia) {
        Vector3 dir = end - start;
        float len = dir.magnitude;
        if (len < 1e-6f) return null;
        var obj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        obj.name = "element";
        obj.transform.position = (start + end) / 2f;
        obj.transform.localScale = new Vector3(dia, len / 2f, dia);
        obj.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
        obj.GetComponent<Collider>().enabled = true;
        obj.GetComponent<Renderer>().sharedMaterial = mat;
        return obj;
    }

    void BuildStructureFromJSON(string jsonPath) {
        if (!File.Exists(jsonPath)) {
            Debug.LogError($"[ERROR] No se encontro JSON: {jsonPath}");
            return;
        }
        data = JsonUtility.FromJson<StructuralDataset>(File.ReadAllText(jsonPath));
        if (data?.elements == null) {
            Debug.LogError("[ERROR] JSON invalido.");
            return;
        }

        EnsureMaterials();
        ComputePlanOffsets();
        nodeById = new Dictionary<string, NodeData>();
        foreach (var nd in data.nodes) nodeById[nd.id] = nd;
        resById = new Dictionary<string, ElementResult>();
        foreach (var r in data.results) resById[r.id] = r;

        elementMonos = new List<ElementMono>();
        GameObject parent = new GameObject("Estructura");
        GameObject losasGroup = new GameObject("Losas");
        losasGroup.transform.SetParent(parent.transform, false);
        GameObject volGroup = new GameObject("Voladizos");
        volGroup.transform.SetParent(parent.transform, false);

        Material matApoyoObj = matApoyo;
        foreach (var nd in data.nodes) {
            if (nd.fix == null || nd.fix.Count < 1 || nd.fix[0] != 1) continue;
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            s.name = "APOYO-" + nd.id;
            s.transform.position = ModelToWorld(nd.building, nd.x, nd.y, nd.z);
            s.transform.localScale = Vector3.one * 0.8f;
            s.GetComponent<Renderer>().sharedMaterial = matApoyoObj;
            s.transform.SetParent(parent.transform, false);
        }

        foreach (var ed in data.elements) {
            if (ed.nodes == null || ed.nodes.Count < 2) continue;
            if (!nodeById.TryGetValue(ed.nodes[0], out var n1) ||
                !nodeById.TryGetValue(ed.nodes[1], out var n2)) continue;

            Vector3 p1 = ModelToWorld(n1.building, n1.x, n1.y, n1.z);
            Vector3 p2 = ModelToWorld(n2.building, n2.x, n2.y, n2.z);
            Material mat = MaterialFor(ed.type, ed.phase);

            GameObject go;
            if (ed.type == "column")
                go = CreateColumn(p1, p2, mat, 0.7f);
            else if (ed.type == "wall")
                go = CreateBox(p1, p2, mat, new Vector3(0.2f, 1.0f, 0.2f));
            else
                go = CreateBox(p1, p2, mat, new Vector3(0.6f, 0.8f, 0.8f));
            if (go == null) continue;

            go.transform.SetParent(parent.transform, true);
            go.name = string.IsNullOrEmpty(ed.cad_id) ? "ELEM-" + ed.id : ed.cad_id;

            var mono = go.AddComponent<ElementMono>();
            mono.elementTag = int.Parse(ed.id);
            mono.building = ed.building;
            mono.elementType = ed.type;
            mono.cadID = ed.cad_id;
            mono.sectionTag = ed.sectionTag;
            mono.material = ed.material;
            mono.floor = n1.floor;
            mono.lvl = ed.lvl;
            mono.orient = ed.orient;
            mono.phase = ed.phase;
            mono.tribArea = ed.trib_area;
            mono.wG = ed.w_G;
            mono.wQ = ed.w_Q;
            mono.length = ed.length;
            mono.nodeIds = ed.nodes;
            mono.worldStart = p1;
            mono.worldEnd = p2;

            if (ed.local != null && ed.local.Count >= 9) {
                mono.localL1 = new Vector3(ed.local[0], ed.local[2], ed.local[1]);
                mono.localL2 = new Vector3(ed.local[3], ed.local[5], ed.local[4]);
                mono.localL3 = new Vector3(ed.local[6], ed.local[8], ed.local[7]);
            }

            if (resById.TryGetValue(ed.id, out var rr)) {
                mono.N  = rr.N;  mono.Vy = rr.Vy; mono.Mz = rr.Mz;
                mono.T  = rr.T;  mono.My = rr.My; mono.Vz = rr.Vz;
                mono.demandP = rr.P;
                mono.demandM = rr.M;
            }
            elementMonos.Add(mono);
        }

        if (data.slabs != null) {
            foreach (var s in data.slabs) {
                Vector3 center = SlabCenter(s);
                float dx = SlabRange(s.x);
                float dy = SlabRange(s.y);
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"Losas-{s.lvl}-{s.building}";
                go.transform.localScale = new Vector3(dx, s.e, dy);
                go.transform.position = ModelToWorld(s.building, center.x, center.y, s.z + s.e / 2f);
                go.GetComponent<Collider>().enabled = false;
                go.GetComponent<Renderer>().sharedMaterial = matSlab;
                go.transform.SetParent(losasGroup.transform, false);
            }
        }

        if (data.voladizos != null) {
            foreach (var v in data.voladizos) {
                Vector3 center = VolCenter(v);
                float dx = VolRangeX(v);
                float dy = VolRangeY(v);
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = v.id;
                go.transform.localScale = new Vector3(dx, v.e, dy);
                go.transform.position = ModelToWorld(v.building, center.x, center.y, v.z + v.e / 2f);
                go.GetComponent<Collider>().enabled = false;
                go.GetComponent<Renderer>().sharedMaterial = matVol;
                go.transform.SetParent(volGroup.transform, false);
            }
        }

        Debug.Log($"[OK] Estructura: {data.elements.Count} elem, {data.nodes.Count} nodos, "
                  + $"{(data.slabs != null ? data.slabs.Count : 0)} losas, "
                  + $"{(data.voladizos != null ? data.voladizos.Count : 0)} voladizos.");
    }

    static Vector3 SlabCenter(SlabData s) {
        float cx = (s.x[0] + s.x[1] + s.x[2] + s.x[3]) / 4f;
        float cy = (s.y[0] + s.y[1] + s.y[2] + s.y[3]) / 4f;
        return new Vector3(cx, cy, 0f);
    }

    static float SlabRange(List<float> vals) {
        float mn = float.MaxValue, mx = float.MinValue;
        foreach (float v in vals) { if (v < mn) mn = v; if (v > mx) mx = v; }
        return mx - mn;
    }

    static Vector3 VolCenter(VoladizoData v) {
        float cx = (v.x[0] + v.x[1] + v.x[2] + v.x[3]) / 4f;
        float cy = (v.y[0] + v.y[1] + v.y[2] + v.y[3]) / 4f;
        return new Vector3(cx, cy, 0f);
    }

    static float VolRangeX(VoladizoData v) {
        float mn = float.MaxValue, mx = float.MinValue;
        foreach (float vx in v.x) { if (vx < mn) mn = vx; if (vx > mx) mx = vx; }
        return mx - mn;
    }

    static float VolRangeY(VoladizoData v) {
        float mn = float.MaxValue, mx = float.MinValue;
        foreach (float vy in v.y) { if (vy < mn) mn = vy; if (vy > mx) mx = vy; }
        return mx - mn;
    }
}
