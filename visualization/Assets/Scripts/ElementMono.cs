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
    public Vector3 localL1;
    public Vector3 localL2;
    public Vector3 localL3;
    public List<string> nodeIds;
    public bool isSelected;
    public Material originalMaterial;

    public Vector3 worldStart { get; set; }
    public Vector3 worldEnd { get; set; }
}
