// The overlay's content height, which Pages/MeterPage.razor.cs asks for after each render: nothing in the meter
// scrolls, so when the panel's content (rows and notices) is taller than the window, the window grows to show all of
// it (Windows/OverlayFit). Until it has, the panel's max-height clips it to the window, so the panel's scroll height
// is its content's height; its borders are added. Only reads the page: no requests, no calls into .NET.
window.scarletOverlay = {
    contentHeight() {
        const panel = document.querySelector('[data-window=overlay]');
        if (!panel) return 0;
        return panel.scrollHeight + (panel.offsetHeight - panel.clientHeight);
    }
};
