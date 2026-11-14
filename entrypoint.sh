#!/bin/sh
set -eu

if [ -n "${PRODUCTION_JWT_KEY:-}" ]; then
  export OAuth__JwtKey="$PRODUCTION_JWT_KEY"
fi

node ui/index.js &
node_pid=$!

ready=0
for attempt in 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 29 30; do
  if node -e 'const net = require("net"); const socket = net.connect(3000, "127.0.0.1", () => { socket.end(); process.exit(0); }); socket.on("error", () => process.exit(1));'; then
    ready=1
    break
  fi
  sleep 1
done
if [ "$ready" -ne 1 ]; then
  echo "SSR service did not become ready." >&2
  kill "$node_pid" 2>/dev/null || true
  wait "$node_pid" 2>/dev/null || true
  exit 1
fi

dotnet Gradinware.dll &
dotnet_pid=$!

shutdown() {
  kill "$node_pid" "$dotnet_pid" 2>/dev/null || true
  wait "$node_pid" 2>/dev/null || true
  wait "$dotnet_pid" 2>/dev/null || true
}
trap shutdown EXIT INT TERM

if wait "$dotnet_pid"; then
  exit_code=0
else
  exit_code=$?
fi
exit "$exit_code"
