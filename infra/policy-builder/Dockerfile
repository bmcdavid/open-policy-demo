# The official opa image has no shell, so copy its static binary into a minimal image that can run a watch loop.
FROM openpolicyagent/opa:latest AS opa

FROM alpine:latest
COPY --from=opa /opa /usr/local/bin/opa
COPY watch.sh /watch.sh
ENTRYPOINT ["/bin/sh", "/watch.sh"]
