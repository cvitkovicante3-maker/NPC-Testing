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
    [Range(0f, 90f)] public float sunLatitude = 20f;
    [Range(-180f, 180f)] public float sunLongitude = -90f;
    [Tooltip("Base intensity of the sun. For URP / built-in use about 1 - 2.")]
    public float sunIntensity = 1f;
    public AnimationCurve sunIntensityMultiplier = AnimationCurve.Linear(0f, 1f, 1f, 1f);
    [Tooltip("Value x 10000 = temperature in Kelvin (0.55 = 5500 K).")]
    public AnimationCurve sunTemperatureCurve = AnimationCurve.Linear(0f, 0.55f, 1f, 0.55f);

    [Space]
    [Header("🌙 Moon Configuration")]
    public Light moonLight;
    [Range(0f, 90f)] public float moonLatitude = 40f;
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

    void Start()
    {
        EnableTemperature(sunLight);
        EnableTemperature(moonLight);
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
            sunLight.useColorTemperature = true;
            sunLight.colorTemperature = Mathf.Clamp(sunTemperatureCurve.Evaluate(normalizedTime) * 10000f, 1500f, 20000f);
        }

        if (moonLight != null)
        {
            float moonRotation = (currentTime + 12f) / 24f * 360f;
            moonLight.transform.localRotation = Quaternion.Euler(moonLatitude - 90f, moonLongitude, 0f) * Quaternion.Euler(0f, moonRotation, 0f);
            float phaseMultiplier = Mathf.Sin(lunarDay / 30f * Mathf.PI);
            moonLight.intensity = moonIntensityMultiplier.Evaluate(normalizedTime) * moonIntensity * phaseMultiplier;
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