using System;
using System.Diagnostics;
using System.Globalization;

namespace BiosOptimizer.Core.Services;

public static class StateNormalizer
{
    public static bool IsSatisfied(string? current, string? target)
    {
        if (current == null && target == null) return true;
        if (current == null || target == null) return false;

        // Handle empty strings (e.g. registry default value for Classic Context Menu)
        if (current.Length == 0 && target.Length == 0) return true;

        string cur = current.Trim();
        string tgt = target.Trim();

        // 1. Direct equality
        if (string.Equals(cur, tgt, StringComparison.OrdinalIgnoreCase)) return true;

        // 2. Base string comparison without parenthesized extra info
        string curBase = cur.Contains('(') ? cur.Substring(0, cur.IndexOf('(')).Trim() : cur;
        string tgtBase = tgt.Contains('(') ? tgt.Substring(0, tgt.IndexOf('(')).Trim() : tgt;
        if (string.Equals(curBase, tgtBase, StringComparison.OrdinalIgnoreCase)) return true;

        // 3. Normalized semantic state comparison (e.g. "0" vs "Disabled", "1" vs "Enabled")
        var normCur = Normalize(curBase);
        var normTgt = Normalize(tgtBase);
        if (!string.IsNullOrEmpty(normCur) && !string.IsNullOrEmpty(normTgt) && string.Equals(normCur, normTgt, StringComparison.OrdinalIgnoreCase))
            return true;

        // 4. Integer and Hexadecimal equivalence
        if (TryNumericMatch(curBase, tgtBase))
            return true;

        // 5. Power Plan GUIDs and Names
        if (IsPowerPlanMatch(curBase, tgtBase))
            return true;

        // 6. Service Start Types (2: Automatic, 3: Manual, 4: Disabled)
        if (IsServiceStartTypeMatch(curBase, tgtBase))
            return true;

        // 7. HAGS Mode (2 == Enabled)
        if ((cur.Equals("Enabled", StringComparison.OrdinalIgnoreCase) && tgt.Equals("2", StringComparison.OrdinalIgnoreCase)) ||
            (cur.Equals("2", StringComparison.OrdinalIgnoreCase) && tgt.Equals("Enabled", StringComparison.OrdinalIgnoreCase)))
            return true;

        // 8. Appx Package removal ("Not Installed" / "Removed" == "Disabled" / "0")
        if ((cur.Equals("Not Installed", StringComparison.OrdinalIgnoreCase) || cur.Equals("Removed", StringComparison.OrdinalIgnoreCase)) &&
            (tgt.Equals("Not Installed", StringComparison.OrdinalIgnoreCase) || tgt.Equals("Removed", StringComparison.OrdinalIgnoreCase) || tgt.Equals("0", StringComparison.OrdinalIgnoreCase) || tgt.Equals("Disabled", StringComparison.OrdinalIgnoreCase)))
            return true;

        // 9. Startup entry: Not Found / Unknown / Disabled == target Disabled / 0
        if ((cur.Equals("Unknown", StringComparison.OrdinalIgnoreCase) || cur.Equals("Not Found", StringComparison.OrdinalIgnoreCase) || cur.Equals("Not Set", StringComparison.OrdinalIgnoreCase)) &&
            (tgt.Equals("Disabled", StringComparison.OrdinalIgnoreCase) || tgt.Equals("0", StringComparison.OrdinalIgnoreCase)))
            return true;

        // 10. NTFS-style bit-flag registry values (NtfsDisableLastAccessUpdate bit 0)
        if (tgt.Equals("1", StringComparison.OrdinalIgnoreCase) && long.TryParse(cur, out long curLong) && (curLong & 1) == 1)
            return true;

        if (tgt.Equals("0", StringComparison.OrdinalIgnoreCase) && long.TryParse(cur, out long curLong2) && (curLong2 & 1) == 0 && curLong2 != 0)
            return true;

        return false;
    }

