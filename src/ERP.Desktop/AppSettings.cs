using System.Text.Json;

namespace ERP.Desktop;

/// <summary>
/// Local, per-installation configuration for connecting to the Firebird database.
/// </summary>
/// <remarks>
/// Fixes source-of-truth Appendix A #2/#3: the database path, client library path,
/// and credentials used to be hard-coded to <c>D:\RetailERPData\...</c> and the
/// Firebird factory login, which broke on any machine (or drive layout) other than
/// the original developer's. This loads them from a per-user settings file instead,
/// defaulting to a path under LocalAppData that always exists.
///
/// The default username/password are still Firebird's own well-known factory
/// defaults ("SYSDBA"/"masterkey") for a freshly created embedded database — not a
/// real secret, and not something a real Installer should ship. A generated,
/// per-installation credential belongs to the One-click Installer (source-of-truth
/// §15.7/§15.8), which does not exist yet. Until then this file is the single place
/// that default lives, so it can be swapped out in one spot rather than searched for
/// across the codebase.
/// </remarks>
public sealed record AppSettings(
    string DatabasePath,
    string ClientLibraryPath,
    string UserName,
    string Password)
{
    private static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PishkarERP");

    private static readonly string SettingsFilePath = Path.Combine(DataDirectory, "appsettings.json");

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public static AppSettings LoadOrCreateDefault()
    {
        Directory.CreateDirectory(DataDirectory);

        if (File.Exists(SettingsFilePath))
        {
            var json = File.ReadAllText(SettingsFilePath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json);
            if (loaded is not null)
            {
                return loaded;
            }
        }

        var defaults = new AppSettings(
            Path.Combine(DataDirectory, "retail-erp.fdb"),
            Path.Combine(DataDirectory, "fbclient.dll"),
            "SYSDBA",
            "masterkey");

        File.WriteAllText(
            SettingsFilePath,
            JsonSerializer.Serialize(defaults, SerializerOptions));

        return defaults;
    }
}
