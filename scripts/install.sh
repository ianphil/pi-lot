#!/usr/bin/env bash

set -euo pipefail

# ── Defaults ─────────────────────────────────────────────────────────────────
install_path="${HOME}/.local/share/llm-svc"
service_name="copilot-llm-proxy"
endpoint="http://127.0.0.1:5100"
dotnet_bin=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --install-path)
      install_path="$2"
      shift 2
      ;;
    --endpoint)
      endpoint="$2"
      shift 2
      ;;
    --help|-h)
      cat <<'EOF'
Usage: scripts/install.sh [options]

Publishes llm-svc and installs it as a systemd user service.
The service starts automatically at login and has access to the
desktop session (D-Bus, Secret Service) for Copilot credential lookup.

No root required.

Options:
  --install-path <path>   Where to publish binaries (default: ~/.local/share/llm-svc)
  --endpoint <url>        Kestrel listen URL (default: http://127.0.0.1:5100)
  --help, -h              Show this help
EOF
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      exit 1
      ;;
  esac
done

# ── Find dotnet ──────────────────────────────────────────────────────────────
if command -v mise >/dev/null 2>&1; then
  dotnet_bin="$(mise which dotnet 2>/dev/null || true)"
fi

if [[ -z "$dotnet_bin" ]]; then
  if command -v dotnet >/dev/null 2>&1; then
    dotnet_bin="$(command -v dotnet)"
  else
    echo "dotnet is not on PATH" >&2
    exit 1
  fi
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/.." && pwd)"
unit_dir="${HOME}/.config/systemd/user"
unit_file="${unit_dir}/${service_name}.service"

# ── Stop existing service if running ─────────────────────────────────────────
if systemctl --user is-active --quiet "$service_name" 2>/dev/null; then
  echo "Stopping existing ${service_name}..."
  systemctl --user stop "$service_name"
fi

# ── Resolve DOTNET_ROOT ───────────────────────────────────────────────────────
dotnet_root="$(dirname "$(dirname "$dotnet_bin")")"
if [[ ! -d "${dotnet_root}/shared" ]]; then
  dotnet_root="$(dirname "$dotnet_bin")"
fi

# ── Publish ──────────────────────────────────────────────────────────────────
echo "Publishing to ${install_path}..."
"$dotnet_bin" publish "${repo_root}/llm-svc.csproj" -c Release -o "$install_path" --nologo
echo "Published successfully."

# ── Install systemd user service ─────────────────────────────────────────────
mkdir -p "$unit_dir"

cat > "$unit_file" <<UNIT
[Unit]
Description=Copilot LLM Proxy — local OpenAI-compatible proxy to GitHub Copilot
After=default.target

[Service]
Type=exec
ExecStart=${install_path}/llm-svc
WorkingDirectory=${install_path}
Environment=Kestrel__Endpoints__Http__Url=${endpoint}
Environment=DOTNET_ROOT=${dotnet_root}
Restart=on-failure
RestartSec=5

[Install]
WantedBy=default.target
UNIT

echo "Installed unit file at ${unit_file}"

# ── Enable and start ─────────────────────────────────────────────────────────
systemctl --user daemon-reload
systemctl --user enable "$service_name"
systemctl --user start "$service_name"

sleep 2

if systemctl --user is-active --quiet "$service_name"; then
  echo ""
  echo "Copilot LLM Proxy is running!"
  echo "  Endpoint: ${endpoint}/v1"
  echo "  Health:   ${endpoint}/health"
  echo ""
  echo "The proxy starts automatically at login."
  echo "Manage with:"
  echo "  Status:   systemctl --user status ${service_name}"
  echo "  Logs:     journalctl --user -u ${service_name} -f"
  echo "  Stop:     systemctl --user stop ${service_name}"
  echo "  Start:    systemctl --user start ${service_name}"
  echo "  Remove:   scripts/uninstall.sh"
else
  echo "Service registered but may not be running. Check with:"
  echo "  systemctl --user status ${service_name}"
  echo "  journalctl --user -u ${service_name} --no-pager -n 20"
fi
