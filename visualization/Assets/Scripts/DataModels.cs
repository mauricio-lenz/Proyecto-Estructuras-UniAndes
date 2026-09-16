using System;
using System.Collections.Generic;

[Serializable]
public class NodeData {
    public string id;
    public string building;
    public float x, y, z;
    public int floor;
    public int phase;
    public List<int> fix;
}

[Serializable]
public class ElementData {
    public string id;
    public string building;
    public string type;
    public List<string> nodes;
    public string sectionTag;
    public string cad_id;
    public int phase;
    public float trib_area;
    public float w_G;
    public float w_Q;
    public float length;
    public string orient;
    public string lvl;
}

[Serializable]
public class ElementResult {
    public string id;
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
    public List<NodeData> nodes;
    public List<ElementData> elements;
    public List<ElementResult> results;
    public PMCapacity pm_capacity;
}