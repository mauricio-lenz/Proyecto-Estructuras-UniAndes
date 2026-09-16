#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using System.IO;

public class AutoSceneBuilder {
    static Font BuiltinFont() {
        Font f = null;
        try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch (System.Exception) { }
        if (f == null) {
            try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch (System.Exception) { }
        }
        return f;
    }

    [MenuItem("Estructuras/Generar Escena Automatica")]
    public static void BuildScene() {
        string scenesDir = "Assets/Scenes";
        if (!Directory.Exists(scenesDir)) Directory.CreateDirectory(scenesDir);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 1. Cámara y Luz (apuntando al centro del edificio ED2)
        //    Edificio: x [11.1, 42.35], y [10.93, 27.08], z [-7.97, 11.83]
        Vector3 edCenter = new Vector3( (11.1f+42.35f)/2f, ((27.08f+10.93f)/2f), ((11.83f-7.97f)/2f) );
        Vector3 lookAt = new Vector3(edCenter.x, edCenter.z, edCenter.y);  // Unity (x, z, y)
        GameObject camObj = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        camObj.tag = "MainCamera";
        camObj.transform.position = new Vector3(80, 55, -55);
        camObj.transform.LookAt(lookAt + new Vector3(0, 5, 0));

        GameObject lightObj = new GameObject("Directional Light", typeof(Light));
        Light light = lightObj.GetComponent<Light>();
        light.type = LightType.Directional;
        lightObj.transform.rotation = Quaternion.Euler(35, -25, 0);

        // 2. UI Canvas
        GameObject canvasObj = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObj.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        // Panel de información grande (recuadro seleccionado)
        GameObject infoObj = new GameObject("TxtInfo", typeof(RectTransform), typeof(Text));
        infoObj.transform.SetParent(canvasObj.transform, false);
        Text txtInfo = infoObj.GetComponent<Text>();
        txtInfo.font = BuiltinFont();
        txtInfo.fontSize = 14;
        txtInfo.color = Color.black;
        txtInfo.alignment = TextAnchor.UpperLeft;
        txtInfo.horizontalOverflow = HorizontalWrapMode.Wrap;
        txtInfo.verticalOverflow = VerticalWrapMode.Overflow;
        RectTransform rtInfo = infoObj.GetComponent<RectTransform>();
        rtInfo.anchorMin = new Vector2(0, 0.5f);
        rtInfo.anchorMax = new Vector2(0, 1);
        rtInfo.pivot = new Vector2(0, 1);
        rtInfo.anchoredPosition = new Vector2(20, -20);
        rtInfo.sizeDelta = new Vector2(380, 460);
        txtInfo.text = "Haz clic en un elemento de la estructura...";

        // Gráfico P-M RawImage
        GameObject imgObj = new GameObject("PMCanvas", typeof(RectTransform), typeof(RawImage));
        imgObj.transform.SetParent(canvasObj.transform, false);
        RawImage rawImg = imgObj.GetComponent<RawImage>();
        RectTransform rtImg = imgObj.GetComponent<RectTransform>();
        rtImg.anchorMin = new Vector2(1, 0);
        rtImg.anchorMax = new Vector2(1, 0);
        rtImg.pivot = new Vector2(1, 0);
        rtImg.anchoredPosition = new Vector2(-20, 120);
        rtImg.sizeDelta = new Vector2(180, 180);

        // Leyenda de tipos
        GameObject legObj = new GameObject("TxtLeyenda", typeof(RectTransform), typeof(Text));
        legObj.transform.SetParent(canvasObj.transform, false);
        Text txtLeg = legObj.GetComponent<Text>();
        txtLeg.font = BuiltinFont();
        txtLeg.fontSize = 13;
        txtLeg.color = Color.black;
        RectTransform rtLeg = txtLeg.GetComponent<RectTransform>();
        rtLeg.anchorMin = new Vector2(1, 1);
        rtLeg.anchorMax = new Vector2(1, 1);
        rtLeg.pivot = new Vector2(1, 1);
        rtLeg.anchoredPosition = new Vector2(-20, -20);
        rtLeg.sizeDelta = new Vector2(220, 80);
        txtLeg.text = "AZUL: columna\nNARANJA: viga\nVERDE: muro\nClaro: fase 1  Oscuro: fase 2";

        // 3. Manager Estructural
        GameObject manager = new GameObject("StructuralManager", typeof(StructuralLoader), typeof(UIInspector));
        UIInspector inspector = manager.GetComponent<UIInspector>();
        inspector.txtElementInfo = txtInfo;
        inspector.pmPlotCanvas = rawImg;

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/MainScene.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scene.path, true) };
        Debug.Log("[OK] Escena 'MainScene.unity' generada y lista para presionar PLAY.");
    }
}
#endif