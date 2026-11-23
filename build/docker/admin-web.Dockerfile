FROM node:24-alpine AS build
WORKDIR /workspace

RUN corepack enable && corepack prepare pnpm@11.25.0 --activate

COPY . .
RUN pnpm install --frozen-lockfile

ARG VITE_API_BASE_URL
ARG VITE_GRAPHQL_URL
ARG VITE_KEYCLOAK_URL
ARG VITE_KEYCLOAK_REALM=Backoffice
ARG VITE_KEYCLOAK_CLIENT_ID=backoffice-console

ENV VITE_API_BASE_URL=$VITE_API_BASE_URL \
    VITE_GRAPHQL_URL=$VITE_GRAPHQL_URL \
    VITE_KEYCLOAK_URL=$VITE_KEYCLOAK_URL \
    VITE_KEYCLOAK_REALM=$VITE_KEYCLOAK_REALM \
    VITE_KEYCLOAK_CLIENT_ID=$VITE_KEYCLOAK_CLIENT_ID

RUN pnpm --filter @mkbj/admin-web build

FROM nginx:1.29-alpine AS runtime
COPY build/docker/admin-web.nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /workspace/apps/admin-web/dist /usr/share/nginx/html

EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --retries=5 \
  CMD wget -qO- http://127.0.0.1:8080/health >/dev/null || exit 1

