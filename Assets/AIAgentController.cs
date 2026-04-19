using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class AIAgentController : MonoBehaviour
{
    public enum UiLanguage
    {
        English,
        French
    }

    [Header("Display")]
    public TextMeshProUGUI aiTextDisplay;
    public TextMeshProUGUI detectedObjectTitleText;
    public Image hudPanelImage;
    public TextMeshProUGUI headerBadgeText;
    public Button exportReportButton;
    public bool autoCreateHudIfMissing = true;
    public bool presentationMinimalMode = true;
    public Color idleColor = Color.white;
    public Color analysisColor = new Color(1f, 0.86f, 0.25f);
    public Color warningColor = new Color(1f, 0.34f, 0.25f);
    public Color okColor = new Color(0.38f, 0.95f, 0.53f);

    [Header("Use Case")]
    public string useCaseTitle = "AR AI Maintenance Assistant";
    public string trackedAssetName = "Hydraulic Pump P-204";
    [Range(0.5f, 5f)]
    public float retriggerCooldown = 1.5f;

    [Header("Interaction")]
    public bool enableTapToCycle = false;

    [Header("Localization")]
    public UiLanguage uiLanguage = UiLanguage.French;

    [Header("Screenshot Export")]
    public bool includeScreenshotInExport = true;
    [Range(1, 2)]
    public int screenshotSuperSize = 1;
    [Range(0f, 0.5f)]
    public float screenshotDelaySeconds = 0.15f;

    [Header("Object Identification")]
    public bool enableObjectIdentification = true;
    public bool includeDetectedObjectInSummary = true;
    public bool useCameraFrameForObjectDetection = true;

    [Header("AR Virtual Object")]
    public bool showVirtualMarker = true;
    public Vector3 virtualMarkerLocalPosition = new Vector3(0f, 0.03f, 0f);
    public Vector3 virtualMarkerLocalScale = new Vector3(0.03f, 0.03f, 0.03f);
    public Color virtualMarkerColor = new Color(0.2f, 0.75f, 1f, 0.95f);

    [Header("Gemini AI (Optional)")]
    public bool useGeminiApi = true;
    [TextArea(2, 6)]
    public string geminiApiKey = "AIzaSyAPkkvXWSgvzJX6V0d0fhhb2PUYThbELjM";
    public bool preferEnvironmentApiKey = true;
    public bool requireGoogleApiKeyFormat = true;
    public string environmentVariableName = "GEMINI_API_KEY";
    public string geminiApiVersion = "v1";
    public string geminiModel = "gemini-2.5-flash";
    public bool enableGeminiModelFallback = true;
    public bool enableGeminiApiVersionFallback = true;
    public bool disableGeminiOn404 = true;
    [Range(0f, 1f)]
    public float geminiTemperature = 0.2f;
    [Range(64, 512)]
    public int geminiMaxOutputTokens = 220;
    [Range(5, 40)]
    public int geminiTimeoutSeconds = 18;

    [Header("3D KPI Cards")]
    public bool create3DKpiCards = true;
    [Range(0.05f, 0.2f)]
    public float kpiRadius = 0.11f;
    [Range(0.0001f, 0.001f)]
    public float kpiCardScale = 0.0002f;

    [Header("KPI Stabilization")]
    public bool detachKpiFromTarget = true;
    [Range(4f, 20f)]
    public float kpiFollowSmooth = 6f;
    [Range(4f, 20f)]
    public float kpiBillboardSmooth = 8f;
    public bool kpiYawOnlyBillboard = false;
    public bool kpiFlipFacing = true;

    [Header("KPI Appearance Animation")]
    public bool animateKpiAppearance = true;
    [Range(0.05f, 0.6f)]
    public float kpiAppearDuration = 0.24f;
    [Range(0f, 0.2f)]
    public float kpiAppearStagger = 0.06f;

    private Coroutine simulationRoutine;
    private Coroutine kpiAppearRoutine;
    private bool targetVisible;
    private bool exportInProgress;
    private bool geminiDisabledBy404;
    private bool hasDetectedObject;
    private float lastTriggerTime = -10f;
    private int reportIndex;
    private int currentPanelIndex;
    private UiLanguage lastAppliedLanguage;
    private TextMeshProUGUI exportReportLabel;
    private DetectedObjectInfo lastDetectedObject;
    private GameObject virtualMarker;

    private readonly List<DiagnosticReport> reportHistory = new List<DiagnosticReport>(6);
    private readonly List<KpiCard> kpiCards = new List<KpiCard>(3);
    private static readonly string[] GeminiFallbackModels =
    {
        "gemini-2.5-flash",
        "gemini-2.5-flash-lite",
        "gemini-2.0-flash-lite"
    };
    private static readonly string[] GeminiApiVersionFallbacks = { "v1beta", "v1" };

    private readonly DiagnosticReport[] fallbackReports =
    {
        new DiagnosticReport(
            "Risk: HIGH",
            "Confidence: 92%",
            "Overheat detected near bearing zone.",
            "Abnormal vibration pattern on axis Y.",
            "Action: Stop machine for 5 minutes, inspect bearing and cooling duct."),
        new DiagnosticReport(
            "Risk: MEDIUM",
            "Confidence: 84%",
            "Early wear detected on rotor alignment.",
            "Temperature trend rising +6C in 2 minutes.",
            "Action: Reduce load to 70% and schedule calibration."),
        new DiagnosticReport(
            "Risk: LOW",
            "Confidence: 88%",
            "No critical anomaly detected.",
            "Minor fluctuation remains within tolerance.",
            "Action: Continue operation and run check in 30 minutes.")
    };

    private struct DiagnosticReport
    {
        public readonly string Risk;
        public readonly string Confidence;
        public readonly string FindingA;
        public readonly string FindingB;
        public readonly string Action;

        public DiagnosticReport(string risk, string confidence, string findingA, string findingB, string action)
        {
            Risk = risk;
            Confidence = confidence;
            FindingA = findingA;
            FindingB = findingB;
            Action = action;
        }
    }

    private struct DetectedObjectInfo
    {
        public readonly string ObjectName;
        public readonly string Confidence;
        public readonly string Description;

        public bool IsValid => !string.IsNullOrWhiteSpace(ObjectName);

        public DetectedObjectInfo(string objectName, string confidence, string description)
        {
            ObjectName = objectName;
            Confidence = confidence;
            Description = description;
        }
    }

    private sealed class KpiCard
    {
        public Transform Root;
        public Image Background;
        public TextMeshProUGUI ValueText;
        public Vector3 LocalOffset;
        public string Title;
        public string Unit;
        public float CurrentValue;
        public float TargetValue;
        public float Min;
        public float Max;
    }

    [Serializable]
    private class GeminiGenerateRequest
    {
        public GeminiContent[] contents;
        public GeminiGenerationConfig generationConfig;
    }

    [Serializable]
    private class GeminiContent
    {
        public string role;
        public GeminiPart[] parts;
    }

    [Serializable]
    private class GeminiPart
    {
        public string text;
    }

    [Serializable]
    private class GeminiGenerationConfig
    {
        public float temperature;
        public int maxOutputTokens;
    }

    [Serializable]
    private class GeminiGenerateResponse
    {
        public GeminiCandidate[] candidates;
    }

    [Serializable]
    private class GeminiCandidate
    {
        public GeminiContentResponse content;
    }

    [Serializable]
    private class GeminiContentResponse
    {
        public GeminiPart[] parts;
    }

    [Serializable]
    private class GeminiDiagnosticPayload
    {
        public string riskLevel;
        public string confidence;
        public string findingA;
        public string findingB;
        public string action;
    }

    [Serializable]
    private class GeminiObjectPayload
    {
        public string objectName;
        public string confidence;
        public string shortDescription;
    }

    private void Awake()
    {
        NormalizeRuntimeKpiSettings();
        CleanupLegacyKpiChildren();
        EnsureDisplayReference();
        EnsureThemedHud();
        EnsureDetectedObjectTitle();
        EnsureExportButton();
        EnsureVirtualMarker();
        EnsureKpiCards();
        ApplyReadableLayout();
        UpdateLocalizedStaticUi();
        ShowIdleState();
        SetKpiVisible(false);
        lastAppliedLanguage = uiLanguage;
    }

    private void NormalizeRuntimeKpiSettings()
    {
        // Keep world-space cards legible even if old scene values were serialized too large.
        detachKpiFromTarget = true;
        kpiYawOnlyBillboard = false;
        kpiFlipFacing = true;
        kpiRadius = Mathf.Clamp(kpiRadius, 0.08f, 0.14f);
        kpiCardScale = Mathf.Clamp(kpiCardScale, 0.00012f, 0.00023f);
        kpiFollowSmooth = Mathf.Clamp(kpiFollowSmooth, 5f, 10f);
        kpiBillboardSmooth = Mathf.Clamp(kpiBillboardSmooth, 6f, 12f);

        if (string.IsNullOrWhiteSpace(geminiApiVersion))
        {
            geminiApiVersion = "v1";
        }
    }

    private void CleanupLegacyKpiChildren()
    {
        for (var i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child != null && child.name.StartsWith("KPI_", StringComparison.OrdinalIgnoreCase))
            {
                Destroy(child.gameObject);
            }
        }
    }

    private List<string> BuildGeminiModelCandidates()
    {
        var models = new List<string>(4);

        void AddModel(string model)
        {
            if (string.IsNullOrWhiteSpace(model))
            {
                return;
            }

            var trimmed = model.Trim();
            if (!models.Contains(trimmed))
            {
                models.Add(trimmed);
            }
        }

        AddModel(geminiModel);
        if (enableGeminiModelFallback)
        {
            for (var i = 0; i < GeminiFallbackModels.Length; i++)
            {
                AddModel(GeminiFallbackModels[i]);
            }
        }

        return models;
    }

    private List<string> BuildGeminiApiVersionCandidates()
    {
        var versions = new List<string>(2);

        void AddVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return;
            }

            var trimmed = version.Trim();
            if (!versions.Contains(trimmed))
            {
                versions.Add(trimmed);
            }
        }

        AddVersion(geminiApiVersion);
        if (enableGeminiApiVersionFallback)
        {
            for (var i = 0; i < GeminiApiVersionFallbacks.Length; i++)
            {
                AddVersion(GeminiApiVersionFallbacks[i]);
            }
        }

        return versions;
    }

    private void Update()
    {
        if (lastAppliedLanguage != uiLanguage)
        {
            lastAppliedLanguage = uiLanguage;
            UpdateLocalizedStaticUi();

            if (!targetVisible)
            {
                ShowIdleState();
            }
            else if (simulationRoutine == null && reportHistory.Count > 0)
            {
                DisplayFinalPanel(reportHistory[0]);
            }
        }

        UpdateKpiCardsVisual();

        if (!enableTapToCycle || !targetVisible || simulationRoutine != null || aiTextDisplay == null || reportHistory.Count == 0)
        {
            return;
        }

        if (!HasTapOrClick())
        {
            return;
        }

        currentPanelIndex = (currentPanelIndex + 1) % 3;
        DisplayFinalPanel(reportHistory[0]);
    }

    public void TriggerAIAnalysis()
    {
        if (aiTextDisplay == null)
        {
            Debug.LogWarning("AIAgentController: aiTextDisplay is not assigned.");
            return;
        }

        if (Time.time - lastTriggerTime < retriggerCooldown)
        {
            return;
        }

        targetVisible = true;
        lastTriggerTime = Time.time;
        currentPanelIndex = 0;
        SetVirtualMarkerVisible(true);
        SetKpiVisible(true);
        SetStatusBadge(L("SCANNING", "ANALYSE"));

        if (simulationRoutine != null)
        {
            StopCoroutine(simulationRoutine);
        }

        simulationRoutine = StartCoroutine(SimulateAIAgent());
    }

    public void ResetState()
    {
        targetVisible = false;
        hasDetectedObject = false;
        lastDetectedObject = default;

        if (simulationRoutine != null)
        {
            StopCoroutine(simulationRoutine);
            simulationRoutine = null;
        }

        SetStatusBadge(L("READY", "PRET"));
        UpdateDetectedObjectTitle();
        SetVirtualMarkerVisible(false);
        SetKpiVisible(false);
        ShowIdleState();
    }

    private void EnsureVirtualMarker()
    {
        if (!showVirtualMarker)
        {
            SetVirtualMarkerVisible(false);
            return;
        }

        if (virtualMarker == null)
        {
            virtualMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            virtualMarker.name = "AR_VirtualMarker";
            virtualMarker.transform.SetParent(transform, false);

            var collider = virtualMarker.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            var renderer = virtualMarker.GetComponent<Renderer>();
            if (renderer != null)
            {
                var material = new Material(Shader.Find("Standard"));
                material.color = virtualMarkerColor;
                renderer.material = material;
            }
        }

        virtualMarker.transform.localPosition = virtualMarkerLocalPosition;
        virtualMarker.transform.localScale = virtualMarkerLocalScale;
        SetVirtualMarkerVisible(false);
    }

    private void SetVirtualMarkerVisible(bool visible)
    {
        if (virtualMarker == null)
        {
            return;
        }

        virtualMarker.SetActive(showVirtualMarker && visible);
    }

    private IEnumerator SimulateAIAgent()
    {
        if (aiTextDisplay == null)
        {
            simulationRoutine = null;
            yield break;
        }

        aiTextDisplay.color = analysisColor;
        aiTextDisplay.text = BuildStageText("1/3", L("Collecting live signals", "Collecte des signaux"), 0.33f);
        yield return new WaitForSeconds(0.8f);

        if (enableObjectIdentification)
        {
            var detectedInfo = default(DetectedObjectInfo);
            var detectionCompleted = false;

            StartCoroutine(FetchDetectedObjectInfo(value =>
            {
                detectedInfo = value;
                detectionCompleted = true;
            }));

            var detectElapsed = 0f;
            while (!detectionCompleted)
            {
                detectElapsed += Time.deltaTime;
                var dots = Mathf.FloorToInt(detectElapsed * 3f) % 4;
                aiTextDisplay.text = BuildStageText("2/3", L("Detecting object", "Detection de l'objet") + new string('.', dots), 0.66f);
                yield return null;
            }

            ApplyDetectedObjectInfo(detectedInfo);

            if (presentationMinimalMode)
            {
                var objectReport = BuildObjectFocusedReport();
                AddToHistory(objectReport);
                DisplayFinalPanel(objectReport);
                SetStatusBadge(hasDetectedObject ? L("OBJECT DETECTED", "OBJET DETECTE") : L("NO OBJECT", "AUCUN OBJET"));
                simulationRoutine = null;
                yield break;
            }
        }
        else
        {
            aiTextDisplay.text = BuildStageText("2/3", L("Running anomaly model", "Execution du modele d'anomalie"), 0.66f);
            yield return new WaitForSeconds(0.95f);
        }

        var completed = false;
        var report = GetFallbackReport();

        StartCoroutine(FetchDiagnosticReport(value =>
        {
            report = value;
            completed = true;
        }));

        var elapsed = 0f;
        while (!completed)
        {
            elapsed += Time.deltaTime;
            var dots = Mathf.FloorToInt(elapsed * 3f) % 4;
            aiTextDisplay.text = BuildStageText("3/3", L("Consulting Gemini", "Consultation Gemini") + new string('.', dots), 1f);
            yield return null;
        }

        AddToHistory(report);
        ApplyReportToKpis(report);
        DisplayFinalPanel(report);
        SetStatusBadge(report.Risk.Contains("HIGH") ? L("ALERT", "ALERTE") : L("STABLE", "STABLE"));

        simulationRoutine = null;
    }

    private DiagnosticReport BuildObjectFocusedReport()
    {
        if (!hasDetectedObject)
        {
            return new DiagnosticReport(
                L("Object: Unknown", "Objet : Inconnu"),
                L("Confidence: N/A", "Confiance : N/A"),
                L("No reliable object detected yet.", "Aucun objet fiable detecte pour l'instant."),
                L("Keep object centered and stable.", "Gardez l'objet centre et stable."),
                "Action: " + L("Press Analyze Again.", "Appuyez sur Analyser a nouveau."));
        }

        var confidence = SafeOrDefault(lastDetectedObject.Confidence, "N/A");
        var description = SafeOrDefault(lastDetectedObject.Description, L("Detected from camera frame.", "Detecte depuis l'image camera."));

        return new DiagnosticReport(
            L("Object", "Objet") + ": " + lastDetectedObject.ObjectName,
            L("Confidence", "Confiance") + ": " + confidence,
            description,
            L("Ready for next scan.", "Pret pour le prochain scan."),
            "Action: " + L("Show another object and press Analyze Again.", "Montrez un autre objet puis appuyez sur Analyser a nouveau."));
    }

    private IEnumerator FetchDiagnosticReport(Action<DiagnosticReport> onDone)
    {
        var fallback = GetFallbackReport();

        if (!useGeminiApi || geminiDisabledBy404)
        {
            onDone?.Invoke(fallback);
            yield break;
        }

        var key = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            Debug.LogWarning("AIAgentController: Gemini API key missing. Falling back to local simulation.");
            onDone?.Invoke(fallback);
            yield break;
        }

        if (requireGoogleApiKeyFormat && !LooksLikeGoogleGeminiKey(key))
        {
            geminiDisabledBy404 = true;
            useGeminiApi = false;
            SetStatusBadge(L("LOCAL AI MODE", "MODE IA LOCAL"));
            Debug.LogWarning("AIAgentController: Gemini key format seems invalid for Google Gemini API (expected prefix 'AIza'). Using local AI mode.");
            onDone?.Invoke(fallback);
            yield break;
        }

        var prompt = BuildGeminiPrompt();
        var body = BuildGeminiRequestJson(prompt);
        var apiVersionsToTry = BuildGeminiApiVersionCandidates();
        var modelsToTry = BuildGeminiModelCandidates();
        var lastResponseCode = 0L;
        string lastError = null;

        for (var v = 0; v < apiVersionsToTry.Count; v++)
        {
            var version = apiVersionsToTry[v];

            for (var i = 0; i < modelsToTry.Count; i++)
            {
                var model = modelsToTry[i];
                var endpoint = "https://generativelanguage.googleapis.com/" + version + "/models/" + model + ":generateContent?key=" + key;

                using (var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST))
                {
                    var bodyRaw = Encoding.UTF8.GetBytes(body);
                    request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.timeout = geminiTimeoutSeconds;
                    request.SetRequestHeader("Content-Type", "application/json");

                    yield return request.SendWebRequest();
                    lastResponseCode = request.responseCode;
                    lastError = request.error;

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        // Try the next combination across model/version before giving up.
                        continue;
                    }

                    if (!TryParseGeminiResponse(request.downloadHandler.text, out var report))
                    {
                        continue;
                    }

                    if (!string.Equals(model, geminiModel, StringComparison.OrdinalIgnoreCase))
                    {
                        geminiModel = model;
                        Debug.Log("AIAgentController: Switched active Gemini model to '" + model + "'.");
                    }

                    if (!string.Equals(version, geminiApiVersion, StringComparison.OrdinalIgnoreCase))
                    {
                        geminiApiVersion = version;
                        Debug.Log("AIAgentController: Switched Gemini API version to '" + version + "'.");
                    }

                    onDone?.Invoke(report);
                    yield break;
                }
            }
        }

        if (lastResponseCode == 404 && disableGeminiOn404)
        {
            geminiDisabledBy404 = true;
            useGeminiApi = false;
            SetStatusBadge(L("LOCAL AI MODE", "MODE IA LOCAL"));
        }

        Debug.LogWarning("AIAgentController: Gemini request failed after trying models/API versions. Last HTTP " + lastResponseCode + " " + lastError + ". Falling back.");
        onDone?.Invoke(fallback);
    }

    private IEnumerator FetchDetectedObjectInfo(Action<DetectedObjectInfo> onDone)
    {
        var fallback = new DetectedObjectInfo(
            L("Unknown object", "Objet inconnu"),
            "N/A",
            L("Vision unavailable. Keep target centered and retry.", "Vision indisponible. Gardez la cible au centre puis reessayez."));

        if (!enableObjectIdentification || !useCameraFrameForObjectDetection)
        {
            onDone?.Invoke(fallback);
            yield break;
        }

        if (!useGeminiApi || geminiDisabledBy404)
        {
            onDone?.Invoke(fallback);
            yield break;
        }

        var key = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            onDone?.Invoke(fallback);
            yield break;
        }

        if (requireGoogleApiKeyFormat && !LooksLikeGoogleGeminiKey(key))
        {
            geminiDisabledBy404 = true;
            useGeminiApi = false;
            SetStatusBadge(L("LOCAL AI MODE", "MODE IA LOCAL"));
            onDone?.Invoke(fallback);
            yield break;
        }

        yield return new WaitForEndOfFrame();
        var frame = ScreenCapture.CaptureScreenshotAsTexture();
        if (frame == null)
        {
            onDone?.Invoke(fallback);
            yield break;
        }

        var jpg = frame.EncodeToJPG(65);
        Destroy(frame);

        if (jpg == null || jpg.Length == 0)
        {
            onDone?.Invoke(fallback);
            yield break;
        }

        var prompt = BuildGeminiVisionPrompt();
        var body = BuildGeminiVisionRequestJson(prompt, Convert.ToBase64String(jpg));
        var apiVersionsToTry = BuildGeminiApiVersionCandidates();
        var modelsToTry = BuildGeminiModelCandidates();
        var lastResponseCode = 0L;
        string lastError = null;

        for (var v = 0; v < apiVersionsToTry.Count; v++)
        {
            var version = apiVersionsToTry[v];

            for (var i = 0; i < modelsToTry.Count; i++)
            {
                var model = modelsToTry[i];
                var endpoint = "https://generativelanguage.googleapis.com/" + version + "/models/" + model + ":generateContent?key=" + key;

                using (var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST))
                {
                    var bodyRaw = Encoding.UTF8.GetBytes(body);
                    request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.timeout = geminiTimeoutSeconds;
                    request.SetRequestHeader("Content-Type", "application/json");

                    yield return request.SendWebRequest();
                    lastResponseCode = request.responseCode;
                    lastError = request.error;

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        continue;
                    }

                    if (!TryParseGeminiObjectResponse(request.downloadHandler.text, out var detected))
                    {
                        continue;
                    }

                    if (!string.Equals(model, geminiModel, StringComparison.OrdinalIgnoreCase))
                    {
                        geminiModel = model;
                    }

                    if (!string.Equals(version, geminiApiVersion, StringComparison.OrdinalIgnoreCase))
                    {
                        geminiApiVersion = version;
                    }

                    onDone?.Invoke(detected);
                    yield break;
                }
            }
        }

        if (lastResponseCode == 404 && disableGeminiOn404)
        {
            geminiDisabledBy404 = true;
            useGeminiApi = false;
            SetStatusBadge(L("LOCAL AI MODE", "MODE IA LOCAL"));
        }

        if (!string.IsNullOrEmpty(lastError))
        {
            Debug.LogWarning("AIAgentController: Object detection request failed. Last HTTP " + lastResponseCode + " " + lastError + ".");
        }

        onDone?.Invoke(fallback);
    }

    private string BuildGeminiVisionPrompt()
    {
         return "Identify the PRIMARY PHYSICAL OBJECT visible in this camera frame. " +
             "Examples of valid objectName: phone, t-shirt, bottle, book, person, keyboard. " +
             "If a person is holding a phone, return objectName as phone. " +
             "Do NOT return markdown, do NOT use code blocks, do NOT write ```json. " +
             "Return ONLY compact JSON with keys: objectName, confidence, shortDescription. " +
             "Use confidence like 87%. Keep shortDescription under 10 words.";
    }

    private string BuildGeminiVisionRequestJson(string prompt, string base64Jpeg)
    {
        return "{\"contents\":[{\"role\":\"user\",\"parts\":[{" +
               "\"text\":\"" + EscapeJsonForRequest(prompt) + "\"},{" +
               "\"inline_data\":{\"mime_type\":\"image/jpeg\",\"data\":\"" + base64Jpeg + "\"}}]}]," +
               "\"generationConfig\":{\"temperature\":0.1,\"maxOutputTokens\":120}}";
    }

    private bool TryParseGeminiObjectResponse(string rawResponse, out DetectedObjectInfo detected)
    {
        detected = default;

        if (!TryExtractGeneratedText(rawResponse, out var generatedText))
        {
            return false;
        }

        generatedText = SanitizeModelText(generatedText);

        var jsonPayload = ExtractJsonObject(generatedText);
        if (!string.IsNullOrEmpty(jsonPayload))
        {
            try
            {
                var payload = JsonUtility.FromJson<GeminiObjectPayload>(jsonPayload);
                if (payload != null && !string.IsNullOrWhiteSpace(payload.objectName))
                {
                    var cleanedName = NormalizeDetectedObjectName(payload.objectName);
                    if (string.IsNullOrWhiteSpace(cleanedName))
                    {
                        return false;
                    }

                    detected = new DetectedObjectInfo(
                        cleanedName,
                        NormalizeConfidence(payload.confidence),
                        SafeOrDefault(payload.shortDescription, "Detected object from camera frame."));
                    return true;
                }
            }
            catch
            {
            }
        }

        if (TryBuildDetectedInfoFromLooseFields(generatedText, out var looseDetected))
        {
            detected = looseDetected;
            return true;
        }

        var lines = generatedText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            return false;
        }

        var candidateName = string.Empty;
        var candidateConfidence = "80%";
        var candidateDescription = string.Empty;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            if (IsParserNoiseLine(line))
            {
                continue;
            }

            var lower = line.ToLowerInvariant();
            if (lower.StartsWith("objectname") || lower.StartsWith("object") || lower.StartsWith("name"))
            {
                var colon = line.IndexOf(':');
                if (colon >= 0 && colon < line.Length - 1)
                {
                    candidateName = line.Substring(colon + 1).Trim();
                    continue;
                }
            }

            if (lower.StartsWith("confidence"))
            {
                var colon = line.IndexOf(':');
                if (colon >= 0 && colon < line.Length - 1)
                {
                    candidateConfidence = line.Substring(colon + 1).Trim();
                }

                continue;
            }

            if (string.IsNullOrEmpty(candidateName))
            {
                candidateName = line;
            }
            else if (string.IsNullOrEmpty(candidateDescription))
            {
                candidateDescription = line;
            }
        }

        candidateName = NormalizeDetectedObjectName(candidateName);
        if (string.IsNullOrWhiteSpace(candidateName))
        {
            return false;
        }

        detected = new DetectedObjectInfo(
            candidateName,
            NormalizeConfidence(candidateConfidence),
            string.IsNullOrWhiteSpace(candidateDescription) ? "Detected object from camera frame." : candidateDescription.Trim());
        return true;
    }

    private static bool TryBuildDetectedInfoFromLooseFields(string generatedText, out DetectedObjectInfo detected)
    {
        detected = default;

        if (string.IsNullOrWhiteSpace(generatedText))
        {
            return false;
        }

        var objectNameRaw = string.Empty;
        if (!TryExtractNamedField(generatedText, "objectName", out objectNameRaw) &&
            !TryExtractNamedField(generatedText, "object", out objectNameRaw) &&
            !TryExtractNamedField(generatedText, "name", out objectNameRaw))
        {
            return false;
        }

        var objectName = NormalizeDetectedObjectName(objectNameRaw);
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return false;
        }

        var confidenceRaw = "80%";
        TryExtractNamedField(generatedText, "confidence", out confidenceRaw);

        var descriptionRaw = string.Empty;
        if (!TryExtractNamedField(generatedText, "shortDescription", out descriptionRaw))
        {
            TryExtractNamedField(generatedText, "description", out descriptionRaw);
        }

        detected = new DetectedObjectInfo(
            objectName,
            NormalizeConfidence(confidenceRaw),
            SafeOrDefault(descriptionRaw, "Detected object from camera frame."));
        return true;
    }

    private static bool TryExtractNamedField(string source, string fieldName, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(fieldName))
        {
            return false;
        }

        var escapedField = Regex.Escape(fieldName);

        var quotedPattern = "(?:^|[\\{\\s,])['\"]?" + escapedField + "['\"]?\\s*[:=]\\s*([\"'])(.*?)\\1";
        var quotedMatch = Regex.Match(source, quotedPattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (quotedMatch.Success && quotedMatch.Groups.Count > 2)
        {
            value = quotedMatch.Groups[2].Value.Trim();
            return !string.IsNullOrWhiteSpace(value);
        }

        var rawPattern = "(?:^|[\\{\\s,])['\"]?" + escapedField + "['\"]?\\s*[:=]\\s*([^,}\\]\\r\\n]+)";
        var rawMatch = Regex.Match(source, rawPattern, RegexOptions.IgnoreCase);
        if (rawMatch.Success && rawMatch.Groups.Count > 1)
        {
            value = rawMatch.Groups[1].Value.Trim();
            return !string.IsNullOrWhiteSpace(value);
        }

        return false;
    }

    private static bool IsParserNoiseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return true;
        }

        var trimmed = line.Trim();
        if (trimmed == "{" || trimmed == "}" || trimmed == "[" || trimmed == "]")
        {
            return true;
        }

        var lower = trimmed.ToLowerInvariant();
        return lower == "json" || lower == "```json" || lower == "```";
    }

    private static string SanitizeModelText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var cleaned = text.Trim();
        cleaned = cleaned.Replace("```json", string.Empty).Replace("```", string.Empty).Trim();
        cleaned = cleaned.Replace("\\\"", "\"");
        cleaned = cleaned.Replace("\u201c", "\"").Replace("\u201d", "\"");
        cleaned = cleaned.Replace("\u2018", "'").Replace("\u2019", "'");

        if (cleaned.StartsWith("json", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring(4).Trim();
        }

        return cleaned;
    }

    private static string NormalizeDetectedObjectName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var name = raw.Trim().Trim('"', '\'', '`', ' ', '.', ',', ';', '{', '}', '[', ']');
        var colon = name.IndexOf(':');
        if (colon > 0 && colon < 14 && colon < name.Length - 1)
        {
            name = name.Substring(colon + 1).Trim();
        }

        var paren = name.IndexOf('(');
        if (paren > 0)
        {
            name = name.Substring(0, paren).Trim();
        }

        var comma = name.IndexOf(',');
        if (comma > 0)
        {
            name = name.Substring(0, comma).Trim();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var lower = name.ToLowerInvariant();
        if (lower == "json" || lower == "```json" || lower == "```" || lower.Contains("objectname") || lower == "object" || lower == "name")
        {
            return string.Empty;
        }

        if (lower.Contains("smartphone") || lower.Contains("mobile") || lower.Contains("telephone") || lower == "phone")
        {
            return "phone";
        }

        if (lower.Contains("t-shirt") || lower.Contains("tshirt") || lower == "shirt")
        {
            return "t-shirt";
        }

        return name;
    }

    private static string NormalizeConfidence(string confidence)
    {
        if (string.IsNullOrWhiteSpace(confidence))
        {
            return "80%";
        }

        var clean = confidence.Trim().Trim('"', '\'', '.', ',', ';');
        if (clean.EndsWith("%", StringComparison.Ordinal))
        {
            return clean;
        }

        var hasDigit = false;
        for (var i = 0; i < clean.Length; i++)
        {
            if (char.IsDigit(clean[i]))
            {
                hasDigit = true;
                break;
            }
        }

        return hasDigit ? clean + "%" : "80%";
    }

    private static string EscapeJsonForRequest(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");
    }

    private string ResolveApiKey()
    {
        if (preferEnvironmentApiKey)
        {
            var envKey = Environment.GetEnvironmentVariable(environmentVariableName);
            if (!string.IsNullOrWhiteSpace(envKey))
            {
                var trimmedEnvKey = envKey.Trim();
                if (!requireGoogleApiKeyFormat || LooksLikeGoogleGeminiKey(trimmedEnvKey))
                {
                    return trimmedEnvKey;
                }

                Debug.LogWarning("AIAgentController: Environment API key ignored because it does not match Google key format (expected 'AIza').");
            }
        }

        return string.IsNullOrWhiteSpace(geminiApiKey) ? string.Empty : geminiApiKey.Trim();
    }

    private string BuildGeminiPrompt()
    {
        var temp = ReadKpiTarget("Temperature", 74f);
        var vibration = ReadKpiTarget("Vibration", 2.2f);
        var pressure = ReadKpiTarget("Pressure", 5.5f);
        var activeAssetName = GetActiveAssetName();

        var detectedObjectHint = string.Empty;
        if (hasDetectedObject && !string.IsNullOrWhiteSpace(lastDetectedObject.Description))
        {
            detectedObjectHint = "\nDetected object hint: " + lastDetectedObject.Description;
        }

        return
            "You are an industrial AR maintenance assistant. " +
            "Analyze this asset and return ONLY valid JSON with keys: " +
            "riskLevel, confidence, findingA, findingB, action. " +
            "Use riskLevel as HIGH, MEDIUM or LOW. Keep action concise and executable.\n\n" +
            "Asset: " + activeAssetName + "\n" +
            "Use case: " + useCaseTitle + "\n" +
            "Telemetry:\n" +
            "- Temperature C: " + temp.ToString("F1") + "\n" +
            "- Vibration mm/s: " + vibration.ToString("F2") + "\n" +
            "- Pressure bar: " + pressure.ToString("F2") + "\n" +
            "- Observation: slight oscillation near bearing housing." +
            detectedObjectHint;
    }

    private string BuildGeminiRequestJson(string prompt)
    {
        var request = new GeminiGenerateRequest
        {
            contents = new[]
            {
                new GeminiContent
                {
                    role = "user",
                    parts = new[]
                    {
                        new GeminiPart { text = prompt }
                    }
                }
            },
            generationConfig = new GeminiGenerationConfig
            {
                temperature = geminiTemperature,
                maxOutputTokens = geminiMaxOutputTokens
            }
        };

        return JsonUtility.ToJson(request);
    }

    private bool TryParseGeminiResponse(string rawResponse, out DiagnosticReport report)
    {
        report = GetFallbackReport();
        if (!TryExtractGeneratedText(rawResponse, out var generatedText))
        {
            return false;
        }

        var jsonPayload = ExtractJsonObject(generatedText);
        if (!string.IsNullOrEmpty(jsonPayload))
        {
            try
            {
                var payload = JsonUtility.FromJson<GeminiDiagnosticPayload>(jsonPayload);
                if (payload != null && !string.IsNullOrWhiteSpace(payload.riskLevel))
                {
                    report = BuildReportFromPayload(payload);
                    return true;
                }
            }
            catch
            {
            }
        }

        report = BuildReportFromFreeText(generatedText);
        return true;
    }

    private bool TryExtractGeneratedText(string rawResponse, out string generatedText)
    {
        generatedText = string.Empty;
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return false;
        }

        GeminiGenerateResponse parsed;
        try
        {
            parsed = JsonUtility.FromJson<GeminiGenerateResponse>(rawResponse);
        }
        catch
        {
            return false;
        }

        if (parsed == null || parsed.candidates == null || parsed.candidates.Length == 0)
        {
            return false;
        }

        var candidate = parsed.candidates[0];
        if (candidate == null || candidate.content == null || candidate.content.parts == null || candidate.content.parts.Length == 0)
        {
            return false;
        }

        generatedText = candidate.content.parts[0].text;
        return !string.IsNullOrWhiteSpace(generatedText);
    }

    private static string ExtractJsonObject(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return string.Empty;
        }

        var start = source.IndexOf('{');
        var end = source.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return string.Empty;
        }

        return source.Substring(start, end - start + 1);
    }

    private DiagnosticReport BuildReportFromPayload(GeminiDiagnosticPayload payload)
    {
        var normalizedRisk = NormalizeRisk(payload.riskLevel);
        var confidence = string.IsNullOrWhiteSpace(payload.confidence) ? "85%" : payload.confidence.Trim();
        var findingA = SafeOrDefault(payload.findingA, "Model flagged a pattern requiring inspection.");
        var findingB = SafeOrDefault(payload.findingB, "Secondary signal indicates moderate drift.");
        var action = SafeOrDefault(payload.action, "Action: Run a manual check and schedule maintenance.");

        return new DiagnosticReport(
            "Risk: " + normalizedRisk,
            "Confidence: " + confidence,
            findingA,
            findingB,
            action.StartsWith("Action:", StringComparison.OrdinalIgnoreCase) ? action : "Action: " + action);
    }

    private DiagnosticReport BuildReportFromFreeText(string text)
    {
        var lowered = text.ToLowerInvariant();
        var risk = "MEDIUM";
        if (lowered.Contains("high"))
        {
            risk = "HIGH";
        }
        else if (lowered.Contains("low"))
        {
            risk = "LOW";
        }

        var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        var findingA = lines.Length > 0 ? lines[0].Trim() : "Model produced an unstructured result.";
        var findingB = lines.Length > 1 ? lines[1].Trim() : "Additional verification recommended.";
        var action = lines.Length > 2 ? lines[2].Trim() : "Action: Check component state and re-scan.";

        if (!action.StartsWith("Action:", StringComparison.OrdinalIgnoreCase))
        {
            action = "Action: " + action;
        }

        return new DiagnosticReport(
            "Risk: " + risk,
            "Confidence: 80%",
            findingA,
            findingB,
            action);
    }

    private DiagnosticReport GetFallbackReport()
    {
        var report = fallbackReports[reportIndex];
        reportIndex = (reportIndex + 1) % fallbackReports.Length;
        return report;
    }

    private void AddToHistory(DiagnosticReport report)
    {
        reportHistory.Insert(0, report);
        if (reportHistory.Count > 6)
        {
            reportHistory.RemoveAt(reportHistory.Count - 1);
        }
    }

    private void ApplyDetectedObjectInfo(DetectedObjectInfo detectedInfo)
    {
        if (!detectedInfo.IsValid)
        {
            return;
        }

        var normalizedName = NormalizeDetectedObjectName(detectedInfo.ObjectName);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return;
        }

        hasDetectedObject = true;
        lastDetectedObject = new DetectedObjectInfo(
            normalizedName,
            NormalizeConfidence(detectedInfo.Confidence),
            SafeOrDefault(detectedInfo.Description, "Detected object from camera frame."));

        var objectName = lastDetectedObject.ObjectName;
        if (objectName.Length > 18)
        {
            objectName = objectName.Substring(0, 18) + "...";
        }

        SetStatusBadge(L("OBJECT", "OBJET") + ": " + objectName);
        UpdateDetectedObjectTitle();
    }

    private string GetActiveAssetName()
    {
        return hasDetectedObject && !string.IsNullOrWhiteSpace(lastDetectedObject.ObjectName)
            ? lastDetectedObject.ObjectName
            : trackedAssetName;
    }

    private string BuildDetectedObjectLine()
    {
        if (!includeDetectedObjectInSummary || !hasDetectedObject)
        {
            return string.Empty;
        }

        var confidence = string.IsNullOrWhiteSpace(lastDetectedObject.Confidence) ? "-" : lastDetectedObject.Confidence;
        var description = string.IsNullOrWhiteSpace(lastDetectedObject.Description)
            ? string.Empty
            : "\n<color=#A9D7FF>" + lastDetectedObject.Description + "</color>";

        return "<color=#9CC3FF><b>" + L("Detected Object", "Objet detecte") + ":</b></color> " + lastDetectedObject.ObjectName + " (" + confidence + ")" + description + "\n";
    }

    private string BuildPanelHint(string summaryHint, string fallbackHint)
    {
        if (enableTapToCycle)
        {
            return "<i>" + summaryHint + "</i>";
        }

        return "<i>" + fallbackHint + "</i>";
    }

    private void DisplayFinalPanel(DiagnosticReport report)
    {
        if (aiTextDisplay == null)
        {
            return;
        }

        aiTextDisplay.color = new Color(0.94f, 0.98f, 1f, 1f);

        if (presentationMinimalMode)
        {
            var detectedLine = hasDetectedObject
                ? BuildDetectedObjectLine()
                : "<color=#9CC3FF><b>" + L("Detected Object", "Objet detecte") + ":</b></color> " + L("-- pending --", "-- en attente --") + "\n";

            aiTextDisplay.text =
                "<size=110%><b>" + L("AI Analysis", "Analyse IA") + "</b></size>\n" +
                detectedLine + "\n" +
                "<color=#B8D9FF><b>" + L("Confidence", "Confiance") + ":</b></color> " +
                (hasDetectedObject ? SafeOrDefault(lastDetectedObject.Confidence, "N/A") : "N/A") + "\n" +
                "<color=#B8D9FF><b>" + L("Description", "Description") + ":</b></color> " +
                (hasDetectedObject ? SafeOrDefault(lastDetectedObject.Description, L("Detected from live camera.", "Detecte depuis la camera.")) : L("Waiting for a stable object in camera.", "En attente d'un objet stable dans la camera.")) + "\n\n" +
                "<i>" + L("Press Analyze Again to refresh.", "Appuyez sur Analyser a nouveau.") + "</i>";
            return;
        }

        if (currentPanelIndex == 0)
        {
            aiTextDisplay.text =
                "<size=122%><b>" + useCaseTitle + "</b></size>\n" +
                "<color=#9CC3FF>" + L("Target", "Cible") + ":</color> " + GetActiveAssetName() + "\n" +
                BuildDetectedObjectLine() + "\n" +
                "<b>" + report.Risk + "</b>  |  " + report.Confidence + "\n" +
                "- " + report.FindingA + "\n" +
                "- " + report.FindingB + "\n\n" +
                BuildPanelHint(
                    L("Tap/click for Action panel.", "Touchez/cliquez pour le panneau Action."),
                    L("Press Export Report to save this analysis.", "Appuyez sur Exporter Rapport pour sauvegarder."));
            return;
        }

        if (currentPanelIndex == 1)
        {
            aiTextDisplay.text =
                "<size=122%><b>" + L("Recommended Action", "Action recommandee") + "</b></size>\n" +
                "<color=#9CC3FF>" + L("Target", "Cible") + ":</color> " + GetActiveAssetName() + "\n" +
                BuildDetectedObjectLine() + "\n" +
                report.Action + "\n\n" +
                L("Priority", "Priorite") + ": " + report.Risk.Replace("Risk: ", "") + "\n" +
                BuildPanelHint(
                    L("Tap/click for History panel.", "Touchez/cliquez pour l'historique."),
                    L("Keep the target visible for updated object detection.", "Gardez la cible visible pour mettre a jour la detection."));
            return;
        }

        aiTextDisplay.text =
            "<size=122%><b>" + L("History Snapshot", "Historique") + "</b></size>\n" +
            L("Last scans", "Derniers scans") + ":\n" +
            "1) " + GetHistoryEntry(0) + "\n" +
            "2) " + GetHistoryEntry(1) + "\n" +
            "3) " + GetHistoryEntry(2) + "\n\n" +
            BuildPanelHint(
                L("Tap/click to return to Summary.", "Touchez/cliquez pour revenir au resume."),
                L("Summary is shown by default. Re-scan to refresh.", "Le resume est affiche par defaut. Re-scannez pour actualiser."));
    }

    private string GetHistoryEntry(int index)
    {
        if (index >= reportHistory.Count)
        {
            return L("No data", "Aucune donnee");
        }

        var entry = reportHistory[index];
        return entry.Risk + " / " + entry.Confidence;
    }

    private string BuildStageText(string step, string stage, float progress)
    {
        var bar = BuildProgressBar(progress);
        return "<size=122%><b>" + useCaseTitle + "</b></size>\n" +
               "<color=#9CC3FF>" + L("Target", "Cible") + ":</color> " + GetActiveAssetName() + "\n\n" +
               L("Analysis", "Analyse") + " " + step + "\n" +
               stage + "\n" +
               bar;
    }

    private static string BuildProgressBar(float progress)
    {
        var clamped = Mathf.Clamp01(progress);
        var filled = Mathf.RoundToInt(clamped * 14f);
        var empty = 14 - filled;
        return "[" + new string('#', filled) + new string('-', empty) + "]";
    }

    private void ShowIdleState()
    {
        if (aiTextDisplay == null)
        {
            return;
        }

        aiTextDisplay.color = idleColor;
        aiTextDisplay.text =
            "<size=122%><b>" + useCaseTitle + "</b></size>\n" +
            L("Point the camera at the target image.", "Pointez la camera vers l'image cible.") + "\n" +
            L("AI status: Waiting for detection.", "Statut IA: En attente de detection.") +
            (enableObjectIdentification
                ? "\n" + L("Object ID: enabled", "Identification objet: activee")
                : string.Empty);
    }

    private void EnsureDisplayReference()
    {
        if (aiTextDisplay != null)
        {
            return;
        }

        if (!autoCreateHudIfMissing)
        {
            return;
        }

        var canvas = GetOrCreateOverlayCanvas();

        var panelObject = new GameObject("AI HUD Panel", typeof(RectTransform), typeof(Image));
        panelObject.transform.SetParent(canvas.transform, false);
        hudPanelImage = panelObject.GetComponent<Image>();

        var panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.02f, 0.03f);
        panelRect.anchorMax = new Vector2(0.36f, 0.31f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        var textObject = new GameObject("AI Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(panelObject.transform, false);
        aiTextDisplay = textObject.GetComponent<TextMeshProUGUI>();

        var textRect = aiTextDisplay.rectTransform;
        textRect.anchorMin = new Vector2(0f, 0f);
        textRect.anchorMax = new Vector2(1f, 1f);
        textRect.offsetMin = new Vector2(12f, 12f);
        textRect.offsetMax = new Vector2(-12f, -58f);
    }

    private void EnsureThemedHud()
    {
        if (aiTextDisplay == null)
        {
            return;
        }

        var canvas = GetOrCreateOverlayCanvas();

        if (hudPanelImage != null)
        {
            var hudCanvas = hudPanelImage.GetComponentInParent<Canvas>();
            if (hudCanvas == null || hudCanvas.renderMode != RenderMode.ScreenSpaceOverlay || hudPanelImage.transform.parent != canvas.transform)
            {
                hudPanelImage.transform.SetParent(canvas.transform, false);
            }
        }
        else
        {
            var textCanvas = aiTextDisplay.GetComponentInParent<Canvas>();
            if (textCanvas == null || textCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                aiTextDisplay.transform.SetParent(canvas.transform, false);
            }
        }

        if (hudPanelImage == null)
        {
            var panel = new GameObject("AR AI Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvas.transform, false);
            hudPanelImage = panel.GetComponent<Image>();

            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.02f, 0.03f);
            panelRect.anchorMax = new Vector2(0.36f, 0.31f);
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            aiTextDisplay.transform.SetParent(panel.transform, false);
            var textRect = aiTextDisplay.rectTransform;
            textRect.anchorMin = new Vector2(0f, 0f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.offsetMin = new Vector2(12f, 12f);
            textRect.offsetMax = new Vector2(-12f, -58f);
        }

        hudPanelImage.color = new Color(0.02f, 0.07f, 0.15f, 0.62f);

        if (headerBadgeText == null)
        {
            var badge = new GameObject("Status Badge", typeof(RectTransform), typeof(TextMeshProUGUI));
            badge.transform.SetParent(hudPanelImage.transform, false);
            headerBadgeText = badge.GetComponent<TextMeshProUGUI>();

            var badgeRect = headerBadgeText.rectTransform;
            badgeRect.anchorMin = new Vector2(0f, 1f);
            badgeRect.anchorMax = new Vector2(1f, 1f);
            badgeRect.pivot = new Vector2(0.5f, 1f);
            badgeRect.sizeDelta = new Vector2(0f, 34f);
            badgeRect.anchoredPosition = new Vector2(0f, -8f);
        }

        headerBadgeText.alignment = TextAlignmentOptions.Center;
        headerBadgeText.fontSize = 17f;
        headerBadgeText.color = new Color(0.68f, 0.86f, 1f, 1f);
        SetStatusBadge(L("READY", "PRET"));
    }

    private void EnsureDetectedObjectTitle()
    {
        var canvas = GetOrCreateOverlayCanvas();

        if (detectedObjectTitleText == null)
        {
            var titleObject = new GameObject("Detected Object Title", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleObject.transform.SetParent(canvas.transform, false);
            detectedObjectTitleText = titleObject.GetComponent<TextMeshProUGUI>();

            var titleRect = detectedObjectTitleText.rectTransform;
            titleRect.anchorMin = new Vector2(0.1f, 0.9f);
            titleRect.anchorMax = new Vector2(0.9f, 0.98f);
            titleRect.offsetMin = Vector2.zero;
            titleRect.offsetMax = Vector2.zero;

            detectedObjectTitleText.alignment = TextAlignmentOptions.Center;
            detectedObjectTitleText.fontSize = 36f;
            detectedObjectTitleText.fontStyle = FontStyles.Bold;
            detectedObjectTitleText.color = new Color(0.94f, 0.98f, 1f, 1f);
        }

        UpdateDetectedObjectTitle();
    }

    private void UpdateDetectedObjectTitle()
    {
        if (detectedObjectTitleText == null)
        {
            return;
        }

        if (hasDetectedObject)
        {
            detectedObjectTitleText.text = L("Detected Object: ", "Objet detecte : ") + lastDetectedObject.ObjectName;
        }
        else
        {
            detectedObjectTitleText.text = L("Detected Object: --", "Objet detecte : --");
        }
    }

    private void EnsureExportButton()
    {
        if (hudPanelImage == null)
        {
            return;
        }

        if (exportReportButton == null)
        {
            var buttonObject = new GameObject("Analyze Again Button", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(hudPanelImage.transform, false);

            var buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(1f, 0f);
            buttonRect.anchorMax = new Vector2(1f, 0f);
            buttonRect.pivot = new Vector2(1f, 0f);
            buttonRect.sizeDelta = new Vector2(170f, 40f);
            buttonRect.anchoredPosition = new Vector2(-12f, 10f);

            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.13f, 0.53f, 0.95f, 0.95f);

            exportReportButton = buttonObject.GetComponent<Button>();

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform, false);
            exportReportLabel = labelObject.GetComponent<TextMeshProUGUI>();
            exportReportLabel.alignment = TextAlignmentOptions.Center;
            exportReportLabel.fontSize = 19f;
            exportReportLabel.color = Color.white;

            var labelRect = exportReportLabel.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
        }

        if (exportReportLabel == null)
        {
            exportReportLabel = exportReportButton.GetComponentInChildren<TextMeshProUGUI>();
        }

        if (exportReportLabel != null)
        {
            exportReportLabel.text = L("Analyze Again", "Analyser a nouveau");
        }

        exportReportButton.onClick.RemoveAllListeners();
        exportReportButton.onClick.AddListener(AnalyzeAgain);
    }

    private void AnalyzeAgain()
    {
        if (!targetVisible && !presentationMinimalMode)
        {
            SetStatusBadge(L("SHOW TARGET", "MONTREZ LA CIBLE"));
            ShowIdleState();
            return;
        }

        if (simulationRoutine != null)
        {
            StopCoroutine(simulationRoutine);
        }

        hasDetectedObject = false;
        lastDetectedObject = default;
        UpdateDetectedObjectTitle();

        currentPanelIndex = 0;
        SetStatusBadge(L("SCANNING", "ANALYSE"));

        if (presentationMinimalMode)
        {
            SetVirtualMarkerVisible(false);
            SetKpiVisible(false);
        }

        simulationRoutine = StartCoroutine(SimulateAIAgent());
    }

    private void ExportLatestReport()
    {
        if (reportHistory.Count == 0)
        {
            SetStatusBadge(L("NO REPORT YET", "AUCUN RAPPORT"));
            return;
        }

        if (exportInProgress)
        {
            SetStatusBadge(L("EXPORTING", "EXPORT EN COURS"));
            return;
        }

        StartCoroutine(ExportLatestReportRoutine(reportHistory[0]));
    }

    private IEnumerator ExportLatestReportRoutine(DiagnosticReport latest)
    {
        exportInProgress = true;
        SetStatusBadge(L("EXPORTING", "EXPORT EN COURS"));

        var screenshotPath = string.Empty;
        var reportPath = string.Empty;
        var reportsDir = Path.Combine(Application.persistentDataPath, "Reports");

        try
        {
            Directory.CreateDirectory(reportsDir);
        }
        catch (Exception ex)
        {
            SetStatusBadge(L("EXPORT FAILED", "ECHEC EXPORT"));
            Debug.LogError("AIAgentController: Export failed while preparing directory. " + ex.Message);
            exportInProgress = false;
            yield break;
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        reportPath = Path.Combine(reportsDir, "ar_ai_report_" + stamp + ".txt");
        if (includeScreenshotInExport)
        {
            screenshotPath = Path.Combine(reportsDir, "ar_ai_screenshot_" + stamp + ".png");
        }

        if (includeScreenshotInExport)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(screenshotPath, Mathf.Max(1, screenshotSuperSize));

            if (screenshotDelaySeconds > 0f)
            {
                yield return new WaitForSeconds(screenshotDelaySeconds);
            }
        }

        try
        {
            File.WriteAllText(reportPath, BuildReportDocument(latest, screenshotPath));

            SetStatusBadge(L("REPORT EXPORTED", "RAPPORT EXPORTE"));
            Debug.Log("AIAgentController: Report exported to " + reportPath);
            if (!string.IsNullOrEmpty(screenshotPath))
            {
                Debug.Log("AIAgentController: Screenshot saved to " + screenshotPath);
            }

            if (aiTextDisplay != null)
            {
                aiTextDisplay.text += "\n\n<color=#9CD0FF>" + L("Saved", "Sauvegarde") + ":</color> " + reportPath;
                if (!string.IsNullOrEmpty(screenshotPath))
                {
                    aiTextDisplay.text += "\n<color=#9CD0FF>" + L("Screenshot", "Capture") + ":</color> " + screenshotPath;
                }
            }
        }
        catch (Exception ex)
        {
            SetStatusBadge(L("EXPORT FAILED", "ECHEC EXPORT"));
            Debug.LogError("AIAgentController: Export failed. " + ex.Message);
        }

        exportInProgress = false;
    }

    private string BuildReportDocument(DiagnosticReport latest, string screenshotPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine(L("AR AI REPORT", "RAPPORT AR IA"));
        sb.AppendLine(L("Generated", "Genere") + ": " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine(L("Use case", "Cas d'usage") + ": " + useCaseTitle);
        sb.AppendLine(L("Target", "Cible") + ": " + GetActiveAssetName());

        if (hasDetectedObject)
        {
            sb.AppendLine(L("Detected object", "Objet detecte") + ": " + lastDetectedObject.ObjectName + " (" + SafeOrDefault(lastDetectedObject.Confidence, "N/A") + ")");
            if (!string.IsNullOrWhiteSpace(lastDetectedObject.Description))
            {
                sb.AppendLine(L("Object note", "Note objet") + ": " + lastDetectedObject.Description);
            }
        }

        if (!string.IsNullOrEmpty(screenshotPath))
        {
            sb.AppendLine(L("Screenshot", "Capture") + ": " + screenshotPath);
        }

        sb.AppendLine();
        sb.AppendLine(L("Telemetry snapshot", "Instantane telemetry") + ":");
        sb.AppendLine("- Temperature C: " + ReadKpiTarget("Temperature", 74f).ToString("F1"));
        sb.AppendLine("- Vibration mm/s: " + ReadKpiTarget("Vibration", 2.2f).ToString("F2"));
        sb.AppendLine("- Pressure bar: " + ReadKpiTarget("Pressure", 5.5f).ToString("F2"));
        sb.AppendLine();
        sb.AppendLine(latest.Risk + " | " + latest.Confidence);
        sb.AppendLine(L("Finding A", "Constat A") + ": " + latest.FindingA);
        sb.AppendLine(L("Finding B", "Constat B") + ": " + latest.FindingB);
        sb.AppendLine(latest.Action);
        sb.AppendLine();
        sb.AppendLine(L("History", "Historique") + ":");

        for (var i = 0; i < Mathf.Min(5, reportHistory.Count); i++)
        {
            sb.AppendLine("- " + reportHistory[i].Risk + " | " + reportHistory[i].Confidence);
        }

        return sb.ToString();
    }

    private void EnsureKpiCards()
    {
        if (!create3DKpiCards || kpiCards.Count > 0)
        {
            return;
        }

        kpiCards.Add(CreateKpiCard("Temperature", "C", new Vector3(-kpiRadius, 0.085f, -0.02f), 50f, 110f, 72f));
        kpiCards.Add(CreateKpiCard("Vibration", "mm/s", new Vector3(kpiRadius, 0.085f, -0.02f), 0.5f, 5f, 2.1f));
        kpiCards.Add(CreateKpiCard("Pressure", "bar", new Vector3(0f, 0.165f, 0.03f), 2f, 10f, 5.8f));
    }

    private KpiCard CreateKpiCard(string title, string unit, Vector3 localPosition, float min, float max, float initial)
    {
        var rootObject = new GameObject("KPI_" + title, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));

        if (detachKpiFromTarget)
        {
            rootObject.transform.SetParent(null, true);
            rootObject.transform.position = transform.TransformPoint(localPosition);
        }
        else
        {
            rootObject.transform.SetParent(transform, false);
            rootObject.transform.localPosition = localPosition;
        }

        rootObject.transform.localScale = Vector3.one * kpiCardScale;

        var canvas = rootObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 800;

        var scaler = rootObject.GetComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 20f;

        var rootRect = rootObject.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(300f, 130f);

        var backgroundObject = new GameObject("Background", typeof(RectTransform), typeof(Image));
        backgroundObject.transform.SetParent(rootObject.transform, false);
        var background = backgroundObject.GetComponent<Image>();
        background.color = new Color(0.05f, 0.12f, 0.2f, 0.72f);

        var bgRect = backgroundObject.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        var textObject = new GameObject("Value", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(backgroundObject.transform, false);
        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = 34f;
        text.color = Color.white;

        var textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12f, 12f);
        textRect.offsetMax = new Vector2(-12f, -12f);

        var card = new KpiCard
        {
            Root = rootObject.transform,
            Background = background,
            ValueText = text,
            LocalOffset = localPosition,
            Title = title,
            Unit = unit,
            Min = min,
            Max = max,
            CurrentValue = initial,
            TargetValue = initial
        };

        UpdateKpiLabel(card);
        return card;
    }

    private void UpdateKpiCardsVisual()
    {
        if (kpiCards.Count == 0)
        {
            return;
        }

        var cam = Camera.main;
        foreach (var card in kpiCards)
        {
            if (detachKpiFromTarget && targetVisible)
            {
                var desiredWorldPos = transform.TransformPoint(card.LocalOffset);
                card.Root.position = Vector3.Lerp(card.Root.position, desiredWorldPos, Time.deltaTime * kpiFollowSmooth);
            }

            card.CurrentValue = Mathf.Lerp(card.CurrentValue, card.TargetValue, Time.deltaTime * 4f);
            UpdateKpiLabel(card);

            if (cam != null)
            {
                var forward = -cam.transform.forward;
                var up = cam.transform.up;

                if (kpiYawOnlyBillboard)
                {
                    forward = Vector3.ProjectOnPlane(forward, Vector3.up);
                    up = Vector3.up;
                }

                if (forward.sqrMagnitude > 0.0001f)
                {
                    var desiredRotation = Quaternion.LookRotation(forward.normalized, up);
                    if (kpiFlipFacing)
                    {
                        desiredRotation *= Quaternion.Euler(0f, 180f, 0f);
                    }

                    card.Root.rotation = Quaternion.Slerp(card.Root.rotation, desiredRotation, Time.deltaTime * kpiBillboardSmooth);
                }
            }
        }
    }

    private void SyncKpiWorldPositions(bool instant)
    {
        if (!detachKpiFromTarget)
        {
            return;
        }

        foreach (var card in kpiCards)
        {
            if (card.Root == null)
            {
                continue;
            }

            var desiredWorldPos = transform.TransformPoint(card.LocalOffset);
            if (instant)
            {
                card.Root.position = desiredWorldPos;
            }
            else
            {
                card.Root.position = Vector3.Lerp(card.Root.position, desiredWorldPos, Time.deltaTime * kpiFollowSmooth);
            }
        }
    }

    private void UpdateKpiLabel(KpiCard card)
    {
        card.ValueText.text =
            "<size=68%>" + card.Title + "</size>\n" +
            "<b>" + card.CurrentValue.ToString(card.Unit == "C" ? "F1" : "F2") + " " + card.Unit + "</b>";

        var normalized = Mathf.InverseLerp(card.Min, card.Max, card.CurrentValue);
        card.Background.color = Color.Lerp(new Color(0.06f, 0.28f, 0.18f, 0.88f), new Color(0.55f, 0.15f, 0.12f, 0.92f), normalized);
    }

    private void ApplyReportToKpis(DiagnosticReport report)
    {
        if (kpiCards.Count < 3)
        {
            return;
        }

        var riskScore = 0.62f;
        if (report.Risk.Contains("HIGH"))
        {
            riskScore = 0.92f;
        }
        else if (report.Risk.Contains("LOW"))
        {
            riskScore = 0.35f;
        }

        kpiCards[0].TargetValue = Mathf.Lerp(63f, 99f, riskScore) + UnityEngine.Random.Range(-1.5f, 1.5f);
        kpiCards[1].TargetValue = Mathf.Lerp(1.2f, 4.3f, riskScore) + UnityEngine.Random.Range(-0.15f, 0.15f);
        kpiCards[2].TargetValue = Mathf.Lerp(4.1f, 8.8f, riskScore) + UnityEngine.Random.Range(-0.2f, 0.2f);
    }

    private float ReadKpiTarget(string title, float fallback)
    {
        foreach (var card in kpiCards)
        {
            if (card.Title.Equals(title, StringComparison.OrdinalIgnoreCase))
            {
                return card.TargetValue;
            }
        }

        return fallback;
    }

    private void SetKpiVisible(bool visible)
    {
        if (kpiAppearRoutine != null)
        {
            StopCoroutine(kpiAppearRoutine);
            kpiAppearRoutine = null;
        }

        if (visible)
        {
            SyncKpiWorldPositions(true);
        }

        if (visible && animateKpiAppearance)
        {
            kpiAppearRoutine = StartCoroutine(AnimateKpiCardsIn());
            return;
        }

        foreach (var card in kpiCards)
        {
            if (card.Root != null)
            {
                card.Root.gameObject.SetActive(visible);
                card.Root.localScale = visible ? Vector3.one * kpiCardScale : Vector3.zero;
            }
        }
    }

    private IEnumerator AnimateKpiCardsIn()
    {
        var duration = Mathf.Max(0.05f, kpiAppearDuration);
        var stagger = Mathf.Max(0f, kpiAppearStagger);

        for (var i = 0; i < kpiCards.Count; i++)
        {
            var card = kpiCards[i];
            if (card.Root == null)
            {
                continue;
            }

            card.Root.gameObject.SetActive(true);
            card.Root.localScale = Vector3.zero;
        }

        var elapsed = 0f;
        var total = duration + stagger * Mathf.Max(0, kpiCards.Count - 1);
        while (elapsed <= total + 0.03f)
        {
            for (var i = 0; i < kpiCards.Count; i++)
            {
                var card = kpiCards[i];
                if (card.Root == null)
                {
                    continue;
                }

                var localTime = (elapsed - i * stagger) / duration;
                var t = Mathf.Clamp01(localTime);
                var eased = EaseOutBack(t);
                card.Root.localScale = Vector3.one * kpiCardScale * eased;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        foreach (var card in kpiCards)
        {
            if (card.Root != null)
            {
                card.Root.localScale = Vector3.one * kpiCardScale;
            }
        }

        kpiAppearRoutine = null;
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        var p = t - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
    }

    private void SetStatusBadge(string status)
    {
        if (headerBadgeText == null)
        {
            return;
        }

        headerBadgeText.text = "<b>" + status + "</b>";
    }

    private static Canvas CreateCanvas()
    {
        var canvasObject = new GameObject("AR AI Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 600;

        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        return canvas;
    }

    private static Canvas GetOrCreateOverlayCanvas()
    {
        var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        for (var i = 0; i < canvases.Length; i++)
        {
            if (canvases[i] != null && canvases[i].name == "AR AI Canvas" && canvases[i].renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return canvases[i];
            }
        }

        for (var i = 0; i < canvases.Length; i++)
        {
            if (canvases[i] != null && canvases[i].renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return canvases[i];
            }
        }

        return CreateCanvas();
    }

    private void UpdateLocalizedStaticUi()
    {
        if (exportReportLabel == null && exportReportButton != null)
        {
            exportReportLabel = exportReportButton.GetComponentInChildren<TextMeshProUGUI>();
        }

        if (exportReportLabel != null)
        {
            exportReportLabel.text = L("Analyze Again", "Analyser a nouveau");
        }

        UpdateDetectedObjectTitle();

        if (!targetVisible)
        {
            SetStatusBadge(L("READY", "PRET"));
        }
    }

    private void ApplyReadableLayout()
    {
        if (aiTextDisplay == null)
        {
            return;
        }

        aiTextDisplay.textWrappingMode = TextWrappingModes.Normal;
        aiTextDisplay.fontSize = Mathf.Clamp(aiTextDisplay.fontSize, 20f, 24f);
        aiTextDisplay.lineSpacing = 0f;
        aiTextDisplay.alignment = TextAlignmentOptions.TopLeft;
    }

    private static bool HasTapOrClick()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            return true;
        }

        return Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame;
#else
        if (Input.GetMouseButtonDown(0))
        {
            return true;
        }

        return Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began;
#endif
    }

    private static string NormalizeRisk(string risk)
    {
        if (string.IsNullOrWhiteSpace(risk))
        {
            return "MEDIUM";
        }

        var upper = risk.Trim().ToUpperInvariant();
        if (upper.Contains("HIGH"))
        {
            return "HIGH";
        }

        if (upper.Contains("LOW"))
        {
            return "LOW";
        }

        return "MEDIUM";
    }

    private static string SafeOrDefault(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static bool LooksLikeGoogleGeminiKey(string key)
    {
        return !string.IsNullOrWhiteSpace(key) && key.Trim().StartsWith("AIza", StringComparison.Ordinal);
    }

    private string L(string english, string french)
    {
        return uiLanguage == UiLanguage.French ? french : english;
    }
}