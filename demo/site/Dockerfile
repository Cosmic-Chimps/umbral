# The landing and privacy pages: static files served by static-web-server, a single binary
# on a scratch image. `aspire deploy` builds and pushes this image as it does the sample's.
FROM ghcr.io/static-web-server/static-web-server:2.44.3@sha256:ccf2d666ffe5f8ef2ec96bc40f07757605998c04b78f7d8fdb98a584dd4f5717

COPY sws.toml /etc/sws.toml
COPY public /public

ENV SERVER_CONFIG_FILE=/etc/sws.toml
EXPOSE 8080
