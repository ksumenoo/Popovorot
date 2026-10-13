using System;
using System.Collections.Generic;
using UnityEngine;

/// Общее состояние загадок: набор флагов, единый для всех четырёх миров.
public static class GameState
{
    static readonly HashSet<string> flags = new HashSet<string>();
    static string timedFlag;
    static float timedEnd;

    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        flags.Clear();
        timedFlag = null;
        Changed = null;
    }

    /// Сбрасывает все флаги перед перезапуском сцены.
    public static void ClearFlags()
    {
        flags.Clear();
        timedFlag = null;
    }

    /// Постоянные флаги для сохранения (временный флаг таймера не сохраняется).
    public static string[] Snapshot()
    {
        var list = new List<string>();
        foreach (string f in flags) if (f != timedFlag) list.Add(f);
        return list.ToArray();
    }

    /// Восстанавливает флаги из сохранения без событий: вызывается до загрузки сцены,
    /// поэтому загадки сразу появляются в нужном состоянии, без звуков решения.
    public static void RestoreSilently(IEnumerable<string> saved)
    {
        flags.Clear();
        timedFlag = null;
        if (saved == null) return;
        foreach (string f in saved) if (!string.IsNullOrEmpty(f)) flags.Add(f);
    }

    public static bool Has(string flag)
    {
        return flags.Contains(flag);
    }

    public static void Set(string flag, bool value)
    {
        bool changed = value ? flags.Add(flag) : flags.Remove(flag);
        if (changed) Changed?.Invoke();
    }

    /// Включает флаг на seconds секунд.
    public static void SetTimed(string flag, float seconds)
    {
        timedFlag = flag;
        timedEnd = Time.time + seconds;
        Set(flag, true);
    }

    /// Сколько секунд осталось у временного флага; 0, если таймер не идёт.
    public static float TimerRemaining
    {
        get { return timedFlag == null ? 0f : Mathf.Max(timedEnd - Time.time, 0f); }
    }

    /// Вызывается каждый кадр из Interactor.
    public static void Tick()
    {
        if (timedFlag != null && Time.time >= timedEnd)
        {
            string flag = timedFlag;
            timedFlag = null;
            Set(flag, false);
        }
    }
}
