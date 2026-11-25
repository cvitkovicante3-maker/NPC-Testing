using UnityEngine;

// Day / night cycle for URP or the built-in pipeline (no HDRP types needed).
public class cyclemanager : MonoBehaviour
{
    [Header("⏱️ Time Progression")]
    [Tooltip("Current time of day in 24-hour format.")]
    [Range(0f, 24f)] public float currentTime;

    [Tooltip("Game hours that pass per real second. 1 = one hour per second (a full day in 24 s). Try 0.01 - 0.05 for a calmer day.")]
    public float timeSpeed = 1f;

    [Space]
    [Header("🕒 Current Time Display")]
    public string currentTimeString;

    [Space]
    [Header("☀️ Sun Configuration")]
    public Light sunLight;
    [Tooltip("Lower = the sun climbs higher at noon (0 = straight overhead, 90 = stays on the horizon).")]
    [Range(0f, 90f)] public float sunLatitude = 15f;
    [Range(-180f, 180f)] public float sunLongitude = -90f;
    [Tooltip("Base intensity of the sun. For URP / built-in use about 1 - 2.")]
    public float sunIntensity = 1f;
    public AnimationCurve sunIntensityMultiplier = AnimationCurve.Linear(0f, 1f, 1f, 1f);
    [Tooltip("Value x 10000 = temperature in Kelvin (0.55 = 5500 K).")]
    public AnimationCurve sunTemperatureCurve = AnimationCurve.Linear(0f, 0.55f, 1f, 0.55f);

    [Space]
    [Header("🌙 Moon Configuration")]
    public Light moonLight;
    [Tooltip("Lower = the moon climbs higher at night.")]
    [Range(0f, 90f)] public float moonLatitude = 20f;
    [Range(-180f, 180f)] public float moonLongitude = 90f;
    [Tooltip("Base intensity of the moon. For URP / built-in use about 0.1 - 0.4.")]
    public float moonIntensity = 0.3f;
    public AnimationCurve moonIntensityMultiplier = AnimationCurve.Linear(0f, 1f, 1f, 1f);
    public AnimationCurve moonTemperatureCurve = AnimationCurve.Linear(0f, 0.75f, 1f, 0.75f);

    [Space]
    [Header("🌕 Lunar Cycle Settings")]
    [Range(1, 30)] public int lunarDay = 1;
    public float lunarCycleSpeed = 1f;
    public Transform moonModel;

    [Space]
    [Header("🔁 Status Flags (Read-Only)")]
    public bool isDay = true;
    public bool sunActive = true;
    public bool moonActive = true;

    [Header("Shadows")]
    public LightShadows shadowType = LightShadows.Soft;
    [Tooltip("How dark shadows are. 1 = fully dark, lower = softer shadows that let some light through.")]
    [Range(0f, 1f)] public float shadowStrength = 0.6f;

    [Header("Fill light (keeps shadows from going black)")]
    [Tooltip("Lights shadowed areas with an even ambient colour that follows the time of day.")]
    public bool controlAmbient = true;
    public Color dayAmbient = new Color(0.70f, 0.76f, 0.88f);
    public Color nightAmbient = new Color(0.12f, 0.14f, 0.24f);
    [Tooltip("Multiplier for the ambient light. Raise it if shadows are still too dark.")]
    [Range(0f, 3f)] public float ambientIntensity = 1.2f;

    [Header("Fill light (lights the sides the sun/moon don't reach)")]
    [Tooltip("A soft light that shines from the opposite side of the sun / moon, without shadows. This is what stops the far sides of objects from going black.")]
    public bool useFillLight = true;
    [Range(0f, 1.5f)] public float fillIntensityDay = 0.45f;
    [Range(0f, 1f)] public float fillIntensityNight = 0.08f;
    [Tooltip("How steeply the fill light points down (0 = sideways, 60 = mostly from above).")]
    [Range(0f, 80f)] public float fillAngle = 30f;

    void Start()
    {
        EnableTemperature(sunLight);
        EnableTemperature(moonLight);
        CreateFillLight();
        Refresh();
    }

    void Update()
    {
        currentTime += Time.deltaTime * timeSpeed;

        if (currentTime >= 24f)
        {
            currentTime -= 24f;
            lunarDay = (lunarDay % 30) + 1;
        }

        Refresh();
    }

    void OnValidate()
    {
        Refresh();
    }

    void Refresh()
    {
        UpdateTimeText();
        UpdateLight();
        CheckShadowStatus();
        UpdateMoonPhase();
        UpdateAmbient();
    }

    private Light fillLight;
    private Vector3 lastAway = Vector3.forward;
    private float daylight;   // 0 = night, 1 = full day

    void CreateFillLight()
    {
        if (!useFillLight || fillLight != null) return;
        var go = new GameObject("Fill Light (auto)");
        go.transform.SetParent(transform, false);
        fillLight = go.AddComponent<Light>();
        fillLight.type = LightType.Directional;
        fillLight.shadows = LightShadows.None;
    }

