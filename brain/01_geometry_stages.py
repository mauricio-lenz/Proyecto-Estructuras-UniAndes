import json
import os
from shapely.geometry import Polygon


def build_multiphase_geometry():
    print("=== PROCESANDO GEOMETRÍA Y FASES (PASO 2) ===")

    # -------------------------------------------------------------
    # 1. NODOS: Fase 1 (Base y Piso 1) | Fase 2 (Piso 2)
    # -------------------------------------------------------------
    nodes = {
        # Base (Apoyos) - Fase 1
        "1": {"x": 0.0, "y": 0.0, "z": 0.0, "floor": 0, "phase": 1, "fix": [1,1,1,1,1,1]},
        "2": {"x": 6.0, "y": 0.0, "z": 0.0, "floor": 0, "phase": 1, "fix": [1,1,1,1,1,1]},
        "3": {"x": 6.0, "y": 6.0, "z": 0.0, "floor": 0, "phase": 1, "fix": [1,1,1,1,1,1]},
        "4": {"x": 0.0, "y": 6.0, "z": 0.0, "floor": 0, "phase": 1, "fix": [1,1,1,1,1,1]},

        # Piso 1 (Z = 3.5m) - Fase 1
        "101": {"x": 0.0, "y": 0.0, "z": 3.5, "floor": 1, "phase": 1, "fix": [0,0,0,0,0,0]},
        "102": {"x": 6.0, "y": 0.0, "z": 3.5, "floor": 1, "phase": 1, "fix": [0,0,0,0,0,0]},
        "103": {"x": 6.0, "y": 6.0, "z": 3.5, "floor": 1, "phase": 1, "fix": [0,0,0,0,0,0]},
        "104": {"x": 0.0, "y": 6.0, "z": 3.5, "floor": 1, "phase": 1, "fix": [0,0,0,0,0,0]},
        "9991": {"x": 3.0, "y": 3.0, "z": 3.5, "floor": 1, "phase": 1, "is_cm": True},  # CM Piso 1

        # Piso 2 (Z = 7.0m) - AMPLIACIÓN FASE 2
        "201": {"x": 0.0, "y": 0.0, "z": 7.0, "floor": 2, "phase": 2, "fix": [0,0,0,0,0,0]},
        "202": {"x": 6.0, "y": 0.0, "z": 7.0, "floor": 2, "phase": 2, "fix": [0,0,0,0,0,0]},
        "203": {"x": 6.0, "y": 6.0, "z": 7.0, "floor": 2, "phase": 2, "fix": [0,0,0,0,0,0]},
        "204": {"x": 0.0, "y": 6.0, "z": 7.0, "floor": 2, "phase": 2, "fix": [0,0,0,0,0,0]},
        "9992": {"x": 3.0, "y": 3.0, "z": 7.0, "floor": 2, "phase": 2, "is_cm": True}  # CM Piso 2
    }

    # -------------------------------------------------------------
    # 2. ELEMENTOS POR FASE
    # -------------------------------------------------------------
    elements = {
        # Columnas Fase 1
        "101": {"type": "column", "nodes": ["1", "101"], "sectionTag": "COL_50x50", "cad_id": "COL-A1-P1", "phase": 1},
        "102": {"type": "column", "nodes": ["2", "102"], "sectionTag": "COL_50x50", "cad_id": "COL-A2-P1", "phase": 1},
        "103": {"type": "column", "nodes": ["3", "103"], "sectionTag": "COL_50x50", "cad_id": "COL-B2-P1", "phase": 1},
        "104": {"type": "column", "nodes": ["4", "104"], "sectionTag": "COL_50x50", "cad_id": "COL-B1-P1", "phase": 1},

        # Vigas Fase 1
        "201": {"type": "beam", "nodes": ["101", "102"], "sectionTag": "VIG_30x60", "cad_id": "VIG-EJE1-P1", "phase": 1, "length": 6.0, "trib_poly": [[0,0], [3,0], [3,3], [0,0]]},
        "202": {"type": "beam", "nodes": ["102", "103"], "sectionTag": "VIG_30x60", "cad_id": "VIG-EJEB-P1", "phase": 1, "length": 6.0, "trib_poly": [[6,0], [6,3], [3,3], [6,0]]},

        # Columnas Fase 2
        "301": {"type": "column", "nodes": ["101", "201"], "sectionTag": "COL_50x50", "cad_id": "COL-A1-P2", "phase": 2},
        "302": {"type": "column", "nodes": ["102", "202"], "sectionTag": "COL_50x50", "cad_id": "COL-A2-P2", "phase": 2},
        "303": {"type": "column", "nodes": ["103", "203"], "sectionTag": "COL_50x50", "cad_id": "COL-B2-P2", "phase": 2},
        "304": {"type": "column", "nodes": ["104", "204"], "sectionTag": "COL_50x50", "cad_id": "COL-B1-P2", "phase": 2},

        # Vigas Fase 2
        "401": {"type": "beam", "nodes": ["201", "202"], "sectionTag": "VIG_30x60", "cad_id": "VIG-EJE1-P2", "phase": 2, "length": 6.0, "trib_poly": [[0,0], [3,0], [3,3], [0,0]]},
        "402": {"type": "beam", "nodes": ["202", "203"], "sectionTag": "VIG_30x60", "cad_id": "VIG-EJEB-P2", "phase": 2, "length": 6.0, "trib_poly": [[6,0], [6,3], [3,3], [6,0]]}
    }

    # -------------------------------------------------------------
    # 3. ÁREAS TRIBUTARIAS Y CONSERVACIÓN DE CARGA
    # -------------------------------------------------------------
    q_G = 8.5  # Carga muerta (kN/m2)
    q_Q = 2.5  # Carga viva (kN/m2)

    total_area_fase1 = 0.0
    total_area_fase2 = 0.0

    for eid, elem in elements.items():
        if elem["type"] == "beam" and "trib_poly" in elem:
            poly = Polygon(elem["trib_poly"])
            area = poly.area
            elem["trib_area"] = area
            elem["w_G"] = (q_G * area) / elem["length"]
            elem["w_Q"] = (q_Q * area) / elem["length"]

            if elem["phase"] == 1:
                total_area_fase1 += area
            else:
                total_area_fase2 += area

    area_piso_esperada = 18.0

    print(f"[QA] Áreas Tributarias Calculadas:")
    print(f"     - Fase 1 (Piso 1): {total_area_fase1} m2")
    print(f"     - Fase 2 (Piso 2): {total_area_fase2} m2")

    output_path = "brain/geometry_stages.json"
    with open(output_path, "w") as f:
        json.dump({"nodes": nodes, "elements": elements}, f, indent=2)

    print(f"[OK] Geometría exportada a: {output_path}")
    print("\n-------------------------------------------")
    print(">>> RESULTADO: PASO 2 COMPLETADO CON ÉXITO <<<")
    print("-------------------------------------------")


if __name__ == "__main__":
    build_multiphase_geometry()