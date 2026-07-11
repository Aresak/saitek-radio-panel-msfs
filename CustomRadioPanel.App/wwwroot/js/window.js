// Drag support for the borderless window: the HTML title bar moves the native window via .NET interop.
window.crpDrag = {
    attach: function (el) {
        if (!el || el._crpDragAttached) return;
        el._crpDragAttached = true;

        let dragging = false, startX = 0, startY = 0, winX = 0, winY = 0;
        const dpr = () => window.devicePixelRatio || 1;

        el.addEventListener('pointerdown', async (e) => {
            if (e.button !== 0) return;
            if (e.target.closest('.no-drag')) return; // don't drag when pressing buttons
            const pos = await DotNet.invokeMethodAsync('CustomRadioPanel.App', 'GetWindowPosition');
            winX = pos[0]; winY = pos[1];
            startX = e.screenX; startY = e.screenY;
            dragging = true;
            try { el.setPointerCapture(e.pointerId); } catch { }
        });

        el.addEventListener('pointermove', (e) => {
            if (!dragging) return;
            const d = dpr();
            const nx = Math.round(winX + (e.screenX - startX) * d);
            const ny = Math.round(winY + (e.screenY - startY) * d);
            DotNet.invokeMethodAsync('CustomRadioPanel.App', 'MoveWindow', nx, ny);
        });

        const stop = (e) => {
            if (!dragging) return;
            dragging = false;
            try { el.releasePointerCapture(e.pointerId); } catch { }
        };
        el.addEventListener('pointerup', stop);
        el.addEventListener('pointercancel', stop);
    }
};