    public static string Normalize(string val)
    {
        if (string.IsNullOrWhiteSpace(val)) return "";

        string trimmed = val.Trim();

        if (trimmed.Equals("0", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("disabled", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("off", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("stopped", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("not installed", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("removed", StringComparison.OrdinalIgnoreCase))
        {
            return "DISABLED";
        }

        if (trimmed.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("enabled", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("on", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("running", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("installed", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("active", StringComparison.OrdinalIgnoreCase))
        {
            return "ENABLED";
        }

        return trimmed.ToUpperInvariant();
    }

    public static bool TryParseFlexibleNumeric(string s, out long val)
    {
        val = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;

        string trimmed = s.Trim();

        // 1. Try standard decimal integer
        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out val))
        {
            if (val == -1) val = 0xFFFFFFFFL; // Map 32-bit -1 (unchecked DWORD) to 0xFFFFFFFF
            return true;
        }

        // 2. Try unsigned 64-bit integer
        if (ulong.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong uVal))
        {
            val = (long)uVal;
            return true;
        }

        // 3. Try hex with or without 0x prefix
        string hex = trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? trimmed.Substring(2) : trimmed;
        if (ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong hexVal))
        {
            val = (long)hexVal;
            return true;
        }

        return false;
    }

    private static bool TryNumericMatch(string cur, string tgt)
    {
        bool curParsed = TryParseFlexibleNumeric(cur, out long curNum);
        bool tgtParsed = TryParseFlexibleNumeric(tgt, out long tgtNum);

        if (curParsed && tgtParsed)
        {
            return curNum == tgtNum;
        }

        return false;
    }

    public static string NormalizeServiceState(string? rawState)
    {
        if (string.IsNullOrWhiteSpace(rawState)) return "UNKNOWN";
        string s = rawState.Trim().ToUpperInvariant();

        // Boot
        if (s == "0" || s == "BOOT" || s == "SERVICE_BOOT_START" || s == "BOOT START")
            return "BOOT";

        // System
        if (s == "1" || s == "SYSTEM" || s == "SERVICE_SYSTEM_START" || s == "SYSTEM START")
            return "SYSTEM";

        // Auto / Automatic
        if (s == "2" || s == "AUTO" || s == "AUTOMATIC" || s == "SERVICE_AUTO_START" || s == "AUTO_START" || s.Contains("DELAYED"))
            return "AUTOMATIC";

        // Manual / Demand
        if (s == "3" || s == "MANUAL" || s == "DEMAND" || s == "DEMAND_START" || s == "SERVICE_DEMAND_START" || s == "DEMAND START")
            return "MANUAL";

        // Disabled
        if (s == "4" || s == "DISABLED" || s == "SERVICE_DISABLED" || s == "OFF")
            return "DISABLED";

        return s;
    }

    public static bool IsServiceStartTypeMatch(string cur, string tgt)
    {
        string normCur = NormalizeServiceState(cur);
        string normTgt = NormalizeServiceState(tgt);
        if (normCur != "UNKNOWN" && normTgt != "UNKNOWN" && string.Equals(normCur, normTgt, StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(normCur, normTgt, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static bool IsPowerPlanMatch(string cur, string tgt)
    {
        const string highPerfGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        const string balancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
        const string ultPerfGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";
        const string powerSaverGuid = "a1841308-3541-4fab-bc81-f71556f20b4a";

        bool curIsHigh = cur.Equals(highPerfGuid, StringComparison.OrdinalIgnoreCase) || cur.IndexOf("High Performance", StringComparison.OrdinalIgnoreCase) >= 0;
        bool tgtIsHigh = tgt.Equals(highPerfGuid, StringComparison.OrdinalIgnoreCase) || tgt.IndexOf("High Performance", StringComparison.OrdinalIgnoreCase) >= 0;
        if (curIsHigh && tgtIsHigh) return true;

        bool curIsUlt = cur.Equals(ultPerfGuid, StringComparison.OrdinalIgnoreCase) || cur.IndexOf("Ultimate Performance", StringComparison.OrdinalIgnoreCase) >= 0;
        bool tgtIsUlt = tgt.Equals(ultPerfGuid, StringComparison.OrdinalIgnoreCase) || tgt.IndexOf("Ultimate Performance", StringComparison.OrdinalIgnoreCase) >= 0;
        if (curIsUlt && tgtIsUlt) return true;

        bool curIsBal = cur.Equals(balancedGuid, StringComparison.OrdinalIgnoreCase) || cur.IndexOf("Balanced", StringComparison.OrdinalIgnoreCase) >= 0;
        bool tgtIsBal = tgt.Equals(balancedGuid, StringComparison.OrdinalIgnoreCase) || tgt.IndexOf("Balanced", StringComparison.OrdinalIgnoreCase) >= 0;
        if (curIsBal && tgtIsBal) return true;

        bool curIsSaver = cur.Equals(powerSaverGuid, StringComparison.OrdinalIgnoreCase) || cur.IndexOf("Power Saver", StringComparison.OrdinalIgnoreCase) >= 0;
        bool tgtIsSaver = tgt.Equals(powerSaverGuid, StringComparison.OrdinalIgnoreCase) || tgt.IndexOf("Power Saver", StringComparison.OrdinalIgnoreCase) >= 0;
        if (curIsSaver && tgtIsSaver) return true;

        return false;
    }

    public static void LogReconciliationMismatch(string actionId, string verifyCurrent, string verifyTarget, string rescanCurrent, string rescanTarget, string readSource = "WindowsRegistry/Service")
    {
        string msg = $"[STATE_RECONCILIATION_ERROR] ActionId={actionId} " +
                     $"VerifyCurrent='{verifyCurrent}' VerifyTarget='{verifyTarget}' " +
                     $"RescanCurrent='{rescanCurrent}' RescanTarget='{rescanTarget}' " +
                     $"Source='{readSource}' Timestamp='{DateTime.UtcNow:O}'";
        Debug.WriteLine(msg);
        Console.Error.WriteLine(msg);
    }
}
