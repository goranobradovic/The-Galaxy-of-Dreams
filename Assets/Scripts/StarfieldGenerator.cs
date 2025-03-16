using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class StarfieldGenerator : MonoBehaviour
{
    [Header("Star Field Settings")]
    public int starCount = 10000;
    public float fieldSize = 20000f;
    public float minStarSize = 5f;
    public float maxStarSize = 20f;

    [Header("Visual Settings")]
    public Material starMaterial;
    public Gradient highToneGradient;
    public Gradient midToneGradient;
    public Gradient lowToneGradient;

    private Gradient centerStarGradient;
    public float colorChangeSpeed = .05f;
    public float baseBrightness = 0.7f;
    public float brightnessScale = 0.1f;

    [Header("References")]
    public AudioAnalyzer audioAnalyzer;
    public new ParticleSystem particleSystem;

    [Header("Flame Particle System")]
    public ParticleSystem flameParticleSystem; // Reference to the the flame particle system
    // Internal variables
    private ParticleSystem.Particle[] stars;
    private Vector3[] initialVelocities;
    private float[] initialSizes;
    private float[] colorOffsets;
    private Color[] originalColors;

    // Add these queue fields as instance variables
    private Queue<float> bassSamples;
    private Queue<float> midsSamples;
    private Queue<float> highsSamples;
    const int sampleSize = 10;

    [Header("Fog Settings")]
    public ParticleSystem fogParticleSystem;
    public Material fogMaterial;
    public float fogDensity = 0.1f;
    public float fogScatteringIntensity = 1.0f;
    public int fogParticleCount = 1000;

    private void Start()
    {
        // Initialize the queues
        bassSamples = new Queue<float>(sampleSize / 5);
        midsSamples = new Queue<float>(sampleSize);
        highsSamples = new Queue<float>(sampleSize / 2);

        SetupParticleSystem();
        CreateStarfield();
        SetupFogSystem();
    }

    private void SetupParticleSystem()
    {
        Debug.Log($"Setting up particle system");
        // Get or create a particle system
        particleSystem = GetComponent<ParticleSystem>();
        if (particleSystem == null)
        {
            particleSystem = gameObject.AddComponent<ParticleSystem>();
        }

        // Configure the particle system
        var main = particleSystem.main;
        main.loop = true;
        main.startLifetime = Mathf.Infinity;
        main.startSpeed = 0;
        main.startSize = 1f;
        main.startColor = Color.white;
        main.maxParticles = starCount;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        // Add material if provided
        if (starMaterial != null)
        {
            var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
            renderer.material = starMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.bounds = new Bounds(Vector3.zero, new Vector3(fieldSize, fieldSize, fieldSize)); // Example bounds
        }

        // Clear any existing particles
        particleSystem.Clear();

    }

    private void CreateStarfield()
    {
        // Initialize arrays
        stars = new ParticleSystem.Particle[starCount];
        initialVelocities = new Vector3[starCount];
        initialSizes = new float[starCount];
        colorOffsets = new float[starCount];
        originalColors = new Color[starCount];

        centerStarGradient = midToneGradient;
        // Add central star
        stars[0].position = Vector3.zero; // Place at center
        initialSizes[0] = Mathf.Min(maxStarSize * 20f, fieldSize * 0.02f); // Make it 10x larger than max size
        stars[0].startSize = initialSizes[0];
        colorOffsets[0] = Random.Range(0.0f, 0.5f);
        stars[0].startColor = centerStarGradient.Evaluate(colorOffsets[0]);
        originalColors[0] = stars[0].startColor;

        // Set the flame particle system to the center star
        flameParticleSystem.transform.parent = transform;
        flameParticleSystem.transform.localPosition = stars[0].position;
        var flameMain = flameParticleSystem.main;
        flameMain.startColor = originalColors[0];
        var shape = flameParticleSystem.shape;
        shape.radius = initialSizes[0] * 0.5f;

        // Create stars in a galaxy disc structure
        for (int i = 1; i < starCount; i++)
        {
            // Create spiral arm effect
            float armCount = 2f; // Number of spiral arms
            float armTightness = 0.2f; // How tightly wound the arms are
            float armRandomness = 0.2f; // How much stars can deviate from perfect spiral

            // Choose whether this star is in an arm or scattered
            bool inArm = Random.value < 0.9f; // 90% chance to be in an arm

            float angle;
            float radius;
            if (inArm)
            {
                // Place star in a spiral arm
                angle = Random.Range(0f, Mathf.PI * 2f);
                radius = (0.1f + 0.9f * angle / Mathf.PI / 2f) * fieldSize; // Base spiral shape

                // Apply spiral arm tightness
                angle += armTightness * radius / fieldSize; // Use armOffset to modify angle based on radius

                // Add some randomness within the arm
                angle += Random.Range(-armRandomness, armRandomness);
                radius += Random.Range(-fieldSize * 0.1f, fieldSize * 0.1f);

                // Modify angle to create multiple arms
                angle += 2f * Mathf.PI * (Mathf.Floor(Random.value * armCount) / armCount);
            }
            else
            {
                // Scattered star distribution
                angle = Random.Range(0f, Mathf.PI * 2f);
                radius = Mathf.Sqrt(Random.Range(0.2f, 1f)) * fieldSize;
            }

            float x = radius * Mathf.Cos(angle);
            float y = radius * Mathf.Sin(angle);
            float z = (Random.Range(0.01f, 0.1f) * fieldSize * (Random.value < 0.5f ? -1f : 1f)) * (inArm ? 0.2f : 1f); // Thinner Z for arms, min absolute value 5%

            Vector3 position = new Vector3(x, y, z);
            stars[i].position = position;

            // Debug log to verify star position
            Debug.Log($"Star {i} position: {position}");

            // Random size
            // Calculate normalized radius (0 to 1)
            float normalizedRadius = radius / fieldSize;

            // Lerp size from max (when radius <= 0.5) to mid (when radius = 1.0)
            float targetSize;
            if (normalizedRadius <= 0.5f)
            {
                targetSize = maxStarSize;
            }
            else
            {
                // Map 0.5-1.0 range to 0-1 for lerp
                float t = (normalizedRadius - 0.5f) * 2f;
                targetSize = Mathf.Lerp(maxStarSize, (minStarSize + maxStarSize) * 0.5f, t);
            }

            
            int gradientIndex = i % 3;


            initialSizes[i] = Random.Range(minStarSize * (gradientIndex + 1)/2, targetSize);
            stars[i].startSize = initialSizes[i];

            // Initial color - assign gradient based on index
            colorOffsets[i] = Random.value;
            stars[i].startColor = gradientIndex == 0 ? highToneGradient.Evaluate(colorOffsets[i]) :
                                 gradientIndex == 1 ? midToneGradient.Evaluate(colorOffsets[i]) :
                                                lowToneGradient.Evaluate(colorOffsets[i]);
            originalColors[i] = stars[i].startColor;

            // Ensure stars start with some lifetime
            stars[i].remainingLifetime = Mathf.Infinity;
        }

        // Set the particles in the system
        particleSystem.SetParticles(stars, stars.Length);
    }

    private void SetupFogSystem()
    {
        if (fogParticleSystem == null)
        {
            // Create a new particle system for fog
            GameObject fogObject = new GameObject("FogSystem");
            fogObject.transform.parent = transform;
            fogParticleSystem = fogObject.AddComponent<ParticleSystem>();
        }

        var main = fogParticleSystem.main;
        main.loop = true;
        main.startLifetime = Mathf.Infinity;
        main.startSpeed = 0;
        main.startSize = fieldSize * 0.1f; // Larger, softer particles for fog
        main.maxParticles = fogParticleCount;

        // Set up fog material
        var renderer = fogParticleSystem.GetComponent<ParticleSystemRenderer>();
        renderer.material = fogMaterial;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortMode = ParticleSystemSortMode.Distance;

        // Create fog particles in random clusters
        var particles = new ParticleSystem.Particle[fogParticleCount];
        for (int i = 0; i < fogParticleCount; i++)
        {
            // Create clusters of fog
            float radius = Random.Range(fieldSize * 0.2f, fieldSize * 0.8f);
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float height = Random.Range(-fieldSize * 0.1f, fieldSize * 0.1f);

            Vector3 position = new Vector3(
                radius * Mathf.Cos(angle),
                radius * Mathf.Sin(angle),
                height
            );

            particles[i].position = position;
            particles[i].startSize = Random.Range(fieldSize * 0.05f, fieldSize * 0.15f);
            particles[i].startColor = new Color(1f, 1f, 1f, fogDensity);
        }

        fogParticleSystem.SetParticles(particles, particles.Length);
    }

    private void Update()
    {
        if (audioAnalyzer == null) return;

        // Get audio values
        bassSamples.Enqueue(audioAnalyzer.Bass);
        midsSamples.Enqueue(audioAnalyzer.Mids);
        highsSamples.Enqueue(audioAnalyzer.Highs);

        if (bassSamples.Count > sampleSize / 5) bassSamples.Dequeue();
        if (midsSamples.Count > sampleSize) midsSamples.Dequeue();
        if (highsSamples.Count > sampleSize / 2) highsSamples.Dequeue();

        float bassValue = bassSamples.Average();
        float midsValue = midsSamples.Average();
        float highsValue = highsSamples.Average();
        // Calculate average audio value
        float avgValue = (bassValue + midsValue + highsValue) / 3f;
        float threshold = 0.6f * (bassValue + midsValue + highsValue);

        // For the central star (index 0), determine dominant frequency band
        if (bassValue > threshold)
        {
            centerStarGradient = lowToneGradient;
        }
        else if (highsValue > threshold)
        {
            centerStarGradient = highToneGradient;
        }
        else if (midsValue > threshold)
        {
            centerStarGradient = midToneGradient;
        }

        // Update each particle
        for (int i = 0; i < stars.Length; i++)
        {
            if (i == 0)
            {
                float colorValue = colorOffsets[0] + (avgValue * colorChangeSpeed * Time.deltaTime);
                // Update size/brightness based on avg
                float sizeModifier = 0.8f + (avgValue * brightnessScale);
                stars[i].startSize = initialSizes[i] * sizeModifier;
                var flameMain = flameParticleSystem.main;
                Color currentColor = flameMain.startColor.color;
                Color targetColor = centerStarGradient.Evaluate(colorValue);
                flameMain.startColor = Color.Lerp(currentColor, targetColor, 0.01f);
                stars[i].startColor = Color.Lerp(currentColor, targetColor, 0.01f);
                stars[i].startSize = avgValue * 10f;
            }
            else
            {
                // Other stars use their assigned frequency band
                int gradientIndex = i % 3;
                float colorValue = colorOffsets[i];

                if (gradientIndex == 0)
                {
                    colorValue += (highsValue * colorChangeSpeed * Time.deltaTime);
                    stars[i].startColor = highToneGradient.Evaluate(colorValue % 1f);

                    // Update size/brightness based on high frequencies
                    float sizeModifier = 1.0f + (highsValue * brightnessScale);
                    stars[i].startSize = initialSizes[i] * sizeModifier;
                }
                else if (gradientIndex == 1)
                {
                    colorValue += (midsValue * colorChangeSpeed * Time.deltaTime);
                    stars[i].startColor = midToneGradient.Evaluate(colorValue % 1f);
                    // Update size/brightness based on med frequencies
                    float sizeModifier = 1.0f + (midsValue * brightnessScale);
                    stars[i].startSize = initialSizes[i] * sizeModifier;
                }
                else
                {
                    colorValue += (bassValue * colorChangeSpeed * Time.deltaTime);
                    stars[i].startColor = lowToneGradient.Evaluate(colorValue % 1f);
                    // Update size/brightness based on bass frequencies
                    float sizeModifier = 1.0f + (bassValue * brightnessScale);
                    stars[i].startSize = initialSizes[i] * sizeModifier;
                }
            }

        }

        // Apply the particle changes
        particleSystem.SetParticles(stars, stars.Length);
        UpdateFogEffects(bassValue, midsValue, highsValue);
    }

    private void UpdateFogEffects(float bass, float mids, float highs)
    {
        if (fogParticleSystem == null) return;

        var particles = new ParticleSystem.Particle[fogParticleCount];
        fogParticleSystem.GetParticles(particles);

        // Update fog particles based on nearby star brightness
        for (int i = 0; i < particles.Length; i++)
        {
            Vector3 fogPos = particles[i].position;
            Color fogColor = Color.black;

            // Sample nearby stars and accumulate their influence
            for (int j = 0; j < stars.Length; j++)
            {
                float distance = Vector3.Distance(fogPos, stars[j].position);
                if (distance < stars[j].startSize * 2f)
                {
                    // Add star's contribution to fog color
                    float influence = 1f - (distance / (stars[j].startSize * 2f));
                    influence *= fogScatteringIntensity;
                    fogColor += (Color)stars[j].startColor * influence;
                }
            }

            // Apply the accumulated color to the fog particle
            particles[i].startColor = new Color(
                fogColor.r,
                fogColor.g,
                fogColor.b,
                fogDensity * (1f + (bass * 0.5f)) // Pulse fog density with bass
            );
        }

        fogParticleSystem.SetParticles(particles, particles.Length);
    }
}