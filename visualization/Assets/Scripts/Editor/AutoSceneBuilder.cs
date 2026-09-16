#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using System.IO;

public class AutoSceneBuilder {
    [MenuItem("Estructuras/Generar Escena Automatica")]
    public static void BuildScene() {
        string scenesDir = "Assets/Scenes";
        if (!Directory.Exists(scenesDir)) Directory.CreateDirectory(scenesDir);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 1. Cámara y Luz
        GameObject camObj = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        camObj.tag = "MainCamera";
        camObj.transform.position = new Vector3(12, 10, -10);
        camObj.transform.LookAt(new Vector3(3, 3.5f, 3));

        GameObject lightObj = new GameObject("Directional Light", typeof(Light));
        Light light = lightObj.GetComponent<Light>();
        light.type = LightType.Directional;
        lightObj.transform.rotation = Quaternion.Euler(50, -30, 0);

        // 2. UI Canvas
        GameObject canvasObj = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObj.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        // Panel de información
        GameObject infoObj = new GameObject("TxtInfo", typeof(Text));
        infoObj.transform.SetParent(canvasObj.transform, false);
        Text txtInfo = infoObj.GetComponent<Text>();
        txtInfo.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        txtInfo.fontSize = 18;
        txtInfo.color = Color.black;
        RectTransform rtInfo = txtInfo.GetComponent<RectTransform>();
        rtInfo.anchorMin = new Vector2(0, 1);
        rtInfo.anchorMax = new Vector2(0, 1);
        rtInfo.pivot = new Vector2(0, 1);
        rtInfo.anchoredPosition = new Vector2(20, -20);
        rtInfo.sizeDelta = new Vector2(300, 100);
        txtInfo.text = "Haz clic en una columna o viga...";

        // Gráfico P-M RawImage
        GameObject imgObj = new GameObject("PMCanvas", typeof(RawImage));
        imgObj.transform.SetParent(canvasObj.transform, false);
        RawImage rawImg = imgObj.GetComponent<RawImage>();
        RectTransform rtImg = rawImg.GetComponent<RectTransform>();
        rtImg.anchorMin = new Vector2(1, 0);
        rtImg.anchorMax = new Vector2(1, 0);
        rtImg.pivot = new Vector2(1, 0);
        rtImg.anchoredPosition = new Vector2(-20, 20);
        rtImg.sizeDelta = new Vector2(180, 180);

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