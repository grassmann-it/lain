using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Lain.Client;
using Lain.Core;
using Lain.Desktop.Presentation;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private sealed record ProfileRouterGroup(string Id, string Label, string[] RouteIds, bool NeedsAccount = false)
    { public override string ToString() => Label; }
    private sealed record ProfileCollectionDraft(string? Name, string[] Members);
    private sealed record ProfileCollectionOrderDraft(string[] GroupIds);

    private void ShowProfileGroups(string profileId)
    {
        if (!CanOpenSectionEditor() || _configuration is not { } cfg) return;
        var profile = cfg.Profiles.FirstOrDefault(value => value.Id == profileId);
        if (profile is null) { _notice.Text = "This profile was removed. Review Profiles."; return; }
        RegisterEditor(() => ShowProfileGroups(profileId), cfg.Revision, profile.Name + " · model groups");
        _editing = true;
        var create = Ui.Button("Create group", () => EditProfileCollection(profileId, null));
        var order = Ui.Button("Change group order", () => EditProfileCollectionOrder(profileId));
        order.IsEnabled = (cfg.Collections?.Length ?? 0) > 1;
        var body = Ui.Stack(ProfileHeader("Model groups", profile.Name + " · Saved revision " + cfg.Revision),
            Ui.Text("Custom groups are global collections of existing models. Changes also affect every other profile that uses these models. Only models selected in a profile appear in its export.", 12, Ui.Warning),
            Ui.Text("Save, global Apply and Codex installation are separate steps. After saving, apply the chosen profile and prepare/install a new grouped Codex export.", 12, Ui.Muted),
            ProfileWrap(create, order));
        AutomationProperties.SetName(body, "Profile model groups");
        var collections = cfg.Collections ?? [];
        if (collections.Length == 0) body.Children.Add(Ui.Text("No custom groups yet. Automatic provider views are derived from the profile's selected models.", 12, Ui.Muted));
        foreach (var collection in collections)
        {
            var id = collection.Id;
            var selected = collection.RouteIds.Count(profile.RouteIds.Contains);
            ObjectCommand[] commands =
            [
                new("edit", "Edit group", () => EditProfileCollection(profileId, id)),
                new("remove", "Remove group…", () => RemoveProfileCollection(profileId, id))
            ];
            var content = Ui.Stack(Ui.Text(collection.Name, 15),
                Ui.Text($"{collection.RouteIds.Length} members globally · {selected} selected in this profile", 12, Ui.Muted),
                Ui.Text("Group ID: " + collection.Id, 11, Ui.Muted), ProfileGroupCommands(commands));
            content.ContextMenu = ObjectCommandMenu(commands);
            body.Children.Add(Ui.Card(content));
        }
        body.Children.Add(Ui.Text("AUTOMATIC ROUTING FOR " + profile.Name, 13));
        body.Children.Add(Ui.Text("Provider/account views are derived, not copied. An automatic target walks saved model order for a new conversation, then keeps one model and account. Provider errors never trigger another member.", 12, Ui.Muted));
        var newRouter = Ui.Button("Create automatic target", () => EditProfileRouter(profileId, null));
        AutomationProperties.SetName(newRouter, "Create profile automatic target");
        body.Children.Add(newRouter);
        var routers = (cfg.CollectionRouters ?? []).Where(router => router.ProfileId == profileId).ToArray();
        foreach (var router in routers)
        {
            var id = router.Id;
            var choice = ProfileRouterGroups(cfg, profile, router.GroupId).FirstOrDefault(group => group.Id == router.GroupId);
            var ambiguous = choice?.NeedsAccount == true;
            ObjectCommand[] commands =
            [
                new("edit", ambiguous ? "Choose account target…" : "Edit automatic target", () => EditProfileRouter(profileId, id)),
                new("remove", "Remove automatic target…", () => RemoveProfileRouter(profileId, id))
            ];
            var content = Ui.Stack(Ui.Text(router.Alias, 14),
                Ui.Text((choice?.Label ?? router.GroupId) + (router.Enabled ? " · enabled" : " · disabled"), 12),
                Ui.Text("Policy: " + router.CostPolicy + " · Router ID: " + id, 11, Ui.Muted), ProfileGroupCommands(commands));
            if (ambiguous) content.Children.Add(Ui.Text("This provider now has multiple exported accounts. Choose one explicit account below before Apply or grouped export. Accounts will not be merged automatically.", 12, Ui.Warning));
            content.ContextMenu = ObjectCommandMenu(commands); body.Children.Add(Ui.Card(content));
        }
        if (routers.Length == 0) body.Children.Add(Ui.Text("No automatic targets are saved for this profile. Manual model selection remains available.", 12, Ui.Muted));
        body.Children.Add(Ui.Text("Free is a group name, not a cost guarantee. Free automation requires effective free_only routes and a fresh same-key zero-USD cap. This editor does not change key caps or routing cost policies.", 12, Ui.Muted));
        body.Children.Add(Ui.Button("Back to profiles", () => { _editing = false; Render(); }));
        _page.Content = body;
    }

    // Each visible button and menu item invokes the same frozen command. Native
    // Button/menu keyboard behavior supplies Enter/Space and Shift+F10 parity.
    private Control ProfileGroupCommands(ObjectCommand[] commands)
    {
        var buttons = commands.Select(command =>
        {
            var button = Ui.Button(command.Label, command.Execute);
            button.IsEnabled = command.Available;
            return (Control)button;
        }).ToArray();
        var row = ProfileWrap(buttons);
        row.ContextMenu = ObjectCommandMenu(commands);
        row.KeyDown += (_, e) =>
        {
            if (e.Key != Key.F10 || !e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
            row.ContextMenu?.Open(row); e.Handled = true;
        };
        return row;
    }

    private void EditProfileCollection(string profileId, string? collectionId) => EditProfileCollectionDraft(profileId, collectionId, null);

    private void EditProfileCollectionDraft(string profileId, string? collectionId, ProfileCollectionDraft? retained)
    {
        if (!CanOpenSectionEditor() || _configuration is not { } cfg) return;
        var profile = cfg.Profiles.FirstOrDefault(value => value.Id == profileId);
        if (profile is null) { _notice.Text = "This profile was removed. Review Profiles."; return; }
        var original = (cfg.Collections ?? []).FirstOrDefault(value => value.Id == collectionId);
        if (collectionId is not null && original is null) { _notice.Text = "This group was removed. Reload groups; no replacement was selected."; return; }
        _editing = true;
        var id = original?.Id ?? "group-" + Guid.NewGuid().ToString("N");
        var name = Ui.Input(retained?.Name ?? original?.Name, "Group name"); AutomationProperties.SetName(name, "Custom model group name");
        var members = retained?.Members.ToList() ?? original?.RouteIds.ToList() ?? [];
        RegisterEditor(() =>
        {
            RequireProfileGroupRebuild(profileId, collectionId);
            EditProfileCollectionDraft(profileId, collectionId, new(name.Text, members.ToArray()));
        }, cfg.Revision, (original?.Name ?? "New group") + " · global group");
        var selectedRows = new StackPanel { Spacing = 6 };
        var availableRows = new StackPanel { Spacing = 6 };
        var search = Ui.Input(hint: "Search existing models…"); AutomationProperties.SetName(search, "Search models for group membership");
        var summary = Ui.Text("", 12, Ui.Muted);
        var body = Ui.Stack(ProfileHeader(original is null ? "Create model group" : "Edit model group", profile.Name + " · Global collection · Revision " + cfg.Revision),
            Ui.Text("This collection is shared across profiles. Membership does not enable, hide or duplicate a model. Export includes only members selected by its profile.", 12, Ui.Warning),
            Ui.Field("Group name", name), summary, Ui.Text("MEMBERS · SAVED ORDER", 12), selectedRows,
            Ui.Text("ADD EXISTING MODELS", 12), search, availableRows);
        void RenderMembers()
        {
            selectedRows.Children.Clear(); availableRows.Children.Clear();
            summary.Text = $"{members.Count} members globally · {members.Count(profile.RouteIds.Contains)} in {profile.Name}";
            for (var index = 0; index < members.Count; index++)
            {
                var memberId = members[index]; var position = index;
                var route = cfg.Routes.FirstOrDefault(value => value.Id == memberId);
                ObjectCommand[] commands =
                [
                    new("up", "Move up", () => { MoveProfileGroupItem(members, memberId, -1); RenderMembers(); FocusMember(memberId); }, position > 0),
                    new("down", "Move down", () => { MoveProfileGroupItem(members, memberId, 1); RenderMembers(); FocusMember(memberId); }, position < members.Count - 1),
                    new("remove", "Remove membership", () => { members.Remove(memberId); RenderMembers(); search.Focus(); })
                ];
                var row = Ui.Stack(Ui.Text($"{position + 1}. " + (route?.Alias ?? memberId + " · missing"), 13),
                    Ui.Text(ProfileGroupMemberDescription(cfg, profile, route), 11, route is null || !route.Enabled ? Ui.Warning : Ui.Muted), ProfileGroupCommands(commands));
                row.Tag = memberId; row.ContextMenu = ObjectCommandMenu(commands);
                selectedRows.Children.Add(row);
            }
            if (members.Count == 0) selectedRows.Children.Add(Ui.Text("No members. Add existing models below; an empty group will not appear in the client menu.", 12, Ui.Muted));
            foreach (var route in cfg.Routes.Where(route => !members.Contains(route.Id)
                && $"{route.Alias} {route.UpstreamModel} {route.Provider}".Contains(search.Text?.Trim() ?? "", StringComparison.OrdinalIgnoreCase)))
            {
                var memberId = route.Id;
                var add = Ui.Button("Add " + route.Alias, () => { members.Add(memberId); RenderMembers(); FocusMember(memberId); });
                AutomationProperties.SetName(add, "Add model " + route.Alias + " to group");
                availableRows.Children.Add(Ui.Stack(add, Ui.Text(ProfileGroupMemberDescription(cfg, profile, route), 11, Ui.Muted)));
            }
        }
        void FocusMember(string memberId)
        {
            var row = selectedRows.Children.OfType<StackPanel>().FirstOrDefault(control => Equals(control.Tag, memberId));
            Avalonia.Threading.Dispatcher.UIThread.Post(() => row?.GetLogicalDescendants().OfType<Button>().FirstOrDefault(button => button.IsEnabled)?.Focus());
        }
        search.TextChanged += (_, _) => RenderMembers();
        var save = Action("Save group", async () =>
        {
            RequireCurrentProfileDraft(cfg.Revision, profileId);
            if (collectionId is not null && !(_configuration!.Collections ?? []).Any(value => value.Id == collectionId))
                throw new InvalidOperationException("This group was removed. Your draft is kept; it cannot recreate the removed group.");
            if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException("Enter a group name. Your draft is kept.");
            if (members.Any(member => !_configuration!.Routes.Any(route => route.Id == member)))
                throw new InvalidOperationException("A selected model was removed. Review its membership before saving.");
            _configuration = await _client.SaveCollectionAsync(cfg.Revision, new(id, name.Text.Trim(), members.ToArray(), original?.RouterTargetId), _lifetime.Token);
        }, "Global model group saved. Apply and client installation remain separate.", afterSuccess: () => ShowProfileGroups(profileId));
        save.Classes.Add("primary");
        body.Children.Add(ProfileWrap(save, Ui.Button("Cancel", () => ShowProfileGroups(profileId))));
        _page.Content = body; RenderMembers();
    }

    private static string ProfileGroupMemberDescription(ConfigurationDto cfg, ProfileDto profile, RouteDto? route)
    {
        if (route is null) return "Model no longer exists. Remove this membership before saving.";
        var account = string.Join(", ", route.AccountIds.Select(id => cfg.Accounts.FirstOrDefault(value => value.Id == id)?.Name ?? id));
        return $"{route.Provider} · {account} · {(route.Pooled ? "pool" : "strict binding")} · "
            + (!route.Enabled ? "disabled" : !route.Visible ? "hidden from client menu" : "visible")
            + (profile.RouteIds.Contains(route.Id) ? " · selected in this profile" : " · not selected in this profile");
    }

    private static void MoveProfileGroupItem(List<string> ids, string id, int delta)
    {
        var index = ids.IndexOf(id); var next = index + delta;
        if (index < 0 || next < 0 || next >= ids.Count) return;
        (ids[index], ids[next]) = (ids[next], ids[index]);
    }

    private void EditProfileCollectionOrder(string profileId) => EditProfileCollectionOrderDraft(profileId, null);

    private void EditProfileCollectionOrderDraft(string profileId, ProfileCollectionOrderDraft? retained)
    {
        if (!CanOpenSectionEditor() || _configuration is not { } cfg) return;
        RequireCurrentProfileDraft(cfg.Revision, profileId);
        _editing = true;
        var currentIds = (cfg.Collections ?? []).Select(value => value.Id).ToArray();
        // Preserve the edited relative order of surviving identities. Newly added
        // groups append in saved order; removed groups cannot be resurrected.
        var ids = (retained?.GroupIds ?? currentIds).Where(currentIds.Contains).Concat(currentIds.Where(id => retained is not null && !retained.GroupIds.Contains(id))).ToList();
        RegisterEditor(() =>
        {
            RequireProfileGroupRebuild(profileId);
            EditProfileCollectionOrderDraft(profileId, new(ids.ToArray()));
        }, cfg.Revision, "Global group order");
        var rows = new StackPanel { Spacing = 8 };
        void Draw()
        {
            rows.Children.Clear();
            for (var index = 0; index < ids.Count; index++)
            {
                var id = ids[index]; var position = index;
                ObjectCommand[] commands =
                [
                    new("up", "Move group up", () => { MoveProfileGroupItem(ids, id, -1); Draw(); Focus(id); }, position > 0),
                    new("down", "Move group down", () => { MoveProfileGroupItem(ids, id, 1); Draw(); Focus(id); }, position < ids.Count - 1)
                ];
                var row = Ui.Stack(Ui.Text($"{position + 1}. " + cfg.Collections!.Single(value => value.Id == id).Name), ProfileGroupCommands(commands));
                row.Tag = id; row.ContextMenu = ObjectCommandMenu(commands); rows.Children.Add(row);
            }
        }
        void Focus(string id) => Avalonia.Threading.Dispatcher.UIThread.Post(() => rows.Children.OfType<StackPanel>()
            .FirstOrDefault(row => Equals(row.Tag, id))?.GetLogicalDescendants().OfType<Button>().FirstOrDefault(button => button.IsEnabled)?.Focus());
        var save = Action("Save group order", async () =>
        {
            RequireCurrentProfileDraft(cfg.Revision, profileId);
            _configuration = await _client.ReorderCollectionsAsync(cfg.Revision, ids.ToArray(), _lifetime.Token);
        }, "Global group order saved. Apply and client installation remain separate.", afterSuccess: () => ShowProfileGroups(profileId));
        var scope = retained is not null && !retained.GroupIds.Order().SequenceEqual(currentIds.Order())
            ? "The group inventory changed. Surviving groups retain your edited order; removed groups are omitted and new groups append below. Review before saving."
            : "Affects every profile; automatic provider views remain first.";
        _page.Content = Ui.Stack(ProfileHeader("Global group order", scope), rows,
            ProfileWrap(save, Ui.Button("Cancel", () => ShowProfileGroups(profileId))));
        Draw();
    }

    private void RequireProfileGroupRebuild(string profileId, string? collectionId = null)
    {
        if (_configuration is not { } cfg || cfg.Profiles.All(profile => profile.Id != profileId))
            throw new InvalidOperationException("This profile was removed. Your group draft is kept; cancel and review Profiles.");
        if (collectionId is not null && !(cfg.Collections ?? []).Any(collection => collection.Id == collectionId))
            throw new InvalidOperationException("This group was removed. Your draft is kept; it cannot target a replacement or recreate the removed group.");
    }

    private void RemoveProfileCollection(string profileId, string collectionId)
    {
        if (!CanOpenSectionEditor() || _configuration is not { } cfg) return;
        var collection = (cfg.Collections ?? []).FirstOrDefault(value => value.Id == collectionId);
        if (collection is null) { _notice.Text = "This group was removed. Reload groups."; return; }
        RegisterEditor(() => RemoveProfileCollection(profileId, collectionId), cfg.Revision, collection.Name + " · remove group"); _editing = true;
        var references = (cfg.CollectionRouters ?? []).Where(router => router.GroupId == "collection:" + collectionId).ToArray();
        var body = Ui.Stack(ProfileHeader("Remove " + collection.Name + "?", "Global collection · existing models, route IDs and history are retained."));
        foreach (var router in references)
        {
            var owner = cfg.Profiles.FirstOrDefault(value => value.Id == router.ProfileId);
            body.Children.Add(Ui.Text("Referenced by " + router.Alias + " in " + (owner?.Name ?? router.ProfileId) + ". Remove or retarget that automatic target first.", 12, Ui.Warning));
            body.Children.Add(Ui.Button("Review automatic target in " + (owner?.Name ?? router.ProfileId), () => EditProfileRouter(router.ProfileId, router.Id)));
        }
        if (references.Length == 0) body.Children.Add(Action("Remove group", async () =>
        {
            RequireCurrentProfileDraft(cfg.Revision, profileId);
            _configuration = await _client.RemoveCollectionAsync(cfg.Revision, collectionId, _lifetime.Token);
        }, "Global group removed. Models and history retained.", afterSuccess: () => ShowProfileGroups(profileId)));
        body.Children.Add(Ui.Button("Cancel", () => ShowProfileGroups(profileId))); _page.Content = body;
    }

    private static ProfileRouterGroup[] ProfileRouterGroups(ConfigurationDto cfg, ProfileDto profile, string? existingGroup = null)
    {
        var result = new List<ProfileRouterGroup>();
        var routes = profile.RouteIds.Select(id => cfg.Routes.FirstOrDefault(route => route.Id == id)).OfType<RouteDto>().ToArray();
        var visible = routes.Where(route => route.Enabled && route.Visible && !(route.Provider == "codex" && route.UpstreamModel == "codex-auto-review")).ToArray();
        foreach (var provider in visible.GroupBy(route => route.Provider.ToLowerInvariant()))
        {
            var accounts = provider.Where(route => !route.Pooled && route.AccountIds.Length == 1)
                .SelectMany(route => route.AccountIds).Where(id => cfg.Accounts.Any(account => account.Id == id && account.Enabled)).Distinct().ToArray();
            var ambiguous = accounts.Length > 1;
            var providerId = "provider:" + provider.Key;
            if (!ambiguous || existingGroup == providerId)
                result.Add(new(providerId, ProviderDisplayNames.Get(provider.Key) + (ambiguous ? " · choose a specific account" : " · provider order"),
                    provider.Select(route => route.Id).ToArray(), ambiguous));
            foreach (var accountId in accounts)
            {
                var account = cfg.Accounts.Single(value => value.Id == accountId);
                result.Add(new("account:" + accountId, ProviderDisplayNames.Get(provider.Key) + " (" + account.Name + ") · " + accountId,
                    provider.Where(route => !route.Pooled && route.AccountIds.Length == 1 && route.AccountIds[0] == accountId).Select(route => route.Id).ToArray()));
            }
        }
        foreach (var collection in cfg.Collections ?? [])
            result.Add(new("collection:" + collection.Id, collection.Name + " · custom group", collection.RouteIds.Where(profile.RouteIds.Contains).ToArray()));
        if (existingGroup == "policy:free") result.Add(new("policy:free", "Free · existing free-only policy", profile.RouteIds));
        if (existingGroup is not null && result.All(group => group.Id != existingGroup))
            result.Add(new(existingGroup, existingGroup + " · no current members", []));
        return result.ToArray();
    }

    private void EditProfileRouter(string profileId, string? routerId)
    {
        if (!CanOpenSectionEditor() || _configuration is not { } cfg) return;
        var profile = cfg.Profiles.FirstOrDefault(value => value.Id == profileId);
        if (profile is null) { _notice.Text = "The router's profile was removed. Review Profiles."; return; }
        var original = (cfg.CollectionRouters ?? []).FirstOrDefault(value => value.Id == routerId);
        if (routerId is not null && (original is null || original.ProfileId != profileId))
        { _notice.Text = "This automatic target was removed or belongs to another profile. No replacement was selected."; return; }
        RegisterEditor(() => EditProfileRouter(profileId, routerId), cfg.Revision, (original?.Alias ?? "New automatic target") + " · " + profile.Name);
        _editing = true;
        var choices = ProfileRouterGroups(cfg, profile, original?.GroupId);
        var group = new ComboBox { ItemsSource = choices, SelectedItem = choices.FirstOrDefault(value => value.Id == original?.GroupId),
            HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(group, "Automatic target group or account");
        var references = (cfg.Collections ?? []).Where(collection => collection.RouterTargetId == routerId && routerId is not null).ToArray();
        group.IsEnabled = references.Length == 0;
        var alias = Ui.Input(original?.Alias, "Client name for automatic target"); AutomationProperties.SetName(alias, "Automatic target client name");
        var enabled = new CheckBox { Content = "Enabled after a separate global Apply", IsChecked = original?.Enabled ?? true };
        AutomationProperties.SetName(enabled, "Automatic target enabled");
        var explanation = Ui.Text("Choose a group or one explicit account. No account is selected automatically.", 12, Ui.Muted);
        AutomationProperties.SetName(explanation, "Automatic target scope explanation");
        var members = new StackPanel { Spacing = 4 };
        void Update()
        {
            members.Children.Clear();
            if (group.SelectedItem is not ProfileRouterGroup choice) return;
            explanation.Text = choice.NeedsAccount ? "This provider spans multiple exported accounts. Select an explicit account target before saving. Saving retains the router ID and does not merge accounts."
                : choice.RouteIds.Length == 0 ? "No members are selected by this profile. Add profile models or choose another target before saving."
                : "Saved order shown below. Only the Host's Apply/export compiler can confirm executable members and transport compatibility. Pools are never treated as a single account.";
            foreach (var id in choice.RouteIds)
            {
                var route = cfg.Routes.FirstOrDefault(value => value.Id == id);
                members.Children.Add(Ui.Text((route?.Alias ?? id) + " · " + ProfileGroupMemberDescription(cfg, profile, route), 12, Ui.Muted));
            }
        }
        group.SelectionChanged += (_, _) => Update();
        var save = Action("Save automatic target", async () =>
        {
            RequireCurrentProfileDraft(cfg.Revision, profileId);
            if (routerId is not null && !(_configuration!.CollectionRouters ?? []).Any(value => value.Id == routerId && value.ProfileId == profileId))
                throw new InvalidOperationException("This automatic target was removed. Your draft is kept; it cannot recreate the removed target.");
            if (group.SelectedItem is not ProfileRouterGroup choice || choice.NeedsAccount)
                throw new InvalidOperationException("Choose an explicit group or account target. Multiple accounts will not be joined automatically.");
            if (original is not null && choice.Id != original.GroupId && references.Length > 0)
                throw new InvalidOperationException("This automatic target is referenced by a collection. Remove this automatic target first (clears its collection references), then create the new target. The existing draft is kept; nothing was changed.");
            if (choice.RouteIds.Length == 0) throw new InvalidOperationException("The target has no models selected in this profile. Review profile membership first.");
            if (string.IsNullOrWhiteSpace(alias.Text)) throw new InvalidOperationException("Enter a client name for this automatic target.");
            _configuration = await _client.SaveCollectionRouterAsync(cfg.Revision,
                new(original?.Id ?? "router-" + Guid.NewGuid().ToString("N"), alias.Text.Trim(), choice.Id, profileId,
                    original?.CostPolicy ?? "configured", enabled.IsChecked == true), _lifetime.Token);
        }, "Automatic target saved. Apply and grouped client installation remain separate.", afterSuccess: () => ShowProfileGroups(profileId));
        var body = Ui.Stack(ProfileHeader(original is null ? "Create automatic target" : "Edit automatic target", profile.Name + " · Revision " + cfg.Revision),
            Ui.Field("Client model name", alias), Ui.Field("Group or account target", group), enabled, explanation, members,
            Ui.Text("Policy: " + (original?.CostPolicy ?? "configured") + ". This editor preserves cost rules. Free-only automation requires qualified routes and a fresh same-key zero-USD cap; it is not activated by naming a group Free.", 12, Ui.Muted),
            ProfileWrap(save, Ui.Button("Cancel", () => ShowProfileGroups(profileId))));
        if (references.Length > 0) body.Children.Insert(1, Ui.Text("Target group locked: referenced by " + string.Join(", ", references.Select(collection => collection.Name))
            + ". To change its group, cancel this editor, remove the automatic target to clear these references, then create a new target. Collections and models are retained.", 12, Ui.Warning));
        _page.Content = body; Update();
    }

    private void RemoveProfileRouter(string profileId, string routerId)
    {
        if (!CanOpenSectionEditor() || _configuration is not { } cfg) return;
        var router = (cfg.CollectionRouters ?? []).FirstOrDefault(value => value.Id == routerId && value.ProfileId == profileId);
        if (router is null) { _notice.Text = "This automatic target was removed. Reload groups."; return; }
        RegisterEditor(() => RemoveProfileRouter(profileId, routerId), cfg.Revision, router.Alias + " · remove automatic target"); _editing = true;
        var remove = Action("Remove automatic target", async () =>
        {
            RequireCurrentProfileDraft(cfg.Revision, profileId);
            _configuration = await _client.RemoveCollectionRouterAsync(cfg.Revision, routerId, _lifetime.Token);
        }, "Automatic target removed. Manual models, memberships and history retained.", afterSuccess: () => ShowProfileGroups(profileId));
        _page.Content = Ui.Stack(ProfileHeader("Remove " + router.Alias + "?", "Saved automatic target only; no Apply, model removal or history deletion."),
            ProfileWrap(remove, Ui.Button("Cancel", () => ShowProfileGroups(profileId))));
    }
}
