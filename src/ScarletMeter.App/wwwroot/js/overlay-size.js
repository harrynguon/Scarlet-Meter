// The overlay's heights, which Pages/MeterPage.razor.cs asks for after each render: the panel with the bar area at its
// rows (the window is as tall as that; Windows/OverlayFit) and with no rows (from which a resize of the bar area counts
// rows). For the moment of each measurement data-measuring makes the panel as tall as its content, whatever the window
// (a resize under way, or the screen too short), with the bar area at its rows ("content") or at none ("empty"; the
// list's padding stays, as in the empty meter); it is removed before anything is drawn. Heights are the panel's border
// box, in CSS pixels. Only reads the page: no requests, no calls into .NET.
window.scarletOverlay = {
    heights() {
        const panel = document.querySelector('[data-window=overlay]');
        if (!panel) return null;
        try {
            panel.setAttribute('data-measuring', 'content');
            const content = panel.getBoundingClientRect().height;
            panel.setAttribute('data-measuring', 'empty');
            const empty = panel.getBoundingClientRect().height;
            return { content, empty };
        } finally {
            panel.removeAttribute('data-measuring');
        }
    }
};