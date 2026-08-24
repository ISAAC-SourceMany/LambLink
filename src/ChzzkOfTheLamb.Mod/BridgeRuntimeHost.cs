using System;
using System.Threading;
using BepInEx.Logging;
using UnityEngine;

namespace ChzzkOfTheLamb.Mod;

/// <summary>
/// Owns the Unity main-thread pump independently from the BepInEx plugin component.
/// Some Cult of the Lamb startup configurations invoke Plugin.Awake but never its Update,
/// which leaves bridge commands permanently queued. This hidden persistent GameObject is
/// created explicitly on the Unity main thread and survives scene transitions.
/// </summary>
public sealed class BridgeRuntimeHost : MonoBehaviour
{
    private static BridgeRuntimeHost? _instance;
    private Action? _tick;
    private ManualLogSource? _log;
    private bool _firstUpdateLogged;
    private bool _applicationQuitting;

    internal static void Install(Action tick, ManualLogSource log)
    {
        if (_instance != null)
        {
            _instance._tick = tick;
            _instance._log = log;
            log.LogInfo("[DIAG][RUNTIME-HOST][REUSED] existing persistent dispatcher updated");
            return;
        }

        var hostObject = new GameObject("CHZZK Companion Runtime Host")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        UnityEngine.Object.DontDestroyOnLoad(hostObject);
        var host = hostObject.AddComponent<BridgeRuntimeHost>();
        host._tick = tick;
        host._log = log;
        _instance = host;
        log.LogInfo($"[DIAG][RUNTIME-HOST][INSTALLED] object={hostObject.name}, instance={hostObject.GetInstanceID()}, thread={Thread.CurrentThread.ManagedThreadId}");
    }

    private void Update()
    {
        if (!_firstUpdateLogged)
        {
            _firstUpdateLogged = true;
            _log?.LogInfo($"[DIAG][RUNTIME-HOST][FIRST-UPDATE] thread={Thread.CurrentThread.ManagedThreadId}");
        }

        try { _tick?.Invoke(); }
        catch (Exception ex) { _log?.LogError($"[DIAG][RUNTIME-HOST][TICK-FAILED] {ex}"); }
    }

    private void OnApplicationQuit()
    {
        _applicationQuitting = true;
        _log?.LogInfo("[DIAG][RUNTIME-HOST][APPLICATION-QUIT]");
    }

    private void OnDestroy()
    {
        _log?.LogWarning($"[DIAG][RUNTIME-HOST][DESTROYED] applicationQuitting={_applicationQuitting}");
        if (ReferenceEquals(_instance, this)) _instance = null;
    }
}
