(function () {
    'use strict';
    let pending = null;
    const confirmed = new WeakSet();
    const modalElement = document.getElementById('reservationCancellationPolicy');
    if (!modalElement) return;

    // Run after form-level validation and before the submission spinner.
    document.addEventListener('submit', function (event) {
        const form = event.target;
        if (!form.matches('form[data-cancellation-policy="true"]') || event.defaultPrevented) return;
        if (confirmed.has(form)) return;
        event.preventDefault();
        if (!form.reportValidity() || pending) return;
        pending = { form: form, submitter: event.submitter, focus: document.activeElement };
        const modal = bootstrap.Modal.getOrCreateInstance(modalElement);
        modal.show();
    });

    modalElement.addEventListener('shown.bs.modal', function () {
        // Keep the non-submitting action first for keyboard users.
        modalElement.querySelector('.modal-footer [data-bs-dismiss]').focus();
    });
    modalElement.addEventListener('hidden.bs.modal', function () {
        const previous = pending;
        pending = null;
        previous?.focus?.focus();
    });
    modalElement.querySelector('[data-confirm-reservation-policy]').addEventListener('click', function () {
        if (!pending) return;
        const submission = pending;
        pending = null;
        bootstrap.Modal.getInstance(modalElement).hide();
        confirmed.add(submission.form);
        try {
            // Re-run validation, preserving the original submit button and anti-forgery fields.
            submission.form.requestSubmit(submission.submitter || undefined);
        } finally {
            // Every later attempt needs its own confirmation, even if validation prevented this one.
            confirmed.delete(submission.form);
        }
    });
}());
