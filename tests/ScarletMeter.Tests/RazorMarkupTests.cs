using System.IO;
using System.Text.RegularExpressions;

namespace ScarletMeter.Tests;

/// <summary>
/// Static checks over the app's Razor markup for mistakes the compiler accepts: Razor reads "x@value" as an
/// e-mail address and renders it literally ("x@(value)" is the expression), and a command shown to the player
/// must not depend on the directory their shell starts in (AGENTS.md, "Paths"); and the pages' scripts stay the
/// app's own. The checkout is found from the test assembly's own location, never the working directory.
/// </summary>
public sealed partial class RazorMarkupTests
{
    [Fact]
    public void NoExpressionFollowsALetterOrDigitWithoutParentheses() =>
        Assert.Empty(Offending(line => EmailLikeExpression().IsMatch(line)));

    [Fact]
    public void ScriptCommandsShownToThePlayerAreRootedAtTheCheckout() =>
        // Every scripts\ path in the markup sits under the <checkout> placeholder (escaped in Razor markup).
        Assert.Empty(Offending(line => RelativeScriptPath().IsMatch(line)));

    // Every <script> in index.html (whatever its attributes' order) loads one of the app's own files, which exists,
    // and none is inline; the overlay's effects and size scripts reach nothing outside the page: no requests, no
    // calls into .NET, no eval.
    [Fact]
    public void ThePagesLoadOnlyTheAppsOwnScripts()
    {
        var wwwroot = Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App", "wwwroot");
        var tags = ScriptTag().Matches(File.ReadAllText(Path.Combine(wwwroot, "index.html")));
        Assert.NotEmpty(tags);
        var sources = new List<string>();
        foreach (Match tag in tags)
        {
            Assert.True(string.IsNullOrWhiteSpace(tag.Groups["body"].Value), $"inline script: {tag.Value}");
            var src = ScriptSrc().Match(tag.Groups["attributes"].Value);
            Assert.True(src.Success, $"script without src: {tag.Value}");
            sources.Add(src.Groups[1].Value);
        }
        Assert.Contains("js/overlay-fx.js", sources);
        Assert.Contains("js/overlay-size.js", sources);
        foreach (var source in sources)
        {
            Assert.DoesNotContain(":", source);
            Assert.False(source.StartsWith('/'), source);
            if (!source.StartsWith("_framework/", StringComparison.Ordinal))
                Assert.True(File.Exists(Path.Combine(wwwroot, source.Replace('/', Path.DirectorySeparatorChar))), source);
        }
        foreach (var script in new[] { "overlay-fx.js", "overlay-size.js" })
        {
            var text = File.ReadAllText(Path.Combine(wwwroot, "js", script));
            foreach (var reach in new[] { "fetch(", "XMLHttpRequest", "WebSocket", "sendBeacon", "DotNet.", "eval(", "Function(" })
                Assert.DoesNotContain(reach, text);
        }
    }

