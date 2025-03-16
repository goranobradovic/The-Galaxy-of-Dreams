using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Networking;
using UnityEngine.InputSystem;
using SFB;

public class VisualizerController : MonoBehaviour
{
    [Header("Component References")]
    public AudioAnalyzer audioAnalyzer;
    public StarfieldGenerator starfieldGenerator;
    public Camera mainCamera;

    [Header("Camera Movement")]
    public Vector2 cameraRotationSpeed = new Vector2(1f, 1f);
    public float cameraWobbleAmount = 0.1f;

    [Header("UI Elements")]
    public Button loadAudioButton;
    public Slider volumeSlider;

    private Vector3 initialCameraPosition;
    private Quaternion initialCameraRotation;


    [Header("Camera Path")]
    public float ellipseSpeed = 0.1f;
    public float ellipseRelativeWidth = 0.6f;
    private float ellipseWidth = 5f;
    private float ellipseHeight = 200f;
    private int direction = 1;
    private float starfieldRadius = 0f;

    [Header("Depth Particle System")]
    public ParticleSystem depthParticleSystem;
    private Material depthParticleMaterial;

    private InputAction resetCameraAction;
    private InputAction moveForwardAction;
    private InputAction moveBackwardAction;
    private InputAction rotateLeftAction;
    private InputAction rotateRightAction;

    // Movement inertia variables
    private float forwardVelocity = 0f;
    private float rotationVelocity = 0f;
    private float maxForwardSpeed = 2f;
    private float maxRotationSpeed = .05f;
    private float accelerationTime = 5f;
    private float decelerationTime = 5f;

    // Add new input actions for vertical movement
    private InputAction moveUpAction;
    private InputAction moveDownAction;
    private float verticalVelocity = 0f;
    private float currentVerticalAngle = 0f; // Track vertical angle

    // Add near the top with other input actions
    private InputAction openFileAction;

    // Add these fields near the top with other private variables
    private bool isResetting = false;
    private float resetTransitionDuration = 3f;
    const int sampleSize = 50;
    private Queue<float> speedSamples;
    

    private void Awake()
    {
        // Create a new InputAction for resetting the camera
        resetCameraAction = new InputAction(type: InputActionType.Button, binding: "<Keyboard>/r");
        resetCameraAction.performed += ctx => ResetCameraPosition();
        resetCameraAction.Enable();

        // Create movement input actions
        moveForwardAction = new InputAction(type: InputActionType.Button, binding: "<Keyboard>/w");
        moveBackwardAction = new InputAction(type: InputActionType.Button, binding: "<Keyboard>/s");
        rotateLeftAction = new InputAction(type: InputActionType.Button, binding: "<Keyboard>/a");
        rotateRightAction = new InputAction(type: InputActionType.Button, binding: "<Keyboard>/d");

        moveForwardAction.Enable();
        moveBackwardAction.Enable();
        rotateLeftAction.Enable();
        rotateRightAction.Enable();

        // Create vertical movement input actions
        moveUpAction = new InputAction(type: InputActionType.Button, binding: "<Keyboard>/q");
        moveDownAction = new InputAction(type: InputActionType.Button, binding: "<Keyboard>/e");
        
        moveUpAction.Enable();
        moveDownAction.Enable();

        // Create a new InputAction for quitting the application
        var quitAction = new InputAction(type: InputActionType.Button, binding: "<Keyboard>/escape");
        quitAction.performed += ctx => Application.Quit();
        quitAction.Enable();

        // Create open file action
        openFileAction = new InputAction(type: InputActionType.Button, binding: "<Keyboard>/o");
        openFileAction.performed += ctx => OpenFileDialog();
        openFileAction.Enable();
    }

