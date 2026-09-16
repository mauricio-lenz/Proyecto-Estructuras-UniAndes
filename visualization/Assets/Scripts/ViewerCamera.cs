using UnityEngine;

/// <summary>
/// Cámara orbital estilo viewer P1L2: encuadra TODOS los elementos juntos
/// (bounds combinado de ambos edificios) y permite navegar.
///   Clic derecho: rotar   Rueda: zoom   Clic medio: pan   F: reencuadrar.
/// No traslada geometría: usa las coordenadas del contrato (misma convención
/// OsToUnity que StructuralLoader).
/// </summary>
public class ViewerCamera : MonoBehaviour {
    public float rotateSpeed = 0.4f;
    public float minDistance = 8f;
    public float maxDistance = 500f;
    public bool frameOnStart = true;

    private StructuralLoader loader;
    private Vector3 target;
    private float pitch = 35f;
    private float yaw = 45f;
    private float distance = 80f;

    System.Collections.IEnumerator Start() {
        loader = Object.FindFirstObjectByType<StructuralLoader>();
        // Deja que StructuralLoader.Start() construya la geometría.
        yield return null;
        if (loader == null) loader = Object.FindFirstObjectByType<StructuralLoader>();
        transform.position = new Vector3(30f, 45f, 95f);
        transform.LookAt(new Vector3(30f, 4f, 45f));
        if (frameOnStart) LookAtAllElements();
    }

    void Update() {
        if (Input.GetMouseButton(1)) {
            yaw += Input.GetAxis("Mouse X") * rotateSpeed * 4f;
            pitch -= Input.GetAxis("Mouse Y") * rotateSpeed * 4f;
            pitch = Mathf.Clamp(pitch, 5f, 89f);
        }
        if (Input.GetMouseButton(2)) {
            target += transform.right * (-Input.GetAxis("Mouse X") * rotateSpeed);
            target += transform.up * (-Input.GetAxis("Mouse Y") * rotateSpeed);
        }
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 1e-4f) {
            distance *= 1f - scroll * 1.2f;
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
        }

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        transform.position = target + rot * (Vector3.back * distance);
        transform.LookAt(target);

        if (Input.GetKeyDown(KeyCode.F)) LookAtAllElements();
    }

    public void LookAt(Vector3 center, float radius) {
        target = center;
        distance = Mathf.Clamp(radius * 1.6f, minDistance, maxDistance);
        pitch = 35f;
        yaw = 45f;
    }

    /// <summary>Encuadra todos los elementos creados (barras + losas + voladizos).</summary>
    public void LookAtAllElements() {
        if (loader == null) loader = Object.FindFirstObjectByType<StructuralLoader>();
        if (loader == null) return;

        bool has = false;
        Bounds b = new Bounds();
        if (loader.elementMonos != null) {
            foreach (var m in loader.elementMonos) {
                var r = m.GetComponent<Renderer>();
                if (r == null) continue;
                if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
            }
        }
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)) {
            if (r.gameObject.GetComponentInParent<ElementMono>() != null) continue;
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        if (!has) return;
        LookAt(b.center, b.extents.magnitude);
    }
}
