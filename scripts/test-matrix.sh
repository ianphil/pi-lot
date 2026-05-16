#!/usr/bin/env bash
#
# Runs real CLI/proxy/SDK scenarios matching the test matrix.
#
# Each row exercises a different surface + upstream routing path.
# Starts llm-svc automatically if the port is free; reuses an
# already-running proxy otherwise.
#
# Models used:
#   gpt-5.4-mini     -> /responses only
#   gpt-5.4          -> /responses only
#   claude-haiku-4.5 -> /chat/completions only
#   gpt-5-mini       -> both /chat/completions and /responses (dual)
#   sdk-* commands   -> direct ILlmSdkClient path (no proxy endpoint flag)
#
# Usage:  bash scripts/test-matrix.sh [--port 5110] [--no-stream]

set -euo pipefail

port=5100
host=127.0.0.1
no_stream=0
use_env_token=0
dotnet_bin=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --port)   port="$2";   shift 2 ;;
    --host)   host="$2";   shift 2 ;;
    --no-stream) no_stream=1; shift ;;
    --use-env-token) use_env_token=1; shift ;;
    --dotnet) dotnet_bin="$2"; shift 2 ;;
    --help|-h)
      cat <<'EOF'
Usage: scripts/test-matrix.sh [options]

Starts llm-svc (if not already running), builds the CLI, and runs
every test-matrix scenario against the proxy.

Options:
  --port <port>         Port to use (default: 5100)
  --host <host>         Host to bind (default: 127.0.0.1)
  --no-stream           Disable streaming for all tests
  --use-env-token       Leave COPILOT_TOKEN unchanged instead of unsetting it
  --dotnet <path>       Path to dotnet binary
  --help, -h            Show this help
EOF
      exit 0
      ;;
    *) echo "Unknown option: $1" >&2; exit 1 ;;
  esac
done

endpoint="http://${host}:${port}"
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/.." && pwd)"
llm_cli="${repo_root}/src/llm-cli"
service_project="${repo_root}/src/llm-svc/llm-svc.csproj"

# ── Resolve dotnet ───────────────────────────────────────────────────────────
if [[ -n "$dotnet_bin" ]]; then
  dotnet="$dotnet_bin"
elif command -v mise &>/dev/null; then
  dotnet="$(mise which dotnet 2>/dev/null || true)"
  [[ -z "$dotnet" ]] && dotnet="$(command -v dotnet)"
else
  dotnet="$(command -v dotnet)"
fi

# Build before starting the proxy so the running service cannot interfere with
# shared project outputs during CLI compilation.
echo -e "\033[33mBuilding llm-cli...\033[0m"
$dotnet build "$llm_cli" --no-restore --disable-build-servers -m:1 --no-incremental 2>&1
echo -e "\033[33mBuild complete.\033[0m"

# ── Service lifecycle ────────────────────────────────────────────────────────
log_file="$(mktemp)"
service_pid=""
started_service=0

cleanup() {
  if [[ "$started_service" -eq 1 && -n "$service_pid" ]] && kill -0 "$service_pid" >/dev/null 2>&1; then
    echo -e "\n\033[33mStopping llm-svc (pid ${service_pid})...\033[0m"
    kill "$service_pid" >/dev/null 2>&1 || true
    wait "$service_pid" >/dev/null 2>&1 || true
  fi
  rm -f "$log_file"
}

trap cleanup EXIT

if ss -ltn "( sport = :${port} )" 2>/dev/null | tail -n +2 | grep -q .; then
  echo -e "\033[33mProxy already running on port ${port} — reusing.\033[0m"
else
  echo -e "\033[33mStarting llm-svc at ${endpoint}...\033[0m"
  if [[ "$use_env_token" -eq 1 ]]; then
    Kestrel__Endpoints__Http__Url="$endpoint" "$dotnet" run --no-launch-profile --project "$service_project" >"$log_file" 2>&1 &
  else
    env -u COPILOT_TOKEN Kestrel__Endpoints__Http__Url="$endpoint" "$dotnet" run --no-launch-profile --project "$service_project" >"$log_file" 2>&1 &
  fi
  service_pid="$!"
  started_service=1

  for _ in $(seq 1 60); do
    if curl -sS -o /dev/null "$endpoint/health" >/dev/null 2>&1; then
      break
    fi
    if ! kill -0 "$service_pid" >/dev/null 2>&1; then
      echo "llm-svc exited before becoming ready" >&2
      cat "$log_file" >&2
      exit 1
    fi
    sleep 1
  done

  echo -e "\033[32mllm-svc ready (pid ${service_pid}).\033[0m"
fi

prompt="Reply with exactly: hello"
tool_prompt="Use the fetch_url tool to fetch https://raw.githubusercontent.com/github/gitignore/main/README.md and summarize it in one sentence"

pass=0
fail=0
total=0

