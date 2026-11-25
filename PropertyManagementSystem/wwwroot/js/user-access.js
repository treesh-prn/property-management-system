(() => {
    const editor = document.getElementById('user-access-editor');
    const content = document.getElementById('user-access-editor-content');
    if (!editor || !content) return;
    let request;

    editor.addEventListener('show.bs.offcanvas', async event => {
        const url = event.relatedTarget?.dataset.editUrl || editor.dataset.initialEditUrl;
        if (!url) return;
        if (event.relatedTarget) document.getElementById('user-access-save-error')?.remove();
        request?.abort();
        const current = new AbortController();
        request = current;
        content.textContent = 'Loading user access…';
        content.setAttribute('aria-busy', 'true');
        try {
            const response = await fetch(url, { signal: current.signal, headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (!response.ok || response.redirected) throw new Error('Unable to load user access. Close this panel and try again, or sign in again.');
            const html = await response.text();
            if (current.signal.aborted) return;
            content.innerHTML = html;
            const form = content.querySelector('form');
            const switches = Array.from(form.querySelectorAll('input[type="checkbox"][name="SelectedRoles"]'));
            const initial = switches.map(input => input.checked);
            const save = form.querySelector('button[type="submit"]');
            const updateState = () => {
                const selected = switches.filter(input => input.checked).length;
                const dirty = switches.some((input, index) => input.checked !== initial[index]);
                form.querySelector('[data-selected-count]').textContent = `${selected} selected`;
                form.querySelector('[data-no-roles]').hidden = selected > 0;
                form.querySelector('[data-edit-status]').textContent = dirty ? 'You have unsaved changes' : 'No changes to save';
                save.disabled = !dirty;
            };
            switches.forEach(input => input.addEventListener('change', updateState));
            updateState();
            form.addEventListener('submit', event => {
                const button = event.currentTarget.querySelector('button[type="submit"]');
                button.disabled = true;
                button.textContent = 'Saving…';
            });
        } catch (error) {
            if (current.signal.aborted) return;
            content.textContent = error.message || 'Unable to load user access. Please try again.';
        } finally {
            if (request === current) content.removeAttribute('aria-busy');
        }
    });
    editor.addEventListener('hidden.bs.offcanvas', () => {
        request?.abort();
        content.replaceChildren();
    });
    if (editor.dataset.initialEditUrl) bootstrap.Offcanvas.getOrCreateInstance(editor).show();
})();
