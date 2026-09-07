using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    private const float NoSandNarrationInterval = 15f * 60f;
    private const float HourlyNarrationInterval = 60f * 60f;
    private const float FullFillThreshold = 99.5f;

    [SerializeField] private bool autoPopulateSoundClips = true;

    [Header("Narration")]
    [SerializeField] private AudioSource narrationSource;
    [SerializeField] private AudioClip[] firstClickNarrations;
    [SerializeField] private AudioClip[] halfFillNarrations;
    [SerializeField] private AudioClip[] fullFillNarrations;
    [SerializeField] private AudioClip[] noSandNarrations;
    [SerializeField] private AudioClip[] hourlyNarrations;

    [Header("Sound Effects")]
    [SerializeField] private AudioSource sfxSource;
    
    [Header("Looping Sound Effects")]
    [SerializeField] private AudioSource loopingSfxSource;
    [SerializeField] private AudioClip sandFlowSfx;

    private readonly Queue<AudioClip> narrationQueue = new();
    private Coroutine narrationRoutine;
    private bool firstClickNarrationPlayed;
    private bool halfFillNarrationPlayed;
    private bool fullFillNarrationPlayed;
    private float idleSandTimer;
    private float hourlyTimer;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        EnsureAudioSources();
    }

    private void Update()
    {
        idleSandTimer += Time.unscaledDeltaTime;
        hourlyTimer += Time.unscaledDeltaTime;

        if (idleSandTimer >= NoSandNarrationInterval)
        {
            idleSandTimer = 0f;
            PlayRandomNarration(noSandNarrations);
        }

        while (hourlyTimer >= HourlyNarrationInterval)
        {
            hourlyTimer -= HourlyNarrationInterval;
            PlayRandomNarration(hourlyNarrations);
        }
    }

    public void NotifySandProduced()
    {
        idleSandTimer = 0f;

        if (!firstClickNarrationPlayed)
        {
            firstClickNarrationPlayed = true;
            PlayRandomNarration(firstClickNarrations);
        }
    }

    public void RegisterFillPercentage(float fillPercentage)
    {
        if (!halfFillNarrationPlayed && fillPercentage >= 50f)
        {
            halfFillNarrationPlayed = true;
            PlayRandomNarration(halfFillNarrations);
        }

        if (!fullFillNarrationPlayed && fillPercentage >= FullFillThreshold)
        {
            fullFillNarrationPlayed = true;
            PlayRandomNarration(fullFillNarrations);
        }
    }

    public void PlayNarration(AudioClip clip)
    {
        if (clip == null || narrationSource == null)
        {
            return;
        }

        narrationQueue.Enqueue(clip);

        narrationRoutine ??= StartCoroutine(PlayNarrationQueue());
    }

    public void ClearNarrationQueue()
    {
        narrationQueue.Clear();

        if (narrationSource != null)
        {
            narrationSource.Stop();
        }

        if (narrationRoutine != null)
        {
            StopCoroutine(narrationRoutine);
            narrationRoutine = null;
        }
    }

    public void PlayRandomNarration(AudioClip[] clips)
    {
        AudioClip clip = ChooseRandomClip(clips);
        if (clip != null)
        {
            PlayNarration(clip);
        }
    }

    public void PlaySfx(AudioClip clip, float volume = 1f)
    {
        if (clip == null || sfxSource == null)
        {
            return;
        }

        sfxSource.PlayOneShot(clip, volume);
    }

    private IEnumerator PlayNarrationQueue()
    {
        while (narrationQueue.Count > 0)
        {
            AudioClip clip = narrationQueue.Dequeue();

            narrationSource.clip = clip;
            narrationSource.Play();

            yield return new WaitWhile(() => narrationSource.isPlaying);
        }

        narrationRoutine = null;
    }
    
    public void PlaySandFlow()
    {
        PlayLoopingSfx(sandFlowSfx);
    }

    public void StopSandFlow()
    {
        StopLoopingSfx();
    }

    public void PlayLoopingSfx(AudioClip clip, float volume = 1f)
    {
        if (clip == null || loopingSfxSource == null)
        {
            return;
        }

        if (loopingSfxSource.clip == clip && loopingSfxSource.isPlaying)
        {
            return;
        }

        loopingSfxSource.clip = clip;
        loopingSfxSource.volume = volume;
        loopingSfxSource.loop = true;
        loopingSfxSource.Play();
    }

    public void StopLoopingSfx()
    {
        if (loopingSfxSource == null)
        {
            return;
        }

        loopingSfxSource.Stop();
        loopingSfxSource.clip = null;
    }

    private static AudioClip ChooseRandomClip(IReadOnlyList<AudioClip> clips)
    {
        if (clips == null || clips.Count == 0)
        {
            return null;
        }

        int startIndex = Random.Range(0, clips.Count);
        for (int offset = 0; offset < clips.Count; offset++)
        {
            AudioClip clip = clips[(startIndex + offset) % clips.Count];
            if (clip != null)
            {
                return clip;
            }
        }

        return null;
    }

    private void EnsureAudioSources()
    {
        narrationSource = EnsureSource(narrationSource, "NarrationSource", false);
        sfxSource = EnsureSource(sfxSource, "SfxSource", false);
        loopingSfxSource = EnsureSource(loopingSfxSource, "LoopingSfxSource", true);
    }

    private AudioSource EnsureSource(AudioSource source, string childName, bool loop)
    {
        if (source == null)
        {
            Transform child = transform.Find(childName);
            if (child == null)
            {
                GameObject sourceObject = new(childName);
                sourceObject.transform.SetParent(transform, false);
                child = sourceObject.transform;
            }

            source = child.GetComponent<AudioSource>();
            if (source == null)
            {
                source = child.gameObject.AddComponent<AudioSource>();
            }
        }

        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f;
        return source;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!autoPopulateSoundClips)
        {
            return;
        }

        firstClickNarrations = LoadEditorClips(
            "first_click_1",
            "first_click_2",
            "firsst_click_2",
            "first_click_3",
            "first_click_4");
        halfFillNarrations = LoadEditorClips("50_fill_1", "50_fill_2", "50_fill_3");
        fullFillNarrations = LoadEditorClips(
            "100_fill_1",
            "100_fil_1",
            "100_fill_2",
            "100_fil_2",
            "100_fill_3",
            "100_fil_3");
        noSandNarrations = LoadEditorClips(
            "15min_no_sand_1",
            "15min_no_sand_2",
            "15min_no_sand_3",
            "15min_no_sand_4");
        hourlyNarrations = LoadEditorClips("1hr_1", "1hr_2", "1hr_3");
        sandFlowSfx = LoadEditorClip("sand_flow");
    }

    private static AudioClip[] LoadEditorClips(params string[] clipNames)
    {
        List<AudioClip> clips = new();
        HashSet<AudioClip> uniqueClips = new();

        foreach (string clipName in clipNames)
        {
            AudioClip clip = LoadEditorClip(clipName);
            if (clip != null && uniqueClips.Add(clip))
            {
                clips.Add(clip);
            }
        }

        return clips.ToArray();
    }

    private static AudioClip LoadEditorClip(string clipName)
    {
        string[] guids = UnityEditor.AssetDatabase.FindAssets(
            $"{clipName} t:AudioClip",
            new[] { "Assets/05Sounds" });

        foreach (string guid in guids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(path) == clipName)
            {
                return UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }
        }

        return null;
    }
#endif
}
