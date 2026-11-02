import { h } from '../ui/dom.js';
import { state } from '../state.js';
import { Kind } from '../api/errors.js';
import { skeletonCards, emptyView, errorView, banner, retryButton } from '../ui/components.js';
import { formatPrice, formatMinutes, formatClock } from '../ui/format.js';

const CACHE_KEY = 'sportclub.catalog.last';
const SEARCH_DELAY_MS = 400;   // пошук не шле запит на кожну літеру

const readCache = () => { try { return JSON.parse(localStorage.getItem(CACHE_KEY)); } catch { return null; } };
const writeCache = (value) => { try { localStorage.setItem(CACHE_KEY, JSON.stringify(value)); } catch { /* офлайн-копія — лише бонус */ } };

/** Список об'єктів з каталогу: пошук (з затримкою), фільтр за категорією, пагінація, чотири стани, застарілі дані без мережі. */
export function mountCatalog({ container, services, api, navigate }) {
  const query = state.catalogQuery;
  let controller = null;
  let requestId = 0;
  let timer = null;
  let disposed = false;

  const search = h('input', {
    id: 'search', type: 'search', placeholder: 'Пошук за назвою…', value: query.search, 'aria-label': 'Пошук за назвою',
    oninput: () => {
      clearTimeout(timer);
      timer = setTimeout(() => { query.search = search.value.trim(); query.page = 1; load(); }, SEARCH_DELAY_MS);
    },
  });

  const category = h('select', {
    id: 'category', 'aria-label': 'Категорія',
    onchange: () => { query.categoryId = category.value; query.page = 1; load(); },
  }, h('option', { value: '' }, 'Усі категорії'));

  const results = h('div', { id: 'results', 'aria-live': 'polite' });
  const notice = h('div', { id: 'catalog-notice' });

  container.replaceChildren(
    h('section', null,
      h('div', { class: 'section-head' }, h('h1', null, 'Заняття'),
        h('p', { class: 'muted' }, 'Оберіть заняття, щоб побачити розклад і записатися.')),
      h('div', { class: 'toolbar' }, search, category),
      notice,
      results));

  // Категорії — допоміжні: якщо не завантажились, фільтр просто не з'являється (екран працює без нього).
  services.catalog.categories().then((list) => {
    if (disposed) return;
    for (const item of list ?? []) category.append(h('option', { value: item.id }, item.name));
    category.value = String(query.categoryId ?? '');
  }).catch(() => { category.hidden = true; });

  function card(item) {
    return h('article', { class: 'card item-card' },
      h('div', { class: 'item-head' },
        h('h3', null, item.title),
        item.isActive === false && h('span', { class: 'badge badge-muted' }, 'не проводиться')),
      h('p', { class: 'muted' }, item.categoryName),
      h('div', { class: 'item-meta' }, h('span', null, formatPrice(item.price)), h('span', null, formatMinutes(item.durationMin))),
      h('a', { class: 'btn btn-primary', href: `#/item/${item.id}` }, 'Деталі й запис'));
  }

  function pager(data) {
    const pages = Math.max(1, Math.ceil(data.totalCount / data.pageSize));
    if (pages <= 1) return null;
    return h('nav', { class: 'pager', 'aria-label': 'Сторінки' },
      h('button', { class: 'btn', type: 'button', disabled: data.page <= 1, onclick: () => { query.page = data.page - 1; load(); } }, '← Назад'),
      h('span', { class: 'muted' }, `Сторінка ${data.page} з ${pages}`),
      h('button', { class: 'btn', type: 'button', disabled: data.page >= pages, onclick: () => { query.page = data.page + 1; load(); } }, 'Далі →'));
  }

  function renderSuccess(data, { stale = null } = {}) {
    notice.replaceChildren(stale
      ? banner('warning', h('strong', null, 'Застарілі дані. '), `Немає зв’язку із сервером, показано список, збережений о ${formatClock(new Date(stale))}. `,
        retryButton({ api, onClick: load, label: 'Оновити' }))
      : '');

    if (!data.items?.length) {
      results.replaceChildren(emptyView({
        title: 'Нічого не знайдено',
        text: query.search || query.categoryId ? 'Спробуйте змінити пошук або категорію.' : 'У каталозі поки немає занять.',
        action: (query.search || query.categoryId) && h('button', {
          class: 'btn', type: 'button',
          onclick: () => { query.search = ''; query.categoryId = ''; query.page = 1; search.value = ''; category.value = ''; load(); },
        }, 'Скинути пошук'),
      }));
      return;
    }

    results.replaceChildren(...[h("div", { class: "grid" }, data.items.map(card)), pager(data)].filter(Boolean));
  }

  async function load() {
    controller?.abort();                  // попередній запит уже не потрібен
    controller = new AbortController();
    const id = ++requestId;
    const params = { search: query.search, categoryId: query.categoryId, page: query.page };
    const cacheKey = JSON.stringify(params);

    notice.replaceChildren();
    results.replaceChildren(skeletonCards(6));
    results.setAttribute('aria-busy', 'true');

    try {
      const data = await services.catalog.list({ ...params, signal: controller.signal });
      if (disposed || id !== requestId) return;   // відповідь на застарілий запит ігноруємо
      writeCache({ cacheKey, savedAt: Date.now(), data });
      renderSuccess(data);
    } catch (error) {
      if (disposed || id !== requestId || error?.name === 'AbortError') return;

      const cached = readCache();
      const offline = [Kind.NETWORK, Kind.TIMEOUT, Kind.UNAVAILABLE, Kind.SERVER, Kind.RATE_LIMITED].includes(error.kind);

      if (offline && cached?.cacheKey === cacheKey) {
        renderSuccess(cached.data, { stale: cached.savedAt });
      } else {
        results.replaceChildren(errorView({ error, api, onRetry: load }));
      }
    } finally {
      if (id === requestId) results.removeAttribute('aria-busy');
    }
  }

  load();

  return () => { disposed = true; clearTimeout(timer); controller?.abort(); };
}
