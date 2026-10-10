# syntax=docker/dockerfile:1

# Build:  docker buildx build --platform linux/amd64,linux/arm64 -t umbral .
#
# The build stage always runs on the machine doing the build ($BUILDPLATFORM)
# and cross-publishes for the target. An arm64 image built on an amd64 runner
# therefore never compiles under QEMU, which turns a two-minute build into a
# thirty-minute one.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# global.json pins SDK 10.0.400 with rollForward=latestFeature, so the image's
# newer 10.0.x SDK satisfies it as is; nothing to override here.
COPY global.json Directory.Build.props ./

# Restore first, from the project files alone, so editing a .fs file does not
# throw away the NuGet layer. The tests are not in the image, so restore the web
# project (which pulls in Umbral.Migrations) rather than the solution.
COPY src/Umbral/Umbral.fsproj src/Umbral/
COPY src/Umbral.Migrations/Umbral.Migrations.csproj src/Umbral.Migrations/
RUN case "$TARGETARCH" in \
      amd64) rid=linux-x64 ;; \
      arm64) rid=linux-arm64 ;; \
      *) echo "unsupported TARGETARCH: $TARGETARCH" >&2; exit 1 ;; \
    esac \
 && dotnet restore src/Umbral/Umbral.fsproj -r "$rid"

COPY src/ src/
# Framework-dependent: the aspnet base image supplies the runtime. A
# RID-specific publish still emits the /app/Umbral apphost, which is the
# command the reset-credential docs tell an operator to run.
RUN case "$TARGETARCH" in \
      amd64) rid=linux-x64 ;; \
      arm64) rid=linux-arm64 ;; \
    esac \
 && dotnet publish src/Umbral/Umbral.fsproj -c Release -r "$rid" \
      --no-restore --no-self-contained -o /app


# Debian, not Alpine. musl adds a second RID to SQLite's native asset, and a
# missing libe_sqlite3.so is the classic way an ARM container fails to start.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Everything Umbral persists lives in this one directory: umbral.db, the token
# signing keys, and the Data Protection key ring.
#
# LOSING IT MEANS RE-ENTERING EVERY SECRET. Connector client secrets and app
# secrets are encrypted with that key ring and cannot be read without it, and
# every issued token stops validating. Back it up by copying this one directory
# (stop the container first, or copy umbral.db together with its -wal file).
#
# Created and chowned here so a fresh named volume, which copies this
# directory's ownership, is writable by the non-root user. A bind mount keeps
# the host's ownership instead: `chown 1654:1654 ./data` before the first run
# (1654 is the aspnet image's `app` user).
RUN mkdir /data && chown $APP_UID:$APP_UID /data
VOLUME /data
ENV UMBRAL__DATA_DIRECTORY=/data

# SC-007: idle residency under 150 MB. Workstation GC is how; the project file
# already sets it, and this keeps an inherited environment from changing it.
ENV DOTNET_gcServer=0

# The base image already sets ASPNETCORE_HTTP_PORTS=8080. Plain http inside the
# container is deliberate: TLS belongs to the reverse proxy in front of it, and
# UMBRAL__PUBLIC_ORIGIN (required, https) says where people reach it from.
EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["/app/Umbral"]
