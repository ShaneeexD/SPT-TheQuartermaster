using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using TMPro;
using UnityEngine;

namespace TheQuartermaster.Client.Patches;

public class QuestExpiryCountdownPatch : ModulePatch
{
    private static readonly Regex ExpiryTagRegex = new Regex(
        @"\[QM_EXPIRY:(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z)\]",
        RegexOptions.Compiled
    );

    protected override MethodBase GetTargetMethod()
    {
        var method = AccessTools.DeclaredMethod(typeof(NotesTaskDescription), "Show", new[] { typeof(QuestClass), typeof(object) });
        if (method == null)
        {
            method = AccessTools.DeclaredMethod(typeof(NotesTaskDescription), "Show", new Type[] { typeof(QuestClass), AccessTools.TypeByName("GInterface221") });
        }
        if (method == null)
        {
            var allShows = typeof(NotesTaskDescription).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            Plugin.Log.LogError($"[TheQuartermaster] Could not find Show method. Available: {string.Join(", ", allShows.Select(m => $"{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})"))}");
            if (allShows.Length > 0) method = allShows[0];
        }
        if (Plugin.DebugLogging)
            Plugin.Log.LogDebug($"[TheQuartermaster] Patched: {method?.DeclaringType?.Name}.{method?.Name}");
        return method;
    }

    [PatchPostfix]
    public static void Postfix(NotesTaskDescription __instance, QuestClass quest)
    {
        try
        {
            var descriptionField = AccessTools.Field(typeof(NotesTaskDescription), "_description");
            if (descriptionField == null) return;

            var tmpText = descriptionField.GetValue(__instance) as TMP_Text;
            if (tmpText == null) return;

            // Always destroy any existing LiveCountdownBehaviour first, before reading text.
            // Use DestroyImmediate so OnDestroy fires synchronously and can't clobber text later.
            var existing = tmpText.GetComponent<LiveCountdownBehaviour>();
            if (existing != null)
            {
                existing.DisableCleanup = true;
                UnityEngine.Object.DestroyImmediate(existing);
            }

            var text = tmpText.text;
            if (string.IsNullOrEmpty(text)) return;

            var match = ExpiryTagRegex.Match(text);
            if (!match.Success) return;

            var expiryStr = match.Groups[1].Value;
            if (!DateTime.TryParseExact(
                expiryStr,
                "yyyy-MM-ddTHH:mm:ssZ",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var expiryUtc))
            {
                return;
            }

            var behaviour = tmpText.gameObject.AddComponent<LiveCountdownBehaviour>();
            behaviour.Initialise(tmpText, text, expiryUtc);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[TheQuartermaster] Countdown patch error: {ex}");
        }
    }
}

public class LiveCountdownBehaviour : MonoBehaviour
{
    private TMP_Text _tmpText;
    private string _baseText;
    private DateTime _expiryUtc;
    private float _updateTimer;
    internal bool DisableCleanup;
    private static readonly Regex ExpiryTagRegex = new Regex(
        @"\[QM_EXPIRY:\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z\]\nExpires in [^\n]*",
        RegexOptions.Compiled
    );

    public void Initialise(TMP_Text tmpText, string currentText, DateTime expiryUtc)
    {
        _tmpText = tmpText;
        _baseText = currentText;
        _expiryUtc = expiryUtc;
        UpdateText();
    }

    private void Update()
    {
        if (_tmpText == null)
        {
            Destroy(this);
            return;
        }

        _updateTimer += Time.deltaTime;
        if (_updateTimer >= 1f)
        {
            _updateTimer = 0f;
            UpdateText();
        }
    }

    private void UpdateText()
    {
        var remaining = _expiryUtc - DateTime.UtcNow;
        if (remaining.TotalSeconds <= 0)
        {
            var expiredText = ExpiryTagRegex.Replace(_baseText, "Expired");
            _tmpText.text = expiredText;
            Destroy(this);
            return;
        }

        var hours = (int)remaining.TotalHours;
        var minutes = remaining.Minutes;
        var seconds = remaining.Seconds;
        var timeLabel = hours > 0
            ? $"{hours}h {minutes}m {seconds}s"
            : minutes > 0
                ? $"{minutes}m {seconds}s"
                : $"{seconds}s";

        var newText = ExpiryTagRegex.Replace(_baseText, $"Expires in {timeLabel}");
        _tmpText.text = newText;
    }
}