invoke_llm() {
  local label="$1"
  local verb="$2"
  local prompt_text="$3"
  local model="$4"
  local stream="$5"
  local tools="${6:-0}"
  local use_endpoint="${7:-1}"
  local -a extra_args=()
  if [[ "$#" -gt 7 ]]; then
    shift 7
    extra_args=("$@")
  fi

  local -a cli_args=("$verb" "$prompt_text" "-m" "$model")
  if [[ "$use_endpoint" == "1" ]]; then
    cli_args+=("-e" "$endpoint")
  fi
  if [[ "$stream" == "0" ]]; then
    cli_args+=("--no-stream")
  fi
  if [[ "$tools" == "1" ]]; then
    cli_args+=("--tools")
  fi
  if [[ "${#extra_args[@]}" -gt 0 ]]; then
    cli_args+=("${extra_args[@]}")
  fi

  echo ""
  echo -e "─── \033[36m${label}\033[0m ───"
  echo -e "  \033[90mllm ${cli_args[*]}\033[0m"

  total=$((total + 1))

  local output
  if output=$($dotnet run --project "$llm_cli" --no-build -- "${cli_args[@]}" 2>&1); then
    local text
    text=$(echo "$output" | tr '\n' ' ' | head -c 200)
    if echo "$output" | grep -qiE "Unhandled exception|FAIL|error"; then
      echo -e "  \033[31mFAIL: ${text}\033[0m"
      fail=$((fail + 1))
    else
      echo -e "  \033[32mOK: ${text}\033[0m"
      pass=$((pass + 1))
    fi
  else
    local text
    text=$(echo "$output" | tr '\n' ' ' | head -c 200)
    echo -e "  \033[31mFAIL: ${text}\033[0m"
    fail=$((fail + 1))
  fi
}

invoke_proxy_knobs() {
  local label="$1"
  local model="$2"
  local response_file
  response_file="$(mktemp)"

  echo ""
  echo -e "─── \033[36m${label}\033[0m ───"
  echo -e "  \033[90mcurl ${endpoint}/v1/responses with proxy knob headers\033[0m"

  total=$((total + 1))

  local status
  if status=$(curl -sS \
      -o "$response_file" \
      -w "%{http_code}" \
      -X POST "${endpoint}/v1/responses" \
      -H "Content-Type: application/json" \
      -H "X-LLM-Request-Id: test-matrix-proxy-knobs" \
      -H "X-LLM-Correlation-Id: test-matrix-proxy-correlation" \
      -H "X-LLM-Metadata-test: proxy-knobs" \
      -H "X-LLM-Timeout-Ms: 60000" \
      -H "X-LLM-Max-Retries: 1" \
      -H "X-LLM-Max-Retry-Delay-Ms: 1000" \
      --data "{\"model\":\"${model}\",\"input\":\"${prompt}\",\"stream\":false}" 2>&1); then
    local output
    output="$(cat "$response_file")"
    rm -f "$response_file"

    local text
    text=$(echo "$output" | tr '\n' ' ' | head -c 200)
    if [[ "$status" =~ ^2 ]]; then
      echo -e "  \033[32mOK: ${text}\033[0m"
      pass=$((pass + 1))
    else
      echo -e "  \033[31mFAIL (${status}): ${text}\033[0m"
      fail=$((fail + 1))
    fi
  else
    rm -f "$response_file"
    local text
    text=$(echo "$status" | tr '\n' ' ' | head -c 200)
    echo -e "  \033[31mFAIL: ${text}\033[0m"
    fail=$((fail + 1))
  fi
}

use_stream=1
if [[ "$no_stream" == "1" ]]; then
  use_stream=0
fi

# ══════════════════════════════════════════════════════════════════════════════
# /responses surface (llm ask)
# ══════════════════════════════════════════════════════════════════════════════

invoke_llm "1. ask → responses-only model, plain"                ask "$prompt"      gpt-5.4-mini     0
invoke_llm "2. ask → responses-only model, streaming"            ask "$prompt"      gpt-5.4-mini     "$use_stream"
invoke_llm "3. ask → chat-only model, plain translation"         ask "$prompt"      claude-haiku-4.5 0
invoke_llm "4. ask → chat-only model, streaming translation"     ask "$prompt"      claude-haiku-4.5 "$use_stream"
invoke_llm "5. ask → chat-only model, tools"                     ask "$tool_prompt" claude-haiku-4.5 0 1
invoke_llm "6. ask → chat-only model, streaming + tools"         ask "$tool_prompt" claude-haiku-4.5 "$use_stream" 1
invoke_llm "7. ask → dual-endpoint model, prefers responses"     ask "$prompt"      gpt-5-mini       0
invoke_llm "7b. ask → proxy CLI per-call knobs"                  ask "$prompt"      gpt-5.4-mini     0 0 1 \
  --request-id test-matrix-cli-ask \
  --correlation-id test-matrix-cli-correlation \
  --metadata surface=ask \
  --timeout-ms 60000 \
  --max-retries 1 \
  --max-retry-delay-ms 1000
invoke_proxy_knobs "7c. responses → raw proxy per-call knobs"    gpt-5.4-mini