    // Nothing in the overlay scrolls: the page reports its whole content height after each render and the window grows
    // to show all of it (Windows/OverlayPlacement, OverlayFit), saving the player's own placement rather than the grown
    // one, and never mid-drag. Source-text guards: the WPF window and the WebView have no test host here;
    // OverlayFitTests and OverlayPlacementTests cover the placement rules.
    [Fact]
    public void TheOverlayWindowGrowsToItsContent()
    {
        var app = Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App");
        var page = File.ReadAllText(Path.Combine(app, "Pages", "MeterPage.razor.cs"));
        Assert.Contains("await JS.InvokeAsync<double>(\"scarletOverlay.contentHeight\"", page);
        // The fit and the "cut" question are guarded: an exception out of OnAfterRenderAsync would end the process.
        var start = page.IndexOf("protected override async Task OnAfterRenderAsync", StringComparison.Ordinal);
        var end = page.IndexOf("private void OnAlertsChanged", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var afterRender = page[start..end];
        // Which heights reach the window is OverlayFit.IsNewHeight's (tested there: the first, NaN before it, included),
        // never a comparison written out here (Codex review on PR #28).
        Assert.Matches(@"try\s*\{\s*if \(OverlayFit\.IsNewHeight\(reportedHeight, height\)\)\s*\{\s*Windows\.FitOverlayToContent\(height\);\s*reportedHeight = height;", afterRender);
        Assert.DoesNotContain("Math.Abs(height - reportedHeight)", page);
        // The window says whether it cuts the content; a change renders again, and so does a fit the window made on
        // its own (a restore, a resize's end, a display change), so the rows' fade follows the window at once.
        Assert.Matches(@"var cut = Windows\.OverlayCutsContent\(height\);[^}]*if \(cut == contentCut\) return;\s*contentCut = cut;\s*StateHasChanged\(\);", afterRender);
        Assert.Matches(@"\}\s*catch \(Exception ex\)\s*\{\s*if \(!fitFailing\)", afterRender);
        Assert.Contains("(contentCut ? \" cut\" : \"\")", page);
        Assert.Contains("Windows.OverlayFitted += OnOverlayFitted;", page);
        Assert.Contains("Windows.OverlayFitted -= OnOverlayFitted;", page);
        Assert.Contains("private void OnOverlayFitted(object? sender, EventArgs e) => _ = RenderAsync();", page);
        var windows = File.ReadAllText(Path.Combine(app, "Windows", "MeterWindows.cs"));
        Assert.Contains("public bool OverlayCutsContent(double contentHeight) => !closing && overlay?.CutsContent(contentHeight) == true;", windows);
        Assert.Contains("overlay.Fitted += (_, _) => OverlayFitted?.Invoke(this, EventArgs.Empty);", windows);
        Assert.Contains("public bool CutsContent(double height) => IsLoaded && ActualHeight < Math.Ceiling(height) - 0.5;",
            File.ReadAllText(Path.Combine(app, "Windows", "OverlayWindow.cs")));

        // The whole content: the panel's scroll height (not its client height, which the window clips) and its
        // borders, with the rows at full height while it is measured.
        var script = File.ReadAllText(Path.Combine(app, "wwwroot", "js", "overlay-size.js"));
        Assert.Contains("contentHeight()", script);
        Assert.Contains("return panel.scrollHeight + (panel.offsetHeight - panel.clientHeight);", script);
        Assert.True(script.IndexOf("panel.setAttribute('data-measuring', '');", StringComparison.Ordinal)
            < script.IndexOf("return panel.scrollHeight", StringComparison.Ordinal));
        Assert.Matches(@"finally \{\s*panel\.removeAttribute\('data-measuring'\);", script);

        var overlay = File.ReadAllText(Path.Combine(app, "Windows", "OverlayWindow.cs"));
        Assert.Matches(@"var workArea = ScreenPlacement\.WorkAreaOf\(this\);\s*var target = overlayPlacement\.Target\(contentHeight, workArea\);\s*if \(!shrinkNow && overlayPlacement\.WaitsToShrink\(target, workArea\)\)", overlay);
        Assert.Contains("if (sizingOrMoving || !IsLoaded || WindowState != WindowState.Normal", overlay);
        Assert.Contains("applying = true;", overlay);
        Assert.Contains("if (applying || sizingOrMoving", overlay);
        Assert.Contains("overlayPlacement.PlayerPlaced(Top, ActualHeight);", overlay);
        Assert.Contains("overlayPlacement.MovedTo(Top);", overlay);
        Assert.Contains("SavePlacementSoon();", overlay);
        Assert.Contains("if (changed) Fitted?.Invoke(this, EventArgs.Empty);", overlay);
        Assert.Contains("Closed += (_, _) => shrinkLater.Stop();", overlay);
        // A restore fits after WPF's own size handling, not inside StateChanged; so does a change of the display's
        // scale or arrangement, of the work area, and an outside move.
        Assert.Contains("private void RefitSoon() => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => Refit()));", overlay);
        Assert.Contains("else if (WindowState == WindowState.Normal) RefitSoon();", overlay);
        Assert.Matches(@"msg is NativeMethods\.WmDpiChanged or NativeMethods\.WmDisplayChange\s*\|\| \(msg == NativeMethods\.WmSettingChange && wParam == NativeMethods\.SpiSetWorkArea\)\)\s*\{[^}]*RefitSoon\(\);", overlay);
        Assert.Matches(@"overlayPlacement\.MovedTo\(Top\);\s*RefitSoon\(\);", overlay);
        Assert.Matches(@"protected override Rect\? PlacementBounds =>\s*sizingOrMoving \? null\s*: overlayPlacement\.Started \? new Rect\(Left, overlayPlacement\.PlayerTop, ActualWidth, overlayPlacement\.PlayerHeight\)", overlay);
        // The overlay's minimum height is the settings' floor for a saved size, so a player's smallest overlay is kept
        // (SettingsTests.TheSmallestOverlayAPlayerCanMakeIsKept).
        Assert.Contains("MinHeight = WindowPlacement.MinSize;", overlay);
        // The page's CSS pixels are the window's: its WebView does not zoom.
        Assert.Contains("protected override bool WebViewZoomable => false;", overlay);
        var host = File.ReadAllText(Path.Combine(app, "Windows", "ScarletWindow.cs"));
        Assert.Contains("if (!WebViewZoomable) e.WebView.CoreWebView2.Settings.IsZoomControlEnabled = false;", host);
        Assert.Contains("() => PlacementBounds", host);
        Assert.Contains("bounds() is not { } b) return;", File.ReadAllText(Path.Combine(app, "Windows", "WindowPlacementTracker.cs")));
    }

    // A player's row reads DPS, then the share with the total damage under it (Harry: three numbers in a line were too
    // many; the share in bold white, the total "more of a 'i should look here less'"). DPS and the share column keep
    // fixed widths so the columns line up; the total is smaller and dimmer than the share.
    [Fact]
    public void APlayersRowShowsDpsThenTheShareOverTheTotal()
    {
        var page = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App", "Pages", "MeterPage.razor"));
        var columns = RowNumberColumn().Matches(page).Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(["num dps", "num pct", "num total"], columns);
        Assert.Matches(@"<span class=""share"">\s*<span class=""num pct""[^>]*>@Compact\.Percent\(player\.DamageShare\)</span>\s*<span class=""num total""[^>]*>@Compact\.Number\(player\.TotalDamage\)</span>\s*</span>", page);
        Assert.True(page.IndexOf("player.Dps", StringComparison.Ordinal) < page.IndexOf("player.DamageShare", StringComparison.Ordinal));

        var css = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App", "wwwroot", "css", "app.css"));
        static string Block(string source, string selector) =>
            Regex.Match(source, @"(?m)^" + Regex.Escape(selector) + @" \{(?<body>[^}]*)\}").Groups["body"].Value;
        Assert.Matches(@"min-width:", Block(css, ".row .dps"));
        var share = Block(css, ".row .share");
        Assert.Matches(@"flex-direction:\s*column;", share);
        Assert.Matches(@"min-width:", share);
        var pct = Block(css, ".row .pct");
        Assert.Matches(@"font-weight:\s*600;", pct);
        Assert.Matches(@"color:\s*var\(--text\);", pct);
        static double Size(string body) => double.Parse(Regex.Match(body, @"font-size:\s*([\d.]+)px").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var total = Block(css, ".row .total");
        Assert.True(Size(total) < Size(pct), "the total is smaller than the share");
        Assert.True(Size(total) >= 10, "but not too small to read over the game");
        Assert.DoesNotMatch(@"font-weight", total);
    }

    // The overlay holds its notices back by the player's "Hide during boss fights" switch, keeps the strip mounted
    // and silent while they are held, and tells the window layer, whose restore for an event goes through the gate.
    // Source-text guards: the strip's rendering and the WPF window layer have no test host here.
    [Fact]
    public void TheOverlayHoldsItsNoticesByThePlayersSwitch()
    {
        var pages = Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App", "Pages");
        var code = File.ReadAllText(Path.Combine(pages, "MeterPage.razor.cs"));
        var markup = File.ReadAllText(Path.Combine(pages, "MeterPage.razor"));
        Assert.Contains("AlertStripLayout.HiddenForFight(alertSettings.HideDuringBossFights, bossFightUnderWay)", code);
        Assert.Contains("bossFightUnderWay = bossFight.Update(snapshot.Target, now);", code);
        Assert.Contains("Windows.HoldAlerts(NoticesHeld);", code);
        Assert.Matches(@"(?m)^\s*<AlertStrip [^>]*Held=""NoticesHeld"" />", markup);

        // The strip shows nothing and announces nothing while held, before either of its other branches.
        var strip = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App", "Components", "AlertStrip.razor"));
        Assert.Contains(@"<span class=""sr-only"" role=""status"">@(Held ? string.Empty : Announcement)</span>", strip);
        Assert.Matches(@"(?s)@if \(Held\)\s*\{\s*@\*[^*]*\*@\s*\}\s*else if \(View\.Alerts\.Count > 0\)", strip);

        // The window layer asks the gate before every restore for an event, and acts on the one that waited.
        var windows = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App", "Windows", "MeterWindows.cs"));
        Assert.Contains("if (restoreGate.Approaching()) RestoreForAlert();", windows);
        Assert.Contains("if (restoreGate.Hold(held, alertMonitor?.Current.Alerts.Count > 0)) RestoreForAlert();", windows);
        Assert.Equal(3, Regex.Matches(windows, @"\bRestoreForAlert\(\)").Count); // the two calls and its definition
    }

    // The overlay's panel is only as tall as its content, so the room the notices leave in a boss fight is see-through
    // rather than empty panel; nothing in it scrolls (Harry: "i dont like scrolling"): the notice strip always takes
    // its full height, the expanded strip has no cap, and the window grows instead (TheOverlayWindowGrowsToItsContent);
    // only where it cannot (the screen's height) do the rows give way, faded, never the notices (Codex review on
    // PR #28); and the picture is laid out against the window, so it does not rescale as the panel changes height.
    [Fact]
    public void TheOverlayPanelFitsItsContent()
    {
        var styles = Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App", "wwwroot", "css");
        var css = File.ReadAllText(Path.Combine(styles, "app.css"));
        var alerts = File.ReadAllText(Path.Combine(styles, "alerts.css"));
        static string Block(string source, string selector) =>
            Regex.Match(source, @"(?m)^" + Regex.Escape(selector) + @" \{(?<body>[^}]*)\}").Groups["body"].Value;
        var overlay = Block(css, ".overlay");
        Assert.Matches(@"(?m)^\s*max-height:\s*100%;", overlay);
        Assert.DoesNotMatch(@"(?m)^\s*height:", overlay);
        // The panel's max-height is a percentage of #app, which must itself be the window's height.
        Assert.Matches(@"(?m)^\s*height:\s*100%;", Block(css, "#app"));
        Assert.Matches(@"(?m)^\s*height:\s*100%;", Block(css, "html, body"));
        // No other rule for the panel itself (.overlay.fx, .overlay[...]; not its descendants) gives it a height.
        Assert.DoesNotMatch(@"(?m)^\.overlay[.:\[][^\s{,]*\s*\{[^}]*(?<![-\w])height\s*:", css.Replace(".overlay::before {", ".before-layer {"));
        Assert.Matches(@"var\(--panel-image, none\) center / cover no-repeat fixed;", Block(css, ".overlay::before"));
        // The strip never shrinks, nor do the rows, except where the window cannot grow further (.overlay.cut, set by
        // the page from IMeterWindows.OverlayCutsContent), faded there, and measured at full height even then (the
        // data-measuring rule comes later, so it wins); and no rule in the overlay's styles makes anything scroll:
        // the rows' overflow is clipped (a gliding row cannot stretch the measured height), the expanded strip has
        // no cap.
        var rows = Block(css, ".rows");
        Assert.Matches(@"(?m)^\s*flex:\s*none;", rows);
        Assert.Matches(@"(?m)^\s*overflow:\s*clip;", rows);
        var cut = Block(css, ".overlay.cut .rows");
        Assert.Matches(@"(?m)^\s*flex:\s*0 1 auto;", cut);
        Assert.Matches(@"(?m)^\s*min-height:\s*0;", cut);
        Assert.Matches(@"(?m)^\s*mask-image:\s*linear-gradient\(", cut);
        Assert.Matches(@"(?m)^\s*flex:\s*none;", Block(css, ".overlay[data-measuring] .rows"));
        Assert.True(css.IndexOf(".overlay.cut .rows {", StringComparison.Ordinal) < css.IndexOf(".overlay[data-measuring] .rows {", StringComparison.Ordinal));
        Assert.Matches(@"(?m)^\s*flex:\s*none;", Block(alerts, ".alerts"));
        Assert.DoesNotMatch(@"(?m)^\s*min-height:", Block(alerts, ".alerts"));
        Assert.DoesNotMatch(@"(?m)^\s*max-height:", Block(alerts, ".alerts.expanded"));
        Assert.DoesNotMatch(@"overflow(-[xy])?:\s*(auto|scroll)", alerts);
        Assert.DoesNotMatch(@"position:\s*sticky", alerts);
        var overlayRules = Regex.Matches(css, @"(?m)^(?<selector>\.(overlay|rows|row|target|ov-)[^{]*)\{(?<body>[^}]*)\}");
        Assert.NotEmpty(overlayRules);
        Assert.All(overlayRules, rule => Assert.DoesNotMatch(@"overflow(-[xy])?:\s*(auto|scroll)", rule.Groups["body"].Value));
        // A margin under whatever ends the panel (the last row, or the notices), as above the header and card.
        Assert.Matches(@"(?m)^\s*padding-bottom:\s*[1-9]\d*px;", overlay);
    }

    // An upcoming event's notice uses its own washed-out gold (--soon), never the bright "you" gold (--me), which
    // Harry found too loud in the strip; an open entry keeps its teal.
    [Fact]
    public void UpcomingNoticesUseTheSoftGold()
    {
        var styles = Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App", "wwwroot", "css");
        var app = File.ReadAllText(Path.Combine(styles, "app.css"));
        var alerts = File.ReadAllText(Path.Combine(styles, "alerts.css"));
        Assert.Matches(@"(?m)^\s*--soon:\s*#dcc68c;", app);
        Assert.DoesNotContain("var(--me)", alerts, StringComparison.Ordinal);
        Assert.DoesNotContain("255, 183, 3", alerts, StringComparison.Ordinal);
        // The strip's bar takes the same colours as the notice it names.
        Assert.Matches(@"(?m)^\.alert\.soon \.alert-dot,\s*\.alert-bar\.soon \.alert-dot \{[^}]*background:\s*var\(--soon\);", alerts);
        Assert.Matches(@"(?m)^\.alert-progress \{[^}]*background:\s*var\(--soon\);", alerts);
        Assert.Matches(@"(?m)^\.alert\.open \.alert-progress,\s*\.alert-bar\.open \.alert-progress \{[^}]*background:\s*var\(--good\);", alerts);
    }

    // The boss-timers window shows a third-party site, so it is kept apart from the meter: a plain WebView2 in its own
    // profile, locked down before it navigates, every top-level navigation and new-window request through
    // BossTimersSite's decisions (tested there), browser launches through the gate (BrowserLaunchGateTests) and out of
    // WebView2's callback, and nothing that would let the page reach the meter, the meter's local hosts, this PC's
    // files or other programs; its start and its buttons cannot end the meter (deep audit on PR #29: a click after
    // the browser process crashed would have). Not owned by the overlay. The header's hourglass opens it.
    // Source-text guards: the WebView has no test host here.
    [Fact]
    public void TheBossTimersWindowIsKeptApartFromTheMeter()
    {
        var app = Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App");
        Assert.Contains(@"@onclick=""Windows.OpenBossTimers""", File.ReadAllText(Path.Combine(app, "Pages", "MeterPage.razor")));
        var window = File.ReadAllText(Path.Combine(app, "Windows", "BossTimersWindow.cs"));
        Assert.Contains("public sealed class BossTimersWindow : Window", window);
        Assert.Contains("public const string ProfileFolder = \"webview2-timers\";", window);
        Assert.Contains("CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: profile)", window);
        Assert.Contains("await webView.EnsureCoreWebView2Async(environment);", window);
        Assert.Contains("new() { DefaultBackgroundColor = PageBackground, AllowExternalDrop = false }", window);
        foreach (var locked in new[]
        {
            "s.AreHostObjectsAllowed = false;", "s.IsWebMessageEnabled = false;", "s.AreDevToolsEnabled = false;",
            "s.IsPasswordAutosaveEnabled = false;", "s.IsGeneralAutofillEnabled = false;",
            "s.AreDefaultContextMenusEnabled = false;", "s.AreBrowserAcceleratorKeysEnabled = false;",
            "core.DownloadStarting += (_, e) => e.Cancel = true;",
            "core.SaveAsUIShowing += (_, e) => e.Cancel = true;",
            "core.SaveFileSecurityCheckStarting += (_, e) => e.CancelSave = true;",
            "core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;",
            "core.ScreenCaptureStarting += (_, e) => e.Cancel = true;",
            "core.BasicAuthenticationRequested += (_, e) => e.Cancel = true;",
            "core.ClientCertificateRequested += (_, e) => e.Cancel = true;",
            "core.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;",
            "core.NavigationStarting += OnNavigationStarting;", "core.NewWindowRequested += OnNewWindowRequested;",
            "BossTimersSite.DecideNavigation(e.Uri, e.IsUserInitiated, e.IsRedirected)",
            "BossTimersSite.DecideNewWindow(e.Uri, e.IsUserInitiated, e.OriginalSourceFrameInfo?.Source)",
            "e.Handled = true;",
            "!BossTimersSite.IsWebPage(uri) || !launches.TryPass()) return;",
            "_ = Dispatcher.InvokeAsync(() => DefaultBrowser.Open(uri, logger));",
        })
            Assert.Contains(locked, window);
        // The start catches everything (a window closed meanwhile simply stops), and every button acts through the
        // guard that survives a crashed browser.
        Assert.Matches(@"catch \(Exception\) when \(closed\)\s*\{\s*\}\s*catch \(Exception ex\)\s*\{", window);
        Assert.Equal(3, Regex.Matches(window, @"\(_, _\) => OnPage\(core =>").Count);
        Assert.Matches(@"catch \(Exception ex\) when \(ex is InvalidOperationException or COMException or ObjectDisposedException\)", window);
        Assert.DoesNotMatch(@"webView\.CoreWebView2\?\.", window);
        // "Keep on top" follows the checked state, so UI Automation's toggle works too (PR #29's window check: a Click
        // handler missed it).
        Assert.Contains("keepOnTop.Checked += (_, _) => KeepOnTop(true);", window);
        Assert.Contains("keepOnTop.Unchecked += (_, _) => KeepOnTop(false);", window);
        Assert.DoesNotContain("keepOnTop.Click +=", window, StringComparison.Ordinal);
        // Locked down before the first navigation (the call present, not only the method), and never started through
        // Source or CreationProperties (a default profile).
        var lockAt = window.IndexOf("            Lock(core);", StringComparison.Ordinal);
        var firstNavigation = window.IndexOf("core.Navigate(BossTimersSite.Start.AbsoluteUri);", StringComparison.Ordinal);
        Assert.True(lockAt >= 0 && firstNavigation > lockAt, "the settings are locked before the first navigation");
        foreach (var reach in new[]
        {
            "BlazorWebView", "WebAssetHosts", "AddHostObjectToScript", "PostWebMessage", "ExecuteScriptAsync",
            "AddScriptToExecuteOnDocumentCreated", "SetVirtualHostNameToFolderMapping", "WebResourceRequested",
            "CreationProperties", "SourceProperty", "Owner =", "Process.Start",
        })
            Assert.DoesNotContain(reach, window, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\bSource\s*=[^=>]", window);
        Assert.DoesNotMatch(@"Are\w+Enabled = true|Is\w+Enabled = true|AreHostObjectsAllowed = true|AllowExternalDrop = true", window);

        var windows = File.ReadAllText(Path.Combine(app, "Windows", "MeterWindows.cs"));
        Assert.Contains("var window = new BossTimersWindow(services, overlay);", windows);
        Assert.Contains("bossTimers?.Close();", windows);
    }

    // Nothing in the notice strip blinks or loops (Harry: the pulsing dot was "too distracting"): the only animations
    // are a notice's one-time entrance, the progress line's width transition and the bar's chevron turning.
    [Fact]
    public void TheNoticeStripNeverBlinks()
    {
        var alerts = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App", "wwwroot", "css", "alerts.css"));
        Assert.DoesNotContain("infinite", alerts, StringComparison.Ordinal);
        Assert.DoesNotContain("alternate", alerts, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"animation-iteration-count", alerts);
        Assert.Equal(["alert-in"], Regex.Matches(alerts, @"@keyframes\s+([\w-]+)").Select(m => m.Groups[1].Value));
    }

    // The notice strip is one bar that opens and closes (Harry asked for the whole section to fold away rather than
    // always show two): a real button that says whether it is open, listing the notices only when open, and bound to
    // the player's saved choice, so it stays as they left it.
    [Fact]
    public void TheNoticeStripIsOneBarThatOpensAndCloses()
    {
        var app = Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App");
        var strip = File.ReadAllText(Path.Combine(app, "Components", "AlertStrip.razor"));
        Assert.Contains(@"<button type=""button"" class=""alert-bar @Kind(first)"" aria-expanded=""@(Expanded ? ""true"" : ""false"")""", strip);
        Assert.Contains(@"@onclick=""Toggle""", strip);
        Assert.Contains("private Task Toggle() => ExpandedChanged.InvokeAsync(!Expanded);", strip);
        Assert.Contains("@foreach (var alert in AlertStripLayout.Shown(View.Alerts, Expanded))", strip);
        Assert.Single(Regex.Matches(strip, @"@foreach\b"));

        var pages = Path.Combine(app, "Pages");
        Assert.Matches(@"<AlertStrip [^>]*Expanded=""alertSettings\.NoticesExpanded"" ExpandedChanged=""SetNoticesExpanded""",
            File.ReadAllText(Path.Combine(pages, "MeterPage.razor")));
        Assert.Contains("Settings.Update(s => s with { Alerts = s.Alerts with { NoticesExpanded = expanded } })",
            File.ReadAllText(Path.Combine(pages, "MeterPage.razor.cs")));

        var alerts = File.ReadAllText(Path.Combine(app, "wwwroot", "css", "alerts.css"));
        Assert.Matches(@"(?m)^\.alerts\.expanded \.alert-chevron \{[^}]*transform:\s*rotate\(-135deg\);", alerts);
        Assert.Matches(@"(?m)^\.alert-bar \{[^}]*height:\s*24px;", alerts); // as tall as a notice: closing never jumps
    }

    // A row's text reads over any bar (Harry found the white names hard to read): every bit of it sits on a
    // crisp dark edge, the names are bold, and the death count is the meter's white-ish text, not an alarm red.
    [Fact]
    public void ARowsTextSitsOnACrispDarkEdge()
    {
        var css = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App", "wwwroot", "css", "app.css"));
        static string Block(string source, string selector) =>
            Regex.Match(source, @"(?m)^" + Regex.Escape(selector) + @" \{(?<body>[^}]*)\}").Groups["body"].Value;
        Assert.Matches(@"--row-edge:\s*0 0 1px rgba\(0, 0, 0, 0\.95\)", Block(css, ".rows"));
        foreach (var text in new[] { ".row .name", ".row .cp", ".row .deaths", ".row .num" })
            Assert.Matches(@"text-shadow:\s*var\(--row-edge\);", Block(css, text));
        Assert.Matches(@"font-weight:\s*700;", Block(css, ".row .name"));
        Assert.Matches(@"color:\s*rgba\(235, 233, 242, 0\.85\);", Block(css, ".row .deaths"));
        // Nothing later sets a row's text back to a soft shadow.
        Assert.DoesNotMatch(@"(?m)^\.row[^{]*\{[^}]*text-shadow:(?!\s*var\(--row-edge\);)", css);
    }

    // The player's own row is marked by a ring round the row, never by a stripe on the bar (a white inset at the
    // bar's left edge read as a glitch); bar effects replace the ring with their glow.
    [Fact]
    public void TheOwnRowIsMarkedByARingNotOnTheBar()
    {
        var css = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App", "wwwroot", "css", "app.css"));
        var page = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App", "Pages", "MeterPage.razor.cs"));
        Assert.DoesNotContain("me-mark", css, StringComparison.Ordinal);
        Assert.DoesNotContain("me-mark", page, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\.fill[^{]*\{[^}]*box-shadow:\s*inset", css);
        // Nothing styles the own row's bar but its gold fill when class colours are off (.fill.self).
        Assert.DoesNotMatch(@"\.row\.me[^{,]*\.fill", css);
        Assert.Matches(@"(?m)^\.row\.me \{[^}]*box-shadow:", css);
        Assert.Matches(@"(?m)^\.fx \.row\.me \{[^}]*box-shadow:", css);
    }

    [GeneratedRegex(@"<span class=""(num[^""]*)""")]
    private static partial Regex RowNumberColumn();

    [GeneratedRegex(@"<script\b(?<attributes>[^>]*)>(?<body>.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ScriptTag();

    [GeneratedRegex(@"\bsrc\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptSrc();

    [GeneratedRegex(@"[A-Za-z0-9]@[A-Za-z_]")]
    private static partial Regex EmailLikeExpression();

    // A script command ("scripts\Name.ps1", either slash) not directly preceded by "&lt;checkout&gt;\" or
    // "<checkout>\". A web asset such as "scripts/app.js" is not a command and does not match.
    [GeneratedRegex(@"(?<!&lt;checkout&gt;[\\/])(?<!<checkout>[\\/])\bscripts[\\/][\w-]+\.ps1\b", RegexOptions.IgnoreCase)]
    private static partial Regex RelativeScriptPath();

    // Lines of the app's .razor files (outside bin and obj) that match, as "file:line: text".
    private static List<string> Offending(Func<string, bool> matches)
    {
        var app = Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App");
        var files = Directory.EnumerateFiles(app, "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(files);
        return files
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
            .Where(l => matches(l.Text))
            .Select(l => $"{Path.GetRelativePath(app, l.File)}:{l.Line}: {l.Text.Trim()}")
            .ToList();
    }

    // The directory holding ScarletMeter.slnx, above the test assembly.
    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ScarletMeter.slnx"))) return dir.FullName;
        throw new InvalidOperationException("ScarletMeter.slnx was not found above " + AppContext.BaseDirectory);
    }
}
