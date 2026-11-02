#!/bin/sh
# Записує config.js зі змінної середовища. Адреса — це ШЛЮЗ, яким користується браузер користувача
# (тому в docker-compose це http://localhost:5000, а не ім'я контейнера). Жодних секретів тут немає.
set -e
cat > /usr/share/nginx/html/config.js <<CONFIG
window.APP_CONFIG = { apiBaseUrl: '${API_BASE_URL:-http://localhost:5000}' };
CONFIG
echo "client: apiBaseUrl = ${API_BASE_URL:-http://localhost:5000}"
