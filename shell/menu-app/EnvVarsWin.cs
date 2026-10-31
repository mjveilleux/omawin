// The registry behind `omawin env` (EnvVars.cs, roadmap 8.36): yours under HKCU\Environment, the
// PC's under HKLM's Session Manager\Environment, read raw (%VARS% unexpanded) so a change writes
// back exactly what it means. The PC's are written by bin/settings-admin.ps1 in the one prompt.

using Microsoft.Win32;

namespace SystemMenu;

sealed class EnvVarsWin : IEnvStore
{
    const string UserKey = "Environment";
    const string MachineKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";

    public static readonly EnvVarsWin Instance = new();
    public static SettingHistory History { get; } = new(Path.Combine(MenuModel.StateDir, "env-history.tsv"));

    static RegistryKey? Open(EnvScope scope, bool write = false) => scope == EnvScope.User
        ? (write ? Registry.CurrentUser.CreateSubKey(UserKey) : Registry.CurrentUser.OpenSubKey(UserKey))
        : Registry.LocalMachine.OpenSubKey(MachineKey);

    public IReadOnlyList<EnvVar> All(EnvScope scope)
    {
        using var k = Open(scope);
        return k == null ? [] : k.GetValueNames().Where(n => n.Length > 0).Select(n => Read(k, n)).OfType<EnvVar>().ToList();
    }

    public EnvVar? Get(EnvScope scope, string name)
    {
        using var k = Open(scope);
        return k == null ? null : Read(k, name);
    }

    static EnvVar? Read(RegistryKey k, string name)
    {
        // The name as Windows stores it (case-insensitive lookups, but shown as written).
        var stored = k.GetValueNames().FirstOrDefault(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (stored == null) return null;
        var value = k.GetValue(stored, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return value == null ? null : new EnvVar(stored, SettingValues.RawText(value) ?? "", k.GetValueKind(stored) == RegistryValueKind.ExpandString);
    }

    public string? SetUser(string name, string? value)
    {
        using var k = Open(EnvScope.User, write: true)!;
        var stored = k.GetValueNames().FirstOrDefault(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? name;
        if (value == null) { k.DeleteValue(stored, throwOnMissingValue: false); return null; }
        // A value naming another variable (%USERPROFILE%\bin) is kept unexpanded, as Windows' dialog does.
        bool expand = value.Contains('%') || (Read(k, stored)?.Expand ?? false);
        k.SetValue(stored, value, expand ? RegistryValueKind.ExpandString : RegistryValueKind.String);
        return null;
    }

    public async Task<(bool Ran, string? Error)> SetMachineAsync(string name, string? value)
    {
        var line = string.Join('\t', EnvVars.Id(EnvScope.Machine, name), "env", name, "", EnvVars.Recorded(value));
        var run = await WinSettings.RunElevated([line]);
        return (run.Ran, run.Failures.FirstOrDefault());
    }

    public void Announce() => SettingApply.Broadcast("Environment");
}
