using FluidBar.Plugins.Builtin;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Capabilities;
using FluidBar.Sdk.Versioning;

namespace FluidBar.App.Plugins;

/// <summary>
/// Builds the synthetic manifest for a compiled-in plugin. The App owns the descriptor (id, name,
/// capabilities, declarative contributions), so the manifest is trusted by construction and is never
/// validated or inspected like a downloaded package.
/// </summary>
public static class BuiltinPluginManifests
{
    /// <summary>Creates the manifest for <paramref name="descriptor"/>.</summary>
    public static PluginManifest Create(BuiltinPluginDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return new PluginManifest
        {
            SchemaVersion = 2,
            Id = descriptor.Id,
            Version = "2.0.0",
            ApiVersion = PluginApiVersion.Current.ToString(),
            MinAppVersion = "2.0.0",
            Publisher = new PublisherInfo
            {
                Id = "fluidbar",
                DisplayName = "FluidBar",
                Url = "https://fluidbar.doulor.cn",
            },
            Name = descriptor.DisplayName,
            Summary = LocalizedString.From("Built-in FluidBar plugin."),
            License = "MIT",
            Categories = [descriptor.Category],
            Entry = new PluginEntryPoint
            {
                Runtime = PluginEntryRuntime.InProcess,
                Assembly = "FluidBar.Plugins.Builtin.dll",
                Type = descriptor.Id,
                TargetFramework = "net10.0",
            },
            Platforms = ["win-x64"],
            Activation = ["startup"],
            Contributes = descriptor.Contributions,
            Capabilities = CreateCapabilities(descriptor.Capabilities),
        };
    }

    private static PluginCapabilities CreateCapabilities(IReadOnlyList<PluginCapability> capabilities)
    {
        bool Has(PluginCapability capability) => capabilities.Contains(capability);
        return new PluginCapabilities
        {
            Clipboard = Has(PluginCapability.Clipboard) ? new ClipboardCapability { Read = true, Write = true } : null,
            Files = Has(PluginCapability.Files) ? new FilesCapability
            {
                Read = [FileScope.PluginData, FileScope.Pictures],
                Write = [FileScope.PluginData],
                Reason = "Stores user-selected file references and screenshot paths.",
            } : null,
            Media = Has(PluginCapability.Media) ? new MediaCapability { Sessions = true, Control = true } : null,
            Notifications = Has(PluginCapability.Notifications) ? new NotificationsCapability { Read = true } : null,
            Background = Has(PluginCapability.Background) ? new BackgroundCapability { Reason = "Runs while FluidBar is active." } : null,
            Ui = Has(PluginCapability.Ui) ? new UiCapability
            {
                Island = true, Settings = true, Commands = true, Pages = true,
                PageTemplates = ["list"],
            } : null,
        };
    }
}
