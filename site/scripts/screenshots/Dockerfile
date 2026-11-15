# The browser side of the screenshot script: Playwright and its browsers, sharp,
# and the scripts. It shares the network namespace of the Umbral container (see
# docker-compose.yml), so `localhost` is Umbral. Same Playwright pin as
# samples/test/Dockerfile.
FROM mcr.microsoft.com/playwright:v1.64.1-noble
WORKDIR /work
COPY package.json package-lock.json ./
RUN npm ci
COPY capture.mjs optimise.mjs in-container.sh ./
CMD ["bash", "in-container.sh"]
