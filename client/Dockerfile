# Клієнт — це статичні файли: nginx їх віддає, а адреса шлюзу підставляється під час запуску зі змінної API_BASE_URL.
FROM nginx:1.27-alpine
COPY index.html config.js /usr/share/nginx/html/
COPY css /usr/share/nginx/html/css
COPY js /usr/share/nginx/html/js
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY docker-entrypoint.sh /docker-entrypoint.d/40-client-config.sh
# Git у Windows може перетворити переноси рядків на CRLF — з ними shell-скрипт не запуститься.
RUN sed -i 's/\r$//' /docker-entrypoint.d/40-client-config.sh && chmod +x /docker-entrypoint.d/40-client-config.sh
EXPOSE 80