    private void OnDestroy()
    {
        // Disable and dispose of all InputActions
        resetCameraAction.Disable();
        moveForwardAction.Disable();
        moveBackwardAction.Disable();
        rotateLeftAction.Disable();
        rotateRightAction.Disable();
        moveUpAction.Disable();
        moveDownAction.Disable();
        openFileAction.Disable();

        resetCameraAction.Dispose();
        moveForwardAction.Dispose();
        moveBackwardAction.Dispose();
        rotateLeftAction.Dispose();
        rotateRightAction.Dispose();
        moveUpAction.Dispose();
        moveDownAction.Dispose();
        openFileAction.Dispose();
    }

    private void Start()
    {
        speedSamples = new Queue<float>(sampleSize);
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = Color.black;

            // Disable fog if it's enabled
            RenderSettings.fog = false;
        }

        // Set ellipse size to 90% of the starfield radius
        if (starfieldGenerator != null)
        {
            starfieldRadius = starfieldGenerator.fieldSize * 1.1f;
        }

        // Randomize initial camera position on the outer edge of the starfield
        ResetCameraPosition();

        // Setup UI callbacks
        if (loadAudioButton != null)
            loadAudioButton.onClick.AddListener(OpenAudioFileBrowser);

        if (volumeSlider != null)
        {
            volumeSlider.value = AudioListener.volume;
            volumeSlider.onValueChanged.AddListener(SetVolume);
        }

    }

    private void Update()
    {
        if (audioAnalyzer == null || mainCamera == null) return;

        // Get audio values
        float bass = audioAnalyzer.Bass;
        float mids = audioAnalyzer.Mids;
        float highs = audioAnalyzer.Highs;

        // Handle manual camera movement
        HandleManualCameraMovement();

        // Calculate the center direction
        Vector3 toCenter = Vector3.zero - mainCamera.transform.position;
        float distanceToCenter = toCenter.magnitude;

        // Calculate speed based on distance to center
        float speed = Mathf.Lerp(ellipseSpeed * 0.5f, 0f, 1f - (distanceToCenter / (starfieldRadius * 0.25f)));
        speed *= Time.deltaTime;


        // Calculate forward movement based on audio levels
        float audioSum = bass + mids + highs;

        float currentSpeed = (audioSum - 2f) * .75f;
        if (speedSamples.Count >= sampleSize)
        {
            speedSamples.Dequeue();
        }
        speedSamples.Enqueue(currentSpeed);

        float forwardSpeed = speedSamples.Average();

        speed +=  speed * forwardSpeed;


        // Move towards the center
        Vector3 movement = toCenter.normalized * speed;
        mainCamera.transform.position += movement;
        
        if (mainCamera.transform.position.magnitude < starfieldRadius * 0.25f)
        {
            ResetCameraPosition();
        }

        // Point camera towards the center
        mainCamera.transform.rotation = Quaternion.LookRotation(toCenter, Vector3.forward);

        // Apply camera effects based on audio
        ApplyCameraEffects(bass, mids, highs);
    }

    private void HandleManualCameraMovement()
    {
        float radius = mainCamera.transform.position.magnitude;
        float horizontalAngle = Mathf.Atan2(mainCamera.transform.position.y, mainCamera.transform.position.x);
        bool positionChanged = false;

        // Calculate acceleration and deceleration rates
        float forwardAcceleration = maxForwardSpeed / accelerationTime;
        float forwardDeceleration = maxForwardSpeed / decelerationTime;
        float rotationAcceleration = maxRotationSpeed / accelerationTime;
        float rotationDeceleration = maxRotationSpeed / decelerationTime;

        // Handle vertical movement
        if (moveUpAction.IsPressed())
        {
            verticalVelocity = Mathf.Min(verticalVelocity + rotationAcceleration * Time.deltaTime * .02f, maxRotationSpeed);
        }
        else if (moveDownAction.IsPressed())
        {
            verticalVelocity = Mathf.Max(verticalVelocity - rotationAcceleration * Time.deltaTime * .02f, -maxRotationSpeed);
        }
        else
        {
            // Decelerate vertical movement
            if (verticalVelocity > 0)
            {
                verticalVelocity = Mathf.Max(0, verticalVelocity - rotationDeceleration * .02f * Time.deltaTime);
            }
            else if (verticalVelocity < 0)
            {
                verticalVelocity = Mathf.Min(0, verticalVelocity + rotationDeceleration * .02f * Time.deltaTime);
            }
        }

        // Apply vertical movement
        if (Mathf.Abs(verticalVelocity) > 0.0000001f)
        {
            currentVerticalAngle += verticalVelocity * ellipseSpeed * Time.deltaTime;
            // Clamp vertical angle to limit height
            currentVerticalAngle = Mathf.Clamp(currentVerticalAngle, -Mathf.PI * 0.2f, Mathf.PI * 0.2f);
            positionChanged = true;
        }

        // Handle forward/backward velocity
        if (moveForwardAction.IsPressed())
        {
            forwardVelocity = Mathf.Min(forwardVelocity + forwardAcceleration * Time.deltaTime * .02f, maxForwardSpeed);
        }
        else if (moveBackwardAction.IsPressed())
        {
            forwardVelocity = Mathf.Max(forwardVelocity - forwardAcceleration * Time.deltaTime * .02f, -maxForwardSpeed);
        }
        else
        {
            // Decelerate when no input
            if (forwardVelocity > 0)
            {
                forwardVelocity = Mathf.Max(0, forwardVelocity - forwardDeceleration * .02f * Time.deltaTime);
            }
            else if (forwardVelocity < 0)
            {
                forwardVelocity = Mathf.Min(0, forwardVelocity + forwardDeceleration * .02f * Time.deltaTime);
            }
        }

        // Handle rotation velocity
        if (rotateLeftAction.IsPressed())
        {
            rotationVelocity = Mathf.Min(rotationVelocity + rotationAcceleration * Time.deltaTime * .002f, maxRotationSpeed);
        }
        else if (rotateRightAction.IsPressed())
        {
            rotationVelocity = Mathf.Max(rotationVelocity - rotationAcceleration * Time.deltaTime * .002f, -maxRotationSpeed);
        }
        else
        {
            // Decelerate when no input
            if (rotationVelocity > 0)
            {
                rotationVelocity = Mathf.Max(0, rotationVelocity - rotationDeceleration* .002f * Time.deltaTime);
            }
            else if (rotationVelocity < 0)
            {
                rotationVelocity = Mathf.Min(0, rotationVelocity + rotationDeceleration * .002f * Time.deltaTime);
            }
        }

        // Apply velocities if they're not zero
        if (Mathf.Abs(forwardVelocity) > 0.0001f)
        {
            radius = Mathf.Clamp(
                radius - forwardVelocity * ellipseSpeed * Time.deltaTime * 50f,
                starfieldRadius * 0.25f,
                starfieldRadius * 1.1f
            );
            positionChanged = true;
        }

        if (Mathf.Abs(rotationVelocity) > 0.0000001f)
        {
            horizontalAngle += rotationVelocity * ellipseSpeed * Time.deltaTime;
            positionChanged = true;
        }

        // Update position if any movement occurred
        if (positionChanged)
        {
            // Calculate position using spherical coordinates
            float horizontalRadius = radius * Mathf.Cos(currentVerticalAngle);
            float newX = horizontalRadius * Mathf.Cos(horizontalAngle);
            float newY = horizontalRadius * Mathf.Sin(horizontalAngle);
            float newZ = radius * Mathf.Sin(currentVerticalAngle);

            Vector3 newPosition = new Vector3(newX, newY, newZ);
            mainCamera.transform.position = newPosition;

            Vector3 newToCenter = Vector3.zero - mainCamera.transform.position;
            initialCameraRotation = Quaternion.LookRotation(newToCenter, Vector3.up);
            mainCamera.transform.rotation = initialCameraRotation;
        }
    }

    private void ResetCameraPosition()
    {
        if (isResetting) return; // Prevent multiple resets at once
        
        // Reset vertical angle when resetting position
        currentVerticalAngle = 0f;
        verticalVelocity = 0f;

        // Calculate new position
        float angle = Random.Range(0f, Mathf.PI * 2f);
        float x = starfieldRadius * Mathf.Cos(angle);
        float y = starfieldRadius * Mathf.Sin(angle);
        Vector3 targetPosition = new Vector3(x, y, 0);

        // Start transition coroutine
        StartCoroutine(SmoothResetPosition(targetPosition));
    }

    private IEnumerator SmoothResetPosition(Vector3 targetPosition)
    {
        isResetting = true;
        
        Vector3 startPosition = mainCamera.transform.position;
        Quaternion startRotation = mainCamera.transform.rotation;
        float elapsedTime = 0f;

        while (elapsedTime < resetTransitionDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / resetTransitionDuration;
            
            // Use smooth step for more pleasing motion
            float smoothT = t * t * (3f - 2f * t);
            
            // Interpolate position
            mainCamera.transform.position = Vector3.Lerp(startPosition, targetPosition, smoothT);
            
            // Calculate and interpolate rotation
            Vector3 toCenter = Vector3.zero - mainCamera.transform.position;
            Quaternion targetRotation = Quaternion.LookRotation(toCenter, Vector3.forward);
            mainCamera.transform.rotation = Quaternion.Slerp(startRotation, targetRotation, smoothT);

            yield return null;
        }

        // Ensure we end up exactly at the target
        mainCamera.transform.position = targetPosition;
        Vector3 finalToCenter = Vector3.zero - targetPosition;
        mainCamera.transform.rotation = Quaternion.LookRotation(finalToCenter, Vector3.forward);
        
        // Update the stored positions
        initialCameraPosition = targetPosition;
        initialCameraRotation = mainCamera.transform.rotation;
        
        isResetting = false;
    }

    private void ApplyCameraEffects(float bass, float mids, float highs)
    {
        // Apply forward movement based on wobble magnitude
        Vector3 forwardDirection = mainCamera.transform.forward;
        float wobbleMagnitude = cameraWobbleAmount * bass;
        mainCamera.transform.position += forwardDirection * wobbleMagnitude;
        
    }

    private void SetVolume(float volume)
    {
        AudioListener.volume = volume;
    }

    private void OpenAudioFileBrowser()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        // This would use a platform-specific file browser
        // For simplicity in the editor, we'll use a hardcoded path
        // In a real implementation, you'd use a proper file browser API
        
        Debug.Log("Opening file browser (simulated in editor)");
        StartCoroutine(SimulateFileSelection());
#endif
    }

    private IEnumerator SimulateFileSelection()
    {
        // In a real app, this would come from a file browser
        string path = Application.streamingAssetsPath + "/sample.mp3";

        yield return StartCoroutine(LoadAudioFromPath(path));
    }

    private IEnumerator LoadAudioFromPath(string path)
    {
        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip("file://" + path, AudioType.MPEG))
        {
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                if (clip != null && audioAnalyzer != null)
                {
                    audioAnalyzer.ChangeAudioClip(clip);
                    Debug.Log("Audio loaded successfully: " + path);
                }
            }
            else
            {
                Debug.LogError("Error loading audio: " + www.error);
            }
        }
    }

    private void OpenFileDialog()
    {
        var extensions = new [] {
            new SFB.ExtensionFilter("Audio Files", "mp3"),
            new SFB.ExtensionFilter("All Files", "*"),
        };
        
        string[] paths = SFB.StandaloneFileBrowser.OpenFilePanel("Open Audio File", "", extensions, false);
        
        if (paths != null && paths.Length > 0)
        {
            StartCoroutine(LoadAudioFromPath(paths[0]));
        }
    }
}