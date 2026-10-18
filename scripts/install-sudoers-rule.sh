#!/bin/sh
# Lets the current user start openfortivpn as root without a password prompt (MVP elevation,
# see docs/architecture.md). Run it yourself: ./scripts/install-sudoers-rule.sh
# Remove the rule again with: sudo rm /etc/sudoers.d/openfortivpn
set -eu

OPENFORTIVPN="${1:-$(command -v openfortivpn || true)}"
if [ -z "$OPENFORTIVPN" ] || [ ! -x "$OPENFORTIVPN" ]; then
    echo "openfortivpn not found. Install it with: brew install openfortivpn" >&2
    exit 1
fi

# The rule is for the user who runs the app. Under sudo, id -un would be root.
if [ "$(id -u)" -eq 0 ]; then
    echo "Run this script as yourself, without sudo; it asks for your password when it needs it." >&2
    exit 1
fi
USER_NAME="$(id -un)"
RULE_FILE="/etc/sudoers.d/openfortivpn"
TMP_FILE="$(mktemp)"
trap 'rm -f "$TMP_FILE"' EXIT

printf '%s ALL=(root) NOPASSWD: %s\n' "$USER_NAME" "$OPENFORTIVPN" > "$TMP_FILE"

echo "This rule will be installed in $RULE_FILE:"
cat "$TMP_FILE"
printf 'Continue? [y/N] '
read -r answer
[ "$answer" = "y" ] || [ "$answer" = "Y" ] || exit 1

# Check the syntax before installing: a broken sudoers file can lock you out of sudo.
sudo visudo -cf "$TMP_FILE"
sudo install -m 0440 -o root -g wheel "$TMP_FILE" "$RULE_FILE"
# Check the way the app runs it: drop cached sudo credentials, then run without a password.
sudo -k
if sudo -n "$OPENFORTIVPN" --version >/dev/null 2>&1; then
    echo "Done: $OPENFORTIVPN can now run without a password."
else
    echo "The rule was installed, but sudo still asks for a password for $OPENFORTIVPN." >&2
    exit 1
fi
