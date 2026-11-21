using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace LibriaMods.S3.KatesPace
{
    /// <summary>
    /// Kate's Pace for Syberia 3: faster walking/running (+/-), skip spoken lines.
    /// Kate moves with root motion, so the animator is sped up while she is moving
    /// (idle, interactions and cutscenes keep their normal speed).
    /// </summary>
    [BepInPlugin("LibriaMods.S3.KatesPace", "Kate's Pace (Syberia 3)", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        const float Step = 0.1f;

        ConfigEntry<float> multiplier;
        ConfigEntry<bool> speedOn;
        ConfigEntry<KeyCode> speedUpKey, speedDownKey, toggleKey, skipLineKey, skipLinePad;
        ConfigEntry<bool> showSpeed;

        // game types (resolved by name: most members are private)
        Type kateControllerType, dialogManagerType, cutsceneManagerType;
        FieldInfo kateAnimatorField, dmSelectedField;
        MethodInfo dlgForceNext, dlgCurrentPhasis, cmIsPlaying, cmSkipPressed;
        PropertyInfo cmInstance, dmInstance;

        Component kate;
        Animator kateAnimator;
        UnityEngine.NavMeshAgent agent;   // point & click mode
        float agentBaseSpeed = -1f;
        float lastSetAnimSpeed = 1f;
        float refindTimer;

        string toast; float toastUntil;

        void Awake()
        {
            multiplier   = Config.Bind("Speed", "Multiplier", 1.6f, "Walk/run speed multiplier while Kate is moving (1 = normal)");
            speedOn      = Config.Bind("Speed", "Enabled", true, "Extra speed on/off (toggle in game)");
            speedUpKey   = Config.Bind("Keys", "SpeedUpKey", KeyCode.KeypadPlus, "Increase the multiplier by 0.1 (main-row '=' also works)");
            speedDownKey = Config.Bind("Keys", "SpeedDownKey", KeyCode.KeypadMinus, "Decrease the multiplier by 0.1 (main-row '-' also works)");
            toggleKey    = Config.Bind("Keys", "ToggleKey", KeyCode.F8, "Extra speed on/off");
            skipLineKey  = Config.Bind("Keys", "SkipLineKey", KeyCode.Space, "Skip the current spoken line (dialogues and cutscenes)");
            skipLinePad  = Config.Bind("Keys", "SkipLinePadButton", KeyCode.JoystickButton1,
                "Gamepad button that skips a line. JoystickButton1 = B / Circle (Xbox layout, Steam Input). Use None to disable");
            showSpeed    = Config.Bind("Display", "ShowSpeedChanges", true, "Show the new multiplier on screen when it changes");

            var asm = Assembly.Load("Assembly-CSharp");
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
            kateControllerType  = asm.GetType("KateController");
            dialogManagerType   = asm.GetType("DialogManager");
            cutsceneManagerType = asm.GetType("CutsceneManager");
            var dialogType      = asm.GetType("Dialog");

            kateAnimatorField = kateControllerType?.GetField("m_animator", all);
            dmSelectedField   = dialogManagerType?.GetField("m_Selected", all);
            dmInstance        = dialogManagerType?.GetProperty("Instance", all);
            dlgForceNext      = dialogType?.GetMethod("ForceNextPhasis", all, null, Type.EmptyTypes, null);
            dlgCurrentPhasis  = dialogType?.GetMethod("GetCurrentPhasis", all, null, Type.EmptyTypes, null);
            cmInstance        = cutsceneManagerType?.GetProperty("Instance", all);
            cmIsPlaying       = cutsceneManagerType?.GetMethod("isPlayingCutscene", all, null, Type.EmptyTypes, null);
            cmSkipPressed     = cutsceneManagerType?.GetMethod("onSkipButtonPressed", all, null, Type.EmptyTypes, null);

            Logger.LogInfo($"Kate's Pace ready: KateController={kateControllerType != null}, animator={kateAnimatorField != null}, " +
                           $"dialogs={dmSelectedField != null && dlgForceNext != null}, cutscenes={cmSkipPressed != null}");
        }

        void Update()
        {
            if (Input.GetKeyDown(speedUpKey.Value) || Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.Plus)) ChangeSpeed(+Step);
            if (Input.GetKeyDown(speedDownKey.Value) || Input.GetKeyDown(KeyCode.Minus)) ChangeSpeed(-Step);
            if (Input.GetKeyDown(toggleKey.Value))
            {
                speedOn.Value = !speedOn.Value;
                Toast(speedOn.Value ? $"Kate's Pace x{multiplier.Value:0.0}" : "Kate's Pace off");
            }
            if (Input.GetKeyDown(skipLineKey.Value) || (skipLinePad.Value != KeyCode.None && Input.GetKeyDown(skipLinePad.Value)))
                SkipLine();

            ApplySpeed();
        }

        void ChangeSpeed(float d)
        {
            multiplier.Value = Mathf.Clamp((float)Math.Round(multiplier.Value + d, 1), 0.5f, 4f);
            speedOn.Value = true;
            Toast($"Kate's Pace x{multiplier.Value:0.0}");
        }

        // ------------------------------------------------------------------ speed

        void ApplySpeed()
        {
            refindTimer -= Time.unscaledDeltaTime;
            if ((kate == null || kateAnimator == null) && refindTimer <= 0f)
            {
                refindTimer = 1f;
                kate = kateControllerType != null ? FindObjectOfType(kateControllerType) as Component : null;
                kateAnimator = kate != null ? kateAnimatorField?.GetValue(kate) as Animator : null;
                if (kateAnimator == null && kate != null) kateAnimator = kate.GetComponent<Animator>();
                agent = kate != null ? kate.GetComponent<UnityEngine.NavMeshAgent>() : null;
                agentBaseSpeed = agent != null ? agent.speed : -1f;
                lastSetAnimSpeed = kateAnimator != null ? kateAnimator.speed : 1f;
            }
            if (kateAnimator == null) return;

            bool keyboardMoving = SafeFloat(kateAnimator, "Speed") > 0.05f;
            bool agentMoving = agent != null && agent.enabled && agent.hasPath && agent.velocity.sqrMagnitude > 0.01f;
            float target = speedOn.Value && (keyboardMoving || agentMoving) ? multiplier.Value : 1f;

            // Only touch the animator speed if nobody else (cutscenes) changed it since our last write.
            if (Mathf.Approximately(kateAnimator.speed, lastSetAnimSpeed) && !Mathf.Approximately(kateAnimator.speed, target))
            {
                kateAnimator.speed = target;
                lastSetAnimSpeed = target;
            }
            else if (!Mathf.Approximately(kateAnimator.speed, lastSetAnimSpeed) && Mathf.Approximately(target, 1f))
            {
                lastSetAnimSpeed = kateAnimator.speed; // someone else owns it now
            }

            if (agent != null && agentBaseSpeed > 0f)
                agent.speed = agentBaseSpeed * (speedOn.Value ? multiplier.Value : 1f);
        }

        static float SafeFloat(Animator a, string p)
        {
            try { return a.GetFloat(p); } catch { return 0f; }
        }

        // ------------------------------------------------------------------ skip line

        void SkipLine()
        {
            try
            {
                // Cutscenes: the game's own "jump to the next character speech"
                var cm = cmInstance?.GetValue(null, null);
                if (cm != null && cmIsPlaying != null && (bool)cmIsPlaying.Invoke(cm, null))
                {
                    cmSkipPressed?.Invoke(cm, null);
                    Logger.LogInfo("Skipped cutscene line");
                    return;
                }

                // Dialogues: next phase, but only for spoken lines (never choices, duels or inventory)
                var dm = dmInstance?.GetValue(null, null);
                var dialog = dm != null ? dmSelectedField?.GetValue(dm) as UnityEngine.Object : null;
                if (dialog == null) return;
                var phasis = dlgCurrentPhasis?.Invoke(dialog, null);
                string kind = phasis?.GetType().Name ?? "";
                if (kind == "DialogPhasisStandard" || kind == "DialogPhasisOneLiner" || kind == "DialogPhasisUninteractive" || kind == "DialogPhasisNotification")
                {
                    dlgForceNext.Invoke(dialog, null);
                    Logger.LogInfo("Skipped line (" + kind + ")");
                }
                else Logger.LogInfo("Not skipping " + kind);
            }
            catch (Exception e) { Logger.LogWarning("Skip failed: " + (e.InnerException ?? e).Message); }
        }

        // ------------------------------------------------------------------ toast

        void Toast(string s)
        {
            Logger.LogInfo(s);
            if (!showSpeed.Value) return;
            toast = s; toastUntil = Time.unscaledTime + 1.5f;
        }

        GUIStyle style;
        void OnGUI()
        {
            if (toast == null || Time.unscaledTime > toastUntil) return;
            if (style == null) style = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.UpperCenter };
            var r = new Rect(0, 40, Screen.width, 50);
            style.normal.textColor = new Color(0, 0, 0, 0.8f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), toast, style);
            style.normal.textColor = Color.white;
            GUI.Label(r, toast, style);
        }
    }
}
