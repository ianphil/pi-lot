#!/usr/bin/env bash

set -euo pipefail

port=5100
host=127.0.0.1
model="gpt-5.4-mini"
prompt="Reply with exactly: hello"
use_env_token=0
stream_flag="--no-stream"
dotnet_bin=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --port)
      port="$2"
      shift 2
      ;;
    --host)
      host="$2"
      shift 2
      ;;
    --model)
      model="$2"
      shift 2
      ;;
    --prompt)
      prompt="$2"
      shift 2
      ;;
    --stream)
      stream_flag=""
      shift
      ;;
    --use-env-token)
      use_env_token=1
      shift
      ;;
    --help|-h)
      cat <<'EOF'
Usage: scripts/test-linux-auth.sh [options]

Starts llm-svc on a temporary local endpoint, checks /health,
lists models, runs a single llm ask prompt, and then shuts down the service.

Options:
  --port <port>         Port to use (default: 5100)
  --host <host>         Host to bind (default: 127.0.0.1)
  --model <model>       Model for llm ask (default: gpt-5.4-mini)
  --prompt <text>       Prompt for llm ask
  --stream              Use streaming for llm ask
  --use-env-token       Leave COPILOT_TOKEN unchanged instead of unsetting it
  --help, -h            Show this help
EOF
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      exit 1
      ;;
  esac
done

if ! command -v dotnet >/dev/null 2>&1; then
  if ! command -v mise >/dev/null 2>&1; then
    echo "dotnet is not on PATH" >&2
    exit 1
  fi
fi

if ! command -v curl >/dev/null 2>&1; then
  echo "curl is required" >&2
  exit 1
fi

if command -v mise >/dev/null 2>&1; then
  dotnet_bin="$(mise which dotnet 2>/dev/null || true)"
fi

if [[ -z "$dotnet_bin" ]]; then
  dotnet_bin="$(command -v dotnet)"
fi

endpoint="http://${host}:${port}"
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/.." && pwd)"
service_project="${repo_root}/llm-svc.csproj"
cli_project="${repo_root}/llm-cli"
log_file="$(mktemp)"
health_file="$(mktemp)"
service_pid=""

cleanup() {
  rm -f "$health_file"
  if [[ -n "$service_pid" ]] && kill -0 "$service_pid" >/dev/null 2>&1; then
    kill "$service_pid" >/dev/null 2>&1 || true
    wait "$service_pid" >/dev/null 2>&1 || true
  fi
  rm -f "$log_file"
}

trap cleanup EXIT

if ss -ltn "( sport = :${port} )" | tail -n +2 | grep -q .; then
  echo "Port ${port} is already in use" >&2
  exit 1
fi

echo "Starting llm-svc at ${endpoint}"
echo "Using dotnet: ${dotnet_bin}"
if [[ "$use_env_token" -eq 1 ]]; then
  Kestrel__Endpoints__Http__Url="$endpoint" "$dotnet_bin" run --no-launch-profile --project "$service_project" >"$log_file" 2>&1 &
else
  env -u COPILOT_TOKEN Kestrel__Endpoints__Http__Url="$endpoint" "$dotnet_bin" run --no-launch-profile --project "$service_project" >"$log_file" 2>&1 &
fi
service_pid="$!"

for _ in $(seq 1 60); do
  if curl -sS -o "$health_file" "$endpoint/health" >/dev/null 2>&1; then
    break
  fi

  if ! kill -0 "$service_pid" >/dev/null 2>&1; then
    echo "llm-svc exited before becoming ready" >&2
    cat "$log_file" >&2
    exit 1
  fi

  sleep 1
done

echo
echo "Health:"
curl -sS "$endpoint/health"
echo

echo
echo "Models:"
"$dotnet_bin" run --project "$cli_project" -- models -e "$endpoint"

echo
echo "Ask:"
if [[ -n "$stream_flag" ]]; then
  "$dotnet_bin" run --project "$cli_project" -- ask "$prompt" -m "$model" -e "$endpoint" "$stream_flag"
else
  "$dotnet_bin" run --project "$cli_project" -- ask "$prompt" -m "$model" -e "$endpoint"
fi
