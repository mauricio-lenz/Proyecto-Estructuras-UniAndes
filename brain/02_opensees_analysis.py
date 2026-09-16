import json
import os
import openseespy.opensees as ops


def run_staged_analysis():
    print("=== PROCESANDO MODELO EVOLUTIVO OPENSEES (PASO 3) ===")

    # 1. Cargar Geometría del Paso 2
    geom_path = "brain/geometry_stages.json"
    if not os.path.exists(geom_path):
        print("[ERROR] No se encontró 'brain/geometry_stages.json'. Ejecuta el Paso 2 primero.")
        return

    with open(geom_path, "r") as f:
        data = json.load(f)

    nodes = data["nodes"]
    elements = data["elements"]

    # 2. Inicializar Modelo OpenSees 3D (6 DOF por nodo)
    ops.wipe()
    ops.model('basic', '-ndm', 3, '-ndf', 6)

    # Definir Nodos
    for nid, ndata in nodes.items():
        ops.node(int(nid), ndata["x"], ndata["y"], ndata["z"])
        fix = ndata.get("fix", [0, 0, 0, 0, 0, 0])
        if sum(fix) > 0:
            ops.fix(int(nid), *fix)

    # Aplicar Diafragmas Rígidos
    ops.rigidDiaphragm(3, 9991, 101, 102, 103, 104)  # Diafragma Piso 1
    ops.rigidDiaphragm(3, 9992, 201, 202, 203, 204)  # Diafragma Piso 2
    # Fijar DOF del nodo maestro no acoplados por el diafragma (z, rx, ry) para evitar singularidad
    ops.fix(9991, 0, 0, 1, 1, 1, 0)
    ops.fix(9992, 0, 0, 1, 1, 1, 0)

    # Transformaciones Geométricas Locales
    # Columnas (eje local Z): vecxz ortogonal -> global X
    ops.geomTransf('Linear', 1, 1.0, 0.0, 0.0)
    # Vigas sobre eje X: vecxz = global Z (ortogonal al eje local X)
    ops.geomTransf('Linear', 2, 0.0, 0.0, 1.0)
    # Vigas sobre eje Y: vecxz = global X (ortogonal al eje local Y)
    ops.geomTransf('Linear', 3, 1.0, 0.0, 0.0)

    def transf_for(eid, edata):
        if edata["type"] == "column":
            return 1
        n1, n2 = edata["nodes"]
        dy = nodes[n2]["y"] - nodes[n1]["y"]
        return 3 if abs(dy) > 1e-9 else 2

    # Definir Elementos Marco Elásticos (ElasticBeamColumn)
    E_conc = 2.5e7  # Módulo de elasticidad Hormigón (kPa)
    G_conc = 1.0e7  # Módulo de corte (kPa)

    for eid, edata in elements.items():
        transf = transf_for(eid, edata)

        # Propiedades de Sección
        if edata["type"] == "column":
            A, Iz, Iy, J = 0.25, 0.0052, 0.0052, 0.008  # 50x50 cm
        else:
            A, Iz, Iy, J = 0.18, 0.0054, 0.00135, 0.004  # 30x60 cm

        ops.element('elasticBeamColumn', int(eid), *[int(n) for n in edata["nodes"]], A, E_conc, G_conc, J, Iy, Iz, transf)

    # 3. PATRÓN DE CARGA GRAVITACIONAL Y SÍSMICA
    ops.timeSeries('Linear', 1)
    ops.pattern('Plain', 1, 1)

    # Aplicar carga uniformemente distribuida sobre vigas
    for eid, edata in elements.items():
        if edata["type"] == "beam":
            w_combo = edata["w_G"] + 0.5 * edata["w_Q"]  # Carga Combinada (kN/m)
            ops.eleLoad('-ele', int(eid), '-type', '-beamUniform', -w_combo, 0.0)

    # Carga Sísmica Pseudoestática Basal (20% g aplicado al Centro de Masas)
    ops.load(9991, 75.0, 0.0, 0.0, 0.0, 0.0, 0.0)  # EX Piso 1
    ops.load(9992, 150.0, 0.0, 0.0, 0.0, 0.0, 0.0)  # EX Piso 2

    # 4. RESOLVER SISTEMA MATRICIAL
    ops.system('BandGeneral')
    ops.numberer('RCM')
    ops.constraints('Transformation')
    ops.integrator('LoadControl', 1.0)
    ops.algorithm('Linear')
    ops.analysis('Static')
    ops.analyze(1)

    # 5. EXTRAER FUERZAS INTERNAS Y DEFORMACIONES
    results = {}
    for eid in elements.keys():
        f = ops.basicForce(int(eid))
        results[eid] = {
            "N": [round(f[0], 2), round(-f[0], 2)],
            "Vy": [round(f[1], 2), round(-f[1], 2)],
            "Mz": [round(f[2], 2), round(-f[2], 2)]
        }

    # 6. ENVOLVENTE DE CAPACIDAD HORMIGÓN ARMADO (CURVA P-M)
    pm_capacity = {
        "P": [-2500.0, -1800.0, -1000.0, 0.0, 200.0],
        "M": [0.0, 180.0, 250.0, 120.0, 0.0]
    }

    # 7. EXPORTAR DATOS A STREAMINGASSETS DE UNITY
    final_output = {
        "nodes": nodes,
        "elements": elements,
        "results": results,
        "pm_capacity": pm_capacity
    }

    unity_json_path = "visualization/Assets/StreamingAssets/structural_data.json"
    os.makedirs(os.path.dirname(unity_json_path), exist_ok=True)

    with open(unity_json_path, "w") as f:
        json.dump(final_output, f, indent=2)

    print(f"[OK] Análisis OpenSeesPy completado.")
    print(f"[OK] JSON exportado a Unity en: {unity_json_path}")
    print("\n-------------------------------------------")
    print(">>> RESULTADO: PASO 3 COMPLETADO CON ÉXITO <<<")
    print("-------------------------------------------")


if __name__ == "__main__":
    run_staged_analysis()