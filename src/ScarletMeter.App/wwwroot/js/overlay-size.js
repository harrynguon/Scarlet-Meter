// The overlay's content height, which Pages/MeterPage.razor.cs asks for after each render: the panel with the bar area
// at its rows (the window is as tall as that; Windows/OverlayFit). For the moment of the measurement data-measuring
// makes the panel as tall as its content, whatever the window (the screen too short for it); it is removed before
// anything is drawn. The height is the panel's border box, in CSS pixels. Only reads the page: no requests, no calls
// into .NET.
window.scarletOverlay = {
    contentHeight() {
        const panel = document.querySelector('[data-window=overlay]');
        if (!panel) return null;
        try {
            panel.setAttribute('data-measuring', '');
            return panel.getBoundingClientRect().height;
        } finally {
            panel.removeAttribute('data-measuring');
        }
    }
};
