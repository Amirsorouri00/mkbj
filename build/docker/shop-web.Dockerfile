FROM node:24-alpine AS build
WORKDIR /workspace

RUN corepack enable && corepack prepare pnpm@11.25.0 --activate

COPY . .
RUN pnpm install --frozen-lockfile

ARG NEXT_PUBLIC_MKBJ_API_GRAPHQL_URL
ARG NEXT_PUBLIC_KEYCLOAK_URL
ARG NEXT_PUBLIC_KEYCLOAK_REALM=Shop
ARG NEXT_PUBLIC_KEYCLOAK_CLIENT_ID=modular-monolith
ARG NEXT_PUBLIC_MKBJ_MEDIA_BASE_URL

ENV NEXT_PUBLIC_MKBJ_API_GRAPHQL_URL=$NEXT_PUBLIC_MKBJ_API_GRAPHQL_URL \
    NEXT_PUBLIC_KEYCLOAK_URL=$NEXT_PUBLIC_KEYCLOAK_URL \
    NEXT_PUBLIC_KEYCLOAK_REALM=$NEXT_PUBLIC_KEYCLOAK_REALM \
    NEXT_PUBLIC_KEYCLOAK_CLIENT_ID=$NEXT_PUBLIC_KEYCLOAK_CLIENT_ID \
    NEXT_PUBLIC_MKBJ_MEDIA_BASE_URL=$NEXT_PUBLIC_MKBJ_MEDIA_BASE_URL

RUN pnpm --filter @mkbj/shop-web build

FROM node:24-alpine AS runtime
WORKDIR /app

ENV NODE_ENV=production \
    HOSTNAME=0.0.0.0 \
    PORT=3000

RUN addgroup --system --gid 1001 nodejs && adduser --system --uid 1001 nextjs

COPY --from=build --chown=nextjs:nodejs /workspace/apps/shop-web/.next/standalone ./
COPY --from=build --chown=nextjs:nodejs /workspace/apps/shop-web/.next/static ./apps/shop-web/.next/static
COPY --from=build --chown=nextjs:nodejs /workspace/apps/shop-web/public ./apps/shop-web/public

USER nextjs
EXPOSE 3000
HEALTHCHECK --interval=15s --timeout=5s --retries=5 \
  CMD wget -qO- http://127.0.0.1:3000/health >/dev/null || exit 1

CMD ["node", "apps/shop-web/server.js"]

