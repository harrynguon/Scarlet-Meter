// Caps the page's animations at 60 frames a second. The overlay's bar effects and its bars' and rows' glides are CSS
// animations and transitions, which the web view otherwise draws at the screen's refresh rate (four times as often on
// a 240 Hz screen), and every frame it draws is captured and copied into the WPF window, work that competes with the
// game (Harry, 2026-10-30: "how about also capping the meter to 60fps?"). Each running animation on the document's
// timeline is paused where it is, on the first frame after it starts, and moved on by hand from the timeline's time
// at most 60 times a second, so it keeps its timing; a finite one is finished once its time is up, so its end events
// fire and its end state applies. An animation paused before this script took it over is left alone; one it drives
// no longer follows a later pause (`animation-play-state` or script), which nothing in the meter uses. An animation
// that fails to be driven is let go to run as it would without the cap, so one error never freezes the others. The
// loop runs only while there are animations to drive: one starting wakes it, and it stops when none is left. Loaded
// by index.html for every window. No network, no Blazor interop.
(() => {
    'use strict';

    const interval = 1000 / 60;
    // Each animation this script drives, with the timeline time its current time counts from.
    const origins = new WeakMap();
    let next = 0;
    let looping = false;
    let started = false;

    // Takes over any animation running on its own (newly started, or played again), paused where it is; when advance
    // is set, moves every driven one on to the timeline's time. True while any is still driven.
    function step(now, advance) {
        let driving = false;
        for (const animation of document.getAnimations()) {
            if (animation.timeline !== document.timeline) continue;
            try {
                if (animation.playState === 'running' && animation.playbackRate === 1
                    && typeof animation.currentTime === 'number') {
                    origins.set(animation, now - animation.currentTime);
                    animation.pause();
                } else if (animation.playState !== 'paused' || !origins.has(animation)) {
                    continue;
                }
                driving = true;
                if (!advance) continue;
                const time = now - origins.get(animation);
                const end = animation.effect ? animation.effect.getComputedTiming().endTime : Infinity;
                if (time >= end) {
                    origins.delete(animation);
                    animation.finish();
                    continue;
                }
                animation.currentTime = time;
            } catch {
                origins.delete(animation);
                try { animation.play(); } catch { /* left as it is */ }
            }
        }
        return driving;
    }

    // On a frame where the next 60th of a second is due (within a millisecond, as frame times jitter), moves every
    // animation on, so a 240 Hz screen steps every fourth frame and a 144 Hz one every second or third; after a pause
    // it starts afresh. On the frames between, it only takes over animations that started since the last frame.
    function frame(now) {
        const time = document.timeline.currentTime;
        if (typeof time === 'number') {
            if (now >= next - 1) {
                next = now - next > interval ? now + interval : next + interval;
                started = false;
                looping = step(time, true);
            } else if (started) {
                started = false;
                looping = step(time, false);
            }
        }
        if (looping) requestAnimationFrame(frame);
    }

    function wake() {
        started = true;
        if (looping) return;
        looping = true;
        requestAnimationFrame(frame);
    }

    // A CSS animation or transition starting announces itself; the loop then runs until none is left to drive.
    document.addEventListener('animationstart', wake, true);
    document.addEventListener('transitionrun', wake, true);
    wake();
})();