    // Soft light everywhere (also in shadow, also on the sides facing away from the sun).
    void UpdateAmbient()
    {
        // how high the sun is: 0 = at or below the horizon, 1 = straight overhead
        float height = sunLight != null ? Mathf.Clamp01(-sunLight.transform.forward.y) : (isDay ? 1f : 0f);
        daylight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.0f, 0.35f, height));

        if (controlAmbient)
        {
            // sky, horizon and ground each get their own tone, so sides and undersides are lit too
            Color baseColor = Color.Lerp(nightAmbient, dayAmbient, daylight) * ambientIntensity;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = baseColor;
            RenderSettings.ambientEquatorColor = baseColor * 0.85f;
            RenderSettings.ambientGroundColor = baseColor * 0.65f;
        }

        UpdateFillLight();
    }

    void UpdateFillLight()
    {
        if (fillLight == null)
        {
            if (useFillLight && Application.isPlaying) CreateFillLight();
            if (fillLight == null) return;
        }

        fillLight.enabled = useFillLight;
        if (!useFillLight) return;

        // shines from the side opposite the sun (by day) or the moon (by night)
        Light source = (daylight > 0.05f || moonLight == null) ? sunLight : moonLight;
        Vector3 away = source != null ? source.transform.forward : Vector3.forward;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = lastAway;   // sun straight overhead: keep the previous side
        away.Normalize();
        lastAway = away;

        // the sun's light travels along 'away', so the fill travels the opposite way
        Vector3 dir = (-away) * Mathf.Cos(fillAngle * Mathf.Deg2Rad) + Vector3.down * Mathf.Sin(fillAngle * Mathf.Deg2Rad);
        fillLight.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

        fillLight.intensity = Mathf.Lerp(fillIntensityNight, fillIntensityDay, daylight);
        fillLight.color = Color.Lerp(nightAmbient * 3f, dayAmbient, daylight);
    }

    // Light temperature only has an effect when this is switched on
    static void EnableTemperature(Light l)
    {
        if (l != null) l.useColorTemperature = true;
    }

    void UpdateTimeText()
    {
        int hours = Mathf.FloorToInt(currentTime);
        int minutes = Mathf.FloorToInt((currentTime - hours) * 60f);
        currentTimeString = hours.ToString("00") + ":" + minutes.ToString("00");
    }

    void UpdateLight()
    {
        float normalizedTime = currentTime / 24f;

        if (sunLight != null)
        {
            float sunRotation = normalizedTime * 360f;
            sunLight.transform.localRotation = Quaternion.Euler(sunLatitude - 90f, sunLongitude, 0f) * Quaternion.Euler(0f, sunRotation, 0f);
            sunLight.intensity = sunIntensityMultiplier.Evaluate(normalizedTime) * sunIntensity;
            sunLight.shadowStrength = shadowStrength;
            sunLight.useColorTemperature = true;
            sunLight.colorTemperature = Mathf.Clamp(sunTemperatureCurve.Evaluate(normalizedTime) * 10000f, 1500f, 20000f);
        }

        if (moonLight != null)
        {
            float moonRotation = (currentTime + 12f) / 24f * 360f;
            moonLight.transform.localRotation = Quaternion.Euler(moonLatitude - 90f, moonLongitude, 0f) * Quaternion.Euler(0f, moonRotation, 0f);
            float phaseMultiplier = Mathf.Sin(lunarDay / 30f * Mathf.PI);
            moonLight.intensity = moonIntensityMultiplier.Evaluate(normalizedTime) * moonIntensity * phaseMultiplier;
            moonLight.shadowStrength = shadowStrength;
            moonLight.useColorTemperature = true;
            moonLight.colorTemperature = Mathf.Clamp(moonTemperatureCurve.Evaluate(normalizedTime) * 10000f, 1500f, 20000f);
        }
    }

    void CheckShadowStatus()
    {
        float t = currentTime;
        isDay = t >= 6f && t <= 18f;

        if (sunLight != null)
        {
            sunLight.shadows = isDay ? shadowType : LightShadows.None;
            sunActive = t >= 5.7f && t <= 18.3f;
            sunLight.gameObject.SetActive(sunActive);
        }

        if (moonLight != null)
        {
            moonLight.shadows = !isDay ? shadowType : LightShadows.None;
            moonActive = !(t >= 6.3f && t <= 17.7f);
            moonLight.gameObject.SetActive(moonActive);
        }
    }

    void UpdateMoonPhase()
    {
        if (moonModel == null) return;
        float phase = (lunarDay - 1) / 29f;
        float scale = Mathf.Max(Mathf.Sin(phase * Mathf.PI), 0.1f);
        moonModel.localScale = Vector3.one * scale;
    }
}