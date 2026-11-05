# The browser side of the harness: Playwright and its browsers, and the two
# scripts. It shares the network namespace of the Umbral container (see
# docker-compose.yml), so `localhost` is Umbral and the sample.
FROM mcr.microsoft.com/playwright:v1.64.1-noble
WORKDIR /work
COPY package.json package-lock.json ./
RUN npm ci
COPY playwright.config.ts setup.spec.ts walk.spec.ts ./
