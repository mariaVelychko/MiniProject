import { h } from '../ui/dom.js';
import { state } from '../state.js';
import { Kind, userMessage } from '../api/errors.js';
import { skeletonRows, emptyView, errorView, banner } from '../ui/components.js';
import { formatDateTime, formatMinutes } from '../ui/format.js';
import { toast } from '../ui/toast.js';

const STATUS = { upcoming: ['Заплановано', 'badge-ok'], past: ['Минуло', 'badge-muted'] };

/** Мої операції: список записів зі статусами та скасуванням. Статус і «чи можна скасувати» приходять від сервера. */
export function mountBookings({ container, services, api }) {
  let disposed = false;
  let controller = null;
  const view = h('div', { id: 'my-bookings' });

  container.replaceChildren(h('section', null,
    h('div', { class: 'section-head' }, h('h1', null, 'Мої записи')), view));

  if (state.flash) {
    toast(state.flash.text, { kind: state.flash.kind });
    state.flash = null;
  }

  function row(booking, items) {
    const [label, css] = STATUS[booking.status] ?? [booking.status, 'badge-muted'];
    const problem = h('div', { class: 'inline-problem' });
    const actions = h('div', { class: 'row-actions' });

    const askCancel = () => {
      const confirm = h('button', { class: 'btn btn-danger', type: 'button', onclick: () => cancel(confirm, no) }, 'Так, скасувати');
      const no = h('button', { class: 'btn', type: 'button', onclick: reset }, 'Ні');
      actions.replaceChildren(h('span', { class: 'muted' }, 'Скасувати запис?'), confirm, no);
      confirm.focus();
    };

    const reset = () => {
      actions.replaceChildren(booking.canCancel
        ? h('button', { class: 'btn', type: 'button', onclick: askCancel }, 'Скасувати запис')
        : null);
    };

    async function cancel(confirm, no) {
      if (confirm.disabled) return;   // подвійне натискання
      confirm.disabled = true;
      no.disabled = true;
      confirm.textContent = 'Скасовуємо…';
      problem.replaceChildren();

      try {
        await services.bookings.cancel(booking.workoutId);
        state.screens.clear();   // кількість учасників змінилась
        toast('Запис скасовано.', { kind: 'success' });
        const left = items.filter((b) => b.workoutId !== booking.workoutId);
        items.length = 0;
        items.push(...left);
        render(items);
      } catch (error) {
        if (error.kind === Kind.UNAUTHORIZED) return;
        if (error.kind === Kind.NOT_FOUND) { toast('Цього запису вже немає.'); load(); return; }
        confirm.disabled = false;
        no.disabled = false;
        confirm.textContent = 'Так, скасувати';
        problem.replaceChildren(banner('error', userMessage(error)));
      }
    }

    reset();

    return h('article', { class: 'card booking', dataset: { workoutId: booking.workoutId } },
      h('div', { class: 'booking-main' },
        h('div', { class: 'item-head' }, h('h3', null, booking.title), h('span', { class: `badge ${css}` }, label)),
        h('p', null, formatDateTime(booking.time)),
        h('p', { class: 'muted' }, `${booking.hall} · тренер ${booking.coachName} · ${formatMinutes(booking.durationMin)}`)),
      actions, problem);
  }

  function render(items) {
    if (!items.length) {
      view.replaceChildren(emptyView({
        title: 'У вас ще немає записів',
        text: 'Оберіть заняття в каталозі та запишіться на тренування.',
        action: h('a', { class: 'btn btn-primary', href: '#/' }, 'До каталогу'),
      }));
      return;
    }
    view.replaceChildren(h('div', { class: 'stack' }, items.map((b) => row(b, items))));
  }

  async function load() {
    controller?.abort();
    controller = new AbortController();
    view.replaceChildren(skeletonRows(3));

    try {
      const items = await services.bookings.list({ signal: controller.signal });
      if (!disposed) render(items);
    } catch (error) {
      if (disposed || error?.name === 'AbortError' || error.kind === Kind.UNAUTHORIZED) return;
      view.replaceChildren(errorView({ error, api, onRetry: load, title: 'Не вдалося завантажити записи' }));
    }
  }

  load();
  return () => { disposed = true; controller?.abort(); };
}
