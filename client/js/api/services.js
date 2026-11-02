
export function createServices(api, tokens) {
  return {
    auth: {
      async login(email, password) {
        const data = await api.post('/api/v1/auth/login', { email, password }, { auth: false });
        tokens.set(data);
      },

      async logout() {
        const refreshToken = tokens.refreshToken;
        tokens.clear();
        if (!refreshToken) return;
        try {
          await api.post('/api/v1/auth/logout', { refreshToken }, { auth: true });
        } catch {
          
        }
      },
    },

    catalog: {
      
      list: ({ search, categoryId, page = 1, pageSize = 6, signal } = {}) =>
        api.get('/api/v1/catalog/items', { query: { search, categoryId, page, pageSize }, signal }),

      categories: ({ signal } = {}) => api.get('/api/v1/catalog/categories', { signal }),
    },

    screens: {
      training: (itemId, { signal } = {}) => api.send('GET', `/api/v1/screens/training/${encodeURIComponent(itemId)}`, { signal }).then((r) => r.data),
    },

    bookings: {
      list: ({ signal } = {}) => api.get('/api/v1/me/bookings', { signal }),

      create: (workoutId, idempotencyKey) =>
        api.post('/api/v1/me/bookings', { workoutId }, { headers: { 'Idempotency-Key': idempotencyKey } }),

      cancel: (workoutId) => api.delete(`/api/v1/me/bookings/${encodeURIComponent(workoutId)}`),
    },
  };
}
