using System.Text.RegularExpressions;
using ModlauncherIV.Core.Detection;

namespace ModlauncherIV.Core.Protection;

public enum GuardState
{
    /// <summary>The platform cannot update the game behind your back.</summary>
    Locked,

    /// <summary>The platform is allowed to update. A downgrade is at risk.</summary>
    Unlocked,

    /// <summary>This platform has no switch we could set.</summary>
    NoSwitch,

    Unknown,
}

public sealed record GuardStatus(
    GuardState State,
    string Summary,
    string? Detail,
    string? ManifestPath,
    IReadOnlyList<string> Instructions);

/// <summary>
/// Keeps the platform from undoing a downgrade.
///
/// Deliberately not a recipe: a recipe runs once, this guard has to be checked
/// again on every start. Steam resets its appmanifest by itself now and then,
/// and "verify files" undoes any downgrade regardless.
///
/// For the Rockstar Games Launcher there is no switch. The route there is a
/// different one: with FusionFix and its Legacy Addon, GTAIV.exe starts
/// directly and the launcher is out of the picture. That is more reliable than
/// offline mode, which keeps breaking after launcher updates.
/// </summary>
public static class UpdateGuard
{
    private const string AutoUpdateKey = "AutoUpdateBehavior";

    /// <summary>"1" = only update on launch. "2" would be never, but is unreliable.</summary>
    private const string LockedValue = "1";

    public static GuardStatus Check(GameInstall install) => install.Platform switch
    {
        GamePlatform.Steam => CheckSteam(install),
        GamePlatform.RockstarLauncher => new GuardStatus(
            GuardState.NoSwitch,
            "Rockstar Games Launcher: no switch against updates.",
            "The launcher cannot be stopped from checking the installation and resetting it.",
            null,
            [
                "Extract FusionFix and the FusionFix Legacy Addon into the game directory.",
                "Afterwards start the game directly via GTAIV.exe, not through the launcher.",
                "Careful: skipping the launcher can cut off access to TLAD and TBoGT.",
            ]),

        GamePlatform.Epic => new GuardStatus(
            GuardState.NoSwitch,
            "Epic: auto-update can only be turned off in the settings.",
            "There is no file we could set for it.",
            null,
            [
                "Disable automatic updates for the game in the Epic library.",
                "Never run \"Verify\" — that restores the original state.",
            ]),

        GamePlatform.Retail => new GuardStatus(
            GuardState.Locked,
            "Retail installation: nothing updates itself here.",
            null,
            null,
            []),

        _ => new GuardStatus(
            GuardState.Unknown,
            "The origin of this installation is unknown.",
            "Without a known platform there is no way to tell whether something can patch it back.",
            null,
            []),
    };

    private static GuardStatus CheckSteam(GameInstall install)
    {
        var manifest = InstallLocator.FindSteamManifest(install.Path);

        if (manifest is null)
        {
            return new GuardStatus(
                GuardState.Unknown,
                "Steam manifest not found.",
                $"Looked for appmanifest_{InstallLocator.SteamAppId}.acf above the game folder.",
                null,
                []);
        }

        var value = ReadValue(manifest, AutoUpdateKey);

        return value == LockedValue
            ? new GuardStatus(
                GuardState.Locked,
                "Steam only updates on launch.",
                "Careful: \"verify files\" still undoes a downgrade.",
                manifest,
                [])
            : new GuardStatus(
                GuardState.Unlocked,
                $"Steam may update at any time ({AutoUpdateKey} = {value ?? "not set"}).",
                "A downgrade can be reverted by that without warning.",
                manifest,
                ["mliv guard --apply sets the switch."]);
    }

    /// <summary>
    /// Sets the lock. Makes a copy of the manifest first — it lives outside the
    /// game directory and therefore outside the snapshots.
    /// </summary>
    public static bool TryLock(GameInstall install, out string message)
    {
        if (install.Platform != GamePlatform.Steam)
        {
            message = "Only Steam has a switch we could set.";
            return false;
        }

        var manifest = InstallLocator.FindSteamManifest(install.Path);
        if (manifest is null)
        {
            message = $"appmanifest_{InstallLocator.SteamAppId}.acf not found.";
            return false;
        }

        try
        {
            var backup = manifest + ".mliv-backup";
            if (!File.Exists(backup))
            {
                File.Copy(manifest, backup);
            }

            var content = File.ReadAllText(manifest);
            var pattern = $"\"{AutoUpdateKey}\"\\s*\"[^\"]*\"";
            var replacement = $"\"{AutoUpdateKey}\"\t\t\"{LockedValue}\"";

            var updated = Regex.IsMatch(content, pattern)
                ? Regex.Replace(content, pattern, replacement)
                : InsertValue(content, replacement);

            if (updated is null)
            {
                message = "The manifest has an unexpected format and was left untouched.";
                return false;
            }

            File.WriteAllText(manifest, updated);
            message = $"{AutoUpdateKey} set to {LockedValue}. Backup: {Path.GetFileName(backup)}";
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            message = $"Manifest is not writable: {e.Message}";
            return false;
        }
    }

    /// <summary>Inserts the key after the opening brace when it is missing.</summary>
    private static string? InsertValue(string content, string line)
    {
        var brace = content.IndexOf('{');
        return brace < 0
            ? null
            : content.Insert(brace + 1, Environment.NewLine + "\t" + line);
    }

    private static string? ReadValue(string file, string key)
    {
        try
        {
            var match = Regex.Match(
                File.ReadAllText(file), $"\"{Regex.Escape(key)}\"\\s*\"([^\"]*)\"");

            return match.Success ? match.Groups[1].Value : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
