using System.IO;
using Microsoft.Web.WebView2.Core;
using ScarletMeter.App.Settings;

namespace ScarletMeter.App.Windows;

/// <summary>
/// Virtual hosts through which the pages load images that are not part of the app's own web assets. Each
/// maps exactly one folder, so the pages can never read the history database, logs or settings:
/// <see cref="Data"/> is the data directory's backgrounds folder, <see cref="Assets"/> the class icons
/// copied from upstream's UI at build time. The ".example" domain is reserved and never resolves.
/// </summary>
public static class WebAssetHosts
{
    public const string Data = "data.scarlet.example";
    public const string Assets = "assets.scarlet.example";

    /// <summary>The folder beside the executable that holds icons taken from upstream's UI project.</summary>
    public static string AssetsFolder => Path.Combine(AppContext.BaseDirectory, "assets");

    /// <summary>The URL of an imported background picture, or null when none is set.</summary>
    public static string? BackgroundUrl(string? storedName) =>
        storedName is not null && BackgroundImages.IsStoredName(storedName) ? $"https://{Data}/{storedName}" : null;

    /// <summary>The URL of a class icon (upstream ships 11 to 19), or null when there is none.</summary>
    public static string? ClassIconUrl(int classId) => classId is >= 11 and <= 19 ? $"https://{Assets}/classes/{classId}.png" : null;

    /// <summary>Maps both hosts on a newly initialized WebView2. Missing folders are created so the mapping succeeds.</summary>
    public static void Map(CoreWebView2 webView, string dataDirectory)
    {
        var backgrounds = BackgroundImages.Folder(dataDirectory);
        Directory.CreateDirectory(backgrounds);
        webView.SetVirtualHostNameToFolderMapping(Data, backgrounds, CoreWebView2HostResourceAccessKind.DenyCors);
        if (Directory.Exists(AssetsFolder))
            webView.SetVirtualHostNameToFolderMapping(Assets, AssetsFolder, CoreWebView2HostResourceAccessKind.DenyCors);
    }
}
