#!/bin/bash
set -eu

if [ -n "${PRODUCTION_JWT_KEY:-}" ]; then
  export OAuth__JwtKey="$PRODUCTION_JWT_KEY"
fi
if [ -n "${PRODUCTION_RECAPTCHA_SITE_KEY:-}" ]; then
  export Recaptcha__SiteKey="$PRODUCTION_RECAPTCHA_SITE_KEY"
fi
if [ -n "${PRODUCTION_RECAPTCHA_SECRET_KEY:-}" ]; then
  export Recaptcha__SecretKey="$PRODUCTION_RECAPTCHA_SECRET_KEY"
fi
if [ -n "${PRODUCTION_RECAPTCHA_HOSTNAME:-}" ]; then
  export Recaptcha__Hostname="$PRODUCTION_RECAPTCHA_HOSTNAME"
fi

node_pid=
dotnet_pid=
shutdown() {
  trap - EXIT INT TERM
  for pid in "$node_pid" "$dotnet_pid"; do
    if [ -n "$pid" ]; then kill "$pid" 2>/dev/null || true; fi
  done
  for pid in "$node_pid" "$dotnet_pid"; do
    if [ -n "$pid" ]; then wait "$pid" 2>/dev/null || true; fi
  done
}
trap shutdown EXIT
trap 'exit 143' TERM
trap 'exit 130' INT

node ui/ssr.cjs &
node_pid=$!

ready=0
for attempt in {1..30}; do
  if ! kill -0 "$node_pid" 2>/dev/null; then
    echo "SSR service exited before becoming ready." >&2
    exit 1
  fi
  if node -e 'const http = require("node:http"); const req = http.get("http://127.0.0.1:3000/health", res => { res.resume(); process.exit(res.statusCode === 204 ? 0 : 1); }); req.setTimeout(1000, () => req.destroy()); req.on("error", () => process.exit(1));'; then
    ready=1
    break
  fi
  sleep 1
done
if [ "$ready" -ne 1 ]; then
  echo "SSR service did not become ready." >&2
  exit 1
fi

dotnet Gradinware.dll &
dotnet_pid=$!

# A renderer crash must take the container down, even if .NET is still healthy.
exit_code=0
wait -n "$node_pid" "$dotnet_pid" || exit_code=$?
if ! kill -0 "$node_pid" 2>/dev/null; then
  echo "SSR service exited; stopping the application." >&2
  if [ "$exit_code" -eq 0 ]; then exit_code=1; fi
fi
exit "$exit_code"
