using System.Collections.Generic;
using UnityEngine;

public class ElementMono : MonoBehaviour {
    public int elementTag;
    public string building;
    public string elementType;
    public string cadID;
    public string sectionTag;
    public string material;
    public int floor;
    public string lvl;
    public string orient;
    public int phase;
    public float length;
    public float tribArea;
    public float wG;
    public float wQ;
    public List<float> N;
    public List<float> Vy;
    public List<float> Mz;
    public List<float> T;
    public List<float> My;
    public List<float> Vz;
    public float demandP;
    public float demandM;
    // Fuerzas por caso individual (G, Q, EX, EY): caso -> componente -> [i, j]
    public Dictionary<string, Dictionary<string, List<float>>> forcesByCase;
    public Vector3 localL1;
    public Vector3 localL2;
    public Vector3 localL3;
    public List<string> nodeIds;
    public bool isSelected;
    public Material originalMaterial;

    public Vector3 worldStart { get; set; }
    public Vector3 worldEnd { get; set; }

    /// <summary>Devuelve las fuerzas de un caso (componente -> [i, j]); si no hay
    /// caso individual, devuelve las de la combinacion (campos estandar).</summary>
    public List<float> ForceFor(string comp, string caso = null) {
        if (caso != null && forcesByCase != null && forcesByCase.TryGetValue(caso, out var comps)
            && comps.TryGetValue(comp, out var v) && v != null && v.Count >= 2)
            return v;
        if (comp == "N") return N;
        if (comp == "Vy") return Vy;
        if (comp == "Vz") return Vz;
        if (comp == "T") return T;
        if (comp == "My") return My;
        if (comp == "Mz") return Mz;
        return null;
    }
}
