import { h } from '../ui/dom.js';
import { state } from '../state.js';
import { createAttempt } from '../idempotency.js';
import { ApiError, Kind, userMessage } from '../api/errors.js';
import { skeletonDetails, emptyView, errorView, banner, watchRateLimit } from '../ui/components.js';
import { formatDateTime } from '../ui/format.js';

/**
 * Створення операції: запис на тренування. Форма з валідацією, помилками від сервера біля поля, Idempotency-Key,
 * який генерує клієнт, і захистом від подвійного натискання. Ідентифікатор користувача форма НЕ надсилає:
 * сервер бере його з токена.
 */
export function mountBook({ container, params, services, api, navigate }) {
  const itemId = params.id;
  const attempt = createAttempt();
  let busy = false;
  let disposed = false;
  let controller = null;
  let stopWatching = () => {};

  const view = h('div', { id: 'book' });
  container.replaceChildren(h('section', null, h('a', { class: 'back', href: `#/item/${itemId}` }, '← До заняття'), view));

  async function load() {
    controller?.abort();
    controller = new AbortController();
    view.replaceChildren(skeletonDetails());

    try {
      // Екран деталей уже завантажив складену відповідь — повторного запиту не робимо.
      // Часткову відповідь (без розкладу) повторно не використовуємо: форма без розкладу безглузда, краще спитати ще раз.
      const cached = state.screens.get(String(itemId));
      const data = cached && !cached.partial ? cached : await services.screens.training(itemId, { signal: controller.signal });
      if (disposed) return;
      state.screens.set(String(itemId), data);
      render(data);
    } catch (error) {
      if (disposed || error?.name === 'AbortError') return;
      view.replaceChildren(errorView({ error, api, onRetry: load }));
    }
  }

  function render({ item, sessions }) {
    if (sessions === null) {
      view.replaceChildren(errorView({
        error: new ApiError({ kind: Kind.UNAVAILABLE, detail: 'Розклад тимчасово недоступний, тому записатися зараз не можна.' }),
        api,
        onRetry: () => { state.screens.delete(String(itemId)); load(); },
        title: 'Розклад недоступний',
      }));
      return;
    }

    if (sessions.length === 0) {
      view.replaceChildren(emptyView({ title: 'Немає тренувань для запису', text: 'Для цього заняття ще не заплановано тренувань.', action: h('a', { class: 'btn', href: '#/' }, 'До списку') }));
      return;
    }

    const select = h('select', { id: 'workout', name: 'workoutId', 'aria-describedby': 'workout-error' },
      h('option', { value: '' }, '— оберіть тренування —'),
      sessions.map((s) => h('option', { value: s.id }, `${formatDateTime(s.time)} · ${s.hall} · ${s.coachName}`)));

    if (params.session) select.value = String(params.session);

    const fieldError = h('p', { class: 'field-error', id: 'workout-error', role: 'alert' });
    const message = h('div', { id: 'book-message' });
    const submit = h('button', { class: 'btn btn-primary', type: 'submit' }, 'Підтвердити запис');

    const refresh = (left = api.rateLimit.remainingSeconds) => {
      select.disabled = busy;
      submit.disabled = busy || left > 0;
      submit.textContent = busy ? 'Записуємо…'
        : left > 0 ? `Спробуйте через ${left} с`
        : attempt.current ? 'Повторити спробу' : 'Підтвердити запис';
    };

    stopWatching();
    stopWatching = watchRateLimit(api, submit, refresh);

    const setFieldError = (text) => {
      fieldError.textContent = text ?? '';
      select.toggleAttribute('aria-invalid', Boolean(text));
    };

    const form = h('form', {
      class: 'card form', novalidate: true,
      onsubmit: async (event) => {
        event.preventDefault();
        if (busy) return;   // другий клік, поки йде запит, нічого не відправляє

        setFieldError(null);
        message.replaceChildren();

        const workoutId = Number(select.value);
        if (!workoutId) { setFieldError('Оберіть тренування.'); select.focus(); return; }

        busy = true;
        refresh();

        try {
          await services.bookings.create(workoutId, attempt.keyFor({ workoutId }));
          attempt.settle({ outcomeUnknown: false });
          state.screens.delete(String(itemId));   // кількість учасників змінилась — екран деталей треба перезавантажити
          state.flash = { text: 'Ви записані на тренування.', kind: 'success' };
          navigate('#/my');
        } catch (error) {
          attempt.settle(error);   // мережа/таймаут → ключ лишається, повтор не створить дубль
          busy = false;
          refresh();
          showError(error);
        }
      },
    },
    h('h1', null, 'Запис на тренування'),
    h('p', { class: 'muted' }, item.title),
    message,
    h('label', { for: 'workout' }, 'Тренування'), select, fieldError,
    h('div', { class: 'form-actions' }, submit, h('a', { class: 'btn', href: `#/item/${itemId}` }, 'Скасувати')));

    function showError(error) {
      if (error.kind === Kind.UNAUTHORIZED) return;   // про вхід подбає головний обробник сесії

      const byField = error.fieldErrors?.workoutId;
      if (byField?.length) setFieldError(byField[0]);

      const others = Object.entries(error.fieldErrors ?? {}).filter(([name]) => name !== 'workoutId').flatMap(([, list]) => list);

      if (error.kind === Kind.RATE_LIMITED) {
        message.replaceChildren(banner('warning', `Забагато запитів. Кнопка стане активною через ${error.retryAfterSeconds} с — запит у цей час не надсилається.`));
      } else if (error.outcomeUnknown) {
        message.replaceChildren(banner('error', 'Не вдалося підтвердити запис. Натисніть «Повторити спробу» — повторний запит не створить дубль.'));
      } else if (others.length || !byField?.length) {
        message.replaceChildren(banner('error', others[0] ?? userMessage(error)));
      }
    }

    view.replaceChildren(form);
    refresh();
  }

  load();

  return () => { disposed = true; controller?.abort(); stopWatching(); };
}