# ══════════════════════════════════════════════════════════════════════════════
# /chat/completions surface (llm chat)
# ══════════════════════════════════════════════════════════════════════════════

invoke_llm "8. chat → chat-capable model, plain"                     chat "$prompt"      claude-haiku-4.5 0
invoke_llm "9. chat → chat-capable model, streaming"                 chat "$prompt"      claude-haiku-4.5 "$use_stream"
invoke_llm "10. chat → chat-capable model, streaming + tools"        chat "$tool_prompt" claude-haiku-4.5 "$use_stream" 1
invoke_llm "11. chat → responses-only model, plain translation"      chat "$prompt"      gpt-5.4-mini     0
invoke_llm "12. chat → responses-only model, streaming translation"  chat "$prompt"      gpt-5.4-mini     "$use_stream"
invoke_llm "13. chat → responses-only model, streaming + tools"      chat "$tool_prompt" gpt-5.4-mini     "$use_stream" 1
invoke_llm "14. chat → dual-endpoint model, prefers chat"            chat "$prompt"      gpt-5-mini       0
invoke_llm "15. chat → dual-endpoint model, streaming prefers chat"  chat "$prompt"      gpt-5-mini       "$use_stream"
invoke_llm "15b. chat → proxy CLI per-call knobs"                    chat "$prompt"      gpt-5-mini       0 0 1 \
  --request-id test-matrix-cli-chat \
  --correlation-id test-matrix-cli-correlation \
  --metadata surface=chat \
  --timeout-ms 60000 \
  --max-retries 1 \
  --max-retry-delay-ms 1000

# ══════════════════════════════════════════════════════════════════════════════
# SDK surface (llm sdk-ask / llm sdk-chat)
# ══════════════════════════════════════════════════════════════════════════════

invoke_llm "16. sdk-ask → direct sdk path, plain"                    sdk-ask  "$prompt" gpt-5.4-mini 0 0 0
invoke_llm "17. sdk-ask → direct sdk path, streaming"                sdk-ask  "$prompt" gpt-5.4-mini "$use_stream" 0 0
invoke_llm "18. sdk-ask → direct sdk path, tools"                    sdk-ask  "$tool_prompt" gpt-5.4-mini 0 1 0
invoke_llm "19. sdk-ask → direct sdk path, streaming + tools"        sdk-ask  "$tool_prompt" gpt-5.4-mini "$use_stream" 1 0
invoke_llm "19b. sdk-ask → direct sdk per-call knobs"                sdk-ask  "$prompt" gpt-5.4-mini 0 0 0 \
  --request-id test-matrix-sdk-ask \
  --correlation-id test-matrix-sdk-correlation \
  --metadata surface=sdk-ask \
  --timeout-ms 60000 \
  --max-retries 1 \
  --max-retry-delay-ms 1000
invoke_llm "20. sdk-context-ask → context API, responses plain"      sdk-context-ask "$prompt" gpt-5.4-mini     0 0 0 --api responses
invoke_llm "21. sdk-context-ask → context API, responses streaming"  sdk-context-ask "$prompt" gpt-5.4-mini     "$use_stream" 0 0 --api responses
invoke_llm "22. sdk-context-ask → context API, chat plain"           sdk-context-ask "$prompt" claude-haiku-4.5 0 0 0 --api chat
invoke_llm "23. sdk-context-ask → context API, chat streaming"       sdk-context-ask "$prompt" claude-haiku-4.5 "$use_stream" 0 0 --api chat
invoke_llm "23b. sdk-context-ask → context per-call knobs"           sdk-context-ask "$prompt" gpt-5.4-mini     0 0 0 \
  --api responses \
  --request-id test-matrix-sdk-context \
  --correlation-id test-matrix-sdk-correlation \
  --metadata surface=sdk-context \
  --timeout-ms 60000 \
  --max-retries 1 \
  --max-retry-delay-ms 1000
invoke_llm "24. sdk-chat → direct sdk path, plain"                   sdk-chat "$prompt" gpt-5-mini   0 0 0
invoke_llm "25. sdk-chat → direct sdk path, streaming"               sdk-chat "$prompt" gpt-5-mini   "$use_stream" 0 0
invoke_llm "25b. sdk-chat → direct sdk per-call knobs"               sdk-chat "$prompt" gpt-5-mini   0 0 0 \
  --request-id test-matrix-sdk-chat \
  --correlation-id test-matrix-sdk-correlation \
  --metadata surface=sdk-chat \
  --timeout-ms 60000 \
  --max-retries 1 \
  --max-retry-delay-ms 1000

# ══════════════════════════════════════════════════════════════════════════════
# Summary
# ══════════════════════════════════════════════════════════════════════════════

echo ""
echo "═══════════════════════════════════════"
if [[ "$fail" -eq 0 ]]; then
  echo -e "  \033[32mResults: ${pass} passed, ${fail} failed  (of ${total})\033[0m"
else
  echo -e "  \033[31mResults: ${pass} passed, ${fail} failed  (of ${total})\033[0m"
fi
echo "═══════════════════════════════════════"

exit "$fail"
