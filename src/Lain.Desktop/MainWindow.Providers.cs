using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Lain.Client;
using Lain.Core;
using Lain.Desktop.Presentation;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    // Connect editors keep their mounted private inputs through presentation changes.
    private void SetProviderPresentation(PresentationOptions options) { }

    private async Task RefreshProviderCatalogAsync()
    {
        if (_providerLoading || _lifetime.IsCancellationRequested) return;
        _providerLoading = true;
        try
        {
            var catalog = await _client.ProvidersAsync(_lifetime.Token);
            _providerCatalog = catalog;
            _providers.SetCatalog(catalog, _configuration?.Accounts);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (_providerCatalog is null) _providers.ShowUnavailable(Friendly(error));
            else if (_section == "Providers") _notice.Text = "Provider descriptions could not be refreshed. Showing the previously loaded catalog.";
        }
        finally { _providerLoading = false; }
    }

    private async Task OpenProviderLinkAsync(Uri uri)
    {
        try
        {
            if (uri.Scheme != Uri.UriSchemeHttps || !await Launcher.LaunchUriAsync(uri))
                _notice.Text = "The official page could not be opened. Check the default browser and try again.";
        }
        catch (Exception) { _notice.Text = "The official page could not be opened. Check the default browser and try again."; }
    }

}
