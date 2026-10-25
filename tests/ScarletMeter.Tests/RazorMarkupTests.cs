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

    // A table's sticky header sits flush under the tabs while its rows scroll (Harry: rows showed through a gap above
    // it): a sticky header (top: 0) stops at its scroll container's padding edge, so the scrolling tab body has no top
    // padding, and a 6 px spacer that scrolls away stands in for it: a generated block (content, display: block), so it
    // takes its height. No rule naming the tab body, in either stylesheet, gives it top padding again (padding,
    // padding-top or padding-block[-start], any value but 0; comments ignored). Audit on PR #33: the first guard read
    // only the spacer's height and rules starting with .tab-body.
    [Fact]
    public void StickyTableHeadersSitFlushUnderTheTabs()
    {
        var styles = Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App", "wwwroot", "css");
        var app = File.ReadAllText(Path.Combine(styles, "app.css"));
        var views = File.ReadAllText(Path.Combine(styles, "views.css"));
        static string Block(string source, string selector) =>
            Regex.Match(source, @"(?m)^" + Regex.Escape(selector) + @" \{(?<body>[^}]*)\}").Groups["body"].Value;
        var header = Block(views, ".grid-table th");
        Assert.Matches(@"position:\s*sticky;", header);
        Assert.Matches(@"(?m)^\s*top:\s*0;", header);
        var body = Block(app, ".tab-body");
        Assert.Matches(@"overflow-y:\s*auto;", body);
        Assert.Matches(@"(?m)^\s*padding:\s*0 18px 18px;", body);
        var spacer = Block(app, ".tab-body::before");
        Assert.Matches(@"content:\s*"""";", spacer);
        Assert.Matches(@"display:\s*block;", spacer);
        Assert.Matches(@"height:\s*6px;", spacer);
        var rules = Regex.Replace(app + "\n" + views, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        Assert.DoesNotMatch(@"(?m)^[^{}]*\.tab-body\b[^{}]*\{[^}]*padding(?:-top|-block(?:-start)?)?:(?!\s*0[\s;])", rules);
    }

    // The overlay's panel is only as tall as its content, so the room the notices leave in a boss fight is see-through
    // rather than empty panel; the notices never scroll (Harry: "i dont like scrolling"): the notice strip always takes
    // its full height, the expanded strip has no cap, and the window grows instead (TheOverlayWindowGrowsToItsContent);
    // only where it cannot (the screen's height) do the rows give way, faded, never the notices (Codex review on
    // PR #28); the player list alone scrolls, past its cap (TheRowsScrollPastTheirCap); and the picture covers the
    // panel, laid out against it (Harry: against the window, which is often taller than the panel, it "no longer fits
    // in the background").
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
        var layer = Block(css, ".overlay::before");
        Assert.Matches(@"var\(--panel-image, none\) center / cover no-repeat;", layer);
        Assert.Matches(@"(?m)^\s*inset:\s*0;", layer); // the layer is the panel, so the picture covers the panel
        Assert.DoesNotMatch(@"\bfixed\b", Regex.Replace(layer, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline));
        Assert.DoesNotMatch(@"background-attachment", css);
        // The strip never shrinks, nor do the rows, except where the window cannot grow further (.overlay.cut, set by
        // the page from IMeterWindows.OverlayCutsContent), faded there, and measured at full height even then (the
        // data-measuring rule comes later, so it wins); and no rule in the overlay's styles makes anything scroll but
        // the capped player list (.rows.capped): up to the cap the rows' overflow is clipped (a gliding row cannot
        // stretch the measured height), the expanded strip has no cap.
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
        var scrolling = overlayRules.Where(rule => Regex.IsMatch(rule.Groups["body"].Value, @"overflow(-[xy])?:\s*(auto|scroll)"))
            .Select(rule => rule.Groups["selector"].Value.Trim()).ToList();
        Assert.Equal([".rows.capped"], scrolling);
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
        // Closed, the bar names the most urgent notice (the monitor's first) with its countdown, "+N" for the rest and
        // its progress line, keyed to that notice so another's starts afresh (deep audit on PR #30: none was pinned).
        Assert.Contains("var first = View.Alerts[0];", strip);
        Assert.Matches(@"(?s)else\s*\{\s*<span class=""alert-name"">@first\.Rule\.Name</span>\s*<span class=""alert-when"">@When\(first\)</span>\s*"
            + @"@if \(AlertStripLayout\.MoreCount\(View\.Alerts\) is var more && more > 0\)\s*\{\s*<span class=""alert-count"">\+@more</span>\s*\}\s*"
            + @"(?:@\*.*?\*@\s*)?<span class=""alert-progress"" @key=""@\(\$""\{first\.Rule\.Id\}-\{first\.Kind\}-\{first\.Start\.UtcTicks\}""\)""\s*"
            + @"style=""width:@Compact\.Percent\(Progress\(first, first\.Deadline - Now\)\)""></span>\s*\}", strip);

        var pages = Path.Combine(app, "Pages");
        Assert.Matches(@"<AlertStrip [^>]*Expanded=""alertSettings\.NoticesExpanded"" ExpandedChanged=""SetNoticesExpanded""",
            File.ReadAllText(Path.Combine(pages, "MeterPage.razor")));
        Assert.Contains("Settings.Update(s => s with { Alerts = s.Alerts with { NoticesExpanded = expanded } })",
            File.ReadAllText(Path.Combine(pages, "MeterPage.razor.cs")));

        var alerts = File.ReadAllText(Path.Combine(app, "wwwroot", "css", "alerts.css"));
        Assert.Matches(@"(?m)^\.alerts\.expanded \.alert-chevron \{[^}]*transform:\s*rotate\(-135deg\);", alerts);
        // As tall as a notice: a notice is 24 px inside its 1 px border (content-box), the bar 26 px with its border in.
        static string Block(string source, string selector) =>
            Regex.Match(source, @"(?m)^" + Regex.Escape(selector) + @" \{(?<body>[^}]*)\}").Groups["body"].Value;
        var bar = Block(alerts, ".alert-bar");
        var notice = Block(alerts, ".alert");
        Assert.Matches(@"box-sizing:\s*border-box;", bar);
        Assert.Matches(@"height:\s*26px;", bar);
        Assert.Matches(@"(?m)^\s*height:\s*24px;", notice);
        Assert.Matches(@"border:\s*1px solid", notice);
        Assert.DoesNotMatch(@"box-sizing", notice);
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
        Assert.Single(Regex.Matches(css, @"--row-edge\s*:")); // declared once, so nothing later turns it off
        foreach (var text in new[] { ".row .name", ".row .cp", ".row .deaths", ".row .num" })
            Assert.Matches(@"text-shadow:\s*var\(--row-edge\);", Block(css, text));
        Assert.Matches(@"font-weight:\s*700;", Block(css, ".row .name"));
        Assert.Matches(@"color:\s*rgba\(235, 233, 242, 0\.85\);", Block(css, ".row .deaths"));
        // Nothing sets a row's text back to a soft shadow, under .fx or any other prefix either (deep audit on PR #30:
        // the first guard only read selectors starting with .row). Comments are dropped so a mention cannot match.
        var rules = Regex.Replace(css, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        Assert.DoesNotMatch(@"(?m)^[^{}]*\.row\b[^{}]*\{[^}]*text-shadow:(?!\s*var\(--row-edge\);)", rules);
    }

    // Scarlet's face sits at the header's left in place of the old state dot (Harry: "in permanent place of the grey
    // dot"), always, between fights too: it comes before the fight branch, not inside it. Its ring takes the target's HP
    // band (HpBand.RingClass, whose behaviour FormattingTests pins), glowing while the HP moves and without the glow,
    // a touch dimmer, once it has been still; white-ish while the target's total HP is unknown; grey when the target
    // is down; none between fights. The green is not the theme's teal, which Harry found "a bit weird" there. The
    // history list keeps a dot of its own, styled in views.css alone. The image ships with the meter (the publish
    // layout check requires it) and is a 64 px PNG, enough for 20 px at 300%.
    [Fact]
    public void TheHeaderShowsScarletsFaceRingedByTheFightsState()
    {
        var app = Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App");
        var markup = File.ReadAllText(Path.Combine(app, "Pages", "MeterPage.razor"));
        Assert.Matches(@"(?s)<div class=""ov-title"">\s*(?:@\*.*?\*@\s*)?<span class=""ov-avatar @AvatarClass"" aria-hidden=""true""></span>\s*@if \(snapshot\.Target is \{ \} fighting\)", markup);
        Assert.DoesNotContain("state-dot", markup);
        Assert.Contains("private string AvatarClass => HpBand.RingClass(phase, snapshot.Target);",
            File.ReadAllText(Path.Combine(app, "Pages", "MeterPage.razor.cs")));

        var css = File.ReadAllText(Path.Combine(app, "wwwroot", "css", "app.css"));
        static string Block(string source, string selector) =>
            Regex.Match(source, @"(?m)^" + Regex.Escape(selector) + @" \{(?<body>[^}]*)\}").Groups["body"].Value;
        var avatar = Block(css, ".ov-avatar");
        Assert.Matches(@"(?m)^\s*width:\s*20px;", avatar);
        Assert.Matches(@"(?m)^\s*height:\s*20px;", avatar);
        Assert.Matches(@"border-radius:\s*50%;", avatar);
        Assert.Matches(@"background:\s*url\(""\.\./images/avatar\.png""\) center / cover no-repeat;", avatar);
        Assert.Matches(@"--ring:\s*#5ed36a;", Block(css, ".ov-avatar.hp-high"));
        Assert.Matches(@"--ring:\s*#f2c94c;", Block(css, ".ov-avatar.hp-mid"));
        Assert.Matches(@"--ring:\s*#f04c56;", Block(css, ".ov-avatar.hp-low"));
        Assert.Matches(@"--ring:\s*rgba\(235, 233, 242, 0\.75\);", Block(css, ".ov-avatar.hp-unknown"));
        var glowing = Regex.Match(css, @"(?m)^\.ov-avatar\.hp-high,\s*\.ov-avatar\.hp-mid,\s*\.ov-avatar\.hp-low,\s*\.ov-avatar\.hp-unknown \{(?<body>[^}]*)\}").Groups["body"].Value;
        Assert.Matches(@"box-shadow:\s*0 0 0 1\.5px var\(--ring\), 0 0 7px var\(--ring\);", glowing);
        var still = Regex.Match(css, @"(?m)^\.ov-avatar\.hp-high\.still,\s*\.ov-avatar\.hp-mid\.still,\s*\.ov-avatar\.hp-low\.still,\s*\.ov-avatar\.hp-unknown\.still \{(?<body>[^}]*)\}").Groups["body"].Value;
        Assert.Matches(@"box-shadow:\s*0 0 0 1\.5px var\(--ring\);", still); // the same colour, its glow gone
        Assert.Matches(@"opacity:\s*0\.85;", still); // and a touch dimmer
        Assert.Matches(@"box-shadow:\s*0 0 0 1\.5px var\(--muted\);", Block(css, ".ov-avatar.done"));
        Assert.DoesNotMatch(@"\.ov-avatar[^{]*\{[^}]*var\(--good\)", css); // not the theme's teal
        Assert.DoesNotMatch(@"(?m)^\.state-dot", css);
        Assert.Matches(@"(?m)^\.state-dot \{", File.ReadAllText(Path.Combine(app, "wwwroot", "css", "views.css"))); // the history list's own

        var png = File.ReadAllBytes(Path.Combine(app, "wwwroot", "images", "avatar.png"));
        Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        static int BigEndian(byte[] b, int at) => (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];
        Assert.Equal(64, BigEndian(png, 16));
        Assert.Equal(64, BigEndian(png, 20));
        Assert.Contains(@"'wwwroot\images\avatar.png',", File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "Test-PublishLayout.ps1")));
    }

    // The class icon on the overlay's bars is 5 to 10% larger than elsewhere (Harry: hard to see), square, and leaves
    // at least 3 px above and below it in the 26 px row.
    [Fact]
    public void TheBarsClassIconsAreALittleLarger()
    {
        var css = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App", "wwwroot", "css", "app.css"));
        static string Block(string source, string selector) =>
            Regex.Match(source, @"(?m)^" + Regex.Escape(selector) + @" \{(?<body>[^}]*)\}").Groups["body"].Value;
        static double Px(string body, string property) => double.Parse(
            Regex.Match(body, @"(?m)^\s*" + property + @":\s*([\d.]+)px;").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var base_ = Block(css, ".class-icon");
        var bars = Block(css, ".rows .class-icon");
        var size = Px(bars, "width");
        Assert.Equal(size, Px(bars, "height"));
        Assert.InRange(size / Px(base_, "width"), 1.05, 1.10);
        Assert.True(Px(Block(css, ".row"), "height") - size >= 6, "the icon leaves room above and below it in the row");
    }

    // Odyle energy sits left of the ping (Harry: "place it to the left of the ping display") once the game has sent it
    // and while "Show Odyle energy" is on, as basic then charged; the two sit together at the line's end. The page
    // reads the value on its refresh loop, on the renderer's dispatcher, not from the capture thread's event.
    [Fact]
    public void OdyleEnergySitsLeftOfThePing()
    {
        var app = Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App");
        var markup = File.ReadAllText(Path.Combine(app, "Pages", "MeterPage.razor"));
        var totals = Regex.Match(markup, @"(?s)<div class=""ov-totals"">(?<body>.*?)\n    </div>").Groups["body"].Value;
        Assert.Matches(@"(?s)class=""accent"">.*@if \(appearance\.ShowOdyleEnergy && odyle is \{ \} energy\).*<span class=""odyle"".*"
            + @"@OdyleText\.Basic\(energy\).*@OdyleText\.Charged\(energy\).*<span class=""ping ", totals);
        Assert.True(totals.IndexOf(@"<span class=""odyle""", StringComparison.Ordinal) < totals.IndexOf(@"<span class=""ping", StringComparison.Ordinal),
            "the energy comes before the ping");
        Assert.DoesNotContain("<img", totals); // the crystal is Scarlet's own drawing, not the game's art

        var code = File.ReadAllText(Path.Combine(app, "Pages", "MeterPage.razor.cs"));
        Assert.Matches(@"snapshot = Engine\.GetSnapshot\(\);\s*(?://[^\n]*\n\s*)*odyle = Engine\.OdyleEnergy;", code);
        Assert.DoesNotContain("OdyleEnergyUpdated", code);

        var css = File.ReadAllText(Path.Combine(app, "wwwroot", "css", "app.css"));
        static string Block(string source, string selector) =>
            Regex.Match(source, @"(?m)^" + Regex.Escape(selector) + @" \{(?<body>[^}]*)\}").Groups["body"].Value;
        Assert.Matches(@"margin-left:\s*auto;", Block(css, ".odyle"));
        Assert.Matches(@"margin-left:\s*0;", Block(css, ".odyle + .ping"));
        Assert.Matches(@"margin-left:\s*auto;", Block(css, ".ping")); // without the energy, the ping still ends the line
    }

    // Past "Rows before scrolling" the player list scrolls (Harry: in big raids the meter was "just too long
    // vertically"; "cap the dps bars to like 8-10 players"), so the window stops growing there; the notices below
    // never scroll (TheOverlayPanelFitsItsContent). The cap is that many rows, by the row's own height and gap, in the
    // list's zoomed pixels; the next row's top shows in the padding, faded. The list is "capped" only with more rows
    // than that, so a small party's list stays clipped. The player's own row stays in view, pinned at either edge in a
    // whole row's place, with a solid backdrop only while pinned; the glide script measures again on scroll, since a
    // pinned row's offsetTop follows it. Source-text guards: the WebView has no test host here; a headless Edge render
    // (WORKLOG) measured the pinned positions and the backdrop.
    [Fact]
    public void TheRowsScrollPastTheirCap()
    {
        var app = Path.Combine(RepositoryRoot(), "src", "ScarletMeter.App");
        var markup = File.ReadAllText(Path.Combine(app, "Pages", "MeterPage.razor"));
        Assert.Contains(@"<ul class=""rows @RowsClass"">", markup);
        var code = File.ReadAllText(Path.Combine(app, "Pages", "MeterPage.razor.cs"));
        Assert.Contains(@"private string RowsClass => Visible.Count() > appearance.VisibleRows ? ""capped"" : string.Empty;", code);
        Assert.Contains("--visible-rows:{appearance.VisibleRows};", code);
        var settings = File.ReadAllText(Path.Combine(app, "Pages", "SettingsPage.razor"));
        // Within the one element ("=>" in its lambdas rules out [^>]).
        Assert.Matches(@"(?s)<RangeSetting Label=""Rows before scrolling""(?:(?!/>).)*Min=""@AppearanceSettings\.MinVisibleRows"" Max=""@AppearanceSettings\.MaxVisibleRows"" Step=""1""(?:(?!/>).)*VisibleRows = \(int\)Math\.Round\(v\)", settings);

        var css = File.ReadAllText(Path.Combine(app, "wwwroot", "css", "app.css"));
        static string Block(string source, string selector) =>
            Regex.Match(source, @"(?m)^" + Regex.Escape(selector) + @" \{(?<body>[^}]*)\}").Groups["body"].Value;
        static double Px(string body, string property) => double.Parse(
            Regex.Match(body, @"(?m)^\s*" + property + @":\s*([\d.]+)px;").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var row = Block(css, ".row");
        var step = Px(row, "height") + Px(row, "margin-bottom");
        var capped = Block(css, ".rows.capped");
        var cap = Regex.Match(capped, @"(?m)^\s*max-height:\s*calc\(var\(--visible-rows, (?<rows>\d+)\) \* (?<step>[\d.]+)px\);");
        Assert.True(cap.Success, "the list's height is a number of rows");
        Assert.Equal(new ScarletMeter.App.Settings.AppearanceSettings().VisibleRows, int.Parse(cap.Groups["rows"].Value, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(step, double.Parse(cap.Groups["step"].Value, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Matches(@"(?m)^\s*overflow-y:\s*auto;", capped);
        Assert.Matches(@"(?m)^\s*overflow-x:\s*hidden;", capped);
        Assert.Matches(@"(?m)^\s*mask-image:\s*linear-gradient\(to bottom, #000 calc\(100% - \d+px\), transparent\);", capped);
        // Measured at its capped height: the measuring rule only gives the rows their full flex size back.
        Assert.DoesNotMatch(@"height", Block(css, ".overlay[data-measuring] .rows"));

        var mine = Block(css, ".rows.capped > .row.me");
        Assert.Matches(@"(?m)^\s*position:\s*sticky;", mine);
        Assert.Matches(@"(?m)^\s*top:\s*0;", mine);
        Assert.Equal(Px(row, "margin-bottom"), Px(mine, "bottom")); // in the last whole row's place, above the fade
        Assert.Matches(@"(?m)^\s*container-type:\s*scroll-state;", mine);
        Assert.DoesNotMatch(@"background", Block(css, ".rows.capped > .row.me::before"));
        Assert.Matches(@"@container scroll-state\(\(stuck: top\) or \(stuck: bottom\)\) \{\s*\.rows\.capped > \.row\.me::before \{\s*background:", css);

        var fx = File.ReadAllText(Path.Combine(app, "wwwroot", "js", "overlay-fx.js"));
        Assert.Contains("list.addEventListener('scroll', remember, { passive: true });", fx);
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
