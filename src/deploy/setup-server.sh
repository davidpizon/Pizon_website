#!/usr/bin/env bash
# One-time setup of a fresh Hostinger KVM VPS (Ubuntu 24.04) for PIZON BBS. Run as root over SSH:
#   scp src/deploy/setup-server.sh root@<vps-ip>:/root/ && ssh root@<vps-ip> bash /root/setup-server.sh
# What it does:
#   - creates the non-root "bbs" user (with your root SSH key) and adds it to the docker group
#   - installs Docker Engine and the compose plugin from Docker's apt repository
#   - firewall: allow only 22, 80 and 443 (ufw)
#   - SSH: key-only login, no root password login
#   - unattended security upgrades
# Safe to run again: each step checks whether it is already done.
set -euo pipefail

if [[ $EUID -ne 0 ]]; then
  echo "Run as root." >&2
  exit 1
fi

export DEBIAN_FRONTEND=noninteractive
apt-get update -y
apt-get install -y ca-certificates curl gnupg ufw unattended-upgrades

# Docker Engine + compose plugin.
if ! command -v docker >/dev/null; then
  install -m 0755 -d /etc/apt/keyrings
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
  chmod a+r /etc/apt/keyrings/docker.asc
  . /etc/os-release
  echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu ${VERSION_CODENAME} stable" \
    > /etc/apt/sources.list.d/docker.list
  apt-get update -y
  apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
fi
systemctl enable --now docker

# The bbs user, reachable with the same SSH key as root.
if ! id bbs >/dev/null 2>&1; then
  adduser --disabled-password --gecos "PIZON BBS" bbs
fi
usermod -aG docker bbs
install -d -m 700 -o bbs -g bbs /home/bbs/.ssh
if [[ -f /root/.ssh/authorized_keys ]]; then
  install -m 600 -o bbs -g bbs /root/.ssh/authorized_keys /home/bbs/.ssh/authorized_keys
fi
install -d -o bbs -g bbs /home/bbs/pizon /home/bbs/pizon/backups

# Firewall.
ufw default deny incoming
ufw default allow outgoing
ufw allow 22/tcp
ufw allow 80/tcp
ufw allow 443/tcp
ufw allow 443/udp
ufw --force enable

# SSH: keys only.
cat > /etc/ssh/sshd_config.d/99-pizon.conf <<'CONF'
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitRootLogin prohibit-password
CONF
systemctl reload ssh

# Automatic security updates.
dpkg-reconfigure -f noninteractive unattended-upgrades

echo "Done. Next: create /home/bbs/pizon/.env (see .env.example), then run deploy.ps1 from your PC."
