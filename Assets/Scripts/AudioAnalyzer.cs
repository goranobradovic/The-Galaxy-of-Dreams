using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class AudioAnalyzer : MonoBehaviour
{
    [Header("Audio Settings")]
    public AudioSource audioSource;
    public AudioClip musicClip;
    public bool loadFromFile = false;
    public string filePath = "";
    
    [Header("Analysis Settings")]
    public int sampleSize = 1024; // Must be a power of 2
    public int bandCount = 8;     // Number of frequency bands
    
    [Range(0.0f, 10.0f)]
    public float amplitudeMultiplier = 2.0f;
    
    [Header("Visualization Parameters")]
    public float bassImpact = 1.0f;
    public float midImpact = 0.7f;
    public float highImpact = 0.5f;
    
    // Arrays to store audio data
    private float[] samples;
    private float[] freqBands;
    private float[] bandBuffer;
    private float[] bufferDecrease;
    
    // Properties accessible to other scripts
    public float[] FrequencyBands => freqBands;
    public float[] BandBuffer => bandBuffer;
    
    // Specific band getters for easy access
    public float Bass => freqBands[0] + freqBands[1];
    public float Mids => freqBands[2] + freqBands[3] + freqBands[4];
    public float Highs => freqBands[5] + freqBands[6] + freqBands[7];
    
    private void Start()
    {
        // Initialize arrays
        samples = new float[sampleSize];
        freqBands = new float[bandCount];
        bandBuffer = new float[bandCount];
        bufferDecrease = new float[bandCount];
        
        // Create AudioSource if not assigned
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        
        // Load audio file if specified
        if (loadFromFile && !string.IsNullOrEmpty(filePath))
        {
            StartCoroutine(LoadAudioClip(filePath));
        }
        else if (musicClip != null)
        {
            audioSource.clip = musicClip;
            audioSource.Play();
        }
    }
    
    private void Update()
    {
        if (audioSource.isPlaying)
        {
            // Get spectrum data
            GetSpectrumData();
            
            // Process frequency bands
            MakeFrequencyBands();
            
            // Create smoothed buffer values for visualization
            CreateBandBuffer();
        }
    }
    
    private void GetSpectrumData()
    {
        audioSource.GetSpectrumData(samples, 0, FFTWindow.Blackman);
    }
    
    private void MakeFrequencyBands()
    {
        // Define frequency ranges
        int[] sampleCount = new[] { 2, 4, 8, 16, 32, 64, 128, 256 }; // Must sum to < sampleSize/2
        
        int sampleIndex = 0;
        
        for (int i = 0; i < bandCount; i++)
        {
            float average = 0;
            int sampleCountInBand = sampleCount[i];
            
            for (int j = 0; j < sampleCountInBand; j++)
            {
                if (sampleIndex < sampleSize)
                {
                    average += samples[sampleIndex] * (sampleIndex + 1);
                    sampleIndex++;
                }
            }
            
            average /= sampleCountInBand;
            freqBands[i] = average * amplitudeMultiplier;
        }
    }
    
    private void CreateBandBuffer()
    {
        for (int i = 0; i < bandCount; i++)
        {
            // If current value is higher than buffer, use current value
            if (freqBands[i] > bandBuffer[i])
            {
                bandBuffer[i] = freqBands[i];
                bufferDecrease[i] = 0.005f;
            }
            
            // Otherwise, decrease buffer value gradually
            if (freqBands[i] < bandBuffer[i])
            {
                bandBuffer[i] -= bufferDecrease[i];
                bufferDecrease[i] *= 1.2f; // Increase falloff speed over time
            }
            
            // Ensure buffer doesn't go below actual value
            if (bandBuffer[i] < freqBands[i])
                bandBuffer[i] = freqBands[i];
        }
    }
    
    private IEnumerator LoadAudioClip(string path)
    {
        // For WebGL builds use WWW
        #if UNITY_WEBGL
        using (UnityEngine.Networking.UnityWebRequest www = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip("file://" + path, AudioType.MPEG))
        {
            yield return www.SendWebRequest();
            
            if (www.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                audioSource.clip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(www);
                audioSource.Play();
            }
            else
            {
                Debug.LogError("Error loading audio file: " + www.error);
            }
        }
        #else
        // For standalone builds use System.IO
        string fullPath = System.IO.Path.Combine(Application.streamingAssetsPath, path);
        
        if (System.IO.File.Exists(fullPath))
        {
            using (var www = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip("file://" + fullPath, AudioType.MPEG))
            {
                yield return www.SendWebRequest();
                
                if (www.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    audioSource.clip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(www);
                }
                else 
                {
                    Debug.LogError("Error loading audio file: " + www.error);
                }
            }
            audioSource.Play();
        }
        else
        {
            Debug.LogError("File not found: " + fullPath);
        }
        #endif
    }
    
    // Method to change the current audio clip at runtime
    public void ChangeAudioClip(AudioClip newClip)
    {
        if (newClip != null)
        {
            audioSource.Stop();
            audioSource.clip = newClip;
            audioSource.Play();
        }
    }
    
    // Visualize the frequency bands in the editor (for debugging)
    private void OnDrawGizmos()
    {
        if (!Application.isPlaying || freqBands == null) return;
        
        float width = 0.5f;
        for (int i = 0; i < bandCount; i++)
        {
            Gizmos.color = Color.Lerp(Color.blue, Color.red, (float)i / bandCount);
            Gizmos.DrawCube(
                new Vector3(transform.position.x + i * width, transform.position.y + freqBands[i]/2, transform.position.z),
                new Vector3(width * 0.9f, freqBands[i], width * 0.9f)
            );
        }
    }
}