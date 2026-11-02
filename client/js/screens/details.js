import { h } from '../ui/dom.js';
import { state } from '../state.js';
import { Kind, userMessage } from '../api/errors.js';
import { skeletonDetails, emptyView, errorView, banner, retryButton } from '../ui/components.js';
import { formatDateTime, formatPrice, formatMinutes } from '../ui/format.js';

const NAMES = { sessions: 'розклад тренувань', me: 'дані профілю', item: 'опис заняття' };

/**
 * Деталі об'єкта. ОДИН запит до складеного ендпоінта шлюзу (ЛР3) повертає і опис, і розклад, і профіль —
 * клієнт не ходить окремо в каталог, розклад і Auth. Частково деградовану відповідь показуємо чесно:
 * що є — те видно, про відсутнє сказано прямо (а не «тренувань немає»).
 */
export function mountDetails({ container, params, services, api }) {
  const itemId = params.id;
  let controller = null;
  let disposed = false;

  const view = h('div', { id: 'details' });
  container.replaceChildren(h('section', null, h('a', { class: 'back', href: '#/' }, '← До списку'), view));

  function sessionCard(session) {
    return h('article', { class: 'card session' },
      h('div', null,
        h('h3', null, formatDateTime(session.time)),
        h('p', { class: 'muted' }, `${session.hall} · тренер ${session.coachName} · ${formatMinutes(session.durationMin)}`),
        h('p', { class: 'muted small' }, `Записано: ${session.participants} з ${session.maxParticipants}`)),
      h('a', { class: 'btn btn-primary', href: `#/book/${itemId}/${session.id}` }, 'Записатися'));
  }

  function render(data) {
    const { item, sessions, me, partial, unavailable } = data;
    const parts = [];

    if (partial) {
      parts.push(banner('warning',
        h('strong', null, 'Показано не все. '),
        `Тимчасово недоступно: ${unavailable.map((name) => NAMES[name] ?? name).join(', ')}. Решта даних актуальна. `,
        retryButton({ api, onClick: load, label: 'Оновити' })));
    }

    parts.push(h('div', { class: 'card item-detail' },
      h('div', { class: 'item-head' },
        h('h1', null, item.title),
        item.isActive === false && h('span', { class: 'badge badge-muted' }, 'не проводиться')),
      h('p', null, item.description),
      h('div', { class: 'item-meta' },
        h('span', null, item.categoryName), h('span', null, formatPrice(item.price)), h('span', null, formatMinutes(item.durationMin))),
      me && h('p', { class: 'muted small' }, `Ви увійшли як ${me.displayName}`)));

    parts.push(h('h2', null, 'Найближчі тренування'));

    if (sessions === null) {
      parts.push(banner('error', 'Розклад не вдалося завантажити — це не означає, що тренувань немає. Натисніть «Оновити».'));
    } else if (sessions.length === 0) {
      parts.push(emptyView({ title: 'Найближчих тренувань немає', text: 'Зазирніть пізніше або оберіть інше заняття.' }));
    } else {
      parts.push(h('div', { class: 'stack' }, sessions.map(sessionCard)));
    }

    view.replaceChildren(...parts);
  }

  async function load() {
    controller?.abort();
    controller = new AbortController();
    view.replaceChildren(skeletonDetails());

    try {
      const data = await services.screens.training(itemId, { signal: controller.signal });
      if (disposed) return;
      state.screens.set(String(itemId), data);
      if (data.me) state.userName = data.me.displayName;
      render(data);
    } catch (error) {
      if (disposed || error?.name === 'AbortError') return;

      view.replaceChildren(error.kind === Kind.NOT_FOUND
        ? emptyView({ title: 'Заняття не знайдено', text: userMessage(error), action: h('a', { class: 'btn', href: '#/' }, 'До списку') })
        : errorView({ error, api, onRetry: load }));
    }
  }

  load();
  return () => { disposed = true; controller?.abort(); };
}
