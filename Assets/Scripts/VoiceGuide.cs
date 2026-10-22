using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Speaks route guidance from pre-recorded clips: turn prompts, distance
/// milestones, wrong-way warnings and arrival. A line never cuts off another,
/// and a queued line is dropped if it is out of date by the time it would play.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class VoiceGuide : MonoBehaviour
{
    private const string Tag = "Voice";

    // Higher plays first when several lines are waiting.
    private enum Priority { Info, Arrival, Turn, Warning }

    [Flags]
    private enum TurnCue { None = 0, Armed = 1, Warned = 2, Now = 4, Passed = 8 }

    private class Line
    {
        public List<AudioClip> clips;
        public Priority priority;
        public bool urgent;
        public Func<bool> isCurrent;
        public int order;
    }

    private struct Milestone
    {
        public float meters;
        public string clip;
        public bool armed;
        public bool done;
    }

    [Header("References")]
    public GuidanceController guidance;

    [Tooltip("Voice clips, looked up by asset name (e.g. turn_now_right).")]
    public List<AudioClip> clips = new List<AudioClip>();

    [Header("Timing (seconds)")]
    [SerializeField] private float gapBetweenLines = 2.5f;
    [SerializeField] private float gapBetweenClips = 0.1f;
    [SerializeField] private float wrongWayRepeat = 15f;

    [Header("Distances (metres)")]
    [SerializeField] private float turnWarningDistance = 10f;

    [Tooltip("A turn this close after another is announced with it: 'Turn right now, then turn left'.")]
    [SerializeField] private float comboTurnGap = 6f;

    [Tooltip("Say 'Continue straight' after a turn when the next one is at least this far.")]
    [SerializeField] private float continueStraightDistance = 25f;

    [Tooltip("Skip a distance callout when a turn is this close to it.")]
    [SerializeField] private float milestoneTurnClearance = 5f;

    [Tooltip("Skip distance callouts this close to the route's total, already said at the start.")]
    [SerializeField] private float milestoneStartClearance = 15f;

    private AudioSource source;
    private readonly Dictionary<string, AudioClip> clipsByName = new Dictionary<string, AudioClip>();
    private readonly HashSet<string> reportedMissing = new HashSet<string>();
    private readonly List<Line> queue = new List<Line>();
    private readonly Queue<AudioClip> playing = new Queue<AudioClip>();
    private float clipEndTime;
    private float nextLineTime;
    private int lineCount;

    private IReadOnlyList<PathTurn> turns;
    private TurnCue[] turnCues;
    private Milestone[] milestones;
    private float totalMeters;
    private float nextWrongWayTime = float.PositiveInfinity;
    private WrongWayKind wrongWayKind;

    void Awake()
    {
        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;

        if (guidance == null) guidance = FindAnyObjectByType<GuidanceController>();
        foreach (AudioClip clip in clips)
        {
            if (clip != null) clipsByName[clip.name] = clip;
        }
    }

    void OnEnable()
    {
        if (guidance == null) return;
        guidance.OnStarted += HandleStarted;
        guidance.OnGuidanceUpdated += HandleGuidanceUpdated;
        guidance.OnArrived += HandleArrived;
        guidance.OnWrongWay += HandleWrongWay;
        guidance.OnBackOnRoute += HandleBackOnRoute;
    }

    void OnDisable()
    {
        if (guidance == null) return;
        guidance.OnStarted -= HandleStarted;
        guidance.OnGuidanceUpdated -= HandleGuidanceUpdated;
        guidance.OnArrived -= HandleArrived;
        guidance.OnWrongWay -= HandleWrongWay;
        guidance.OnBackOnRoute -= HandleBackOnRoute;
    }

    void Update()
    {
        if (guidance == null) return;
        if (!guidance.IsActive)
        {
            if (turns != null || queue.Count > 0 || source.isPlaying) Silence();
            return;
        }

        if (guidance.IsWrongWay && Time.time >= nextWrongWayTime) QueueWrongWay();

        if (Time.time < clipEndTime) return;
        if (playing.Count > 0)
        {
            Play(playing.Dequeue());
            return;
        }

        Line next = TakeNext();
        if (next == null) return;
        foreach (AudioClip clip in next.clips) playing.Enqueue(clip);
        Play(playing.Dequeue());
    }

    // ───────────────────────── Guidance events ─────────────────────────

    private void HandleStarted()
    {
        Silence();
        turns = guidance.Turns;
        turnCues = new TurnCue[turns.Count];
        totalMeters = guidance.TotalDistance;
        milestones = new[]
        {
            new Milestone { meters = 100f, clip = "remain_100" },
            new Milestone { meters = 75f, clip = "remain_75" },
            new Milestone { meters = 50f, clip = "remain_50" },
            new Milestone { meters = 25f, clip = "remain_25" },
            new Milestone { meters = 10f, clip = "dest_ahead_10" }
        };

        Enqueue(Priority.Info, false, () => true, "route_started", TotalClip(totalMeters));
    }

    private void HandleGuidanceUpdated(GuidanceUpdate update)
    {
        if (turns == null || guidance.IsWrongWay) return;
        if (update.status != GuidanceStatus.OnRoute && update.status != GuidanceStatus.OffRoute) return;

        UpdateTurns(guidance.Progress);
        UpdateMilestones(update.remainingMeters);
    }

    private void HandleArrived()
    {
        turns = null;
        queue.Clear();
        nextWrongWayTime = float.PositiveInfinity;
        Enqueue(Priority.Arrival, true, () => true, "arrived");
    }

    private void HandleWrongWay(WrongWayKind kind)
    {
        wrongWayKind = kind;
        QueueWrongWay();
    }

    private void HandleBackOnRoute()
    {
        nextWrongWayTime = float.PositiveInfinity;
        Enqueue(Priority.Warning, false, () => !guidance.IsWrongWay, "back_on_route");
    }

    private void QueueWrongWay()
    {
        nextWrongWayTime = Time.time + wrongWayRepeat;
        string clip = wrongWayKind == WrongWayKind.Drifted ? "drifted" : "wrong_way";
        Enqueue(Priority.Warning, false, () => guidance.IsWrongWay, clip);
    }

    // ───────────────────────── Cues ─────────────────────────

    private void UpdateTurns(float progress)
    {
        float nowDistance = guidance.TurnNowDistance;
        for (int i = 0; i < turns.Count; i++)
        {
            float distance = turns[i].distanceAlong - progress;
            TurnCue cue = turnCues[i];

            if (distance < -1f)
            {
                if ((cue & TurnCue.Passed) == 0)
                {
                    turnCues[i] |= TurnCue.Passed;
                    QueueContinueStraight(i, progress);
                }
                continue;
            }

            if (distance > turnWarningDistance)
            {
                turnCues[i] |= TurnCue.Armed;
            }
            else if (distance <= nowDistance)
            {
                if ((cue & TurnCue.Now) == 0) QueueTurnNow(i);
            }
            else if ((cue & (TurnCue.Armed | TurnCue.Warned)) == TurnCue.Armed)
            {
                // Only warn about turns that crossed the warning distance, so "In 10 metres"
                // is never said about a turn that is already much closer.
                turnCues[i] |= TurnCue.Warned;
                int index = i;
                float staleDistance = turnWarningDistance * 0.6f;
                Enqueue(Priority.Turn, false, () => turns != null && turns[index].distanceAlong - guidance.Progress > staleDistance,
                    "chime", $"{Kind(turns[i])}_in10_{Side(turns[i])}");
            }
        }
    }

    private void QueueTurnNow(int i)
    {
        turnCues[i] |= TurnCue.Now | TurnCue.Warned;
        var names = new List<string> { "chime", $"{Kind(turns[i])}_now_{Side(turns[i])}" };

        if (i + 1 < turns.Count && turns[i + 1].distanceAlong - turns[i].distanceAlong < comboTurnGap)
        {
            names.Add($"then_{Kind(turns[i + 1])}_{Side(turns[i + 1])}");
            turnCues[i + 1] |= TurnCue.Warned;
        }

        int index = i;
        Enqueue(Priority.Turn, true, () => turns != null && turns[index].distanceAlong - guidance.Progress > -2f, names.ToArray());
    }

    private void QueueContinueStraight(int passedTurn, float progress)
    {
        float remaining = totalMeters - progress;
        float ahead = passedTurn + 1 < turns.Count ? turns[passedTurn + 1].distanceAlong - progress : remaining;
        if (ahead < continueStraightDistance) return;

        float expires = Time.time + 6f;
        Enqueue(Priority.Info, false, () => Time.time < expires, "continue_straight");
    }

    private void UpdateMilestones(float remaining)
    {
        for (int i = 0; i < milestones.Length; i++)
        {
            ref Milestone milestone = ref milestones[i];
            if (milestone.done) continue;

            if (remaining > milestone.meters)
            {
                if (totalMeters - milestone.meters >= milestoneStartClearance) milestone.armed = true;
                continue;
            }

            milestone.done = true;
            if (!milestone.armed || TurnNear(totalMeters - milestone.meters)) continue;

            float staleBelow = Mathf.Max(milestone.meters - 8f, 3f);
            Enqueue(Priority.Info, false, () => guidance.TotalDistance - guidance.Progress > staleBelow, milestone.clip);
        }
    }

    private bool TurnNear(float distanceAlong)
    {
        foreach (PathTurn turn in turns)
        {
            if (Mathf.Abs(turn.distanceAlong - distanceAlong) <= milestoneTurnClearance) return true;
        }
        return false;
    }

    private static string Kind(PathTurn turn) => turn.IsSlight ? "slight" : "turn";
    private static string Side(PathTurn turn) => turn.IsRight ? "right" : "left";

    /// <summary>Nearest recorded total: 10 m steps up to 100 m, then 50 m steps up to 300 m.</summary>
    private static string TotalClip(float meters)
    {
        if (meters > 325f) return "total_over300";
        int rounded = meters <= 100f
            ? Mathf.Clamp(Mathf.RoundToInt(meters / 10f) * 10, 10, 100)
            : Mathf.Clamp(Mathf.RoundToInt(meters / 50f) * 50, 100, 300);
        return $"total_{rounded}";
    }

    // ───────────────────────── Playback ─────────────────────────

    private void Enqueue(Priority priority, bool urgent, Func<bool> isCurrent, params string[] names)
    {
        var lineClips = new List<AudioClip>();
        foreach (string name in names)
        {
            if (clipsByName.TryGetValue(name, out AudioClip clip)) lineClips.Add(clip);
            else if (reportedMissing.Add(name)) AppLog.Warn(Tag, $"Missing voice clip '{name}'.");
        }
        if (lineClips.Count == 0) return;

        queue.Add(new Line { clips = lineClips, priority = priority, urgent = urgent, isCurrent = isCurrent, order = lineCount++ });
    }

    private Line TakeNext()
    {
        queue.RemoveAll(line => !line.isCurrent());

        Line best = null;
        foreach (Line line in queue)
        {
            if (best == null || line.priority > best.priority || (line.priority == best.priority && line.order < best.order)) best = line;
        }
        if (best == null || (!best.urgent && Time.time < nextLineTime)) return null;

        queue.Remove(best);
        return best;
    }

    private void Play(AudioClip clip)
    {
        source.clip = clip;
        source.Play();
        clipEndTime = Time.time + clip.length + gapBetweenClips;
        if (playing.Count == 0) nextLineTime = clipEndTime + gapBetweenLines;
    }

    private void Silence()
    {
        turns = null;
        queue.Clear();
        playing.Clear();
        nextWrongWayTime = float.PositiveInfinity;
        if (source != null) source.Stop();
        clipEndTime = 0f;
        nextLineTime = 0f;
    }
}
