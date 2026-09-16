using System;
using System.Collections.Generic;

[Serializable]
public class NodeData {
    public float x, y, z;
    public int floor;
    public int phase;
    public List<int> fix;
}

[Serializable]
public class ElementData {
    public string type;
    public List<string> nodes;
    public string sectionTag;
    public string cad_id;
    public int phase;
    public float trib_area;
    public float w_G;
    public float w_Q;
}

[Serializable]
public class ElementResult {
    public List<float> N;
    public List<float> Vy;
    public List<float> Mz;
}

[Serializable]
public class PMCapacity {
    public List<float> P;
    public List<float> M;
}

[Serializable]
public class StructuralDataset {
    public Dictionary<string, NodeData> nodes;
    public Dictionary<string, ElementData> elements;
    public Dictionary<string, ElementResult> results;
    public PMCapacity pm_capacity;
}