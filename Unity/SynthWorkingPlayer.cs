using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using AK.Wwise;
using UnityEngine.Networking;
using System.IO;
using static SynthWorkingPlayer;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class SynthWorkingPlayer : MonoBehaviour
{
    public bool executeOnStart = true;
    private bool isPlaying = false;

    public AK.Wwise.Event wwiseEvent;
    public float delayBetweenEvents = 0.25f;
    public AK.Wwise.Event secondWwiseEvent;
    public List<CSVFile> csvFiles = new List<CSVFile>();
    public int numberOfEvents = 1;
    public float duration = 1f;
    public AnimationCurve timeCurve;

    public AK.Wwise.RTPC rtpc;
    public string csvFileName = "";
    public float delayInSeconds = 0.1f;
    public float currentRTPCValue = 0f;
    public List<KeyValuePair<float, float>> animationData = new List<KeyValuePair<float, float>>();
    public List<float> interpolatedValues = new List<float>();

    public AK.Wwise.RTPC rtpc2;
    public string csvFileName2 = "";
    public float delayInSeconds2 = 0.1f;
    public float currentRTPCValue2 = 0f;
    public List<KeyValuePair<float, float>> animationData2 = new List<KeyValuePair<float, float>>();
    public List<float> interpolatedValues2 = new List<float>();

    public Camera mainCamera;
    public GameObject targetGameObject;
    public Vector3 minScale = new Vector3(1, 1, 1);
    public Vector3 maxScale = new Vector3(2, 2, 2);

    public RandomShapeGenerator shapeGenerator;

    public Color primaryColor = Color.white;
    [Range(0, 1)] public float colorVariance = 0.1f;

    // New fields for auto play
    public bool autoPlay = false;
    public float autoPlayDelay = 2.0f;

    private float[] eventTimes;
    private int nextEventIndex = 0;
    private float elapsedTime = 0f;
    [SerializeField] private bool enableDebugLogs = false;
    [System.Serializable]
    public class CSVData
    {
        public string Parameter;
        public float Value;
        public float MinRandomRange;
        public float MaxRandomRange;
    }
    [System.Serializable]
    public class CSVFile
    {
        public string fileName;
        [HideInInspector] public bool loaded = false;
        public bool loadNow = false;
        public List<CSVData> data = new List<CSVData>();
    }

    void Start()
    {
        if (executeOnStart)
        {
            CalculateEventTimes();
            if (delayInSeconds > 0) { StartCoroutine(LoadAndPlayCSV()); }

            foreach (CSVFile file in csvFiles)
            {
                if (file.loadNow && !file.loaded)
                {
                    LoadCSV(file);
                    DeselectOtherFiles(file);
                }
            }
            Play();
        }
        // Start auto play if enabled
        if (autoPlay)
        {
            StartCoroutine(AutoPlayPresets());
        }

    }
    void Update()
    {
        if (eventTimes != null)
        {
            elapsedTime += Time.deltaTime;
            while (nextEventIndex < eventTimes.Length && elapsedTime >= eventTimes[nextEventIndex])
            { TriggerWwiseEvent(); nextEventIndex++; }
        }

        foreach (CSVFile file in csvFiles)
        {
            if (file.loadNow && !file.loaded)
            {
                { LoadCSV(file); }
                DeselectOtherFiles(file);
            }
        }
        if (Input.GetKeyDown(KeyCode.Keypad0))
        {
            if (isPlaying)
            {
                Stop();
            }
            else
            {
                Play();
            }
            isPlaying = !isPlaying;
        }
        if (targetGameObject != null)
        {
            // Calculate direction to target
            Vector3 directionToTarget = targetGameObject.transform.position - transform.position;
            directionToTarget.Normalize();

            // Rotate towards target
            transform.rotation = Quaternion.LookRotation(directionToTarget);

            // Move towards target
            transform.position += directionToTarget * Time.deltaTime;
        }
    }

    private void DeselectOtherFiles(CSVFile selectedFile)
    {
        foreach (CSVFile file in csvFiles)
        {
            if (file != selectedFile)
            {
                file.loadNow = false;
                file.data.Clear();
                file.loaded = false;
            }
        }
    }

    void CalculateEventTimes()
    {
        eventTimes = new float[numberOfEvents];
        float totalCurveTime = 0f;
        for (int i = 0; i < numberOfEvents; i++)
        {
            float t = (float)i / (numberOfEvents - 1);
            float inverseT = 1f - t;
            float curveValue = timeCurve.Evaluate(inverseT);
            eventTimes[i] = totalCurveTime + (curveValue * duration / (numberOfEvents - 1));
            totalCurveTime = eventTimes[i];
        }
    }
    private void LoadCSV(CSVFile csvFile)
    {
        if (string.IsNullOrEmpty(csvFile.fileName))
        {
            return;
        }
        string filePath = Path.Combine(Application.streamingAssetsPath, csvFile.fileName);
        if (!File.Exists(filePath))
        {
            foreach (CSVFile file in csvFiles)
            {
                if (file != csvFile)
                {
                    file.loadNow = false;
                    file.data.Clear();
                    file.loaded = false;
                }
            }
        }
        ReadCSV(filePath, csvFile);
        csvFile.loaded = true;
        csvFile.loadNow = true; // Marque le fichier comme charg�
        SendValuesToWwise(csvFile);
#if UNITY_EDITOR
        EditorUtility.SetDirty(this);
#endif
    }

    private void LoadCSV2(CSVFile csvFile)
    {
        if (string.IsNullOrEmpty(csvFile.fileName))
        {
            return;
        }
        string filePath = Path.Combine(Application.streamingAssetsPath, csvFile.fileName);
        if (!File.Exists(filePath))
            foreach (CSVFile file in csvFiles)
            {
                if (file != csvFile)
                {
                    file.loadNow = false;
                    file.data.Clear();
                    file.loaded = false;
                }
            }
        ReadCSV(filePath, csvFile);
        csvFile.loaded = true;
        csvFile.loadNow = true; // Marque le fichier comme charg�
        SendValuesToWwise(csvFile);
#if UNITY_EDITOR
        EditorUtility.SetDirty(this);
#endif
    }

    private void ReadCSV(string filePath, CSVFile csvFile)
    {
        var existingData = new HashSet<string>();
        string[] rows = File.ReadAllLines(filePath);

        foreach (string row in rows)
        {
            string[] columns = row.Split(',');
            if (columns.Length >= 5) // V�rifier qu'il y a assez de colonnes
            {
                string parameter = columns[1].Trim();
                string valueStr = columns[2].Trim();
                float minRandomRange = float.Parse(columns[3].Trim(), CultureInfo.InvariantCulture);
                float maxRandomRange = float.Parse(columns[4].Trim(), CultureInfo.InvariantCulture);

                // Utiliser une cl� unique pour v�rifier les doublons
                string uniqueKey = $"{parameter}-{valueStr}-{minRandomRange}-{maxRandomRange}";

                if (!existingData.Contains(uniqueKey) && float.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                {
                    existingData.Add(uniqueKey); // Ajouter la cl� unique
                    CSVData data = new CSVData
                    {
                        Parameter = parameter,
                        Value = value,
                        MinRandomRange = minRandomRange,
                        MaxRandomRange = maxRandomRange
                    };
                    csvFile.data.Add(data);
                }
                else if (enableDebugLogs)
                {
                    Debug.LogWarning($"Ligne dupliqu�e ou incorrecte ignor�e: {row}");
                }
            }
            else if (enableDebugLogs)
            {
                Debug.LogWarning($"Format inattendu ou ligne incompl�te: {row}");
            }
        }
    }

    private void ProcessCSV(string csvText)
    {
        string[] lines = csvText.Split('\n');
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (enableDebugLogs) Debug.Log($"Reading line: {line}");
            string[] values = line.Trim().Split('_');
            if (values.Length >= 2)
            {
                if (float.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float time) &&
                    float.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float rtpcValue))
                {
                    animationData.Add(new KeyValuePair<float, float>(time, rtpcValue));
                    if (enableDebugLogs) Debug.Log($"Loaded CSV line - Time: {time}, RTPC Value: {rtpcValue}");
                }
                else { if (enableDebugLogs) Debug.LogWarning($"Failed to parse values - Time: {values[0]}, RTPC Value: {values[1]}"); }
            }
            else { if (enableDebugLogs) Debug.LogWarning($"Unexpected line format: {line}"); }
        }
        if (enableDebugLogs) Debug.Log($"Total lines loaded: {animationData.Count}");
    }
    private void ProcessCSV2(string csvText)
    {
        string[] lines = csvText.Split('\n');
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (enableDebugLogs) Debug.Log($"Reading line: {line}");
            string[] values = line.Trim().Split('_');
            if (values.Length >= 2)
            {
                if (float.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float time) &&
                    float.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float rtpcValue))
                {
                    animationData2.Add(new KeyValuePair<float, float>(time, rtpcValue));
                    if (enableDebugLogs) Debug.Log($"Loaded CSV line - Time: {time}, RTPC Value: {rtpcValue}");
                }
                else { if (enableDebugLogs) Debug.LogWarning($"Failed to parse values - Time: {values[0]}, RTPC Value: {values[1]}"); }
            }
            else { if (enableDebugLogs) Debug.LogWarning($"Unexpected line format: {line}"); }
        }
        if (enableDebugLogs) Debug.Log($"Total lines loaded: {animationData2.Count}");
    }
    private void CalculateInterpolatedValues()
    {
        interpolatedValues.Clear();
        if (enableDebugLogs) Debug.Log($"animationData.Count: {animationData.Count}");
        for (int i = 0; i < animationData.Count - 1; i++)
        {
            float startTime = animationData[i].Key;
            float endTime = animationData[i + 1].Key;
            float startValue = animationData[i].Value;
            float endValue = animationData[i + 1].Value;
            if (enableDebugLogs) Debug.Log($"startTime: {startTime}, endTime: {endTime}, startValue: {startValue}, endValue: {endValue}");
            if (Mathf.Sign(startValue) != Mathf.Sign(endValue))
            {
                float midTime = (startTime + endTime) / 2f;
                float midValue = Mathf.Abs(startValue) < Mathf.Abs(endValue) ? startValue : endValue;
                InterpolateSegment(startTime, midTime, startValue, midValue);
                InterpolateSegment(midTime, endTime, midValue, endValue);
            }
            else { InterpolateSegment(startTime, endTime, startValue, endValue); }
        }
    }
    private void InterpolateSegment(float startTime, float endTime, float startValue, float endValue)
    {
        int steps = Mathf.CeilToInt((endTime - startTime) * 100);
        for (int j = 0; j <= steps; j++)
        {
            float t = (float)j / steps;
            float interpolatedValue = Mathf.Lerp(startValue, endValue, t);
            interpolatedValues.Add(interpolatedValue);
            if (enableDebugLogs) Debug.Log($"Interpolated Value at {startTime + (t * (endTime - startTime))} ms: {interpolatedValue}");
        }
    }
    public void PlayRTPCCurve()
    {
        if (enableDebugLogs) Debug.Log("Playing RTPC Animation...");
        StartCoroutine(LoadAndPlayCSV());
    }
    private void CalculateInterpolatedValues2()
    {
        interpolatedValues2.Clear();
        if (enableDebugLogs) Debug.Log($"animationData.Count: {animationData2.Count}");
        for (int i = 0; i < animationData2.Count - 1; i++)
        {
            float startTime = animationData2[i].Key;
            float endTime = animationData2[i + 1].Key;
            float startValue = animationData2[i].Value;
            float endValue = animationData2[i + 1].Value;
            if (enableDebugLogs) Debug.Log($"startTime: {startTime}, endTime: {endTime}, startValue: {startValue}, endValue: {endValue}");
            if (Mathf.Sign(startValue) != Mathf.Sign(endValue))
            {
                float midTime = (startTime + endTime) / 2f;
                float midValue = Mathf.Abs(startValue) < Mathf.Abs(endValue) ? startValue : endValue;
                InterpolateSegment2(startTime, midTime, startValue, midValue);
                InterpolateSegment2(midTime, endTime, midValue, endValue);
            }
            else { InterpolateSegment2(startTime, endTime, startValue, endValue); }
        }
    }
    private void InterpolateSegment2(float startTime, float endTime, float startValue, float endValue)
    {
        int steps = Mathf.CeilToInt((endTime - startTime) * 100);
        for (int j = 0; j <= steps; j++)
        {
            float t = (float)j / steps;
            float interpolatedValue = Mathf.Lerp(startValue, endValue, t);
            interpolatedValues2.Add(interpolatedValue);
            if (enableDebugLogs) Debug.Log($"Interpolated Value at {startTime + (t * (endTime - startTime))} ms: {interpolatedValue}");
        }
    }
    public void PlayRTPCCurve2()
    {
        if (enableDebugLogs) Debug.Log("Playing RTPC Animation...");
        StartCoroutine(LoadAndPlayCSV2());
    }
    private void SendValuesToWwise(CSVFile csvFile)
    {
        foreach (CSVData data in csvFile.data)
        {
            float randomizedValue = UnityEngine.Random.Range(data.Value + data.MinRandomRange, data.Value + data.MaxRandomRange);
            string formattedValue = randomizedValue.ToString("0.000000", CultureInfo.InvariantCulture);
            AkSoundEngine.SetRTPCValue(data.Parameter, float.Parse(formattedValue, CultureInfo.InvariantCulture));
        }
    }


    private void ChangeCameraBackgroundColor()
    {
        if (mainCamera != null)
        {
            Color randomColor = GetRandomColorAroundPrimary(primaryColor, colorVariance);
            mainCamera.backgroundColor = randomColor;
        }
    }
    private void SetCameraBackgroundColorToGrey()
    {
        if (mainCamera != null)
        {
            Color greyColor = new Color(0.1f, 0.1f, 0.1f);
            mainCamera.backgroundColor = greyColor;
        }
    }
    private void SetObjectColorToGrey()
    {
        if (targetGameObject != null)
        {
            Renderer renderer = targetGameObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                Color greyColor = new Color(0.1f, 0.1f, 0.1f);
                renderer.material.color = greyColor;
            }
        }
    }
    private void ChangeGameObjectColor()
    {
        if (targetGameObject != null)
        {
            Color randomColor = GetRandomColorAroundPrimary(primaryColor, colorVariance);
            Renderer renderer = targetGameObject.GetComponent<Renderer>();
            renderer.material.color = randomColor;

        }
    }
    void ChangeScale()
    {
        if (targetGameObject != null)
        {
            float randomX = Random.Range(minScale.x, maxScale.x);
            float randomY = Random.Range(minScale.y, maxScale.y);
            float randomZ = Random.Range(minScale.z, maxScale.z);
            targetGameObject.transform.localScale = new Vector3(randomX, randomY, randomZ);
        }
    }

    void TriggerWwiseEvent()
    {
        if (wwiseEvent != null)
        {
            wwiseEvent.Post(gameObject);
            ChangeCameraBackgroundColor();
            ChangeGameObjectColor();
            ChangeScale();
            if (secondWwiseEvent != null)
            {
                StartCoroutine(TriggerSecondWwiseEventWithDelay());
            }
        }
    }


    private IEnumerator TriggerSecondWwiseEventWithDelay()
    {
        yield return new WaitForSeconds(delayBetweenEvents);
        if (secondWwiseEvent != null) { secondWwiseEvent.Post(gameObject); SetCameraBackgroundColorToGrey(); }
    }
    private IEnumerator LoadAndPlayCSV()
    {
        yield return new WaitForSeconds(delayInSeconds);
        if (!string.IsNullOrEmpty(csvFileName))
        {
            yield return LoadCSV();
            CalculateInterpolatedValues();
            yield return StartCoroutine(CurveRTPC());
        }
    }
    private IEnumerator LoadAndPlayCSV2()
    {
        yield return new WaitForSeconds(delayInSeconds2);
        if (!string.IsNullOrEmpty(csvFileName2))
        {
            yield return LoadCSV2();
            CalculateInterpolatedValues2();
            yield return StartCoroutine(CurveRTPC2());
        }
    }
    private IEnumerator LoadCSV()
    {
        animationData.Clear();
        if (!string.IsNullOrEmpty(csvFileName))
        {
            string csvFilePath = Path.Combine(Application.streamingAssetsPath, "Audio", csvFileName);
            if (csvFilePath.StartsWith("http://") || csvFilePath.StartsWith("https://"))
            {
                using (UnityWebRequest www = UnityWebRequest.Get(csvFilePath))
                {
                    yield return www.SendWebRequest();
                    if (www.result != UnityWebRequest.Result.Success)
                    { yield break; }
                    string csvText = www.downloadHandler.text;
                    ProcessCSV(csvText);
                }
            }
            else
            {
                if (File.Exists(csvFilePath))
                {
                    string csvText = File.ReadAllText(csvFilePath);
                    ProcessCSV(csvText);
                }
            }
        }
    }
    private IEnumerator LoadCSV2()
    {
        animationData2.Clear();
        if (!string.IsNullOrEmpty(csvFileName2))
        {
            string csvFilePath = Path.Combine(Application.streamingAssetsPath, "Audio", csvFileName2);
            if (csvFilePath.StartsWith("http://") || csvFilePath.StartsWith("https://"))
            {
                using (UnityWebRequest www = UnityWebRequest.Get(csvFilePath))
                {
                    yield return www.SendWebRequest();
                    if (www.result != UnityWebRequest.Result.Success)
                    { yield break; }
                    string csvText = www.downloadHandler.text;
                    ProcessCSV2(csvText);
                }
            }
            else
            {
                if (File.Exists(csvFilePath))
                {
                    string csvText = File.ReadAllText(csvFilePath);
                    ProcessCSV2(csvText);
                }
            }
        }
    }
    private IEnumerator CurveRTPC()
    {
        float startTime = Time.time;
        int index = 0;
        while (index < interpolatedValues.Count)
        {
            float currentTime = Time.time - startTime;
            rtpc.SetValue(gameObject, interpolatedValues[index]);
            currentRTPCValue = interpolatedValues[index];
            if (enableDebugLogs) Debug.Log($"Time: {currentTime}, RTPC Value: {currentRTPCValue}");
            index++; yield return null;
        }
        if (interpolatedValues.Count > 0)
        {
            rtpc.SetValue(gameObject, interpolatedValues[interpolatedValues.Count - 1]);
            currentRTPCValue = interpolatedValues[interpolatedValues.Count - 1];
            if (enableDebugLogs) Debug.Log($"Final RTPC Value: {currentRTPCValue}");
        }
    }
    private IEnumerator CurveRTPC2()
    {
        float startTime = Time.time;
        int index = 0;
        while (index < interpolatedValues2.Count)
        {
            float currentTime = Time.time - startTime;
            rtpc2.SetValue(gameObject, interpolatedValues2[index]);
            currentRTPCValue2 = interpolatedValues2[index];
            if (enableDebugLogs) Debug.Log($"Time: {currentTime}, RTPC Value: {currentRTPCValue2}");
            index++; yield return null;
        }
        if (interpolatedValues2.Count > 0)
        {
            rtpc2.SetValue(gameObject, interpolatedValues2[interpolatedValues2.Count - 1]);
            currentRTPCValue2 = interpolatedValues2[interpolatedValues2.Count - 1];
            if (enableDebugLogs) Debug.Log($"Final RTPC Value: {currentRTPCValue2}");
        }
    }
    IEnumerator AutoPlayPresets()
    {
        while (true)
        {
            foreach (CSVFile file in csvFiles)
            {
                // D�selectionnez les autres fichiers avant de charger un nouveau preset
                DeselectOtherFiles(file);

                // Chargez et jouez le nouveau fichier
                LoadCSV(file);

                // Attendre avant de passer au preset suivant
                yield return new WaitForSeconds(autoPlayDelay);
            }
        }
    }

    private Color GetRandomColorAroundPrimary(Color primary, float variance)
    {
        float r = Mathf.Clamp01(primary.r + Random.Range(-variance, variance));
        float g = Mathf.Clamp01(primary.g + Random.Range(-variance, variance));
        float b = Mathf.Clamp01(primary.b + Random.Range(-variance, variance));
        return new Color(r, g, b);
    }

    [ContextMenu("Play")]
    public void Play()
    {
        foreach (var csvFile in csvFiles)
            if (csvFile.loadNow) { LoadCSV(csvFile); }
        foreach (var csvFile in csvFiles)
            if (csvFile.loadNow) { LoadCSV2(csvFile); }
        TriggerWwiseEvent();
        PlayRTPCCurve();
        PlayRTPCCurve2();
        nextEventIndex = 0;
        elapsedTime = 0f;
        //shapeGenerator.DestroyPreviousShape();
        CalculateEventTimes();
        isPlaying = true;

    }
    [ContextMenu("Stop")]
    public void Stop()
    {
        SetCameraBackgroundColorToGrey();
       // SetObjectColorToGrey();
        AkSoundEngine.PostEvent(secondWwiseEvent.Id, gameObject);
    }
}