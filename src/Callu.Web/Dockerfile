# ─── Stage 1: Build ───
FROM node:22.23.1-alpine AS build

WORKDIR /app

# Copy package files first (layer caching)
COPY package.json package-lock.json ./

# Install dependencies
RUN npm ci

# Copy source code
COPY . .

# Build production bundle
# No VITE_API_URL needed — defaults to window.location.origin (nginx proxy)
RUN npm run build

# ─── Stage 2: Production (nginx) ───
FROM nginx:1.30.3-alpine AS runtime

# Remove default nginx config
RUN rm /etc/nginx/conf.d/default.conf

# Copy our nginx config
COPY nginx.conf /etc/nginx/conf.d/callu.conf

# Copy built SPA from build stage
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 3000

# 127.0.0.1, not localhost: the container's /etc/hosts maps localhost to both 127.0.0.1 and ::1,
# and busybox wget tries ::1 first. nginx listens on IPv4 only (`listen 3000;`), so the probe got
# "Connection refused" and the container sat unhealthy forever while the UI was serving fine.
# Addressing IPv4 explicitly keeps this independent of resolution order — and, unlike adding
# `listen [::]:3000;`, it cannot fail to bind on a host with IPv6 disabled in the kernel.
HEALTHCHECK --interval=30s --timeout=5s --retries=3 --start-period=10s \
    CMD wget -q -O /dev/null http://127.0.0.1:3000/index.html || exit 1

CMD ["nginx", "-g", "daemon off;"]
