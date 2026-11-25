using UnityEngine;

/// <summary>
/// Turns a street lamp on at night and off in the morning, following the time of the day/night cycle (cyclemanager).
/// Put it on the lamp object (or its prefab). It controls every Light in the lamp and its children,
/// and (optionally) makes the lamp glass glow.
/// </summary>
public class StreetLamp : MonoBehaviour
{
    [Header("When the lamp is on (24-hour clock)")]
    [Range(0f, 24f)] public float turnOnTime = 21f;
    [Range(0f, 24f)] public float turnOffTime = 7f;

    [Header("Look")]
    [Tooltip("How long the lamp takes to fade in or out, in real seconds.")]
    public float fadeSeconds = 2f;
    [Tooltip("Brightness of the lights when fully on. Leave at 0 to use the intensity the Light has in the Inspector.")]
    public float onIntensity = 0f;

    [Header("Glowing glass (optional)")]
    [Tooltip("Drag the lamp's glass / bulb object here to make it glow when on. Its material needs an Emission setting (Lit shader).")]
    public Renderer[] glowingParts;
    [ColorUsage(false, true)] public Color glowColor = new Color(1f, 0.82f, 0.5f) * 2f;

    [Header("Time source")]
    [Tooltip("Filled in automatically. Only drag the manager here if you have several.")]
    public cyclemanager clock;

    private Light[] lights;
    private float[] baseIntensity;
    private Material[] glowMaterials;
    private float amount;   // 0 = off, 1 = on

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private void Awake()
    {
        lights = GetComponentsInChildren<Light>(true);
        baseIntensity = new float[lights.Length];
        for (int i = 0; i < lights.Length; i++)
            baseIntensity[i] = onIntensity > 0f ? onIntensity : lights[i].intensity;

        if (glowingParts != null)
        {
            var list = new System.Collections.Generic.List<Material>();
            foreach (Renderer r in glowingParts)
                if (r != null) list.AddRange(r.materials);   // per-lamp copies, so lamps don't affect each other
            glowMaterials = list.ToArray();
            foreach (Material m in glowMaterials) m.EnableKeyword("_EMISSION");
        }
    }

    private void Start()
    {
        if (clock == null) clock = FindFirstObjectByType<cyclemanager>();
        if (clock == null) Debug.LogWarning("StreetLamp: no cyclemanager found in the scene, so the lamp can't follow the time.", this);

        amount = ShouldBeOn() ? 1f : 0f;   // start in the right state
        Apply();
    }

    private void Update()
    {
        float target = ShouldBeOn() ? 1f : 0f;
        amount = fadeSeconds <= 0.01f ? target : Mathf.MoveTowards(amount, target, Time.deltaTime / fadeSeconds);
        Apply();
    }

    private bool ShouldBeOn()
    {
        if (clock == null) return false;
        float t = clock.currentTime;
        // works across midnight: 21 -> 7 means "from 21:00 until 07:00"
        return turnOnTime <= turnOffTime ? (t >= turnOnTime && t < turnOffTime) : (t >= turnOnTime || t < turnOffTime);
    }

    private void Apply()
    {
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] == null) continue;
            lights[i].intensity = baseIntensity[i] * amount;
            lights[i].enabled = amount > 0.001f;
        }

        if (glowMaterials != null)
            foreach (Material m in glowMaterials)
                if (m != null) m.SetColor(EmissionColorId, glowColor * amount);
    }
}
