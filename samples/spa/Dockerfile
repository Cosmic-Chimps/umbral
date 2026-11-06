FROM node:24-alpine AS build
WORKDIR /src
# Install from the package files alone, so a code edit keeps the npm layer.
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

# nginx-unprivileged runs as a non-root user, as the ASP.NET Core sample does.
FROM nginxinc/nginx-unprivileged:1.29-alpine
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /src/dist /usr/share/nginx/html
ENV PORT=5173
EXPOSE 5173
