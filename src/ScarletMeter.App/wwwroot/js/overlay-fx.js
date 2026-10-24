// Glides the overlay's player rows to their new places when the order changes (one player overtakes another),
// instead of letting them jump, and flags a row that moved up so app.css flashes it once. FLIP: each row's last
// position is remembered; when Blazor moves rows (they are keyed by player, so the elements themselves move),
// each moved row starts from its old place and transitions to the new one. Runs only while the overlay has the
// "fx" class (Settings, Appearance, "Bar effects") and Windows does not ask for reduced motion. Loaded by
// index.html for every window; it acts only where an overlay row list exists. No network, no Blazor interop.
(() => {
    'use strict';

    const glide = 'transform 360ms cubic-bezier(0.2, 0.8, 0.2, 1)';
    const flashMs = 800;
    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    const tops = new WeakMap();
    let list = null;
    let observer = null;

    function enabled() {
        return !reduceMotion.matches && list !== null && list.closest('.overlay.fx') !== null;
    }

    function remember() {
        for (const row of list.children) tops.set(row, row.offsetTop);
    }

    // Called after Blazor changed the list's children: rows added, removed or moved.
    function onRowsChanged() {
        const animate = enabled();
        for (const row of list.children) {
            const before = tops.get(row);
            const after = row.offsetTop;
            tops.set(row, after);
            if (!animate || before === undefined || before === after) continue;
            row.style.transition = 'none';
            row.style.transform = `translateY(${before - after}px)`;
            row.getBoundingClientRect(); // start from the old place before the transition begins
            row.style.transition = glide;
            row.style.transform = '';
            if (before > after) {
                row.removeAttribute('data-overtook');
                row.getBoundingClientRect(); // restart the flash if it was still running
                row.setAttribute('data-overtook', '');
                clearTimeout(row.overtookTimer);
                row.overtookTimer = setTimeout(() => row.removeAttribute('data-overtook'), flashMs);
            }
        }
    }

    // The overlay's row list appears once the page renders and may be replaced; follow whichever is current.
    function attach() {
        const found = document.querySelector('.overlay .rows');
        if (found === list) return;
        if (observer) observer.disconnect();
        list = found;
        observer = null;
        if (!list) return;
        remember();
        observer = new MutationObserver(onRowsChanged);
        observer.observe(list, { childList: true });
    }

    new MutationObserver(attach).observe(document.body, { childList: true, subtree: true });
    attach();
})();
