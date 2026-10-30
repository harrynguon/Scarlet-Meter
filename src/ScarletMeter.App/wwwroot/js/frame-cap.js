// Caps the page's animations at 60 frames a second. The overlay's bar effects and its bars' and rows' glides are CSS
// animations and transitions, which the web view otherwise draws at the screen's refresh rate (four times as often on
// a 240 Hz screen), and every frame it draws is captured and copied into the WPF window, work that competes with the
// game (Harry, 2026-10-30: "how about also capping the meter to 60fps?"). Each running animation on the document's
// timeline is paused and moved on by hand from the frame clock, at most 60 times a second, so it keeps its timing;
// a finite one is finished once its time is up, so its end events fire and its end state applies. Animations that
// something else paused are left alone. The loop runs only while there are animations to drive: one starting wakes
// it, and it stops when none is left. Loaded by index.html for every window. No network, no Blazor interop.
(() => {
    'use strict';

    const interval = 1000 / 60;
    // Each animation this script drives, with the timeline time its current time counts from.
    const origins = new WeakMap();
    let next = 0;
    let looping = false;

    // Moves every driven animation on to the timeline's time, taking over any that runs on its own (newly started,
    // or played again). True while any is still driven.
    function step(now) {
        let driving = false;
        for (const animation of document.getAnimations()) {
            if (animation.timeline !== document.timeline) continue;
            if (animation.playState === 'running' && animation.playbackRate === 1
                && typeof animation.currentTime === 'number') {
                origins.set(animation, now - animation.currentTime);
                animation.pause();
            } else if (animation.playState !== 'paused' || !origins.has(animation)) {
                continue;
            }
            const time = now - origins.get(animation);
            const end = animation.effect ? animation.effect.getComputedTiming().endTime : Infinity;
            if (time >= end) {
                origins.delete(animation);
                animation.finish();
                continue;
            }
            animation.currentTime = time;
            driving = true;
        }
        return driving;
    }

    // Steps when the next 60th of a second is due (within a millisecond, as frame times jitter), so a 240 Hz screen
    // steps every fourth frame and a 144 Hz one every second or third; after a pause it starts afresh.
    function frame(now) {
        if (now >= next - 1) {
            next = now - next > interval ? now + interval : next + interval;
            looping = step(document.timeline.currentTime);
            if (!looping) return;
        }
        requestAnimationFrame(frame);
    }

    function wake() {
        if (looping) return;
        looping = true;
        requestAnimationFrame(frame);
    }

    // A CSS animation or transition starting announces itself; the loop then runs until none is left to drive.
    document.addEventListener('animationstart', wake, true);
    document.addEventListener('transitionrun', wake, true);
    wake();
})();
