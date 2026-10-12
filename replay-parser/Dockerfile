# syntax=docker/dockerfile:1
# Replay parser (Go). Built by the root docker-compose.yml (service replay-parser); standalone:
#   docker build -t dotaanalyst-replay-parser replay-parser
#   docker run --rm -p 8081:8081 -e REPLAY_PARSER_API_KEY=<key> dotaanalyst-replay-parser

# 1) Build: a static binary (CGO_ENABLED=0), so the final image needs no C library.
FROM golang:1.27.0-alpine3.24 AS build
WORKDIR /src
# Modules first, so this layer is cached until go.mod/go.sum change. The cache mounts keep downloaded modules and
# compiled packages between builds (BuildKit, the default in Docker Desktop and compose).
COPY go.mod go.sum ./
RUN --mount=type=cache,target=/go/pkg/mod go mod download
COPY . .
# -trimpath: no local paths in the binary; -s -w: no debug symbols (smaller image).
RUN --mount=type=cache,target=/go/pkg/mod \
    --mount=type=cache,target=/root/.cache/go-build \
    CGO_ENABLED=0 go build -trimpath -ldflags="-s -w" -o /out/replay-parser ./cmd/replay-parser

# 2) Runtime: distroless "static" has only CA certificates (for https replay URLs), tzdata and a non-root user;
# no shell or package manager. Pinned by digest; update it on purpose (docker buildx imagetools inspect <tag>).
FROM gcr.io/distroless/static-debian13:nonroot@sha256:e2e927ec666bae08560abb3c55d0659eceabb657f56b6782ab500a9fc7f555e3
COPY --from=build /out/replay-parser /replay-parser
USER nonroot:nonroot
EXPOSE 8081
# No curl in the image: the binary checks itself (used by the compose healthcheck).
HEALTHCHECK --interval=30s --timeout=5s --start-period=5s --retries=3 CMD ["/replay-parser", "-healthcheck"]
ENTRYPOINT ["/replay-parser"]
