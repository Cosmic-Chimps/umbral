FROM node:24-alpine AS build
WORKDIR /src
ENV NEXT_TELEMETRY_DISABLED=1
# Install from the package files alone, so a code edit keeps the npm layer.
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

# The standalone output is the app and the few packages it needs, run by plain node.
FROM node:24-alpine
WORKDIR /app
ENV PORT=3000 HOSTNAME=0.0.0.0 NODE_ENV=production NEXT_TELEMETRY_DISABLED=1
COPY --from=build /src/.next/standalone ./
COPY --from=build /src/.next/static ./.next/static
USER node
EXPOSE 3000
CMD ["node", "server.js"]
