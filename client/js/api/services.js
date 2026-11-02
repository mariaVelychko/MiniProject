
export function createServices(api, tokens) {
  return {
    auth: {
      async login(email, password) {
        const data = await api.post('/api/v1/auth/login', { email, password }, { auth: false });
        tokens.set(data);
      },

      async logout() {
        const refreshToken = tokens.refreshToken;
        tokens.clear();   // з пристрою токени зникають одразу, навіть якщо сервер недоступний
        if (!refreshToken) return;
        try {
          await api.post('/api/v1/auth/logout', { refreshToken }, { auth: true });
        } catch {
          /* вихід з пристрою вже виконано */
        }
      },
    },

    catalog: {
      /** Список позицій: пошук, категорія, пагінація — на боці сервера. */
      list: ({ search, categoryId, page = 1, pageSize = 6, signal } = {}) =>
        api.get('/api/v1/catalog/items', { query: { search, categoryId, page, pageSize }, signal }),

      categories: ({ signal } = {}) => api.get('/api/v1/catalog/categories', { signal }),
    },

    /** Екран деталей — ОДИН запит до складеного ендпоінта шлюзу (ЛР3), а не три окремі. */
    screens: {
      training: (itemId, { signal } = {}) => api.send('GET', `/api/v1/screens/training/${encodeURIComponent(itemId)}`, { signal }).then((r) => r.data),
    },

    bookings: {
      list: ({ signal } = {}) => api.get('/api/v1/me/bookings', { signal }),

      /** Idempotency-Key генерує клієнт: див. screens/book.js. */
      create: (workoutId, idempotencyKey) =>
        api.post('/api/v1/me/bookings', { workoutId }, { headers: { 'Idempotency-Key': idempotencyKey } }),

      cancel: (workoutId) => api.delete(`/api/v1/me/bookings/${encodeURIComponent(workoutId)}`),
    },
  };
}
