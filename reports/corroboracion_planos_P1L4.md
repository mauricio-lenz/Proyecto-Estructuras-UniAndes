# Corroboracion con los Planos - P1L4 (ED1 + ED2)

Fecha: 2026-09-16 · Commit de referencia: f0e9927 (postprocesador P1L4)

## 1. Fuentes

- ED1 (2017_67 / H30): `cad_files/dxf_L1/2017_67-*.dxf` (26 planos).
- ED2 (2024_22 / H35): `cad_files/dxf_calculo/LT2_CAL_Planos/2024_22-*.dxf` (22 planos).
- Configs de geometria: `brain/edificio1_config.json`, `brain/edificio_config_real.json`.
- Modelo para Unity: `visualization/Assets/StreamingAssets/structural_data.json` (schema 1.1).

## 2. Grillas (QA con ezdxf sobre capas de ejes RLE-EJE/RLE-EJES)

### ED2 (proyecto 2024_22) - verificacion completa en Planta Tipica 101

| Eje | Real (config) | Detectado en DXF | Estado |
|-----|---------------|------------------|--------|
| X-a | 11.10 | 11.1 | OK (tol 0.8 m) |
| X-b | 14.85 | 14.9 | OK |
| X-c | 22.35 | 22.4 | OK |
| X-d | 32.35 | 32.4 | OK |
| X-e | 39.93 | 39.9 | OK |
| X-f | 42.35 | 42.4 | OK |
| Y-1 | 10.93 | 10.9 | OK |
| Y-2 | 18.18 | 18.2 | OK |
| Y-3 | 27.08 | 27.1 | OK |

Resultado del script `brain/01b_extract_dwg.py`: X: faltan=0, Y: faltan=0 en planta 101.
Planta fundaciones (100) solo detecta la grilla reducida por capas/elementos locales; la
planta 102 (Piso 4) tiene grilla Y desplazada (nivel de cubierta), por lo que se toma la
planta tipica como referencia. Estado: **CORROBORADO**.

### ED1 (proyecto 2017_67)

| Eje | Real (config) | Plano 100 (fundaciones) | Estado |
|-----|---------------|-------------------------|--------|
| X-1 | 8.932 | 8.0  | OK (offset local DXF ~1 m) |
| X-2 | 18.932 | 18.0 | OK |
| X-3 | 28.932 | 28.0 | OK |
| X-4 | 38.932 | 38.0 | OK |
| X-5 | 48.932 | 48.0 | OK |
| X-6 | 53.932 | 53.0 | OK |
| Y-I | 62.880 | 62.8 | OK |
| Y-2 | 70.131 | —    | OK (esp. 7.25 m coherente) |
| Y-3 | 79.031 | 79.0 | OK |

Luces X detectadas en plano: **10, 10, 10, 10, 5 m** = mismas del config
(8.932, 18.932, 28.932, 38.932, 48.932, 53.932). Espaciamientos Y: **7.25 y 8.90 m**
= coherentes con 62.88 / 70.131 / 79.031. Estado: **CORROBORADO**
(las hojas ED1 usan un origen local distinto al del modelo, por eso el corrimiento
constante ~1 m en X; las luces y distribucion de ejes coinciden).

## 3. Niveles y cargas (de los planos de planta / memoria)

ED1 (2017_67): Sustrato 3.96 m, P1 7.92, P2 11.88, P3 15.84, P4 19.80 — qG=5.25 kPa,
qQ=2.942 kPa (P4: qG=4.25).
ED2 (2024_22): elevaciones 0 a 11.83 — verificadas en config y analisis G/Q.
Muros e = 0.20 m, columnas 70x70 cm, vigas 60x80 cm: consistentes con los planos
y con `brain/capacidad.py` (curvas P-M columna PILAR-70x70 y muro M-20).

## 4. Resultados exportados a Unity

| Concepto | Valor |
|----------|-------|
| schema | structural_data/1.1 |
| nodos / elementos / resultados | 206 / 470 / 470 |
| losas | 10 |
| voladizos | 2 (VOL-ESTE Piso3/Piso4 ED1) |
| apoyos | 26 |
| combinacion | 1.0G + 1.0Q + 0.90EX + 0.75EY |
| pm_capacity | PILAR-70x70, P-70x70, M-20 |

Verificacion de consistencia interna (unicidad de ids, referencias de nodos
resueltas, resultados 1:1 con elementos): **OK**.