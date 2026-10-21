window.editEquipmentFilter = function () {
    const form = document.getElementById('reservation-edit-form');
    const picker = id => window.jQuery('#' + id).data('kendoDateTimePicker');
    return {
        facilityId: form.dataset.facilityId || null,
        roomId: form.dataset.roomId || null,
        startDate: picker('edit-start')?.value() ? window.kendo.toString(picker('edit-start').value(), 'yyyy-MM-ddTHH:mm:ss') : null,
        endDate: picker('edit-end')?.value() ? window.kendo.toString(picker('edit-end').value(), 'yyyy-MM-ddTHH:mm:ss') : null
    };
};
window.onEditEquipmentChange = function () {
    document.getElementById('reservation-edit-form')?.dispatchEvent(new Event('change'));
};
window.jQuery(function () {
    const form = document.getElementById('reservation-edit-form');
    if (!form) return;
    const start = document.getElementById('edit-start');
    const end = document.getElementById('edit-end');
    const error = document.getElementById('edit-schedule-error');
    const purpose = document.getElementById('edit-purpose');
    const dateFormats = ['MM/dd/yyyy hh:mm tt', 'MM/dd/yyyy h:mm tt', 'yyyy-MM-ddTHH:mm', 'yyyy-MM-ddTHH:mm:ss'];
    const readDate = input => window.kendo.parseDate(input.value, dateFormats);
    [start, end].forEach(input => {
        const initialValue = input.value;
        window.jQuery(input).kendoDateTimePicker({
            format: 'MM/dd/yyyy hh:mm tt',
            timeFormat: 'hh:mm tt',
            parseFormats: dateFormats,
            interval: 30,
            min: new Date(),
            value: window.kendo.parseDate(initialValue, dateFormats),
            change: validate,
            open: validate
        });
        // Preserve invalid submissions so the user can correct them in place.
        if (initialValue && !window.jQuery(input).data('kendoDateTimePicker').value()) input.value = initialValue;
    });
    function updateReview() {
        const put = (key, value) => {
            const target = form.querySelector('[data-edit-review="' + key + '"]');
            if (target) target.textContent = value;
        };
        const dateLabel = input => readDate(input)
            ? readDate(input).toLocaleString([], { month: 'short', day: 'numeric', year: 'numeric', hour: 'numeric', minute: '2-digit' })
            : 'Select date and time';
        const guestInputs = form.querySelectorAll('[name="_guests[]"]');
        put('people', guestInputs.length ? Array.from(guestInputs).map(input => input.value.trim()).filter(Boolean).join(', ') || 'Enter guest names' : document.getElementById('edit-attendees')?.value || 'Not provided');
        put('start', dateLabel(start));
        put('end', dateLabel(end));
        put('purpose', purpose.value.trim() || 'No purpose entered');
        const widget = window.jQuery && window.jQuery('#EquipmentNeeded').data('kendoMultiSelect');
        const names = widget ? widget.dataItems().map(item => item.EquipmentName)
            : Array.from(form.querySelectorAll('#EquipmentNeeded option:checked')).map(option => option.textContent);
        put('equipment', names.join(', ') || 'No equipment selected');
    }
    function validate() {
        const now = new Date();
        const startDate = readDate(start);
        const endDate = readDate(end);
        const minimumStart = new Date(Math.ceil((now.getTime() + 1) / 60000) * 60000);
        const minimumEnd = startDate ? new Date(Math.max(startDate.getTime() + 60000, minimumStart.getTime())) : minimumStart;
        window.jQuery(start).data('kendoDateTimePicker').min(minimumStart);
        window.jQuery(end).data('kendoDateTimePicker').min(minimumEnd);
        let message = '';
        let invalid = start;
        if (!startDate || !endDate) message = 'Enter valid dates and times or select them using the calendar and clock.';
        else if (startDate <= now) message = 'Choose a start date and time in the future. Past schedules cannot be saved.';
        else if (endDate <= startDate) {
            message = 'End date and time must be later than start date and time.';
            invalid = end;
        }
        [start, end].forEach(input => { input.setCustomValidity(''); input.setAttribute('aria-invalid', 'false'); input.classList.remove('is-invalid'); window.jQuery(input).data('kendoDateTimePicker').wrapper.removeClass('is-invalid reservation-date-invalid'); });
        if (message) { invalid.setCustomValidity(message); invalid.setAttribute('aria-invalid', 'true'); window.jQuery(invalid).data('kendoDateTimePicker').wrapper.addClass('reservation-date-invalid'); }
        error.textContent = message;
        error.hidden = !message;
        purpose.setCustomValidity(purpose.value.trim() ? '' : 'Enter a reservation purpose.');
        document.getElementById('edit-purpose-count').textContent = purpose.value.length + '/50';
        updateReview();
        return !message;
    }
    form.addEventListener('input', validate);
    form.addEventListener('change', validate);
    form.addEventListener('focusin', validate);
    form.addEventListener('submit', event => {
        validate();
        if (!form.checkValidity()) { event.preventDefault(); form.reportValidity(); }
    });
    const guests = document.getElementById('edit-guests');
    if (guests) {
        document.getElementById('add-edit-guest').addEventListener('click', () => {
            const row = guests.firstElementChild.cloneNode(true);
            row.querySelectorAll('input, select').forEach(input => { input.value = ''; });
            guests.appendChild(row);
            row.querySelector('input').focus();
            validateGuests();
        });
        guests.addEventListener('click', event => {
            if (event.target.closest('[data-remove-guest]') && guests.children.length > 1) {
                event.target.closest('.guest-edit-row').remove();
                validateGuests();
            }
        });
        function validateGuests() {
            updateReview();
            document.getElementById('edit-guest-count').textContent = guests.children.length + (guests.children.length === 1 ? ' guest' : ' guests');
            Array.from(guests.children).forEach((row, index) => {
                row.querySelector('[data-remove-guest]').hidden = index === 0;
            });
            const names = Array.from(guests.querySelectorAll('[name="_guests[]"]'));
            names.forEach(input => {
                const name = input.value.trim().toLowerCase();
                input.setCustomValidity(!name ? 'Enter a guest name.' : names.filter(other => other.value.trim().toLowerCase() === name).length > 1 ? 'Enter each guest only once.' : '');
            });
        }
        form.addEventListener('input', validateGuests);
        validateGuests();
    }
    validate();
    if (window.jQuery) window.jQuery(updateReview);
});
