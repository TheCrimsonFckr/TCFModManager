using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;
using TCFModManager.Core.ServerMap;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

/// <summary>A machine a profile can be given to, or nobody (ClientId null).</summary>
public sealed record ProfileOwnerChoice(string? ClientId, string Name)
{
    public override string ToString() => Name;
}

/// <summary>One profile on this machine's server and the machine it belongs to.</summary>
public sealed partial class ProfileOwnerRow : ObservableObject
{
    private readonly Func<ProfileOwnerRow, ProfileOwnerChoice, bool> _assign;
    private bool _loading;

    public ProfileOwnerRow(PlayProfileItem profile, IReadOnlyList<ProfileOwnerChoice> choices, ProfileOwnerChoice owner,
        Func<ProfileOwnerRow, ProfileOwnerChoice, bool> assign)
    {
        Profile = profile;
        Choices = choices;
        _assign = assign;
        _loading = true;
        Owner = owner;
        _loading = false;
    }

    public PlayProfileItem Profile { get; }

    public IReadOnlyList<ProfileOwnerChoice> Choices { get; }

    [ObservableProperty]
    private ProfileOwnerChoice _owner;

    private ProfileOwnerChoice? _previous;

    partial void OnOwnerChanging(ProfileOwnerChoice value) => _previous = Owner;

    partial void OnOwnerChanged(ProfileOwnerChoice value)
    {
        if (_loading || _assign(this, value)) return;

        _loading = true;
        Owner = _previous ?? Choices[0];
        _loading = false;
    }
}

//
// Profile rules for the server running on THIS machine: the two switches beside LAN-only in Options
// (written to servermap.json) and, on the Server map page, which machine owns each profile (written
// to profile-owners.json). Only offered where the key file is found.
//
public sealed partial class ServerMapGateViewModel
{
    public ObservableCollection<ProfileOwnerRow> ProfileOwnerRows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoProfileOwnerRows))]
    private bool _isServerHost;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOwnersNotice))]
    private string? _ownersNotice;

    public bool HasOwnersNotice => !string.IsNullOrWhiteSpace(OwnersNotice);

    public bool HasNoProfileOwnerRows => IsServerHost && ProfileOwnerRows.Count == 0;

    [ObservableProperty]
    private bool _serverHideOtherProfiles;

    [ObservableProperty]
    private bool _serverProtectOtherProfiles;

    private bool _loadingProfileRules;

    private void RefreshProfileRules(string? path)
    {
        _loadingProfileRules = true;
        ServerHideOtherProfiles = path is not null && ServerMapServerSettings.ReadSwitch(path, ServerMapServerSettings.HideOtherProfilesProperty);
        ServerProtectOtherProfiles = path is not null && ServerMapServerSettings.ReadSwitch(path, ServerMapServerSettings.ProtectOtherProfilesProperty);
        _loadingProfileRules = false;
    }

    partial void OnServerHideOtherProfilesChanged(bool value) =>
        WriteProfileRule(ServerMapServerSettings.HideOtherProfilesProperty, value, () => ServerHideOtherProfiles = !value);

    partial void OnServerProtectOtherProfilesChanged(bool value) =>
        WriteProfileRule(ServerMapServerSettings.ProtectOtherProfilesProperty, value, () => ServerProtectOtherProfiles = !value);

    private void WriteProfileRule(string property, bool value, Action undo)
    {
        if (_loadingProfileRules) return;

        var path = ServerMapServerSettings.PathFor(_settings.Load().SptInstallPath);
        if (path is not null && ServerMapServerSettings.TryWriteSwitch(path, property, value)) return;

        KeyNotice = Strings.Options_ServerMapProfileRulesFailed;
        _loadingProfileRules = true;
        undo();
        _loadingProfileRules = false;
    }

    public void RefreshProfileOwners(string? sptInstallPath)
    {
        var directory = ServerMapProfileOwners.DirectoryFor(sptInstallPath);
        IsServerHost = directory is not null;
        OwnersNotice = null;
        ProfileOwnerRows.Clear();

        if (directory is not null)
        {
            var owners = ServerMapProfileOwners.Read(directory);
            var machines = ServerMapProfileOwners.Machines(directory)
                .Select(m => new ProfileOwnerChoice(m.ClientId, m.DisplayName.Length > 0 ? m.DisplayName : m.ClientId))
                .ToList();
            var nobody = new ProfileOwnerChoice(null, Strings.ServerMap_OwnerNobody);

            foreach (var profile in SptLocalProfiles.Read(sptInstallPath))
            {
                var choices = new List<ProfileOwnerChoice> { nobody };
                choices.AddRange(machines);

                var owner = nobody;
                if (owners.TryGetValue(profile.ProfileId, out var clientId))
                {
                    owner = machines.FirstOrDefault(m => string.Equals(m.ClientId, clientId, StringComparison.OrdinalIgnoreCase))
                        ?? new ProfileOwnerChoice(clientId, Text(Strings.ServerMap_OwnerUnknownFormat, clientId[..Math.Min(8, clientId.Length)]));
                    if (!choices.Contains(owner)) choices.Add(owner);
                }

                ProfileOwnerRows.Add(new ProfileOwnerRow(new PlayProfileItem(profile), choices, owner, (row, choice) => Assign(directory, row, choice)));
            }
        }

        OnPropertyChanged(nameof(HasNoProfileOwnerRows));
    }

    private bool Assign(string directory, ProfileOwnerRow row, ProfileOwnerChoice choice)
    {
        if (ServerMapProfileOwners.TryAssign(directory, row.Profile.Profile.ProfileId, choice.ClientId))
        {
            OwnersNotice = null;
            return true;
        }

        OwnersNotice = Strings.ServerMap_OwnerFailed;
        return false;
    }
}
