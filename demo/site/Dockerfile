# The landing and privacy pages: static files served by static-web-server, a single binary
# on a scratch image. `aspire deploy` builds and pushes this image as it does the sample's.
FROM ghcr.io/static-web-server/static-web-server:2.44.2@sha256:23be6c04d474979d6e738081918f5874f88502a150d1123fd000719101a0f05c

COPY sws.toml /etc/sws.toml
COPY public /public

ENV SERVER_CONFIG_FILE=/etc/sws.toml
EXPOSE 8080
