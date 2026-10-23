# Thin cedar-agent image with the generated policies baked in. build-policies.sh supplies policies.json in the build
# context; the Api pod points CEDAR_AGENT_POLICIES at /config/policies.json.
FROM permitio/cedar-agent:0.2.2
COPY policies.json /config/policies.json
