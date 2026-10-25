// Glides the overlay's player rows to their new places when the order changes, instead of letting them jump, and
// flags a row that passed another so app.css flashes it once. FLIP: each row's last position and rank are
// remembered; when Blazor moves rows (they are keyed by player, so the elements themselves move), each moved row
// starts from where it was drawn (its old place, plus any glide still under way) and transitions to the new one.
// Positions are measured from the row list (app.css makes it positioned) and measured again when the overlay's
// style or class changes (row size, effects on or off), so a later reorder never starts from stale places. Runs
// only while the overlay has the "fx" class (Settings, Appearance, "Bar effects") and Windows does not ask for
// reduced motion. Loaded by index.html for every window; it acts only where an overlay row list exists. No
// network, no Blazor interop.
(() => {
    'use strict';

    const glide = 'transform 360ms cubic-bezier(0.2, 0.8, 0.2, 1)';
    const flashMs = 800;
    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    const tops = new WeakMap();
    const ranks = new WeakMap();
    let list = null;
    let rowsObserver = null;
    let overlayObserver = null;

    function enabled() {
        return !reduceMotion.matches && list !== null && list.closest('.overlay.fx') !== null;
    }

    function remember() {
        if (!list) return;
        Array.from(list.children).forEach((row, rank) => {
            tops.set(row, row.offsetTop);
            ranks.set(row, rank);
        });
    }

    // How far a row is still drawn from its layout place by a glide under way.
    function shiftOf(row) {
        const transform = getComputedStyle(row).transform;
        return transform && transform !== 'none' ? new DOMMatrixReadOnly(transform).m42 : 0;
    }

    function flash(row) {
        row.removeAttribute('data-overtook');
        row.getBoundingClientRect(); // restart the flash if it was still running
        row.setAttribute('data-overtook', '');
        clearTimeout(row.overtookTimer);
        row.overtookTimer = setTimeout(() => row.removeAttribute('data-overtook'), flashMs);
    }

    // Called after Blazor changed the list's children: rows added, removed or moved.
    function onRowsChanged() {
        const animate = enabled();
        const rows = Array.from(list.children);
        // Everything about the old order first, before any row's style changes.
        const before = new Map(rows.map(row => [row, {
            top: tops.get(row),
            rank: ranks.get(row),
            shift: animate ? shiftOf(row) : 0,
        }]));
        rows.forEach((row, rank) => {
            const was = before.get(row);
            const after = row.offsetTop;
            tops.set(row, after);
            ranks.set(row, rank);
            if (!animate || was.top === undefined) return;
            const from = was.top + was.shift;
            if (Math.abs(from - after) < 0.5) return;
            row.style.transition = 'none';
            row.style.transform = `translateY(${from - after}px)`;
            row.getBoundingClientRect(); // start from where it was drawn before the transition begins
            row.style.transition = glide;
            row.style.transform = '';
            // Overtook: a row that was ahead of it is now behind it (not merely a row above that went away).
            if (was.rank !== undefined && rows.some((other, otherRank) => {
                const o = before.get(other);
                return o.rank !== undefined && o.rank < was.rank && otherRank > rank;
            })) {
                flash(row);
            }
        });
    }

    // The overlay's row list appears once the page renders and may be replaced; follow whichever is current.
    function attach() {
        const found = document.querySelector('.overlay .rows');
        if (found === list) return;
        if (rowsObserver) rowsObserver.disconnect();
        if (overlayObserver) overlayObserver.disconnect();
        list = found;
        rowsObserver = null;
        overlayObserver = null;
        if (!list) return;
        remember();
        rowsObserver = new MutationObserver(onRowsChanged);
        rowsObserver.observe(list, { childList: true });
        overlayObserver = new MutationObserver(remember);
        overlayObserver.observe(list.closest('.overlay'), { attributes: true, attributeFilter: ['style', 'class'] });
        // In a list past "Rows before scrolling" the player's own row is pinned (app.css, .rows.capped > .row.me), and
        // its offsetTop follows the scroll; measure again so a later reorder starts from where it is drawn. A list
        // that is replaced takes its listener with it.
        list.addEventListener('scroll', remember, { passive: true });
    }

    new MutationObserver(attach).observe(document.body, { childList: true, subtree: true });
    attach();
})();
