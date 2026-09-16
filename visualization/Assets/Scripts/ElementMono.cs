using System.Collections.Generic;
using UnityEngine;

public class ElementMono : MonoBehaviour {
    public int elementTag;
    public string building;
    public string elementType;
    public string cadID;
    public string sectionTag;
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
    public bool isSelected;
    public Material originalMaterial;
}