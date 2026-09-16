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
    public float dx, dy, dz;
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
    public string material;
    public List<float> local;
}

[Serializable]
public class ElementResult {
    public string id;
    public string building;
    public List<float> N;
    public List<float> Vy;
    public List<float> Mz;
    public List<float> T;
    public List<float> My;
    public List<float> Vz;
    public float P;
    public float M;
}

[Serializable]
public class PMCapacityEntry {
    public string section;
    public List<float> P;
    public List<float> M;
}

[Serializable]
public class SlabData {
    public string building;
    public string lvl;
    public List<float> x;
    public List<float> y;
    public float z;
    public float e;
    public int phase;
    public float qG;
    public float qQ;
    public float area;
}

[Serializable]
public class VoladizoData {
    public string building;
    public string id;
    public string lvl;
    public List<float> x;
    public List<float> y;
    public float z;
    public float e;
    public int phase;
    public float area;
}

[Serializable]
public class ApoyoData {
    public string node;
    public string building;
    public List<int> fix;
    public float z;
}

[Serializable]
public class StructuralDataset {
    public string schema;
    public List<NodeData> nodes;
    public List<ElementData> elements;
    public List<ElementResult> results;
    public List<CaseResultsWrapper> results_cases;
    public List<CaseDisplacementWrapper> displacements_cases;
    public List<string> casos;
    public List<SlabData> slabs;
    public List<VoladizoData> voladizos;
    public List<ApoyoData> apoyos;
    public List<PMCapacityEntry> pm_capacity;
    public string combinacion;
    public ModelMetadata metadata;
}

[Serializable]
public class CaseResultsWrapper {
    public string name;
    public List<ElementResult> entries;
}

[Serializable]
public class CaseDisplacementWrapper {
    public string name;
    public List<NodeDisplacementData> entries;
}

[Serializable]
public class NodeDisplacementData {
    public string node;
    public List<float> u;
}

[Serializable]
public class StrEntry {
    public string k;
    public string v;
}

[Serializable]
public class ModelMetadata {
    public string fuente;
    public string generador;
    public List<float> materiales;
    public List<StrEntry> controles;
    public List<StrEntry> trazabilidad;
}
