#!/usr/bin/env bash

set -euo pipefail

install_path="${HOME}/.local/share/llm-svc"
service_name="copilot-llm-proxy"
remove_binaries=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --remove-binaries)
      remove_binaries=1
      shift
      ;;
    --install-path)
      install_path="$2"
      shift 2
      ;;
    --help|-h)
      cat <<'EOF'
Usage: scripts/uninstall.sh [options]

Stops and removes the Copilot LLM Proxy systemd user service.

Options:
  --remove-binaries       Also delete published binaries
  --install-path <path>   Where binaries are (default: ~/.local/share/llm-svc)
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

unit_dir="${HOME}/.config/systemd/user"
unit_file="${unit_dir}/${service_name}.service"

if [[ ! -f "$unit_file" ]]; then
  echo "Service '${service_name}' not found. Nothing to do."
  exit 0
fi

if systemctl --user is-active --quiet "$service_name" 2>/dev/null; then
  echo "Stopping ${service_name}..."
  systemctl --user stop "$service_name"
fi

echo "Disabling and removing service..."
systemctl --user disable "$service_name" 2>/dev/null || true
rm -f "$unit_file"
systemctl --user daemon-reload
echo "Service removed."

if [[ "$remove_binaries" -eq 1 ]] && [[ -d "$install_path" ]]; then
  echo "Removing binaries at ${install_path}..."
  rm -rf "$install_path"
  echo "Binaries removed."
fi
