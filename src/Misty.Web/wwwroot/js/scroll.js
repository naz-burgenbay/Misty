window.mistyScroll = (() => {
    const handlers = new WeakMap();
    return {
        attach(el, dotNetRef) {
            if (!el || handlers.has(el)) return;
            const handler = () => {
                if (el.scrollTop < 80) {
                    dotNetRef.invokeMethodAsync('OnNearTopAsync');
                }
            };
            el.addEventListener('scroll', handler, { passive: true });
            handlers.set(el, handler);
        },
        scrollToBottomIfNear(el) {
            if (!el) return;
            const distFromBottom = el.scrollHeight - el.scrollTop - el.clientHeight;
            if (distFromBottom < 120 || el.scrollTop === 0) {
                el.scrollTop = el.scrollHeight;
            }
        },
        detach(el) {
            if (!el) return;
            const handler = handlers.get(el);
            if (handler) {
                el.removeEventListener('scroll', handler);
                handlers.delete(el);
            }
        },
    };
})();
