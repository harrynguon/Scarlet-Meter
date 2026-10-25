// The overlay's content height, which Pages/MeterPage.razor.cs asks for after each render: only the player list
// scrolls, past "Rows before scrolling" (app.css, .rows.capped, measured at that capped height), so when the panel's
// content (rows and notices) is taller than the window, the window grows to show all of it (Windows/OverlayFit).
// The panel's max-height keeps it inside the window, so its scroll height is its content's
// height; its borders are added. Where the window cannot grow further the rows give way (app.css, .overlay.cut), so
// for the moment of the measurement data-measuring gives them their full height again; it is removed before anything
// is drawn. Only reads the page: no requests, no calls into .NET.
window.scarletOverlay = {
    contentHeight() {
        const panel = document.querySelector('[data-window=overlay]');
        if (!panel) return 0;
        panel.setAttribute('data-measuring', '');
        try {
            return panel.scrollHeight + (panel.offsetHeight - panel.clientHeight);
        } finally {
            panel.removeAttribute('data-measuring');
        }
    }
};
